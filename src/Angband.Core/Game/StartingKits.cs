using Angband.Core.Definitions;

namespace Angband.Core.Game;

/// <summary>
/// One of AVABand's alternative starting kits (an unlock earned through feats): the class's own kit,
/// with its Word of Recall scroll traded for other goods worth no more — so the gold left over is the
/// same or a little more, and nothing about the game's balance changes.
/// </summary>
/// <param name="Id">What <see cref="CharacterSpec.Kit"/> names (and the unlock's key).</param>
/// <param name="Gives">What comes instead of the Word of Recall.</param>
public sealed record StartingKitDef(string Id, string Name, string Description, IReadOnlyList<StartItemDef> Gives);

/// <summary>
/// AVABand's alternative starting kits. A character made without one (<see cref="CharacterSpec.Kit"/>
/// null, as the daily dungeon's always is) gets the class's standard kit, as Angband does.
/// </summary>
public static class StartingKits
{
    /// <summary>The kit line every alternative trades away (a Word of Recall can be bought in town later).</summary>
    public const string TradedKind = "scroll_of_word_of_recall";

    /// <summary>Supplies for staying down: oil to throw and burn, food, and ways out of a tight spot.</summary>
    public static readonly StartingKitDef Delver = new("delver", "Delver's kit",
        "Trades the Word of Recall scroll for ten flasks of oil, five rations and five scrolls of Phase Door.",
        [Line("flask_of_oil", 10), Line("ration_of_food", 5), Line("phase_door", 5)]);

    /// <summary>Scrolls of lore: seeing the way, the unseen, and a blessing for the first fights.</summary>
    public static readonly StartingKitDef Scholar = new("scholar", "Scholar's kit",
        "Trades the Word of Recall scroll for scrolls of Magic Mapping, Detect Invisible, Blessing, Light and Remove Hunger.",
        [Line("magic_mapping"), Line("scroll_of_detect_invisible"), Line("scroll_of_blessing"), Line("light"), Line("scroll_of_remove_hunger")]);

    public static IReadOnlyList<StartingKitDef> All { get; } = [Delver, Scholar];

    public static StartingKitDef? Find(string? id) => id is null ? null : All.FirstOrDefault(k => k.Id == id);

    private static StartItemDef Line(string kind, int count = 1) => new() { Kind = kind, Count = count };

    /// <summary>
    /// The kit a class starts with: its own, or with an alternative the Word of Recall line swapped
    /// for the alternative's goods (each left out when the Word of Recall would be, by the same birth
    /// option). A class whose kit has no Word of Recall keeps its own.
    /// </summary>
    public static IReadOnlyList<StartItemDef> For(ClassDef cls, string? kitId)
    {
        if (Find(kitId) is not { } kit || cls.StartingKit.FirstOrDefault(k => k.Kind == TradedKind) is not { } traded) return cls.StartingKit;
        var lines = new List<StartItemDef>();
        foreach (var line in cls.StartingKit)
        {
            if (line != traded)
            {
                lines.Add(line);
                continue;
            }
            lines.AddRange(kit.Gives.Select(g => new StartItemDef { Kind = g.Kind, Count = g.Count, CountMax = g.CountMax, Equip = g.Equip, UnlessOption = traded.UnlessOption }));
        }
        return lines;
    }
}
