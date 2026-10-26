using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>AVABand's death recap: the last hurts, the escapes and healing left unused, and a tip.</summary>
public class DeathRecapTests
{
    /// <summary>A warrior at 50 ft with an empty pack (no test kit) and plenty of hit points.</summary>
    private static GameSession Game(int depth = 1, int level = 10)
    {
        var game = Arena.Create(4,
            "###############",
            "#,,,,,,,,,,,,,#",
            "#,,,,,,@,,,,,,#",
            "#,,,,,,,,,,,,,#",
            "###############");
        TestGames.ClearMonsters(game);
        foreach (var item in game.Player.Inventory.Pack.ToList())
            game.Player.Inventory.Remove(item, item.Number, () => game.Objects.NextSerial++);
        game.Player.Depth = depth;
        game.Player.Level = level;
        game.Player.Hp = game.Player.MaxHp = 100;
        return game;
    }

    private static void Give(GameSession game, string kind, int count = 1, bool known = true)
    {
        var item = game.Objects.Create(kind, count);
        if (known) game.Knowledge.LearnKind(item.Kind);
        game.Player.Inventory.Add(item);
    }

    [Fact]
    public void Hurts_AreKept_WithWhoDidIt_AndTheHitPointsLeft()
    {
        var game = Game();
        game.TakeHit(10, "a Cave orc");
        game.TakeHit(25, "a Snaga");
        Assert.Equal([new HurtRecord("a Cave orc", 10, 90, 100), new HurtRecord("a Snaga", 25, 65, 100)],
            game.RecentHurts.Select(h => h with { Turn = 0 }));
        Assert.Equal("A Snaga: 25 damage, 65 hit points left", DeathRecap.HurtLine(game.RecentHurts[^1]));
    }

    [Fact]
    public void OnlyTheLastFew_AreKept_AndTicksOfTheSameHurt_CountAsOne()
    {
        var game = Game();
        for (var i = 0; i < GameSession.RecentHurtCapacity + 3; i++) game.TakeHit(1, i % 2 == 0 ? "a jackal" : "a Cave orc");
        Assert.Equal(GameSession.RecentHurtCapacity, game.RecentHurts.Count);

        game.TakeHit(1, "poison", note: false);
        game.TakeHit(1, "poison", note: false);
        game.TakeHit(1, "poison", note: false);
        var last = game.RecentHurts[^1];
        Assert.Equal(("poison", 3, 3), (last.Killer, last.Damage, last.Count));
        Assert.Equal("Poison: 3 damage (3 times), 86 hit points left", DeathRecap.HurtLine(last));
    }

    [Fact]
    public void TheLiving_AndWinners_HaveNoRecap()
    {
        var game = Game();
        game.TakeHit(10, "a Cave orc");
        Assert.Null(DeathRecap.For(game));
        game.Player.IsWinner = true;
        game.TakeHit(1000, "a Cave orc");
        Assert.Null(DeathRecap.For(game));
    }

    [Fact]
    public void TheRecap_ShowsTheLastFiveHurts_TheLastOfThemKilling()
    {
        var game = Game();
        for (var i = 1; i <= 6; i++) game.TakeHit(10, $"orc {i}");
        game.TakeHit(1000, "a Black orc");
        var recap = DeathRecap.For(game)!;
        Assert.Equal(DeathRecap.HurtsShown, recap.Hurts.Count);
        Assert.Equal(["orc 3", "orc 4", "orc 5", "orc 6", "a Black orc"], recap.Hurts.Select(h => h.Killer));
        Assert.Equal("A Black orc: 1000 damage, dead", DeathRecap.HurtLine(recap.Hurts[^1]));
    }

    [Fact]
    public void EscapesAndHealing_AreFoundByTheirEffects()
    {
        var data = TestData.Game;
        Assert.True(DeathRecap.IsEscape(data.Object("phase_door")!));
        Assert.True(DeathRecap.IsEscape(data.Object("staff_of_teleportation")!));
        Assert.True(DeathRecap.IsEscape(data.Object("scroll_of_word_of_recall")!));
        Assert.True(DeathRecap.IsHealing(data.Object("cure_light_wounds")!));
        Assert.True(DeathRecap.IsHealing(data.Object("cure_serious_wounds")!));
        Assert.False(DeathRecap.IsEscape(data.Object("cure_light_wounds")!));
        Assert.False(DeathRecap.IsHealing(data.Object("phase_door")!));
        Assert.False(DeathRecap.IsHealing(data.Object("flask_of_oil")!));
    }

    [Fact]
    public void AnUnusedEscape_IsListed_AndTheTipSaysToUseItEarly()
    {
        var game = Game();
        Give(game, "phase_door", 3);
        Give(game, "cure_light_wounds", 2);
        game.TakeHit(1000, "a Cave orc");
        var recap = DeathRecap.For(game)!;
        Assert.Equal(DeathTip.UnusedEscape, recap.TipKind);
        Assert.Single(recap.Escapes, e => e.Contains("Phase Door"));
        Assert.Single(recap.Healing, e => e.Contains("Cure Light Wounds"));
        Assert.Contains("Phase Door", recap.Tip);
    }

    [Fact]
    public void UnknownFlavours_AreNotSpoiled()
    {
        var game = Game();
        Give(game, "teleportation", 3, known: false);
        Give(game, "cure_serious_wounds", 2, known: false);
        game.TakeHit(1000, "a Cave orc");
        var recap = DeathRecap.For(game)!;
        Assert.Empty(recap.Escapes);
        Assert.Empty(recap.Healing);
        Assert.Equal(DeathTip.General, recap.TipKind);
        Assert.DoesNotContain("Teleportation", recap.Tip);
    }

    [Fact]
    public void OnlyASlowEscape_DoesNotGiveTheEscapeTip_ButHealingDoes()
    {
        var game = Game();
        Give(game, "scroll_of_word_of_recall");
        Give(game, "cure_serious_wounds");
        game.TakeHit(1000, "a Cave orc");
        var recap = DeathRecap.For(game)!;
        Assert.Single(recap.Escapes);
        Assert.Equal(DeathTip.UnusedHealing, recap.TipKind);
        Assert.Contains("Cure Serious Wounds", recap.Tip);
    }

    [Fact]
    public void Starvation_AndPoison_AndBleeding_GetTheirOwnTips()
    {
        var game = Game();
        Give(game, "phase_door"); // (what killed comes first)
        game.TakeHit(1000, "starvation", note: false);
        Assert.Equal(DeathTip.Starvation, DeathRecap.For(game)!.TipKind);

        foreach (var killer in new[] { "poison", "a fatal wound" })
        {
            var g = Game();
            g.TakeHit(1000, killer, note: false);
            Assert.Equal(DeathTip.PoisonOrBleeding, DeathRecap.For(g)!.TipKind);
        }
    }

    [Fact]
    public void GoingTooDeep_ForTheCharacterLevel_IsTheTip()
    {
        var game = Game(depth: 15, level: 6);
        Give(game, "phase_door");
        game.TakeHit(1000, "a Cave orc");
        var recap = DeathRecap.For(game)!;
        Assert.Equal(DeathTip.TooDeep, recap.TipKind);
        Assert.Contains("750 ft", recap.Tip);
        Assert.Contains("character level 6", recap.Tip);

        var near = Game(depth: 10, level: 6); // within four levels: not too deep
        near.TakeHit(1000, "a Cave orc");
        Assert.NotEqual(DeathTip.TooDeep, DeathRecap.For(near)!.TipKind);
    }

    [Fact]
    public void ABreath_IsRecorded_AndTheTipIsResistance()
    {
        var game = Game(depth: 10, level: 20);
        var dragon = Arena.AddMonster(game, "baby_red_dragon", game.Player.Position + new Loc(-5, 0));
        dragon.Held = 1000;
        dragon.Hp = 100_000; // a breath strong enough to kill
        game.UpdateView();
        game.CastSpellForTest(dragon, "BR_FIRE");
        Assert.True(game.Player.IsDead);
        var last = game.RecentHurts[^1];
        Assert.True(last.Breath);
        Assert.Equal("fire", last.Element);
        Assert.Equal("baby_red_dragon", last.RaceId);
        var recap = DeathRecap.For(game)!;
        Assert.Equal(DeathTip.Breath, recap.TipKind);
        Assert.Contains("resistance to fire", recap.Tip);
        Assert.StartsWith("A baby red dragon's breath: ", DeathRecap.HurtLine(last));
    }

    [Fact]
    public void AUnique_IsTheTip_WithHowToLookAtIt()
    {
        var game = Game(depth: 2, level: 5);
        var grip = Arena.AddMonster(game, "grip", game.Player.Position + new Loc(3, 0));
        grip.Held = 1000;
        game.Player.Hp = 1;
        game.CastSpellForTest(grip, "BO_COLD");
        Assert.True(game.Player.IsDead);
        var recap = DeathRecap.For(game)!;
        Assert.Equal(DeathTip.Unique, recap.TipKind);
        Assert.StartsWith("Grip, Farmer Maggot's Dog is a unique", recap.Tip);
    }

    [Fact]
    public void StayingInTheFight_AtLowHitPoints_IsTheTip()
    {
        var game = Game();
        game.TakeHit(70, "a Cave orc"); // 30 left: below a third
        TestGames.HoldUntil(game, game.GameTurn + 100); // a few turns pass
        game.TakeHit(1000, "a Cave orc");
        Assert.Equal(DeathTip.FoughtOnLow, DeathRecap.For(game)!.TipKind);

        // Brought low and killed in the same turn: no chance to act, so no such tip.
        var fast = Game();
        fast.TakeHit(70, "a Cave orc");
        fast.TakeHit(1000, "a Cave orc");
        Assert.Equal(DeathTip.General, DeathRecap.For(fast)!.TipKind);
    }

    [Fact]
    public void DyingInTown_HasItsOwnTip()
    {
        var game = Game(depth: 0);
        game.TakeHit(1000, "a scruffy little dog");
        Assert.Equal(DeathTip.Town, DeathRecap.For(game)!.TipKind);
    }

    [Fact]
    public void TheCharacterDump_HasTheLastMoments_OnlyWhenDead()
    {
        var game = Game();
        Give(game, "phase_door", 2);
        game.TakeHit(10, "a Cave orc");
        Assert.DoesNotContain("[Last moments]", CharacterDump.Build(game));
        game.TakeHit(1000, "a Black orc");
        var dump = CharacterDump.Build(game);
        Assert.Contains("[Last moments]", dump);
        Assert.Contains(" A Black orc: 1000 damage, dead", dump);
        Assert.Contains(" Escapes unused: 2 Scrolls of Phase Door", dump);
        Assert.Contains(" Tip: You died with", dump);
    }
}
