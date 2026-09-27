using NLayer;
using NVorbis;

namespace Angband.Audio;

/// <summary>A source of interleaved 16-bit PCM samples.</summary>
public interface IAudioDecoder : IDisposable
{
    int Channels { get; }
    int SampleRate { get; }
    /// <summary>Reads up to <c>buffer.Length</c> interleaved samples; returns how many were read (0 at the end).</summary>
    int Read(short[] buffer);
    /// <summary>Back to the start (for looping music).</summary>
    void Rewind();
}

/// <summary>Opens WAV, Ogg Vorbis or MP3 files by extension.</summary>
public static class AudioDecoders
{
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".wav", ".ogg", ".mp3" };

    public static IAudioDecoder Open(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".wav" => new WavDecoder(path),
        ".ogg" => new VorbisDecoder(path),
        ".mp3" => new Mp3Decoder(path),
        var ext => throw new NotSupportedException($"Unsupported audio format '{ext}' ({path})."),
    };

    /// <summary>Decodes a whole (short) file into memory, for sound effects.</summary>
    public static (short[] Samples, int Channels, int SampleRate) DecodeAll(string path)
    {
        using var decoder = Open(path);
        var chunk = new short[16384];
        var all = new List<short>();
        int read;
        while ((read = decoder.Read(chunk)) > 0) all.AddRange(chunk.AsSpan(0, read).ToArray());
        return (all.ToArray(), decoder.Channels, decoder.SampleRate);
    }

    internal static short ToPcm16(float sample) => (short)Math.Clamp(sample * 32767f, -32768f, 32767f);
}

/// <summary>Uncompressed RIFF WAVE: 8/16-bit integer PCM or 32-bit float.</summary>
public sealed class WavDecoder : IAudioDecoder
{
    private readonly BinaryReader _reader;
    private readonly long _dataStart;
    private readonly long _dataLength;
    private readonly int _bits;
    private readonly bool _float;
    private long _position;

    public WavDecoder(string path)
    {
        _reader = new BinaryReader(File.OpenRead(path));
        if (new string(_reader.ReadChars(4)) != "RIFF") throw new InvalidDataException($"{path} is not a RIFF file.");
        _reader.ReadInt32();
        if (new string(_reader.ReadChars(4)) != "WAVE") throw new InvalidDataException($"{path} is not a WAVE file.");

        var gotFormat = false;
        while (_reader.BaseStream.Position < _reader.BaseStream.Length)
        {
            var id = new string(_reader.ReadChars(4));
            var size = _reader.ReadInt32();
            if (id == "fmt ")
            {
                var format = _reader.ReadInt16();
                Channels = _reader.ReadInt16();
                SampleRate = _reader.ReadInt32();
                _reader.ReadInt32();
                _reader.ReadInt16();
                _bits = _reader.ReadInt16();
                _reader.BaseStream.Seek(size - 16, SeekOrigin.Current);
                _float = format == 3;
                if (format is not (1 or 3) && format != unchecked((short)0xFFFE))
                    throw new NotSupportedException($"{path}: WAV format {format} is not supported.");
                gotFormat = true;
            }
            else if (id == "data")
            {
                if (!gotFormat) throw new InvalidDataException($"{path}: data before fmt.");
                _dataStart = _reader.BaseStream.Position;
                _dataLength = Math.Min(size, _reader.BaseStream.Length - _dataStart);
                return;
            }
            else _reader.BaseStream.Seek(size + (size & 1), SeekOrigin.Current);
        }
        throw new InvalidDataException($"{path}: no data chunk.");
    }

    public int Channels { get; }
    public int SampleRate { get; }

    public int Read(short[] buffer)
    {
        var bytesPerSample = _bits / 8;
        var count = 0;
        while (count < buffer.Length && _position + bytesPerSample <= _dataLength)
        {
            buffer[count++] = (_bits, _float) switch
            {
                (8, _) => (short)((_reader.ReadByte() - 128) << 8),
                (16, _) => _reader.ReadInt16(),
                (24, _) => Read24(),
                (32, true) => AudioDecoders.ToPcm16(_reader.ReadSingle()),
                (32, false) => (short)(_reader.ReadInt32() >> 16),
                _ => throw new NotSupportedException($"{_bits}-bit WAV is not supported."),
            };
            _position += bytesPerSample;
        }
        return count;
    }

    /// <summary>24-bit sample: drop the least significant byte.</summary>
    private short Read24()
    {
        _reader.ReadByte();
        return _reader.ReadInt16();
    }

    public void Rewind()
    {
        _reader.BaseStream.Seek(_dataStart, SeekOrigin.Begin);
        _position = 0;
    }

    public void Dispose() => _reader.Dispose();
}

public sealed class VorbisDecoder(string path) : IAudioDecoder
{
    private readonly VorbisReader _reader = new(path);
    private float[] _floats = [];

    public int Channels => _reader.Channels;
    public int SampleRate => _reader.SampleRate;

    public int Read(short[] buffer)
    {
        if (_floats.Length < buffer.Length) _floats = new float[buffer.Length];
        var read = _reader.ReadSamples(_floats, 0, buffer.Length);
        for (var i = 0; i < read; i++) buffer[i] = AudioDecoders.ToPcm16(_floats[i]);
        return read;
    }

    public void Rewind() => _reader.SamplePosition = 0;
    public void Dispose() => _reader.Dispose();
}

public sealed class Mp3Decoder(string path) : IAudioDecoder
{
    private readonly MpegFile _file = new(path);
    private float[] _floats = [];

    public int Channels => _file.Channels;
    public int SampleRate => _file.SampleRate;

    public int Read(short[] buffer)
    {
        if (_floats.Length < buffer.Length) _floats = new float[buffer.Length];
        var read = _file.ReadSamples(_floats, 0, buffer.Length);
        for (var i = 0; i < read; i++) buffer[i] = AudioDecoders.ToPcm16(_floats[i]);
        return read;
    }

    public void Rewind() => _file.Position = 0;
    public void Dispose() => _file.Dispose();
}
