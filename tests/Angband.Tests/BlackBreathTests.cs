using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>The Black Breath (Angband 4.2 BLACK_BREATH blows and TMD_BLACKBREATH).</summary>
public class BlackBreathTests
{
    private static GameSession Game(string cls = "warrior")
    {
        var arena = Arena.Create(8);
        var game = GameSession.NewGame(TestData.Game, 8, cls);
        game.UseLevel(arena.Level, arena.Player.Position);
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 1_000_000;
        return game;
    }

    [Fact]
    public void TheRingwraiths_Carry_It()
    {
        foreach (var id in new[] { "the_witch_king_of_angmar", "uvatha_the_horseman", "khamul_the_black_easterling" })
            Assert.Contains(TestData.Game.Monster(id)!.Blows, b => b.Effect == "black_breath");
        Assert.Equal(10, TestData.Game.Timed("blackbreath")!.Max);
    }

    [Fact]
    public void ARingwraithsTouch_CanBringOnTheBlackBreath()
    {
        var game = Game();
        game.Player.Armour = 0;
        var wraith = Arena.AddMonster(game, "uvatha_the_horseman", game.Player.Position + new Loc(1, 0));
        for (var i = 0; i < 200 && !game.Player.Timed.Has("blackbreath"); i++) game.RunMonsterTurn(wraith);
        Assert.True(game.Player.Timed.Has("blackbreath"));
        Assert.InRange(game.Player.Timed["blackbreath"], 1, 10);
    }

    [Fact]
    public void ItDrainsStrength_Constitution_AndLife_SustainsOrNot()
    {
        var game = Game();
        game.GainExperience(100_000);
        game.Player.Resists["sust_str"] = 1;
        game.Player.Resists["hold_life"] = 1;
        var exp = game.Player.Experience;
        var messages = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));
        game.IncreaseTimed("blackbreath", 10);
        for (var i = 0; i < 10; i++) game.Execute(new HoldCommand());
        Assert.True(game.Player.StatDrain.GetValueOrDefault("str") > 0);
        Assert.True(game.Player.StatDrain.GetValueOrDefault("con") > 0);
        Assert.True(game.Player.Experience < exp);
        Assert.Contains("The Black Breath saps your strength.", messages);
    }

    [Fact]
    public void HerbalCuring_DrivesItOut()
    {
        Assert.Contains("cure:blackbreath", TestData.Game.Spell("herbal_curing")!.Effect);
        Assert.Contains("cure:blackbreath", TestData.Game.Spell("ranger_herbal_curing")!.Effect);
    }
}
