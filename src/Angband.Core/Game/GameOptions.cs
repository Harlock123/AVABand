namespace Angband.Core.Game;

/// <summary>Kinds of option (Angband list-options.h).</summary>
public enum OptionKind
{
    /// <summary>User interface: change any time; kept in the player's settings, not the save.</summary>
    Interface,
    /// <summary>Birth: chosen when the character is created, fixed afterwards, kept in the save.</summary>
    Birth,
    /// <summary>Cheat: change any time, but using one marks the character, who then can't enter the score table.</summary>
    Cheat,
}

/// <summary>An option: its Angband name, description, kind and default.</summary>
public sealed record OptionDef(string Id, string Description, OptionKind Kind, bool Default);

/// <summary>Option names, as in Angband.</summary>
public static class OptionIds
{
    // Interface options the engine uses (the display-only ones live in the UI).
    public const string ShowDamage = "show_damage";
    public const string UseOldTarget = "use_old_target";
    public const string PickupAlways = "pickup_always";
    public const string PickupInven = "pickup_inven";
    public const string ShowFlavors = "show_flavors";
    public const string DisturbNear = "disturb_near";
    public const string NotifyRecharge = "notify_recharge";
    /// <summary>AVABand's own: the damage monsters and effects deal you, on their messages.</summary>
    public const string ShowDamageTaken = "show_damage_taken";
    /// <summary>AVABand's own: open diagonal-only squeezes in new levels (for keyboards without a keypad).</summary>
    public const string NoDiagonalSqueezes = "gen_no_diagonal_squeezes";

    // Birth options.
    public const string Randarts = "birth_randarts";
    public const string ConnectStairs = "birth_connect_stairs";
    public const string ForceDescend = "birth_force_descend";
    public const string NoRecall = "birth_no_recall";
    public const string NoArtifacts = "birth_no_artifacts";
    public const string LoseArtifacts = "birth_lose_arts";
    public const string Stacking = "birth_stacking";
    public const string NoSelling = "birth_no_selling";
    public const string StartKit = "birth_start_kit";
    public const string AiLearn = "birth_ai_learn";
    public const string PercentDamage = "birth_percent_damage";
    public const string LevelsPersist = "birth_levels_persist";
    public const string KnowRunes = "birth_know_runes";
    public const string KnowFlavors = "birth_know_flavors";
    public const string Feelings = "birth_feelings";
    /// <summary>AVABand's own: its quests — the Prancing Pony, its notice board, and the quests below.</summary>
    public const string AvaQuests = "birth_ava_quests";
    /// <summary>AVABand's own: each race's extra ability (ava_races.json).</summary>
    public const string AvaRaces = "birth_ava_races";
    /// <summary>AVABand's own: Strength counts for more weight, Constitution a little, worn gear 75%, and the burden is named.</summary>
    public const string AvaBurden = "birth_ava_burden";

    // Cheat options.
    public const string CheatHear = "cheat_hear";
    public const string CheatRoom = "cheat_room";
    public const string CheatLive = "cheat_live";
}

/// <summary>The options AVABand supports, in Angband's order.</summary>
public static class OptionCatalog
{
    public static readonly IReadOnlyList<OptionDef> All =
    [
        // (AVABand shows damage by default; Angband's show_damage starts off.)
        new(OptionIds.ShowDamage, "Show damage player deals to monsters", OptionKind.Interface, true),
        new(OptionIds.ShowDamageTaken, "Show damage monsters and effects deal to you", OptionKind.Interface, true),
        new(OptionIds.UseOldTarget, "Use old target by default", OptionKind.Interface, true),
        new(OptionIds.PickupAlways, "Always pickup items", OptionKind.Interface, false),
        new(OptionIds.PickupInven, "Always pickup items matching inventory", OptionKind.Interface, true),
        new(OptionIds.ShowFlavors, "Show flavors in object descriptions", OptionKind.Interface, false),
        new(OptionIds.DisturbNear, "Disturb whenever viewable monster moves", OptionKind.Interface, true),
        new(OptionIds.NotifyRecharge, "Notify on object recharge", OptionKind.Interface, false),
        new(OptionIds.NoDiagonalSqueezes, "New levels never need a diagonal step to get anywhere", OptionKind.Interface, false),

        new(OptionIds.Randarts, "Generate a new, random artifact set", OptionKind.Birth, false),
        new(OptionIds.ConnectStairs, "Generate connected stairs", OptionKind.Birth, true),
        new(OptionIds.ForceDescend, "Force player descent (never make up stairs)", OptionKind.Birth, false),
        new(OptionIds.NoRecall, "Word of Recall has no effect", OptionKind.Birth, false),
        new(OptionIds.NoArtifacts, "Restrict creation of artifacts", OptionKind.Birth, false),
        new(OptionIds.Stacking, "Stack objects on the floor", OptionKind.Birth, true),
        new(OptionIds.LoseArtifacts, "Lose artifacts when leaving level", OptionKind.Birth, false),
        new(OptionIds.Feelings, "Show level feelings", OptionKind.Birth, true),
        new(OptionIds.NoSelling, "Increase gold drops but disable selling", OptionKind.Birth, true),
        new(OptionIds.StartKit, "Start with a kit of useful gear", OptionKind.Birth, true),
        new(OptionIds.AiLearn, "Monsters learn from their mistakes", OptionKind.Birth, true),
        new(OptionIds.LevelsPersist, "Persistent levels (experimental)", OptionKind.Birth, false),
        new(OptionIds.PercentDamage, "To-damage is a percentage of dice (experimental)", OptionKind.Birth, false),
        new(OptionIds.KnowRunes, "Know all runes on birth", OptionKind.Birth, false),
        new(OptionIds.KnowFlavors, "Know all flavors on birth", OptionKind.Birth, false),
        new(OptionIds.AvaQuests, "AVABand's quests (the Prancing Pony and its notice board)", OptionKind.Birth, true),
        new(OptionIds.AvaRaces, "AVABand's racial abilities (a Dwarf's delving, a Human's two weapons...)", OptionKind.Birth, true),
        new(OptionIds.AvaBurden, "AVABand's encumbrance (more from Strength and Constitution; worn gear weighs less)", OptionKind.Birth, true),

        new(OptionIds.CheatHear, "Cheat: Peek into monster creation", OptionKind.Cheat, false),
        new(OptionIds.CheatRoom, "Cheat: Peek into dungeon creation", OptionKind.Cheat, false),
        new(OptionIds.CheatLive, "Cheat: Allow player to avoid death", OptionKind.Cheat, false),
    ];

    public static OptionDef? Find(string id) => All.FirstOrDefault(o => o.Id == id);

    public static IEnumerable<OptionDef> OfKind(OptionKind kind) => All.Where(o => o.Kind == kind);
}

/// <summary>
/// A game's option values. Unknown names are ignored, so saves and settings from other versions
/// still load; anything unset has its default.
/// </summary>
public sealed class GameOptions
{
    private readonly Dictionary<string, bool> _values = new(StringComparer.Ordinal);

    public GameOptions() { }

    public GameOptions(IEnumerable<KeyValuePair<string, bool>> values)
    {
        foreach (var (id, value) in values) Set(id, value);
    }

    public bool this[string id]
    {
        get => _values.TryGetValue(id, out var v) ? v : OptionCatalog.Find(id)?.Default ?? false;
        set => Set(id, value);
    }

    /// <summary>Whether a value was given (rather than left at its default).</summary>
    public bool IsSet(string id) => _values.ContainsKey(id);

    public void Set(string id, bool value)
    {
        if (OptionCatalog.Find(id) is not null) _values[id] = value;
    }

    /// <summary>Every option's current value.</summary>
    public IReadOnlyDictionary<string, bool> Values => OptionCatalog.All.ToDictionary(o => o.Id, o => this[o.Id]);

    /// <summary>The values of one kind of option.</summary>
    public Dictionary<string, bool> OfKind(OptionKind kind) => OptionCatalog.OfKind(kind).ToDictionary(o => o.Id, o => this[o.Id]);

    /// <summary>Copies the values of one kind of option from another set.</summary>
    public void CopyKind(GameOptions from, OptionKind kind)
    {
        foreach (var o in OptionCatalog.OfKind(kind)) _values[o.Id] = from[o.Id];
    }
}
