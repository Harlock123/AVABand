using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>Danger at a glance (AVABand's own): the monster list's tags and the first-sight warning.</summary>
public class DangerTests
{
    private static (GameSession Game, List<string> Said) AtDepth(int depth)
    {
        var game = GameSession.NewGame(TestData.Game, 4, "warrior");
        game.MarkDebugUsed();
        game.Execute(new DebugJumpCommand(depth));
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        return (game, said);
    }

    private static Loc Beside(GameSession game, int n = 0) =>
        game.Level.AllLocs().Where(p => game.Level.IsEmptyFloor(p) && p.ChebyshevTo(game.Player.Position) is >= 1 and <= 2)
            .OrderBy(p => p.Y).ThenBy(p => p.X).Skip(n).First();

    [Fact]
    public void A_far_out_of_depth_kind_is_tagged_and_warned_of_once()
    {
        var (game, said) = AtDepth(2);
        var deep = game.Data.Monsters.First(r => !r.IsUnique && r.Depth >= 2 + GameSession.FarOutOfDepth && r.Depth < 30 && !r.Has("NEVER_MOVE"));
        Arena.AddMonster(game, deep.Id, Beside(game), awake: false);
        Arena.AddMonster(game, deep.Id, Beside(game, 1), awake: false);
        game.UpdateView();
        Assert.Single(said, s => s.StartsWith("Beware:", StringComparison.Ordinal) && s.EndsWith("is far deeper than it belongs.", StringComparison.Ordinal));
        var row = game.MonsterList().Single(r => r.TileKey == "monster:" + deep.Id);
        Assert.EndsWith("— far out of depth", row.Text);
        Assert.Equal("Red", row.Color);
    }

    [Fact]
    public void A_kind_you_know_could_kill_you_is_tagged_red_and_warned_of()
    {
        var (game, said) = AtDepth(1);
        var orc = game.Data.Monster("cave_orc")!;
        game.Lore.For(orc.Id).TotalKills = 20;                                 // (its blows' damage known)
        game.Player.Hp = game.Player.MaxHp = 3;
        Arena.AddMonster(game, orc.Id, Beside(game), awake: false);
        game.UpdateView();
        Assert.Contains(said, s => s.StartsWith("Beware:", StringComparison.Ordinal) && s.Contains("could kill you"));
        var row = game.MonsterList().Single(r => r.TileKey == "monster:cave_orc");
        Assert.EndsWith("— could kill you", row.Text);
        Assert.Equal("Red", row.Color);

        // Strong enough, and it's no great threat: no tag.
        game.Player.Hp = game.Player.MaxHp = 10_000;
        Assert.DoesNotContain("—", game.MonsterList().Single(r => r.TileKey == "monster:cave_orc").Text);
    }
}
