using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>Amnesia (Angband 4.2 TMD_AMNESIA).</summary>
public class AmnesiaTests
{
    private static GameSession Game(string cls = "mage")
    {
        var arena = Arena.Create(5);
        var game = GameSession.NewGame(TestData.Game, 5, cls);
        game.UseLevel(arena.Level, arena.Player.Position);
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        return game;
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    [Fact]
    public void Forget_CausesAmnesia_UnlessSavedAgainst()
    {
        var game = Game();
        game.Player.SkillSave = 0;
        var said = Messages(game);
        var caster = Arena.AddMonster(game, "jackal", game.Player.Position + new Loc(2, 0));
        game.UpdateView();
        game.CastSpellForTest(caster, "FORGET");
        Assert.Contains(said, m => m.EndsWith("tries to make you forget things."));
        Assert.True(game.Player.Timed.Has(TimedIds.Amnesia));
        Assert.Contains("You feel your memories fade.", said);

        var saved = Game();
        saved.Player.SkillSave = 100;
        var heard = Messages(saved);
        var other = Arena.AddMonster(saved, "jackal", saved.Player.Position + new Loc(2, 0));
        saved.UpdateView();
        saved.CastSpellForTest(other, "FORGET");
        Assert.False(saved.Player.Timed.Has(TimedIds.Amnesia));
        Assert.Contains("You retain your presence of mind.", heard);
    }

    [Fact]
    public void WithAmnesia_YouCantRead_AndSpellsAndDevicesFailMore()
    {
        var game = Game();
        var missile = TestData.Game.Spell("magic_missile")!;
        var wand = game.Objects.Create("wand_of_magic_missile");
        var spellFail = game.SpellFailChance(missile);
        var deviceFail = game.DeviceFailChance(wand);
        var scroll = game.Player.Inventory.Add(game.Objects.Create("phase_door"))!; // (joins the kit's scrolls)
        var said = Messages(game);

        game.IncreaseTimed(TimedIds.Amnesia, 20);

        Assert.Equal(Math.Min(95, 50 + spellFail / 2), game.SpellFailChance(missile));
        Assert.True(game.DeviceFailChance(wand) > deviceFail);
        Assert.False(game.Execute(new UseCommand(scroll)));
        Assert.Contains("You can't remember how to read!", said);
    }

    [Fact]
    public void CureCriticalWounds_BringsTheMemoriesBack()
    {
        var game = Game();
        game.IncreaseTimed(TimedIds.Amnesia, 50);
        var potion = game.Player.Inventory.Add(game.Objects.Create("potion_of_cure_critical_wounds"))!;
        var said = Messages(game);
        game.Execute(new UseCommand(potion));
        Assert.False(game.Player.Timed.Has(TimedIds.Amnesia));
        Assert.Contains("Your memories come flooding back.", said);
    }

    [Fact]
    public void VeryStrongDarkness_CanMakeYouForget()
    {
        var game = Game();
        for (var i = 0; i < 40 && !game.Player.Timed.Has(TimedIds.Amnesia); i++)
            game.ElementalHit("dark", 600, "a darkness storm", power: 80);
        Assert.True(game.Player.Timed.Has(TimedIds.Amnesia));
    }
}
