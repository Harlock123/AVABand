namespace Angband.Audio;

/// <summary>
/// Evens out how loud sounds are. Sound packs are recorded at very different levels — the Angband
/// effects sit 10-30 dB below the bundled music, and the music tracks differ by 30 dB among
/// themselves — so each effect is brought to <see cref="EffectTargetDb"/> when it loads, and each
/// music track is measured when it starts and settled at <see cref="MusicTargetDb"/>, a step below
/// the effects. Boosts are capped (quiet recordings would otherwise turn into hiss) and peaks are
/// kept under <see cref="PeakCeilingDb"/> so nothing clips.
/// </summary>
public static class Loudness
{
    /// <summary>Average (RMS) level effects are brought to, in dB below full scale.</summary>
    public const double EffectTargetDb = -20;
    /// <summary>Average level music is settled at: 10 dB under the effects, so they stand out.</summary>
    public const double MusicTargetDb = -30;
    /// <summary>Ambience loops sit a little under the music.</summary>
    public const double AmbienceTargetDb = -36;
    /// <summary>Most an effect is boosted (16x).</summary>
    public const double EffectMaxBoostDb = 24;
    /// <summary>Most a music track is boosted (10x); loud tracks may be turned down any amount.</summary>
    public const double MusicMaxBoostDb = 20;
    /// <summary>The loudest a peak may end up.</summary>
    public const double PeakCeilingDb = -1;

    /// <summary>Root-mean-square and peak of 16-bit samples, as fractions of full scale.</summary>
    public static (double Rms, double Peak) Measure(ReadOnlySpan<short> samples)
    {
        if (samples.IsEmpty) return (0, 0);
        double sum = 0;
        var peak = 0;
        foreach (var s in samples)
        {
            sum += (double)s * s;
            peak = Math.Max(peak, Math.Abs((int)s));
        }
        return (Math.Sqrt(sum / samples.Length) / 32768.0, peak / 32768.0);
    }

    /// <summary>
    /// The gain that brings audio measuring (<paramref name="rms"/>, <paramref name="peak"/>) to
    /// <paramref name="targetDb"/>, boosting by at most <paramref name="maxBoostDb"/> and keeping
    /// the peak under the ceiling. Silence is left alone.
    /// </summary>
    public static float Gain(double rms, double peak, double targetDb, double maxBoostDb)
    {
        if (rms <= 0 || peak <= 0) return 1f;
        var gain = Math.Pow(10, targetDb / 20) / rms;
        gain = Math.Min(gain, Math.Pow(10, maxBoostDb / 20));
        gain = Math.Min(gain, Math.Pow(10, PeakCeilingDb / 20) / peak);
        return (float)gain;
    }

    /// <summary>Scales samples in place, clamping to 16 bits.</summary>
    public static void Apply(Span<short> samples, float gain)
    {
        if (Math.Abs(gain - 1f) < 0.001f) return;
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (short)Math.Clamp(samples[i] * gain, short.MinValue, short.MaxValue);
    }

    public static double ToDb(double fraction) => fraction <= 0 ? double.NegativeInfinity : 20 * Math.Log10(fraction);
}
