using Angband.Core.Definitions;

namespace Angband.Core.Effects;

/// <summary>Timed effect ids the engine gives behaviour to. Definitions (messages, limits) come from data.</summary>
public static class TimedIds
{
    public const string Poisoned = "poisoned";
    public const string Cut = "cut";
    public const string Stun = "stun";
    public const string Confused = "confused";
    /// <summary>Hallucination (Angband TMD_IMAGE).</summary>
    public const string Image = "image";
    public const string Afraid = "afraid";
    public const string Paralyzed = "paralyzed";
    public const string Blind = "blind";
    public const string Slow = "slow";
    public const string Fast = "fast";
    public const string Blessed = "blessed";
    public const string Hero = "hero";
    public const string Regen = "regen";
}

/// <summary>
/// Countdown status effects (poison, confusion, stun...). Each change reports the message to show,
/// taken from the effect's definition. Values are kept in id order so iteration is deterministic.
/// </summary>
public sealed class TimedEffects
{
    private readonly SortedDictionary<string, int> _values = new(StringComparer.Ordinal);

    public int this[string id] => _values.GetValueOrDefault(id);

    public bool Has(string id) => _values.GetValueOrDefault(id) > 0;

    public IEnumerable<KeyValuePair<string, int>> Active => _values.Where(kv => kv.Value > 0);

    /// <summary>Adds <paramref name="amount"/> following the effect's stacking rule. Returns the message, if any.</summary>
    public string? Increase(TimedEffectDef def, int amount)
    {
        if (amount <= 0) return null;
        var current = this[def.Id];
        int next;
        if (current > 0)
        {
            next = def.Stacking switch
            {
                TimedStacking.NoStack => current,
                TimedStacking.Max => Math.Max(current, amount),
                _ => current + amount,
            };
        }
        else next = amount;

        next = Math.Min(next, def.Max);
        if (next == current) return null;
        _values[def.Id] = next;
        return current == 0 ? Nonempty(def.OnBegin) : Nonempty(def.OnIncrease);
    }

    /// <summary>Sets an exact value. Returns the begin/end message if the effect started or stopped.</summary>
    public string? Set(TimedEffectDef def, int value)
    {
        var current = this[def.Id];
        value = Math.Clamp(value, 0, def.Max);
        _values[def.Id] = value;
        if (current == 0 && value > 0) return Nonempty(def.OnBegin);
        if (current > 0 && value == 0) return Nonempty(def.OnEnd);
        return null;
    }

    /// <summary>Reduces an effect; returns the end message when it wears off.</summary>
    public string? Decrease(TimedEffectDef def, int amount)
    {
        var current = this[def.Id];
        return current <= 0 ? null : Set(def, Math.Max(0, current - amount));
    }

    public void Clear() => _values.Clear();

    /// <summary>Raw values for save games.</summary>
    public IReadOnlyDictionary<string, int> Snapshot() => new Dictionary<string, int>(_values.Where(kv => kv.Value > 0));

    private static string? Nonempty(string s) => string.IsNullOrEmpty(s) ? null : s;
}
