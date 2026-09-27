using System.Diagnostics;
using Silk.NET.OpenAL;

namespace Angband.Audio;

/// <summary>
/// OpenAL Soft playback. Effects are decoded once, cached as buffers and played on a pool of
/// sources; music streams from its decoder through a queue of small buffers topped up by a
/// background pump, with short fades between tracks. All AL calls are serialised by a lock.
/// </summary>
public sealed unsafe class OpenAlAudioEngine : IAudioEngine
{
    private const int EffectVoices = 16;
    private const int StreamBuffers = 4;
    private const int StreamChunkSamples = 16384;
    private const float FadeStep = 0.08f; // per pump tick (~40 ms): about half a second

    private readonly object _lock = new();
    private readonly AL _al = null!;
    private readonly ALContext _alc = null!;
    private readonly Device* _device;
    private readonly Context* _context;
    private readonly uint[] _voices = new uint[EffectVoices];
    private readonly Dictionary<string, uint?> _buffers = new(StringComparer.Ordinal);
    private readonly uint _musicSource;
    private readonly uint[] _musicBuffers = new uint[StreamBuffers];
    private readonly short[] _chunk = new short[StreamChunkSamples];
    private readonly Timer? _pump;
    private int _nextVoice;

    private float _master = 1, _effects = 1, _music = 0.6f;
    private IAudioDecoder? _decoder;
    private bool _loop;
    private float _fade;          // current music fade level 0..1
    private string? _pendingPath; // track to start once the current one has faded out
    private bool _pendingLoop;

    private OpenAlAudioEngine(AL al, ALContext alc, Device* device, Context* context)
    {
        _al = al;
        _alc = alc;
        _device = device;
        _context = context;
        IsAvailable = true;
        for (var i = 0; i < EffectVoices; i++) _voices[i] = _al.GenSource();
        _musicSource = _al.GenSource();
        for (var i = 0; i < StreamBuffers; i++) _musicBuffers[i] = _al.GenBuffer();
        _pump = new Timer(_ => Pump(), null, 40, 40);
    }

    public bool IsAvailable { get; }
    public string? CurrentMusic { get; private set; }

    /// <summary>Opens the default device; falls back to a silent engine if that is impossible.</summary>
    public static IAudioEngine CreateOrSilent()
    {
        try
        {
            var alc = ALContext.GetApi(soft: true);
            var al = AL.GetApi(soft: true);
            var device = alc.OpenDevice(null);
            if (device == null) return new NullAudioEngine();
            var context = alc.CreateContext(device, null);
            if (context == null || !alc.MakeContextCurrent(context))
            {
                alc.CloseDevice(device);
                return new NullAudioEngine();
            }
            return new OpenAlAudioEngine(al, alc, device, context);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException
                                       or FileNotFoundException or TypeInitializationException)
        {
            Trace.WriteLine($"Audio unavailable: {ex.Message}");
            return new NullAudioEngine();
        }
    }

    public void SetVolumes(float master, float effects, float music)
    {
        lock (_lock)
        {
            (_master, _effects, _music) = (Math.Clamp(master, 0, 1), Math.Clamp(effects, 0, 1), Math.Clamp(music, 0, 1));
            _al.SetSourceProperty(_musicSource, SourceFloat.Gain, _master * _music * _fade);
        }
    }

    public void PlayEffect(string path, float gain = 1f)
    {
        lock (_lock)
        {
            if (!_buffers.TryGetValue(path, out var buffer))
            {
                buffer = LoadBuffer(path);
                _buffers[path] = buffer;
            }
            if (buffer is not { } buf) return;

            // Prefer an idle voice; otherwise take over the oldest one.
            var voice = _voices[_nextVoice];
            for (var i = 0; i < EffectVoices; i++)
            {
                var candidate = _voices[(_nextVoice + i) % EffectVoices];
                _al.GetSourceProperty(candidate, GetSourceInteger.SourceState, out var state);
                if (state != (int)SourceState.Playing) { voice = candidate; break; }
            }
            _nextVoice = (_nextVoice + 1) % EffectVoices;

            _al.SourceStop(voice);
            _al.SetSourceProperty(voice, SourceInteger.Buffer, (int)buf);
            _al.SetSourceProperty(voice, SourceFloat.Gain, _master * _effects * Math.Clamp(gain, 0, 2));
            _al.SourcePlay(voice);
        }
    }

    private uint? LoadBuffer(string path)
    {
        try
        {
            var (samples, channels, rate) = AudioDecoders.DecodeAll(path);
            if (samples.Length == 0) return null;
            var buffer = _al.GenBuffer();
            _al.BufferData(buffer, channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16, samples, rate);
            return buffer;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Could not load sound '{path}': {ex.Message}");
            return null;
        }
    }

    public void PlayMusic(string path, bool loop = true)
    {
        lock (_lock)
        {
            if (path == CurrentMusic && _pendingPath is null) return;
            _pendingPath = path;
            _pendingLoop = loop;
            if (_decoder is null) StartPending();
        }
    }

    public void StopMusic()
    {
        lock (_lock)
        {
            _pendingPath = null;
            CurrentMusic = null; // the pump fades the old track out
        }
    }

    /// <summary>Called under the lock: swap to the pending track and prime the stream.</summary>
    private void StartPending()
    {
        StopStream();
        var path = _pendingPath;
        _pendingPath = null;
        if (path is null) return;
        try
        {
            _decoder = AudioDecoders.Open(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Could not open music '{path}': {ex.Message}");
            return;
        }
        _loop = _pendingLoop;
        CurrentMusic = path;
        _fade = 0;
        foreach (var buffer in _musicBuffers)
            if (Fill(buffer)) _al.SourceQueueBuffers(_musicSource, [buffer]);
        _al.SetSourceProperty(_musicSource, SourceFloat.Gain, 0f);
        _al.SourcePlay(_musicSource);
    }

    private void StopStream()
    {
        _al.SourceStop(_musicSource);
        _al.GetSourceProperty(_musicSource, GetSourceInteger.BuffersQueued, out var queued);
        if (queued > 0)
        {
            var done = new uint[queued];
            _al.SourceUnqueueBuffers(_musicSource, done);
        }
        _decoder?.Dispose();
        _decoder = null;
    }

    /// <summary>Decodes the next chunk into a buffer; loops or ends the track at end of file.</summary>
    private bool Fill(uint buffer)
    {
        if (_decoder is null) return false;
        var read = _decoder.Read(_chunk);
        if (read == 0 && _loop)
        {
            _decoder.Rewind();
            read = _decoder.Read(_chunk);
        }
        if (read == 0) return false;
        _al.BufferData(buffer, _decoder.Channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16,
            _chunk.AsSpan(0, read).ToArray(), _decoder.SampleRate);
        return true;
    }

    /// <summary>Background tick: fades, and keeps the music queue topped up.</summary>
    private void Pump()
    {
        lock (_lock)
        {
            if (_decoder is null)
            {
                if (_pendingPath is not null) StartPending();
                return;
            }

            // Fade out when stopping or switching tracks, fade in otherwise.
            var leaving = CurrentMusic is null || _pendingPath is not null;
            _fade = Math.Clamp(_fade + (leaving ? -FadeStep : FadeStep), 0, 1);
            _al.SetSourceProperty(_musicSource, SourceFloat.Gain, _master * _music * _fade);
            if (leaving && _fade <= 0)
            {
                StopStream();
                if (_pendingPath is not null) StartPending();
                return;
            }

            _al.GetSourceProperty(_musicSource, GetSourceInteger.BuffersProcessed, out var processed);
            for (var i = 0; i < processed; i++)
            {
                var one = new uint[1];
                _al.SourceUnqueueBuffers(_musicSource, one);
                if (Fill(one[0])) _al.SourceQueueBuffers(_musicSource, one);
            }

            _al.GetSourceProperty(_musicSource, GetSourceInteger.SourceState, out var state);
            _al.GetSourceProperty(_musicSource, GetSourceInteger.BuffersQueued, out var queued);
            if (state != (int)SourceState.Playing && queued > 0) _al.SourcePlay(_musicSource); // recover from underrun
            if (queued == 0) { StopStream(); CurrentMusic = null; }
        }
    }

    public void Dispose()
    {
        _pump?.Dispose();
        lock (_lock)
        {
            StopStream();
            foreach (var v in _voices) { _al.SourceStop(v); _al.DeleteSource(v); }
            _al.DeleteSource(_musicSource);
            foreach (var b in _musicBuffers) _al.DeleteBuffer(b);
            foreach (var b in _buffers.Values.OfType<uint>()) _al.DeleteBuffer(b);
            _alc.MakeContextCurrent(null);
            _alc.DestroyContext(_context);
            _alc.CloseDevice(_device);
        }
    }
}
