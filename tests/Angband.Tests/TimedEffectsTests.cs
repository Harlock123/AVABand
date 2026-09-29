using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Game;

namespace Angband.Tests;

public class TimedEffectsTests
{
    private static TimedEffectDef Def(string id) => TestData.Game.Timed(id)!;

    [Fact]
    public void Increase_ReportsBeginThenIncrease()
    {
        var t = new TimedEffects();
        Assert.Equal("You are poisoned!", t.Increase(Def(TimedIds.Poisoned), 5));
        Assert.Equal("You are more poisoned!", t.Increase(Def(TimedIds.Poisoned), 5));
        Assert.Equal(10, t[TimedIds.Poisoned]);
    }

    [Fact]
    public void NoStack_IgnoresNewDosesWhileActive()
    {
        var t = new TimedEffects();
        t.Increase(Def(TimedIds.Paralyzed), 5);
        Assert.Null(t.Increase(Def(TimedIds.Paralyzed), 50));
        Assert.Equal(5, t[TimedIds.Paralyzed]);
    }

    [Fact]
    public void Decrease_ReportsEndAtZero()
    {
        var t = new TimedEffects();
        t.Increase(Def(TimedIds.Confused), 2);
        Assert.Null(t.Decrease(Def(TimedIds.Confused), 1));
        Assert.Equal("You are no longer confused.", t.Decrease(Def(TimedIds.Confused), 1));
        Assert.False(t.Has(TimedIds.Confused));
    }

    [Fact]
    public void Increase_IsCappedAtMax()
    {
        var t = new TimedEffects();
        t.Increase(Def(TimedIds.Stun), 50_000);
        Assert.Equal(Def(TimedIds.Stun).Max, t[TimedIds.Stun]);
    }
}

/// <summary>player_timed.txt as 4.2.5 has it: grades, what prevents an effect, Constitution's healing, the new boons.</summary>
public class PlayerTimed425Tests
{
    private static TimedEffectDef Def(string id) => TestData.Game.Timed(id)!;

    [Fact]
    public void Cuts_go_by_grade_and_say_so_on_each_step_up()
    {
        var t = new TimedEffects();
        Assert.Equal("You have been given a graze.", t.Increase(Def(TimedIds.Cut), 5));
        Assert.Null(t.Increase(Def(TimedIds.Cut), 3)); // still a graze, and CUT has no on-increase
        Assert.Equal("You have been given a light cut.", t.Increase(Def(TimedIds.Cut), 10));
        Assert.Equal("Light Cut", Def(TimedIds.Cut).GradeAt(t[TimedIds.Cut])!.Label);
        Assert.Equal("You have been given a mortal wound.", t.Increase(Def(TimedIds.Cut), 5000));
        Assert.Equal("Mortal Wound", Def(TimedIds.Cut).GradeAt(t[TimedIds.Cut])!.Label);
        Assert.Null(t.Decrease(Def(TimedIds.Cut), 10)); // falling a grade is quiet
    }

    [Fact]
    public void A_mortal_wound_never_heals_and_Constitution_heals_the_rest_faster()
    {
        var game = Arena.Create(3);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        game.IncreaseTimed(TimedIds.Cut, 2000);
        TestGames.HoldUntil(game, game.GameTurn + 10 * 20);
        Assert.Equal(2000, game.Player.Timed[TimedIds.Cut]);

        int PoisonLeft(int con)
        {
            var g = Arena.Create(3);
            g.Player.Hp = g.Player.MaxHp = 100_000;
            g.Player.NaturalStats["con"] = con;
            g.RecalculateBonuses();
            g.IncreaseTimed(TimedIds.Poisoned, 100);
            TestGames.HoldUntil(g, g.GameTurn + 10 * 10);
            return g.Player.Timed[TimedIds.Poisoned];
        }
        Assert.True(PoisonLeft(28) < PoisonLeft(10)); // 18/100 heals five a turn, 10 one
    }

    [Fact]
    public void What_prevents_an_effect_is_player_timeds_fail_lines()
    {
        var game = Arena.Create(3);
        game.Player.IntrinsicResists["free_act"] = 1;
        game.RecalculateBonuses();
        Assert.False(game.IncreaseTimed(TimedIds.Slow, 10));
        Assert.True(game.IncreaseTimed(TimedIds.Slow, 10, check: false)); // TIMED_INC_NO_RES gets through

        game.IncreaseTimed("oppose_pois", 20);
        Assert.False(game.IncreaseTimed(TimedIds.Poisoned, 10)); // fail:5:OPP_POIS

        game.Player.IntrinsicResists["acid"] = -1; // vulnerable to acid: no temporary resistance to it
        game.RecalculateBonuses();
        Assert.False(game.IncreaseTimed("oppose_acid", 10));
    }

    [Fact]
    public void Heroism_is_fearlessness_while_it_lasts()
    {
        var game = Arena.Create(3);
        game.IncreaseTimed(TimedIds.Hero, 20);
        Assert.Equal(1, game.Player.Resists.GetValueOrDefault("fear")); // flag-synonym PROT_FEAR
    }

    [Fact]
    public void The_mystic_shield_and_invulnerability()
    {
        var game = Arena.Create(3);
        var armour = game.Player.Armour;
        game.IncreaseTimed("shield", 20);
        Assert.Equal(armour + 50, game.Player.Armour);
        game.IncreaseTimed("invuln", 20);
        Assert.Equal(armour + 150, game.Player.Armour);
        var hp = game.Player.Hp;
        game.TakeHit(500, "a test");
        Assert.Equal(hp, game.Player.Hp);
    }

    [Fact]
    public void Knocked_out_is_past_heavy_stun()
    {
        var game = Arena.Create(3);
        game.IncreaseTimed(TimedIds.Stun, 150);
        Assert.False(game.Player.IsIncapacitated);
        Assert.True(game.TimedGradeIs(TimedIds.Stun, "Heavy Stun"));
        game.IncreaseTimed(TimedIds.Stun, 1);
        Assert.True(game.Player.IsIncapacitated);
        Assert.True(game.TimedGradeIs(TimedIds.Stun, "Knocked Out"));
    }

    [Fact]
    public void A_temporary_fire_brand_burns_and_names_the_weapon()
    {
        var game = Arena.Create(3);
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        game.IncreaseTimed("att_fire", 100);
        var weapon = game.Player.Inventory.Weapon;
        Assert.Contains(said, m => m == (weapon is null ? "Flames surround your hands!" : $"Flames surround your {weapon.Kind.Name}!"));
        var foe = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Angband.Core.Geometry.Loc(1, 0));
        foe.Held = 1000;
        foe.Hp = foe.MaxHp = 100_000;
        game.UpdateView();
        for (var i = 0; i < 30 && !said.Any(m => m.StartsWith("You burn", StringComparison.Ordinal)); i++)
            game.Execute(new WalkCommand(Angband.Core.Geometry.Direction.East));
        Assert.Contains(said, m => m.StartsWith("You burn", StringComparison.Ordinal));
    }
}
