using Angband.Core.Randomness;

namespace Angband.Tests;

public class GameRandomTests
{
    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var a = new GameRandom(12345);
        var b = new GameRandom(12345);
        for (var i = 0; i < 1000; i++) Assert.Equal(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void DifferentSeeds_Diverge()
    {
        var a = new GameRandom(1);
        var b = new GameRandom(2);
        Assert.NotEqual(Enumerable.Range(0, 8).Select(_ => a.NextULong()), Enumerable.Range(0, 8).Select(_ => b.NextULong()));
    }

    [Fact]
    public void KnownSeed_MatchesReferenceXoshiro()
    {
        // Values from an independent reference implementation of SplitMix64-seeded xoshiro256**.
        // A change here means saved games and recorded replays would no longer reproduce.
        var rng = new GameRandom(42);
        Assert.Equal([558742, 543102, 559009], Enumerable.Range(0, 3).Select(_ => rng.RandInt0(1_000_000)));
    }

    [Fact]
    public void StateRoundTrip_ResumesStream()
    {
        var rng = new GameRandom(99);
        for (var i = 0; i < 50; i++) rng.NextULong();
        var saved = rng.State;
        var expected = Enumerable.Range(0, 20).Select(_ => rng.NextULong()).ToArray();

        var restored = new GameRandom(saved);
        Assert.Equal(expected, Enumerable.Range(0, 20).Select(_ => restored.NextULong()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(100)]
    public void RandInt0_StaysInRange_AndHitsEveryValue(int n)
    {
        var rng = new GameRandom(7);
        var seen = new bool[n];
        for (var i = 0; i < n * 200; i++)
        {
            var v = rng.RandInt0(n);
            Assert.InRange(v, 0, n - 1);
            seen[v] = true;
        }
        Assert.All(seen, Assert.True);
    }

    [Fact]
    public void RandInt0_IsRoughlyUniform()
    {
        var rng = new GameRandom(2024);
        var counts = new int[10];
        const int samples = 100_000;
        for (var i = 0; i < samples; i++) counts[rng.RandInt0(10)]++;
        Assert.All(counts, c => Assert.InRange(c, samples / 10 * 0.95, samples / 10 * 1.05));
    }

    [Fact]
    public void RandRange_IsInclusive()
    {
        var rng = new GameRandom(3);
        var values = Enumerable.Range(0, 2000).Select(_ => rng.RandRange(-2, 2)).ToHashSet();
        Assert.Equal([-2, -1, 0, 1, 2], values.Order());
    }

    [Fact]
    public void Damroll_StaysWithinDiceBounds()
    {
        var rng = new GameRandom(5);
        for (var i = 0; i < 5000; i++) Assert.InRange(rng.Damroll(3, 6), 3, 18);
        Assert.Equal(0, rng.Damroll(4, 0));
    }

    [Fact]
    public void Normal_HasExpectedMeanAndSpread()
    {
        var rng = new GameRandom(11);
        var samples = Enumerable.Range(0, 20_000).Select(_ => rng.Normal(100, 10)).ToArray();
        var mean = samples.Average();
        var sd = Math.Sqrt(samples.Select(s => (s - mean) * (s - mean)).Average());
        Assert.InRange(mean, 99.5, 100.5);
        Assert.InRange(sd, 9.5, 10.5);
    }

    [Fact]
    public void PickWeighted_RespectsWeights()
    {
        var rng = new GameRandom(8);
        var items = new[] { ("never", 0), ("rare", 1), ("common", 9) };
        var picks = Enumerable.Range(0, 10_000).Select(_ => rng.PickWeighted(items, i => i.Item2).Item1).ToList();
        Assert.DoesNotContain("never", picks);
        Assert.InRange(picks.Count(p => p == "rare"), 800, 1200);
    }

    [Fact]
    public void DeriveSeed_IsStableAndSaltSensitive()
    {
        Assert.Equal(GameRandom.DeriveSeed(1, 5, 6), GameRandom.DeriveSeed(1, 5, 6));
        Assert.NotEqual(GameRandom.DeriveSeed(1, 5, 6), GameRandom.DeriveSeed(1, 6, 5));
    }
}
