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

    /// <summary>
    /// Raised (on the audio thread) when a track started without looping has played to its end, so
    /// the next can be chosen.
    /// </summary>
    event Action? MusicEnded;

    /// <summary>Starts an ambience loop under the music (fading from any current one).</summary>
    void PlayAmbience(string path) { }

    void StopAmbience() { }

    /// <summary>The ambience loop now playing, if any.</summary>
    string? CurrentAmbience => null;

    /// <summary>The ambience volume, 0..1 (scaled by the master volume).</summary>
    void SetAmbienceVolume(float volume) { }
}

/// <summary>Silent engine: used when audio is off, unavailable, or in tests.</summary>
public class NullAudioEngine(string? reason = null) : IAudioEngine
{
    public virtual bool IsAvailable => false;

    /// <summary>Why the game is silent, in words for the player (null: audio was simply turned off).</summary>
    public string? Reason { get; } = reason;
    public string? CurrentMusic { get; protected set; }
    public event Action? MusicEnded;
    public virtual void SetVolumes(float master, float effects, float music) { }
    public virtual void PlayEffect(string path, float gain = 1f) { }
    public virtual void PlayMusic(string path, bool loop = true) => CurrentMusic = path;
    public virtual void StopMusic() => CurrentMusic = null;
    public string? CurrentAmbience { get; protected set; }
    public virtual void PlayAmbience(string path) => CurrentAmbience = path;
    public virtual void StopAmbience() => CurrentAmbience = null;
    public virtual void SetAmbienceVolume(float volume) { }

    /// <summary>Ends the current track as if it had played out (for tests).</summary>
    public void FinishMusic()
    {
        CurrentMusic = null;
        MusicEnded?.Invoke();
    }
    public void Dispose() => GC.SuppressFinalize(this);
}
