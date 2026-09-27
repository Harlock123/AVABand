using System.Numerics;

namespace Angband.Core.Randomness;

/// <summary>Serializable snapshot of a <see cref="GameRandom"/> stream.</summary>
public readonly record struct RandomState(ulong S0, ulong S1, ulong S2, ulong S3);

/// <summary>
/// Deterministic xoshiro256** generator. Unlike <see cref="System.Random"/> its algorithm is fixed
/// and its state is exposed, so a seed (or a saved state) reproduces the exact same game on any
/// platform. All helpers are integer-only for the same reason.
/// </summary>
public sealed class GameRandom
{
    private ulong _s0, _s1, _s2, _s3;

    public GameRandom(ulong seed) => Reseed(seed);

    public GameRandom(RandomState state) => State = state;

    public RandomState State
    {
        get => new(_s0, _s1, _s2, _s3);
        set
        {
            if ((value.S0 | value.S1 | value.S2 | value.S3) == 0)
                throw new ArgumentException("xoshiro state must not be all zero.", nameof(value));
            (_s0, _s1, _s2, _s3) = (value.S0, value.S1, value.S2, value.S3);
        }
    }

    public void Reseed(ulong seed)
    {
        var sm = seed;
        _s0 = SplitMix64(ref sm);
        _s1 = SplitMix64(ref sm);
        _s2 = SplitMix64(ref sm);
        _s3 = SplitMix64(ref sm);
    }

    /// <summary>SplitMix64 step; also handy for deriving independent sub-seeds.</summary>
    public static ulong SplitMix64(ref ulong x)
    {
        ulong z = x += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Mixes several values into one well-distributed seed.</summary>
    public static ulong DeriveSeed(ulong seed, params long[] salts)
    {
        var x = seed;
        var result = SplitMix64(ref x);
        foreach (var salt in salts)
        {
            x ^= (ulong)salt;
            result ^= SplitMix64(ref x);
        }
        return result;
    }

    public ulong NextULong()
    {
        var result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);
        return result;
    }

    /// <summary>Creates an independent generator seeded from this stream.</summary>
    public GameRandom Fork() => new(NextULong());

    /// <summary>Uniform integer in [0, n). Returns 0 when n &lt;= 1. (Angband: randint0)</summary>
    public int RandInt0(int n)
    {
        if (n <= 1) return 0;
        var bound = (ulong)n;
        var threshold = (0UL - bound) % bound; // rejection zone for an unbiased result
        while (true)
        {
            var r = NextULong();
            if (r >= threshold) return (int)(r % bound);
        }
    }

    /// <summary>Uniform integer in [1, n]. (Angband: randint1)</summary>
    public int RandInt1(int n) => n <= 1 ? 1 : RandInt0(n) + 1;

    /// <summary>Uniform integer in [min, max] inclusive. (Angband: rand_range)</summary>
    public int RandRange(int min, int max) => max <= min ? min : min + RandInt0(max - min + 1);

    /// <summary>A value in [a - d, a + d]. (Angband: rand_spread)</summary>
    public int Spread(int a, int d) => a + RandInt0(1 + d + d) - d;

    public bool OneIn(int n) => n <= 1 || RandInt0(n) == 0;

    /// <summary>True with the given percentage probability.</summary>
    public bool Percent(int chance) => RandInt0(100) < chance;

    /// <summary>Sum of <paramref name="count"/> rolls of a <paramref name="sides"/>-sided die.</summary>
    public int Damroll(int count, int sides)
    {
        if (sides <= 0) return 0;
        var total = 0;
        for (var i = 0; i < count; i++) total += RandInt1(sides);
        return total;
    }

    /// <summary>
    /// Approximately normal value (Irwin–Hall sum of 12 uniforms) using integer math only, so
    /// it is bit-identical across CPUs. (Angband: Rand_normal)
    /// </summary>
    public int Normal(int mean, int stdDev)
    {
        if (stdDev <= 0) return mean;
        long sum = 0;
        for (var i = 0; i < 12; i++) sum += RandInt0(10_000);
        sum -= 60_000; // mean 0, std-dev 10_000
        return mean + (int)Math.Round(sum * stdDev / 10_000.0, MidpointRounding.AwayFromZero);
    }

    public T Pick<T>(IReadOnlyList<T> items)
    {
        if (items.Count == 0) throw new ArgumentException("Cannot pick from an empty list.", nameof(items));
        return items[RandInt0(items.Count)];
    }

    /// <summary>Picks an item with probability proportional to its weight; default when all weights are 0.</summary>
    public T? PickWeighted<T>(IReadOnlyList<T> items, Func<T, int> weight)
    {
        var total = 0;
        foreach (var item in items) total += Math.Max(0, weight(item));
        if (total <= 0) return default;
        var roll = RandInt0(total);
        foreach (var item in items)
        {
            roll -= Math.Max(0, weight(item));
            if (roll < 0) return item;
        }
        return default; // unreachable
    }

    public void Shuffle<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = RandInt0(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
