using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>
/// Angband 4.2's temporary protections, which act as the matching protection while they last:
/// free action (Grim Purpose), resistance to confusion (Grim Purpose, the Mushroom of Clear Mind)
/// and boldness (a Pint of Fine Wine).
/// </summary>
public class TimedProtectionTests
{
    private static GameSession Game(string cls = "warrior")
    {
        var arena = Arena.Create(7);
        var game = GameSession.NewGame(TestData.Game, 7, cls);
        game.UseLevel(arena.Level, arena.Player.Position);
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        game.Player.SkillSave = 0;
        return game;
    }

    private static void Cast(GameSession game, string spell)
    {
        var caster = Arena.AddMonster(game, "jackal", game.Player.Position + new Loc(2, 0));
        game.UpdateView();
        game.CastSpellForTest(caster, spell);
        game.Level.Monsters.Remove(caster);
    }

    private static void Consume(GameSession game, string kind) =>
        game.Execute(new UseCommand(game.Player.Inventory.Add(game.Objects.Create(kind))!));

    [Fact]
    public void FineWine_MakesYouBold_ImmuneToFear()
    {
        var game = Game();
        game.IncreaseTimed(TimedIds.Afraid, 20);
        Consume(game, "pint_of_fine_wine");
        Assert.InRange(game.Player.Timed["bold"], 80, 120);
        Assert.False(game.Player.Timed.Has(TimedIds.Afraid)); // it drives fear out, as heroism does
        Cast(game, "SCARE");
        Assert.False(game.Player.Timed.Has(TimedIds.Afraid));
    }

    [Fact]
    public void ClearMind_ResistsConfusion_ForAWhile()
    {
        var game = Game();
        Consume(game, "mushroom_of_clear_mind");
        Assert.InRange(game.Player.Timed["oppose_conf"], 57, 99); // 7d7+50
        Cast(game, "CONF");
        Assert.False(game.Player.Timed.Has(TimedIds.Confused));

        var unprotected = Game();
        Cast(unprotected, "CONF");
        Assert.True(unprotected.Player.Timed.Has(TimedIds.Confused));
    }

    [Fact]
    public void GrimPurpose_GrantsFreeAction_AndResistsConfusion()
    {
        Assert.Equal("timed:oppose_conf:12+1d12; timed:free_act:12+1d12", TestData.Game.Spell("grim_purpose")!.Effect);
        var game = Game();
        game.IncreaseTimed("free_act", 20);
        Cast(game, "HOLD");
        Assert.False(game.Player.Timed.Has(TimedIds.Paralyzed));
        Assert.Equal(1, game.Player.Resists["free_act"]);
    }
}
