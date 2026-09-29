using Angband.Core.Combat;
using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>Monster melee as Angband 4.2.5's make_attack_normal and mon-blows.c have it.</summary>
public class MonsterBlows425Tests
{
    private static GameSession Game(ulong seed = 5)
    {
        var game = Arena.Create(seed);
        game.Player.Hp = game.Player.MaxHp = 1_000_000;
        return game;
    }

    [Fact]
    public void Hit_chance_is_test_hit_out_of_ten_thousand()
    {
        Assert.Equal(0.12, CombatMath.HitProbability(0, 10_000, visible: true), 3);     // always 12% ...
        Assert.Equal(0.95, CombatMath.HitProbability(10_000, 0, visible: true), 3);     // ... never over 95%
        Assert.Equal(0.12 + 0.83 * 0.5, CombatMath.HitProbability(60, 45, visible: true), 3); // 60 - 30 of 60
    }

    [Fact]
    public void A_thief_finishes_its_blows_before_it_vanishes()
    {
        var game = Game();
        game.Player.Gold = 10_000;
        var thief = Arena.AddMonster(game, "harowen_the_black_hand", game.Player.Position + new Loc(1, 0));
        var blows = 0;
        var said = new List<string>();
        game.Events.Subscribe<MonsterAttackEvent>(_ => blows++);
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        for (var i = 0; i < 50 && game.Player.Gold == 10_000; i++)
        {
            game.Player.Timed.Set(game.Data.Timed(TimedIds.Paralyzed)!, 5); // no saving throw
            blows = 0;
            said.Clear();
            game.RunMonsterTurn(thief);
        }
        Assert.True(game.Player.Gold < 10_000);
        Assert.Equal(4, blows);                                         // all four, stealing or not
        Assert.Equal("There is a puff of smoke!", said.Last());           // then it blinks away
    }

    [Fact]
    public void Draining_charges_takes_some_by_the_monsters_level_not_all()
    {
        var game = Game();
        foreach (var item in game.Player.Inventory.Pack.ToList()) game.Player.Inventory.Remove(item, item.Number, () => game.Objects.NextSerial++);
        var wand = game.Objects.Create("wand_of_magic_missile");
        wand.Charges = 30;
        game.Player.Inventory.Add(wand);
        var gauth = Arena.AddMonster(game, "gauth", game.Player.Position + new Loc(1, 0));
        for (var i = 0; i < 400 && wand.Charges == 30; i++) game.RunMonsterTurn(gauth);
        // unpower = level 36 / (wand level 3 + 2) + 1 = 8, from each of its two draining gazes.
        Assert.Contains(wand.Charges, new[] { 22, 14 });
    }

    [Fact]
    public void A_blow_that_does_nothing_always_lands()
    {
        var game = Game();
        game.Player.Armour = 5000;
        var idiot = Arena.AddMonster(game, "village_idiot", game.Player.Position + new Loc(1, 0));
        var hits = new List<bool>();
        game.Events.Subscribe<MonsterAttackEvent>(e => hits.Add(e.Hit));
        for (var i = 0; i < 30; i++) game.RunMonsterTurn(idiot);
        Assert.NotEmpty(hits);
        Assert.All(hits, Assert.True);
    }
}
