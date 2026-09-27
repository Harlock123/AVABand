using Angband.Audio;

namespace Angband.Tests;

/// <summary>Loudness levelling: effects stand out over the music whatever level each was recorded at.</summary>
public class LoudnessTests
{
    private static short[] Tone(double amplitude, int n = 44100) =>
        [.. Enumerable.Range(0, n).Select(i => (short)(amplitude * 32767 * Math.Sin(i * 2 * Math.PI * 440 / 44100)))];

    [Fact]
    public void AQuietEffect_IsBroughtUpToTheTarget()
    {
        var tone = Tone(0.02); // about -37 dB average
        var (rms, peak) = Loudness.Measure(tone);
        Loudness.Apply(tone, Loudness.Gain(rms, peak, Loudness.EffectTargetDb, Loudness.EffectMaxBoostDb));
        Assert.InRange(Loudness.ToDb(Loudness.Measure(tone).Rms), -20.5, -19.5);
    }

    [Fact]
    public void BoostsAreCapped_SoFaintRecordingsDontTurnIntoHiss()
    {
        var (rms, peak) = Loudness.Measure(Tone(0.001)); // about -63 dB
        var gain = Loudness.Gain(rms, peak, Loudness.EffectTargetDb, Loudness.EffectMaxBoostDb);
        Assert.InRange(20 * Math.Log10(gain), Loudness.EffectMaxBoostDb - 0.01, Loudness.EffectMaxBoostDb + 0.01);
    }

    [Fact]
    public void PeaksStayUnderTheCeiling()
    {
        // A spiky sound: quiet on average but with one loud click.
        var spiky = Tone(0.01);
        spiky[100] = 30000;
        var (rms, peak) = Loudness.Measure(spiky);
        Loudness.Apply(spiky, Loudness.Gain(rms, peak, Loudness.EffectTargetDb, Loudness.EffectMaxBoostDb));
        Assert.True(Loudness.ToDb(Loudness.Measure(spiky).Peak) <= Loudness.PeakCeilingDb + 0.01);
    }

    [Fact]
    public void LoudMusic_IsTurnedDown_BelowTheEffects()
    {
        var music = Tone(0.3); // about -13 dB
        var (rms, peak) = Loudness.Measure(music);
        var gain = Loudness.Gain(rms, peak, Loudness.MusicTargetDb, Loudness.MusicMaxBoostDb);
        Assert.True(gain < 1);
        Loudness.Apply(music, gain);
        Assert.InRange(Loudness.ToDb(Loudness.Measure(music).Rms), -30.5, -29.5);
        Assert.True(Loudness.MusicTargetDb < Loudness.EffectTargetDb - 5);
    }

    [Fact]
    public void Silence_IsLeftAlone() => Assert.Equal(1f, Loudness.Gain(0, 0, Loudness.EffectTargetDb, Loudness.EffectMaxBoostDb));
}
