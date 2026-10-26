namespace Angband.Core.Game;

/// <summary>
/// One of Play now's characters: a race and class that suit a first game, the stats worth buying
/// for it (most wanted first), and a few words on how it plays.
/// </summary>
public sealed record PlayNowCombo(string RaceId, string ClassId, IReadOnlyList<string> Stats, string Blurb);

/// <summary>
/// Play now (AVABand's own): a one-click way into the game for new players. It picks one of a few
/// friendly race and class pairs, buys its stats as the creation screen would, and starts in the
/// town with the Classic options, no challenges and the standard kit.
/// </summary>
public static class PlayNow
{
    /// <summary>The pairs Play now chooses among: forgiving to play, and each a different way to play.</summary>
    public static IReadOnlyList<PlayNowCombo> Combos { get; } =
    [
        new("human", "warrior", ["str", "con", "dex"], "a straightforward fighter: walk up to things and hit them"),
        new("half_troll", "warrior", ["str", "con", "dex"], "huge, tough and quick to heal, if not very bright"),
        new("dwarf", "priest", ["wis", "con", "str"], "sturdy, blessed and never blinded, with healing prayers to fall back on"),
        new("hobbit", "rogue", ["dex", "int", "con"], "quick fingers, a good sling arm and a knack for staying unseen"),
        new("high_elf", "mage", ["int", "con", "dex"], "a gifted spellcaster who sees the invisible; keep your distance"),
        new("gnome", "mage", ["int", "con", "dex"], "a clever caster who can never be paralysed; keep your distance"),
        new("dunadan", "paladin", ["str", "con", "wis"], "a hardy holy warrior, strong in every way"),
        new("elf", "ranger", ["dex", "con", "str"], "a deadly shot with a bow, best fighting from afar"),
    ];

    /// <summary>
    /// Spends what is left of a point-buy budget on <paramref name="priority"/>: the first stat up to
    /// 16, then a point at a time round the rest, as far as the points go. Unspent points (if any)
    /// become gold at the start, as on the creation screen.
    /// </summary>
    public static Dictionary<string, int> SpendPoints(IReadOnlyDictionary<string, int> stats, IReadOnlyList<string> priority, int budget)
    {
        var bought = new Dictionary<string, int>(stats);
        bool Raise(string stat)
        {
            if (bought[stat] >= Birth.PointBuyMax) return false;
            bought[stat]++;
            if (Birth.PointsSpent(bought) <= budget) return true;
            bought[stat]--;
            return false;
        }

        while (bought[priority[0]] < 16 && Raise(priority[0])) { }
        var raised = true;
        while (raised)
        {
            raised = false;
            foreach (var stat in priority.Skip(1)) raised |= Raise(stat);
        }
        return bought;
    }

    /// <summary>"a" or "an" before a word ("an Elf Ranger", "a Hobbit Rogue").</summary>
    public static string Article(string word) => word.Length > 0 && "AEIOUaeiou".Contains(word[0]) ? "an" : "a";
}
