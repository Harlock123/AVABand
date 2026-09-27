using Angband.Core.Generation;
using Angband.Core.World;

namespace Angband.Core.Game;

// Level feelings (Angband 4.2): the danger is sensed on arrival; the treasure once a tenth of the
// level's hidden feeling squares have come into view. Ctrl+F repeats them. Off with birth_feelings.
public sealed partial class GameSession
{
    // Set while arriving on a level, so the treasure feeling isn't announced twice.
    private bool _arriving;

    private bool FeelingsOn => Options[OptionIds.Feelings];

    /// <summary>Rates a freshly generated level and hides its feeling squares (Angband generate.c).</summary>
    private void PrepareFeeling()
    {
        Level.Feeling = 0;
        Level.FeelingSquaresSeen = 0;
        Level.FeelSquares.Clear();
        if (Level.Depth == 0) return;

        var floorItems = Level.Objects.All.Select(o => o.Item).Where(i => !i.IsGold).ToList();
        var monsterRating = LevelFeelings.MonsterRating(Level.Monsters.All.Select(m => m.Race), Level.Depth);
        var objectRating = LevelFeelings.ObjectRating(floorItems, Data);
        var artifact = floorItems.Any(i => i.IsArtifact);
        Level.Feeling = LevelFeelings.ObjectFeeling(objectRating, Level.Depth, artifact, Options[OptionIds.LoseArtifacts])
                        + LevelFeelings.MonsterFeeling(monsterRating, Level.Depth);

        // Angband place_feeling: up to 100 random passable squares, 500 tries each.
        for (var i = 0; i < LevelFeelings.FeelingTotal; i++)
            for (var tries = 0; tries < 500; tries++)
            {
                var p = new Geometry.Loc(Rng.RandInt0(Level.Width), Rng.RandInt0(Level.Height));
                if (!Level.IsPassable(p) || !Level.FeelSquares.Add(p)) continue;
                break;
            }
    }

    /// <summary>Counts newly seen feeling squares; the tenth brings the treasure feeling (Angband update_one).</summary>
    private void NoticeFeelingSquare(Geometry.Loc p)
    {
        if (!Level.FeelSquares.Remove(p)) return;
        Level.FeelingSquaresSeen++;
        if (Level.FeelingSquaresSeen != LevelFeelings.FeelingNeed || _arriving || !FeelingsOn) return;
        Disturb(); // Angband disturbs
        Publish(new MessageEvent($"You feel that {LevelFeelings.ObjectTexts[Math.Min(Level.Feeling / 10, LevelFeelings.ObjectTexts.Length - 1)]}"));
    }

    /// <summary>Angband display_feeling (on arrival, and Ctrl+F): nothing with birth_feelings off.</summary>
    public bool ShowFeeling()
    {
        if (!FeelingsOn) return false;
        Publish(new MessageEvent(LevelFeelings.Describe(Level)));
        return true;
    }

    /// <summary>The status bar's "LF:5-?" (null in town or with feelings off).</summary>
    public string? FeelingStatus => FeelingsOn ? LevelFeelings.Status(Level) : null;
}
