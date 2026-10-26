using System.Globalization;

namespace Angband.Core.Game;

/// <summary>
/// A keepsake a seasonal event's monsters may let fall: an ordinary object of an existing kind (<paramref name="Kind"/>)
/// under a themed name, with that kind's stats and nothing more. <paramref name="Name"/> uses <c>~</c> for its plural,
/// as the kinds' names do.
/// </summary>
public sealed record SeasonalCosmetic(string Id, string Kind, string Name, string Description);

/// <summary>
/// A seasonal event (AVABand's own): what it is called, the days it runs (by month and day, both ends
/// included; one that runs past the end of the year wraps round), and what it brings — the monsters that
/// join the dungeon (from ava_seasonal_monsters.json; <paramref name="Unique"/> among them), the keepsakes they
/// may let fall, what the town says when you arrive, and its feat.
/// </summary>
public sealed record SeasonDef(
    string Id,
    string Name,
    (int Month, int Day) Start,
    (int Month, int Day) End,
    IReadOnlyList<string> Monsters,
    string Unique,
    IReadOnlyList<SeasonalCosmetic> Cosmetics,
    string TownByDay,
    string TownByNight,
    string FeatName,
    string FeatDescription)
{
    /// <summary>The feat's id (<see cref="Records.Feats"/>).</summary>
    public string FeatId => "season_" + Id;

    /// <summary>Whether the event runs on this day.</summary>
    public bool Covers(DateOnly day)
    {
        var at = day.Month * 100 + day.Day;
        var from = Start.Month * 100 + Start.Day;
        var to = End.Month * 100 + End.Day;
        return from <= to ? at >= from && at <= to : at >= from || at <= to;
    }

    /// <summary>When it runs, in words: "24 October to 7 November".</summary>
    public string When => $"{Date(Start)} to {Date(End)}";

    private static string Date((int Month, int Day) d) =>
        $"{d.Day} {CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(d.Month)}";
}

/// <summary>
/// AVABand's seasonal events: a small calendar of dated events, each with a few monsters of its own
/// that join the dungeon at their depths, keepsakes they may let fall, a word from the town and a feat.
/// The event in force is fixed when a character is born (by the player's own date, unless Settings →
/// "Seasonal events" is off) and kept with the character for life — in its save, and so in its replay,
/// which plays back the same whatever the date it is watched. Never in the daily dungeon, the weekly
/// challenge or the tutorial. To add an event, add it here and its monsters to ava_seasonal_monsters.json.
/// </summary>
public static class Seasons
{
    public static readonly SeasonDef AllHallowsEve = new(
        "halloween", "All Hallows' Eve", (10, 24), (11, 7),
        ["pumpkin_headed_scarecrow", "belfry_bat", "corpse_candle", "the_headless_horseman"], "the_headless_horseman",
        [
            new("pumpkin_lantern", "lantern", "Carved Pumpkin Lantern~",
                "A pumpkin hollowed out and carved with a grinning face, with a lantern's lamp set inside. It gives a lantern's light and burns a lantern's oil; the grin is free."),
            new("sugar_skull", "ration_of_food", "Sugar Skull~",
                "A skull moulded from sugar and iced in bright colours, made for the night the dead walk. It fills you as a ration of food would."),
            new("witchs_hat", "hard_leather_cap", "Witch's Hat~",
                "A tall, pointed hat of black leather with a crooked brim. It guards your head as a hard leather cap does, and makes you look a great deal more sinister than you are."),
        ],
        "Pumpkins grin from every doorstep in the town, and the children are out in masks, begging sweets.",
        "Carved pumpkins flicker on every doorstep. Somewhere out on the road, hooves are drumming.",
        "Trick or Treat", "Slay the Headless Horseman, who rides at All Hallows' Eve.");

    public static readonly SeasonDef Midwinter = new(
        "midwinter", "Midwinter", (12, 18), (1, 6),
        ["frost_sprite", "winter_wolf", "rime_troll", "rimefang_lord_of_the_winter_pack"], "rimefang_lord_of_the_winter_pack",
        [
            new("holly_cloak", "cloak", "Holly-Wreath Cloak~",
                "A green cloak with a wreath of holly and red berries stitched about its collar. It keeps the cold off as any cloak does, and no better."),
            new("gingerbread", "hard_biscuit", "Gingerbread Biscuit~",
                "A biscuit of gingerbread cut in the shape of a little man, with currants for eyes. It is exactly as filling as a hard biscuit."),
            new("yule_candle", "wooden_torch", "Yule Candle~",
                "A fat red candle of the kind lit on the longest night to call the sun back. It burns like a wooden torch, and smells of cloves."),
        ],
        "Holly hangs over every door, the smell of roasting chestnuts drifts from the inn, and snow lies on the roofs.",
        "Candles burn in every window against the longest night. Far off in the hills, wolves are howling.",
        "Midwinter's Night", "Slay Rimefang, Lord of the Winter Pack, who comes down from the north at Midwinter.");

    /// <summary>Every event, in the order of the year.</summary>
    public static IReadOnlyList<SeasonDef> All { get; } = [AllHallowsEve, Midwinter];

    /// <summary>One time in this many, a seasonal monster (not its unique, who always does) lets fall one of its event's keepsakes.</summary>
    public const int CosmeticChance = 6;

    /// <summary>An event by its id (as a character keeps it), or null.</summary>
    public static SeasonDef? Find(string? id) => All.FirstOrDefault(s => s.Id == id);

    /// <summary>The event running on this day, or null.</summary>
    public static SeasonDef? At(DateOnly day) => All.FirstOrDefault(s => s.Covers(day));

    /// <summary>The event a monster race belongs to, or null for any other.</summary>
    public static SeasonDef? Of(string raceId) => All.FirstOrDefault(s => s.Monsters.Contains(raceId));

    /// <summary>A keepsake by its id (as an object keeps it), or null.</summary>
    public static SeasonalCosmetic? Cosmetic(string? id) =>
        id is null ? null : All.SelectMany(s => s.Cosmetics).FirstOrDefault(c => c.Id == id);
}
