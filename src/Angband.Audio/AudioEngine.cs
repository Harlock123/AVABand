namespace Angband.Audio;

/// <summary>Plays sound effects and music. Implementations must be safe to call from any thread.</summary>
public interface IAudioEngine : IDisposable
{
    /// <summary>False when no audio device could be opened (everything becomes a no-op).</summary>
    bool IsAvailable { get; }

    /// <summary>Master, effects and music volume, each 0..1.</summary>
    void SetVolumes(float master, float effects, float music);

    /// <summary>Plays a sound effect once. <paramref name="gain"/> scales the effects volume.</summary>
    void PlayEffect(string path, float gain = 1f);

    /// <summary>Starts a music track (fading out any current one); loops if asked.</summary>
    void PlayMusic(string path, bool loop = true);

    void StopMusic();

    /// <summary>The music file now playing, if any.</summary>
    string? CurrentMusic { get; }
}

/// <summary>Silent engine: used when audio is off, unavailable, or in tests.</summary>
public class NullAudioEngine : IAudioEngine
{
    public virtual bool IsAvailable => false;
    public string? CurrentMusic { get; protected set; }
    public virtual void SetVolumes(float master, float effects, float music) { }
    public virtual void PlayEffect(string path, float gain = 1f) { }
    public virtual void PlayMusic(string path, bool loop = true) => CurrentMusic = path;
    public virtual void StopMusic() => CurrentMusic = null;
    public void Dispose() => GC.SuppressFinalize(this);
}
