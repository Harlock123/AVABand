using Angband.Core.Definitions;
using Angband.Core.Items;
using Angband.Core.World;

namespace Angband.Core.Game;

/// <summary>
/// Angband 4.2's level feelings (generate.c calc_obj_feeling / calc_mon_feeling, cmd-cave.c
/// display_feeling): how dangerous a level is, known on arrival, and how rich, known once a tenth of
/// its hidden feeling squares have been seen.
/// </summary>
public static class LevelFeelings
{
    /// <summary>Angband z_info feeling-total and feeling-need (constants.txt).</summary>
    public const int FeelingTotal = 100, FeelingNeed = 10;

    public static readonly string[] ObjectTexts =
    [
        "Looks like any other level.",
        "you sense an item of wondrous power!",
        "there are superb treasures here.",
        "there are excellent treasures here.",
        "there are very good treasures here.",
        "there are good treasures here.",
        "there may be something worthwhile here.",
        "there may not be much interesting here.",
        "there aren't many treasures here.",
        "there are only scraps of junk here.",
        "there is naught but cobwebs here.",
    ];

    public static readonly string[] MonsterTexts =
    [
        "You are still uncertain about this place",
        "Omens of death haunt this place",
        "This place seems murderous",
        "This place seems terribly dangerous",
        "You feel anxious about this place",
        "You feel nervous about this place",
        "This place does not seem too risky",
        "This place seems reasonably safe",
        "This seems a tame, sheltered place",
        "This seems a quiet, peaceful place",
    ];

    /// <summary>
    /// Angband mon-make.c: each monster adds its level squared, and one out of depth adds that again
    /// for every level it is out of depth.
    /// </summary>
    public static long MonsterRating(IEnumerable<MonsterRaceDef> races, int depth) =>
        races.Sum(r => (long)r.Depth * r.Depth + (r.Depth > depth ? (long)(r.Depth - depth) * r.Depth * r.Depth : 0));

    /// <summary>Angband gen-util.c place_object: each object adds (value / 100)², its value capped at 2,500,000.</summary>
    public static long ObjectRating(IEnumerable<Item> items, GameData data) =>
        items.Sum(i =>
        {
            var value = Math.Clamp(ItemValue.Real(i, data, i.Number), -2_500_000, 2_500_000);
            return value / 100 * (value / 100);
        });

    /// <summary>Angband calc_mon_feeling: 1 (omens of death) to 9 (quiet, peaceful).</summary>
    public static int MonsterFeeling(long rating, int depth)
    {
        if (depth == 0) return 0;
        var x = rating / depth;
        return x switch { > 7000 => 1, > 4500 => 2, > 2500 => 3, > 1500 => 4, > 800 => 5, > 400 => 6, > 150 => 7, > 50 => 8, _ => 9 };
    }

    /// <summary>
    /// Angband calc_obj_feeling (×10): 10 for an artifact that leaving would lose, otherwise 20
    /// (superb) to 100 (cobwebs); any artifact makes it at least 60.
    /// </summary>
    public static int ObjectFeeling(long rating, int depth, bool artifact, bool loseArtifacts)
    {
        if (depth == 0) return 0;
        if (artifact && loseArtifacts) return 10;
        var x = rating / depth;
        if (artifact && x < 641) return 60;
        return x switch
        {
            > 160000 => 20, > 40000 => 30, > 10000 => 40, > 2500 => 50, > 640 => 60, > 160 => 70, > 40 => 80, > 10 => 90, _ => 100,
        };
    }

    /// <summary>Angband display_feeling: the danger, and the treasure once enough has been seen.</summary>
    public static string Describe(Level level)
    {
        if (level.Depth == 0) return "Looks like a typical town.";
        var obj = Math.Min(level.Feeling / 10, ObjectTexts.Length - 1);
        var mon = Math.Min(level.Feeling % 10, MonsterTexts.Length - 1);
        if (level.FeelingSquaresSeen < FeelingNeed) return MonsterTexts[mon] + ".";
        var join = mon <= 5 && obj > 6 || mon > 5 && obj <= 6 ? ", yet" : ", and";
        return $"{MonsterTexts[mon]}{join} {ObjectTexts[obj]}";
    }

    /// <summary>
    /// The status bar's "LF:5-3" (Angband prt_level_feeling): danger 1–9 (higher is worse), then
    /// treasure 1–9 (higher is better), '$' for a special find, '?' until known. Null in town.
    /// </summary>
    public static string? Status(Level level)
    {
        if (level.Depth == 0) return null;
        var obj = level.Feeling / 10;
        var mon = level.Feeling % 10;
        var danger = mon == 0 ? "?" : (10 - mon).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var treasure = level.FeelingSquaresSeen < FeelingNeed ? "?"
            : obj == 0 ? "*" : obj == 1 ? "$" : (11 - obj).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"LF:{danger}-{treasure}";
    }
}
