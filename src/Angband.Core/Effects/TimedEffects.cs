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
    /// <summary>Amnesia (Angband TMD_AMNESIA): no reading, spells and devices much harder.</summary>
    public const string Amnesia = "amnesia";
    public const string Afraid = "afraid";
    public const string Paralyzed = "paralyzed";
    public const string Blind = "blind";
    public const string Slow = "slow";
    public const string Fast = "fast";
    public const string Blessed = "blessed";
    public const string Hero = "hero";
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

    /// <summary>
    /// Adds <paramref name="amount"/> (Angband player_inc_timed): nothing while a non-stacking
    /// effect lasts. Returns the message, if any.
    /// </summary>
    public string? Increase(TimedEffectDef def, int amount, bool notify = true)
    {
        if (amount <= 0) return null;
        var current = this[def.Id];
        if (current > 0 && def.Stacking == TimedStacking.NoStack) return null;
        var next = current > 0 && def.Stacking == TimedStacking.Max ? Math.Max(current, amount) : current + amount;
        return Set(def, next, notify);
    }

    /// <summary>
    /// Sets a value (Angband player_set_timed): held between the lower bound and the top grade's
    /// most. Reaching a higher grade always says so, as does falling to one with a message for it;
    /// otherwise, when <paramref name="notify"/>, the end, decrease or increase message. Returns the
    /// message (with Angband's <c>{kind}</c>, <c>{s}</c>, <c>{is}</c> tags left for the caller to fill in).
    /// </summary>
    public string? Set(TimedEffectDef def, int value, bool notify = true)
    {
        var current = this[def.Id];
        value = Math.Max(Math.Max(value, def.LowerBound), 0);
        if (value == current) return null;
        var grades = def.AllGrades;
        var top = grades[^1].Max;
        if (value > top)
        {
            if (current == top) return null; // already as high as it goes
            value = top;
        }
        var newGrade = GradeIndex(grades, value);
        var oldGrade = GradeIndex(grades, current);
        _values[def.Id] = value;

        if (newGrade > oldGrade) return Nonempty(grades[newGrade - 1].Message);
        if (newGrade < oldGrade && newGrade > 0 && grades[newGrade - 1].DownMessage is { Length: > 0 } down) return down;
        if (!notify) return null;
        if (value == 0) return Nonempty(def.OnEnd);
        if (current > value) return Nonempty(def.OnDecrease);
        return Nonempty(def.OnIncrease);
    }

    /// <summary>
    /// Reduces an effect (Angband player_dec_timed): quietly unless <paramref name="notify"/>, but
    /// always saying when it ends.
    /// </summary>
    public string? Decrease(TimedEffectDef def, int amount, bool notify = false)
    {
        var current = this[def.Id];
        if (current <= 0) return null;
        var next = current - amount;
        return Set(def, next, next <= 0 || notify);
    }

    /// <summary>1 for the first grade, 2 for the second...; 0 for nothing.</summary>
    private static int GradeIndex(IReadOnlyList<TimedGradeDef> grades, int value)
    {
        if (value <= 0) return 0;
        for (var i = 0; i < grades.Count; i++)
            if (value <= grades[i].Max) return i + 1;
        return grades.Count;
    }

    public void Clear() => _values.Clear();

    /// <summary>Raw values for save games.</summary>
    public IReadOnlyDictionary<string, int> Snapshot() => new Dictionary<string, int>(_values.Where(kv => kv.Value > 0));

    private static string? Nonempty(string s) => string.IsNullOrEmpty(s) ? null : s;
}
