using Angband.Core.Generation.Generators;
using Angband.Core.Geometry;

namespace Angband.Core.Game;

// AVABand's own: the town grows with you. Each story quest finished and each notice-board job paid
// earns the town renown, and at each threshold it improves: lamps light the square at night, the
// rubble is cleared and a well dug, market stalls come, then a fountain and lamps at every door, and
// last a statue of you (TownGrowth builds them, with no dice, from the stage). The shops stock more,
// and better made, from the second stage. The stage is reached on coming back up to town (and
// announced then, once), and kept in the save. A character without AVABand's quests, the daily
// dungeon, the weekly challenge and the tutorial keep the town as it always was; so does a replay
// recorded before the town could grow.
public sealed partial class GameSession
{
    /// <summary>The renown each stage needs: the quests and jobs done.</summary>
    public static readonly int[] TownStageRenown = [0, 1, 3, 6, 10, 15];

    /// <summary>What each stage brought, as the Knowledge screen and the announcement tell it.</summary>
    public static readonly string[] TownStageNews =
    [
        "The town as you found it: dark at night, and a little run down.",
        "Lamps now light the square at night.",
        "The rubble is cleared from the streets, a well is dug, and the shops stock a little more, and better made.",
        "Market stalls stand in the square, and the shops stock more again.",
        "A fountain plays at the heart of the square, a lamp burns by every shop door, and the shops are better stocked still.",
        "A statue of you stands in the square, and the shops keep the best stock the town has seen.",
    ];

    /// <summary>Whether the town can grow for this character (AVABand's quests, and not a seeded run or the tutorial).</summary>
    public bool TownGrowthOn => AvaQuestsOn && !Player.IsSeededRun && !IsTutorial && !TownGrowthHeld;

    /// <summary>A replay recorded before the town could grow plays it back without (ReplayPlayer sets it).</summary>
    internal bool TownGrowthHeld { get; set; }

    /// <summary>The town's renown: story quests finished (not failed) and notice-board jobs done.</summary>
    public int TownRenown => AvaQuests.Quests.Values.Count(q => q.IsDone && q.Stage != "lost") + AvaQuests.JobsDone;

    /// <summary>The stage the town has reached (0: as it always was), as built and kept in the save.</summary>
    public int TownStage => TownGrowthOn ? Math.Clamp(AvaQuests.TownStage, 0, TownGrowth.MaxStage) : 0;

    /// <summary>The stage this much renown earns.</summary>
    public static int TownStageFor(int renown)
    {
        var stage = 0;
        while (stage < TownGrowth.MaxStage && renown >= TownStageRenown[stage + 1]) stage++;
        return stage;
    }

    /// <summary>Extra piles each ordinary shop keeps at this stage (none until the second).</summary>
    public static int TownShopSlots(int stage) => Math.Max(0, stage - 1);

    /// <summary>Levels added to the most a shop's stock is made at, at this stage.</summary>
    public static int TownShopMagic(int stage) => 2 * TownShopSlots(stage);

    private List<string>? _townNews;

    /// <summary>Coming back up to town: the stage your renown has earned is reached (before the town is built).</summary>
    private void GrowTown()
    {
        if (!TownGrowthOn) return;
        var earned = TownStageFor(TownRenown);
        if (earned <= AvaQuests.TownStage) return;
        var news = TownStageNews.Skip(AvaQuests.TownStage + 1).Take(earned - AvaQuests.TownStage).ToList();
        AvaQuests.TownStage = earned;
        _townNews = news;
    }

    /// <summary>In town again: a new stage announced (once), or a word on how the town is doing.</summary>
    private void TownWelcome()
    {
        if (_townNews is { } news)
        {
            _townNews = null;
            // (A line for each stage, so none runs off the message bar.)
            Publish(new MessageEvent("The town has grown with your renown."));
            foreach (var line in news) Publish(new MessageEvent(line));
            return;
        }
        if (TownStage > 0) Publish(new MessageEvent(TownThriving[(int)(GameTurn / 10 % TownThriving.Length)]));
    }

    private static readonly string[] TownThriving =
    [
        "The town is busy: people nod to you in the street.",
        "Children run after you shouting your name. The town is thriving.",
        "A carter tips his hat to you. Trade is good in town these days.",
        "Someone has hung bunting over the street. The town is doing well.",
    ];

    /// <summary>What Butterbur adds, at the Prancing Pony, of how the town is doing.</summary>
    private string TownInnWord => TownStage switch
    {
        0 => "",
        1 or 2 => " \"The lamps in the square are your doing, they say. Folk walk out after dark again.\"",
        3 or 4 => " \"Market days again, and the room full every night. You've been good for business.\"",
        _ => " \"Seen the statue? A fair likeness, mostly. Your money's no good here.\"",
    };

    /// <summary>At night, the squares the town's lamps light (none at stage 0).</summary>
    private HashSet<Loc> TownLampLight() => TownStage > 0 ? TownGrowth.LampLit(Level) : [];

    /// <summary>Walking into one of the town's new things: a word about it. True if it was one.</summary>
    private bool TownBump(Loc at)
    {
        if (Player.Depth != 0) return false;
        var text = Level.FeatureAt(at).Id switch
        {
            TownGrowth.Statue => $"A statue of you, {Player.Name}, stands on a plinth here. The likeness is fair, mostly.",
            TownGrowth.Fountain => "You trail a hand in the fountain's cool water.",
            TownGrowth.Well => "You draw up a bucket of cold water and drink.",
            TownGrowth.Stall => "The stallholder holds out an apple. \"For you? On the house.\"",
            TownGrowth.Lamp => "A lamp post stands in the way.",
            _ => null,
        };
        if (text is null) return false;
        Publish(new MessageEvent(text));
        return true;
    }
}
