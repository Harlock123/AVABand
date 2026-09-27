using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>
/// Monster memory for spells (Angband 4.2 mon-lore.c): what your spells show about a monster's
/// resistances, and what its own spells can do to you.
/// </summary>
public class SpellLoreTests
{
    private static GameSession Game()
    {
        var game = Arena.Create(3,
            "#################",
            "#,,,,,,,,,,,,,,,#",
            "#,,@,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,#",
            "#################");
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        game.Player.SkillDevice = 500;
        return game;
    }

    private static Monster Add(GameSession game, string race, int x = 8)
    {
        var m = Arena.AddMonster(game, race, new Loc(x, 2));
        m.Hp = m.MaxHp = 100_000;
        m.Held = 10_000;
        game.UpdateView();
        return m;
    }

    private static void Zap(GameSession game, string wandKind, Monster target)
    {
        var wand = game.Objects.Create(wandKind);
        wand.Charges = 5;
        game.Knowledge.LearnKind(wand.Kind);
        var item = game.Player.Inventory.Add(wand)!;
        for (var i = 0; i < 20 && item.Charges == 5; i++) game.Execute(new UseCommand(item, target.Position));
    }

    [Fact]
    public void AFireBolt_ShowsWhetherItResistsFire_EitherWay()
    {
        var game = Game();
        var jackal = Add(game, "jackal");
        Zap(game, "wand_of_fire_bolts", jackal);
        Assert.Contains("IM_FIRE", game.Lore.For("jackal").FlagsLacking);
        Assert.Contains("does not resist fire", game.Recall(jackal.Race));

        var salamander = Add(game, "salamander", 10); // IM_FIRE
        game.Level.Monsters.Remove(jackal);
        Zap(game, "wand_of_fire_bolts", salamander);
        Assert.Contains("resists fire", game.Recall(salamander.Race));
        Assert.DoesNotContain("does not resist", game.Recall(salamander.Race));
    }

    [Fact]
    public void BeingHurtByFire_SaysMoreThanNotResistingIt()
    {
        var game = Game();
        var patch = Add(game, "grey_mushroom_patch"); // HURT_FIRE
        Zap(game, "wand_of_fire_bolts", patch);
        var text = game.Recall(patch.Race);
        Assert.Contains("is hurt by fire", text);
        Assert.DoesNotContain("does not resist fire", text);
    }

    [Fact]
    public void UnseenMonsters_TeachNothing()
    {
        var game = Game();
        var jackal = Add(game, "jackal");
        jackal.IsVisible = false;
        game.LearnMonsterResponse(jackal, "IM_FIRE");
        Assert.Empty(game.Lore.For("jackal").FlagsLacking);
    }

    [Fact]
    public void Light_ThatDoesntHurtAMonster_CountsAsResisted()
    {
        var game = Game();
        var jackal = Add(game, "jackal");
        var orc = Add(game, "cave_orc", 11); // HURT_LIGHT
        var wand = game.Objects.Create("wand_of_light");
        wand.Charges = 5;
        game.Knowledge.LearnKind(wand.Kind);
        var item = game.Player.Inventory.Add(wand)!;
        for (var i = 0; i < 20 && item.Charges == 5; i++)
            game.Execute(new UseCommand(item, Direction: Direction.East)); // a beam of light through both
        Assert.Contains("resists light", game.Recall(jackal.Race));
        Assert.Contains("is hurt by light", game.Recall(orc.Race));
    }

    [Fact]
    public void AllFourKindsOfClause_ReadAsAngbandsSentence()
    {
        var game = Game();
        var race = game.Data.Monster("grey_mushroom_patch")!;
        var lore = game.Lore.For(race.Id);
        lore.FlagsKnown.Add("HURT_FIRE");
        lore.FlagsLacking.UnionWith(["IM_FIRE", "IM_ACID", "IM_ELEC", "IM_COLD"]);
        if (race.Has("NO_CONF")) lore.FlagsKnown.Add("NO_CONF");
        var text = game.Recall(race);
        Assert.Contains("It is hurt by fire, and does not resist acid, lightning, or cold", text);
    }

    [Fact]
    public void ProbingTellsWhatItDoesNotResist()
    {
        var game = Game();
        var jackal = game.Data.Monster("jackal")!;
        game.Lore.For(jackal.Id).Probed = true;
        Assert.Contains("does not resist acid, lightning, fire, cold, or poison", game.Recall(jackal));
    }

    [Fact]
    public void Spells_ShowTheirMostDamage_InnateAndCastSeparately()
    {
        var game = Game();
        var scout = game.Data.Monster("scout")!; // ARROW 1 in 3 (innate-freq), HASTE 1 in 10 (spell-freq)
        var lore = game.Lore.For(scout.Id);
        lore.SpellsSeen.UnionWith(["ARROW", "HASTE"]);
        lore.CastsInnate = 1;
        lore.CastsSpell = 1;
        var arrow = game.Data.MonsterSpell("ARROW")!;
        var max = MonsterRecall.LoreDamage(arrow, scout, knowHp: false);
        Assert.Equal(arrow.Damage.Max + scout.Depth / arrow.LevelDivisor, max);
        var text = game.Recall(scout);
        // Each sentence has its own frequency. 1 in 3 is a 33% chance, which Angband's guess rounds up
        // to 40%: "about 1 time in 2"; 1 in 10 is 10%, "about 1 time in 10".
        Assert.Equal((3, 10), (scout.InnateFrequency, scout.SpellFrequency));
        Assert.Contains($"He may fire small arrows ({max}); about 1 time in 2.", text);
        Assert.Contains("He may cast spells which haste-self; about 1 time in 10.", text);
    }

    [Fact]
    public void Frequencies_BecomeExact_AfterFiftyCasts()
    {
        var game = Game();
        var priest = game.Data.Monster("dark_elven_priest")!; // 1_IN_5
        var lore = game.Lore.For(priest.Id);
        lore.SpellsSeen.Add("BLINK");
        lore.CastsSpell = 50;
        Assert.Contains("about 1 time in 5", game.Recall(priest));
        lore.CastsSpell = 51;
        Assert.Contains("; 1 time in 5.", game.Recall(priest));
    }

    [Fact]
    public void Breaths_ShowDamage_OnlyOnceOneHasBeenKilled()
    {
        var game = Game();
        var dragon = game.Data.Monster("baby_red_dragon")!;
        var lore = game.Lore.For(dragon.Id);
        lore.SpellsSeen.Add("BR_FIRE");
        lore.CastsInnate = 1;
        Assert.Contains("may breathe fire;", game.Recall(dragon));
        lore.TotalKills = 1;
        var breath = game.Data.MonsterSpell("BR_FIRE")!;
        Assert.Contains($"may breathe fire ({dragon.HitPoints / breath.BreathDivisor})", game.Recall(dragon));
    }

    [Fact]
    public void SpellColours_FollowYourResistances()
    {
        var game = Game();
        var bolt = game.Data.MonsterSpell("BO_FIRE")!;
        var level = bolt.LoreFor(20);
        string Color() => MonsterRecall.SpellColor(game.Data, bolt, level, game.RecallViewer);
        Assert.Equal("Orange", Color());
        game.Player.IntrinsicResists["fire"] = 1;
        game.RecalculateBonuses();
        Assert.Equal("Yellow", Color());
        game.Player.IntrinsicResists["fire"] = 3;
        game.RecalculateBonuses();
        Assert.Equal("Light Green", Color());

        // A fear spell is harmless to the fearless (a saving-throw spell, like Angband's).
        var scare = game.Data.MonsterSpell("SCARE")!;
        Assert.Equal("Yellow", MonsterRecall.SpellColor(game.Data, scare, scare.LoreFor(1), game.RecallViewer));
        game.Player.IntrinsicResists["fear"] = 1;
        game.RecalculateBonuses();
        Assert.Equal("Light Green", MonsterRecall.SpellColor(game.Data, scare, scare.LoreFor(1), game.RecallViewer));
    }

    [Fact]
    public void GearYouHaventIdentified_DoesntCountForTheColours()
    {
        var game = Game();
        var cloak = game.Objects.Create("cloak");
        cloak.Resists.Add("fire");
        game.Player.Inventory.Wield(cloak, () => game.Objects.NextSerial++);
        game.RecalculateBonuses();
        Assert.Equal(1, game.Player.Resists["fire"]);
        Assert.Equal(0, game.KnownResist("fire"));
        game.Knowledge.LearnRune(RuneIds.Resist("fire"));
        Assert.Equal(1, game.KnownResist("fire"));
    }

    [Fact]
    public void WoundSpells_AreDescribedByTheirPower()
    {
        Assert.Equal("cause light wounds", TestData.Game.MonsterSpell("CAUSE_1")!.LoreFor(1)!.Text);
        Assert.Equal("cause critical wounds", TestData.Game.MonsterSpell("CAUSE_4")!.LoreFor(1)!.Text);
    }

    [Fact]
    public void TheMarkedRecall_CarriesTheColours()
    {
        var game = Game();
        var dragon = game.Data.Monster("baby_red_dragon")!;
        var lore = game.Lore.For(dragon.Id);
        lore.SpellsSeen.Add("BR_FIRE");
        lore.FlagsKnown.Add("IM_FIRE");
        var runs = RecallMarkup.Parse(game.RecallMarked(dragon)).ToList();
        Assert.Contains(runs, r => r.Text == "breathe " && r.Color == RecallColors.Verb);
        Assert.Contains(runs, r => r.Text == "fire" && r.Color == "Orange");
        Assert.Contains(runs, r => r.Text == "fire" && r.Color == RecallColors.Resist);
        Assert.Equal(game.Recall(dragon), RecallMarkup.Strip(game.RecallMarked(dragon)));
        Assert.DoesNotContain('\u0001', game.Recall(dragon));
    }

    [Fact]
    public void WhatWasLearned_IsKeptInTheLoreBook()
    {
        var game = Game();
        var lore = game.Lore.For("jackal");
        lore.FlagsLacking.Add("IM_FIRE");
        lore.CastsInnate = 7;
        var path = Path.Combine(Directory.CreateTempSubdirectory("avaband-lore-").FullName, "lore.json");
        game.Lore.Save(path);
        var loaded = MonsterLoreBook.Load(path);
        Assert.Contains("IM_FIRE", loaded.For("jackal").FlagsLacking);
        Assert.Equal(7, loaded.For("jackal").CastsInnate);
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }
}
