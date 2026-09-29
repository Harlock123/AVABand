using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>What projections do to monsters, as Angband 4.2.5's project-mon.c has it.</summary>
public class ProjectMonster425Tests
{
    private static (GameSession Game, List<string> Said) Game()
    {
        var game = Arena.Create(9);
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        return (game, said);
    }

    [Fact]
    public void Fire_burns_what_it_hurts_twice_as_hard()
    {
        var (game, said) = Game();
        var mold = Arena.AddMonster(game, "grey_mold", game.Player.Position + new Loc(2, 0));
        mold.Hp = mold.MaxHp = 1000;
        game.ProjectileHitsMonster(mold, "fire bolt", "fire", 20);
        Assert.Equal(960, mold.Hp);
        Assert.Contains("The grey mold catches fire!", said);
    }

    [Fact]
    public void Light_makes_the_light_hating_cringe()
    {
        var (game, said) = Game();
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(2, 0));
        orc.Hp = orc.MaxHp = 1000;
        game.ProjectileHitsMonster(orc, "light", "light", 10);
        Assert.Equal(980, orc.Hp);
        Assert.Contains("The cave orc cringes from the light!", said);
    }

    [Fact]
    public void Nether_leaves_the_undead_untouched()
    {
        var (game, said) = Game();
        var ghoul = Arena.AddMonster(game, "ghoul", game.Player.Position + new Loc(2, 0));
        var hp = ghoul.Hp;
        game.ProjectileHitsMonster(ghoul, "nether bolt", "nether", 50);
        Assert.Equal(hp, ghoul.Hp);
        Assert.Contains("The ghoul is immune.", said);
    }

    [Fact]
    public void A_plain_hit_shows_pain_and_a_death_says_how()
    {
        var (game, said) = Game();
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(2, 0));
        orc.Hp = orc.MaxHp = 100;
        game.ProjectileHitsMonster(orc, "magic missile", "missile", 10);
        Assert.Contains("The cave orc grunts with pain.", said);
        game.ProjectileHitsMonster(orc, "magic missile", "missile", 500);
        Assert.Contains("The cave orc dies.", said);
        Assert.DoesNotContain(said, m => m.StartsWith("You have slain"));
    }

    [Fact]
    public void Sound_can_stun_and_disenchantment_unsettles_casters()
    {
        var (game, _) = Game();
        var shaman = Arena.AddMonster(game, "kobold_shaman", game.Player.Position + new Loc(2, 0));
        for (var i = 0; i < 30 && (shaman.Stun == 0 || shaman.Disenchanted == 0); i++)
        {
            shaman.Hp = shaman.MaxHp = 10_000;
            game.ProjectileHitsMonster(shaman, "sound", "sound", 1);
            game.ProjectileHitsMonster(shaman, "disenchantment", "disen", 1);
        }
        Assert.True(shaman.Stun > 0);
        Assert.True(shaman.Disenchanted > 0);
    }
}

/// <summary>Your blows, as Angband 4.2.5's py_attack_real strikes them.</summary>
public class PlayerMelee425Tests
{
    [Fact]
    public void A_punch_does_one_and_your_bonuses()
    {
        var game = Arena.Create(4);
        Arena.StripGear(game);
        game.RecalculateBonuses();
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Angband.Core.Geometry.Loc(1, 0));
        orc.Hp = orc.MaxHp = 10_000;
        var hurt = new List<int>();
        game.Events.Subscribe<PlayerAttackEvent>(e => { if (e.Hit) hurt.Add(e.Damage); });
        for (var i = 0; i < 20; i++) game.Execute(new WalkCommand(Direction.East));
        Assert.NotEmpty(hurt);
        Assert.All(hurt, d => Assert.Equal(Math.Max(0, 1 + game.Player.EffectiveToDam), d));
        Assert.Contains(said, m => m.StartsWith("You punch") || m.StartsWith("You fail to harm"));
    }

    [Fact]
    public void An_attack_frees_a_held_monster()
    {
        var game = Arena.Create(4);
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Angband.Core.Geometry.Loc(1, 0));
        orc.Hp = orc.MaxHp = 10_000;
        orc.Held = 10;
        game.Execute(new WalkCommand(Direction.East));
        Assert.Equal(0, orc.Held);
    }
}
