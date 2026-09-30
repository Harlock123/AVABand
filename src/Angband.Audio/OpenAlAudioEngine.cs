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
    private readonly Channel _musicChannel;
    private readonly Channel _ambienceChannel;
    private readonly short[] _chunk = new short[StreamChunkSamples];
    private readonly Timer? _pump;
    private int _nextVoice;

    private float _master = 1, _effects = 1, _music = 0.6f, _ambience = 0.5f;

    /// <summary>
    /// A streamed, fading channel: the music, or the ambience loop under it. Its source is fed from
    /// a decoder through a small queue of buffers; the pump fades it and keeps the queue full.
    /// </summary>
    private sealed class Channel(uint source, uint[] buffers, double targetDb, double maxBoostDb)
    {
        public readonly uint Source = source;
        public readonly uint[] Buffers = buffers;
        public readonly double TargetDb = targetDb;
        public readonly double MaxBoostDb = maxBoostDb;
        public IAudioDecoder? Decoder;
        public bool Loop;
        public float Fade;             // 0..1
        public float Gain = 1;         // evens out the track's loudness
        public string? Current;
        public string? PendingPath;    // to start once the current one has faded out
        public bool PendingLoop;
    }

    private OpenAlAudioEngine(AL al, ALContext alc, Device* device, Context* context)
    {
        _al = al;
        _alc = alc;
        _device = device;
        _context = context;
        IsAvailable = true;
        for (var i = 0; i < EffectVoices; i++) _voices[i] = _al.GenSource();
        _musicChannel = NewChannel(Loudness.MusicTargetDb, Loudness.MusicMaxBoostDb);
        _ambienceChannel = NewChannel(Loudness.AmbienceTargetDb, Loudness.MusicMaxBoostDb);
        _pump = new Timer(_ => Pump(), null, 40, 40);
    }

    public bool IsAvailable { get; }

    /// <summary>The mixing period asked for at start-up (0: OpenAL's default).</summary>
    public int PeriodFrames { get; private init; }
    public string? CurrentMusic => _musicChannel.Current;
    public string? CurrentAmbience => _ambienceChannel.Current;

    private Channel NewChannel(double targetDb, double maxBoostDb)
    {
        var buffers = new uint[StreamBuffers];
        for (var i = 0; i < StreamBuffers; i++) buffers[i] = _al.GenBuffer();
        return new Channel(_al.GenSource(), buffers, targetDb, maxBoostDb);
    }

    public event Action? MusicEnded;

    /// <summary>
    /// Opens the default device; falls back to a silent engine if that is impossible.
    /// <paramref name="periodFrames"/> is how many sample frames OpenAL mixes at a time (0: its
    /// default). Bigger periods cost a little latency but ride out a jittery audio path — a
    /// virtual machine's emulated sound card underruns constantly at the default size.
    /// </summary>
    public static IAudioEngine CreateOrSilent(int periodFrames = 0)
    {
        try
        {
            // Before OpenAL Soft first reads its configuration (on the first call below).
            if (periodFrames > 0) OpenAlConfig.UsePeriod(periodFrames);
            var alc = ALContext.GetApi(soft: true);
            var al = AL.GetApi(soft: true);
            var device = alc.OpenDevice(null);
            if (device == null) return new NullAudioEngine(AudioFailure.NoDevice);
            var context = alc.CreateContext(device, null);
            if (context == null || !alc.MakeContextCurrent(context))
            {
                alc.CloseDevice(device);
                return new NullAudioEngine(AudioFailure.NoContext);
            }
            return new OpenAlAudioEngine(al, alc, device, context) { PeriodFrames = periodFrames };
        }
        catch (Exception ex)
        {
            // Whatever went wrong, the game plays on in silence — and the Sound tab says why.
            Trace.WriteLine($"Audio unavailable: {ex}");
            return new NullAudioEngine(AudioFailure.Describe(ex, AudioFailure.MissingVcRuntimeHere()));
        }
    }

    public void SetVolumes(float master, float effects, float music)
    {
        lock (_lock)
        {
            (_master, _effects, _music) = (Math.Clamp(master, 0, 1), Math.Clamp(effects, 0, 1), Math.Clamp(music, 0, 1));
            ApplyGain(_musicChannel);
            ApplyGain(_ambienceChannel);
        }
    }

    public void SetAmbienceVolume(float volume)
    {
        lock (_lock)
        {
            _ambience = Math.Clamp(volume, 0, 1);
            ApplyGain(_ambienceChannel);
        }
    }

    private void ApplyGain(Channel c) =>
        _al.SetSourceProperty(c.Source, SourceFloat.Gain, _master * (c == _musicChannel ? _music : _ambience) * c.Fade);

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
            // Bring every effect to the same loudness, whatever level it was recorded at.
            var (rms, peak) = Loudness.Measure(samples);
            Loudness.Apply(samples, Loudness.Gain(rms, peak, Loudness.EffectTargetDb, Loudness.EffectMaxBoostDb));
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
        lock (_lock) Queue(_musicChannel, path, loop);
    }

    public void StopMusic()
    {
        lock (_lock) Stop(_musicChannel);
    }

    public void PlayAmbience(string path)
    {
        lock (_lock) Queue(_ambienceChannel, path, loop: true);
    }

    public void StopAmbience()
    {
        lock (_lock) Stop(_ambienceChannel);
    }

    private void Queue(Channel c, string path, bool loop)
    {
        if (path == c.Current && c.PendingPath is null) return;
        c.PendingPath = path;
        c.PendingLoop = loop;
        if (c.Decoder is null) StartPending(c);
    }

    private static void Stop(Channel c)
    {
        c.PendingPath = null;
        c.Current = null; // the pump fades it out
    }

    /// <summary>Called under the lock: swap a channel to its pending track and prime the stream.</summary>
    private void StartPending(Channel c)
    {
        StopStream(c);
        var path = c.PendingPath;
        c.PendingPath = null;
        if (path is null) return;
        try
        {
            c.Decoder = AudioDecoders.Open(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Could not open '{path}': {ex.Message}");
            return;
        }
        c.Loop = c.PendingLoop;
        c.Current = path;
        c.Gain = MeasureMusic(c);
        c.Fade = 0;
        foreach (var buffer in c.Buffers)
            if (Fill(c, buffer)) _al.SourceQueueBuffers(c.Source, [buffer]);
        _al.SetSourceProperty(c.Source, SourceFloat.Gain, 0f);
        _al.SourcePlay(c.Source);
    }

    private void StopStream(Channel c)
    {
        _al.SourceStop(c.Source);
        _al.GetSourceProperty(c.Source, GetSourceInteger.BuffersQueued, out var queued);
        if (queued > 0)
        {
            var done = new uint[queued];
            _al.SourceUnqueueBuffers(c.Source, done);
        }
        c.Decoder?.Dispose();
        c.Decoder = null;
    }

    /// <summary>How much music may be read to judge a track's loudness (seconds).</summary>
    private const int MusicMeasureSeconds = 30;

    /// <summary>
    /// Listens to the start of a track to settle its loudness (see <see cref="Loudness"/>), then
    /// rewinds it.
    /// </summary>
    private float MeasureMusic(Channel c)
    {
        var decoder = c.Decoder!;
        var limit = MusicMeasureSeconds * decoder.SampleRate * Math.Max(1, decoder.Channels);
        double sumSquares = 0;
        long count = 0;
        var peak = 0.0;
        int read;
        while (count < limit && (read = decoder.Read(_chunk)) > 0)
        {
            var (rms, chunkPeak) = Loudness.Measure(_chunk.AsSpan(0, read));
            sumSquares += rms * rms * read;
            count += read;
            peak = Math.Max(peak, chunkPeak);
        }
        decoder.Rewind();
        return count == 0 ? 1f : Loudness.Gain(Math.Sqrt(sumSquares / count), peak, c.TargetDb, c.MaxBoostDb);
    }

    /// <summary>Decodes the next chunk into a buffer; loops or ends the track at end of file.</summary>
    private bool Fill(Channel c, uint buffer)
    {
        if (c.Decoder is not { } decoder) return false;
        var read = decoder.Read(_chunk);
        if (read == 0 && c.Loop)
        {
            decoder.Rewind();
            read = decoder.Read(_chunk);
        }
        if (read == 0) return false;
        Loudness.Apply(_chunk.AsSpan(0, read), c.Gain);
        _al.BufferData(buffer, decoder.Channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16,
            _chunk.AsSpan(0, read).ToArray(), decoder.SampleRate);
        return true;
    }

    /// <summary>Background tick: fades, and keeps both channels' queues topped up.</summary>
    private void Pump()
    {
        bool ended;
        lock (_lock)
        {
            ended = PumpChannel(_musicChannel);
            PumpChannel(_ambienceChannel);
        }
        if (ended) MusicEnded?.Invoke();
    }

    /// <summary>One channel's tick (under the lock); true when a track that wasn't looping played out.</summary>
    private bool PumpChannel(Channel c)
    {
        if (c.Decoder is null)
        {
            if (c.PendingPath is not null) StartPending(c);
            return false;
        }

        // Fade out when stopping or switching tracks, fade in otherwise.
        var leaving = c.Current is null || c.PendingPath is not null;
        c.Fade = Math.Clamp(c.Fade + (leaving ? -FadeStep : FadeStep), 0, 1);
        ApplyGain(c);
        if (leaving && c.Fade <= 0)
        {
            StopStream(c);
            if (c.PendingPath is not null) StartPending(c);
            return false;
        }

        _al.GetSourceProperty(c.Source, GetSourceInteger.BuffersProcessed, out var processed);
        for (var i = 0; i < processed; i++)
        {
            var one = new uint[1];
            _al.SourceUnqueueBuffers(c.Source, one);
            if (Fill(c, one[0])) _al.SourceQueueBuffers(c.Source, one);
        }

        _al.GetSourceProperty(c.Source, GetSourceInteger.SourceState, out var state);
        _al.GetSourceProperty(c.Source, GetSourceInteger.BuffersQueued, out var queued);
        if (state != (int)SourceState.Playing && queued > 0) _al.SourcePlay(c.Source); // recover from underrun
        if (queued > 0) return false;
        // The track played to its end (it wasn't looping).
        StopStream(c);
        c.Current = null;
        return true;
    }

    public void Dispose()
    {
        _pump?.Dispose();
        lock (_lock)
        {
            foreach (var c in new[] { _musicChannel, _ambienceChannel })
            {
                StopStream(c);
                _al.DeleteSource(c.Source);
                foreach (var b in c.Buffers) _al.DeleteBuffer(b);
            }
            foreach (var v in _voices) { _al.SourceStop(v); _al.DeleteSource(v); }
            foreach (var b in _buffers.Values.OfType<uint>()) _al.DeleteBuffer(b);
            _alc.MakeContextCurrent(null);
            _alc.DestroyContext(_context);
            _alc.CloseDevice(_device);
        }
    }
}
