using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>
/// Angband 4.2's birth_ai_learn: monsters remember how the player stood up to their attacks
/// (update_smart_learn) and stop casting what they know won't work (remove_bad_spells).
/// </summary>
public class MonsterLearningTests
{
    private static GameSession Game()
    {
        var game = Arena.Create(4,
            "#############",
            "#,,,,,,,,,,,#",
            "#,,,,,@,,,,,#",
            "#,,,,,,,,,,,#",
            "#############");
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        return game;
    }

    private static Monster Near(GameSession game, string race)
    {
        var m = Arena.AddMonster(game, race, game.Player.Position + new Loc(3, 0));
        m.Held = 1000;
        game.UpdateView();
        return m;
    }

    private static List<MonsterSpellDef> Spells(GameSession game, Monster m) =>
        [.. m.Race.Spells.Select(game.Data.MonsterSpell).OfType<MonsterSpellDef>()];

    [Fact]
    public void TheOption_IsAngbands_AndOnByDefault()
    {
        var option = OptionCatalog.Find(OptionIds.AiLearn)!;
        Assert.Equal(OptionKind.Birth, option.Kind);
        Assert.True(Game().Options[OptionIds.AiLearn]);
    }

    [Fact]
    public void ASmartCaster_LearnsYourResistance_AndForgetsItWhenYouLoseIt()
    {
        var game = Game();
        var mage = Near(game, "mage"); // SMART
        game.Player.Resists["fire"] = 1;
        for (var i = 0; i < 3 && !mage.KnownPlayer.ContainsKey("fire"); i++) game.CastSpellForTest(mage, "BO_FIRE");
        Assert.Equal(1, mage.KnownPlayer["fire"]);

        game.Player.Resists.Remove("fire");
        for (var i = 0; i < 3 && mage.KnownPlayer.ContainsKey("fire"); i++) game.CastSpellForTest(mage, "BO_FIRE");
        Assert.False(mage.KnownPlayer.ContainsKey("fire"));
    }

    [Fact]
    public void AStatusSpell_TeachesWhetherYouAreProtected()
    {
        var game = Game();
        var mage = Near(game, "mage");
        game.Player.Resists["conf"] = 1;
        for (var i = 0; i < 3 && !mage.KnownPlayer.ContainsKey("conf"); i++) game.CastSpellForTest(mage, "CONF");
        Assert.Equal(1, mage.KnownPlayer["conf"]);
    }

    [Fact]
    public void StupidMonsters_NeverLearn_AndNoOneDoesWithTheOptionOff()
    {
        var game = Game();
        var mushrooms = Near(game, "magic_mushroom_patch"); // STUPID
        game.Player.Resists["fear"] = 1;
        for (var i = 0; i < 20; i++) game.CastSpellForTest(mushrooms, "SCARE");
        Assert.Empty(mushrooms.KnownPlayer);

        var off = Game();
        off.Options[OptionIds.AiLearn] = false;
        var mage = Near(off, "mage");
        off.Player.Resists["fire"] = 1;
        for (var i = 0; i < 20; i++) off.CastSpellForTest(mage, "BO_FIRE");
        Assert.Empty(mage.KnownPlayer);
    }

    [Fact]
    public void KnowingYouAreImmune_ASmartCasterDropsTheBolt_ExceptWhenItForgets()
    {
        var game = Game();
        var mage = Near(game, "mage");
        var spells = Spells(game, mage);
        var kept = 0;
        for (var i = 0; i < 400; i++)
        {
            mage.KnownPlayer["fire"] = 3; // immune: 150% chance to drop it
            if (game.WithoutKnownFailures(mage, spells).Any(s => s.Id == "BO_FIRE")) kept++;
        }
        Assert.InRange(kept, 5, 45); // only in the one turn in 20 it forgets (about 20 of 400)

        // Knowing nothing, it keeps everything.
        mage.KnownPlayer.Clear();
        Assert.Equal(spells.Count, game.WithoutKnownFailures(mage, spells).Count);
    }

    [Fact]
    public void AnOrdinaryCaster_DropsResistedElements_OnlyAQuarterOfTheTimePerLevel()
    {
        var game = Game();
        var caster = Near(game, "dark_dwarven_lord"); // neither smart nor stupid
        var spells = Spells(game, caster);
        var dropped = 0;
        for (var i = 0; i < 1000; i++)
        {
            caster.KnownPlayer["fire"] = 1;
            if (!game.WithoutKnownFailures(caster, spells).Any(s => s.Id == "BO_FIRE")) dropped++;
        }
        Assert.InRange(dropped, 170, 310); // 25%, less the turns it forgets
    }

    [Fact]
    public void AKnownProtection_RulesOutTheStatusSpell()
    {
        var game = Game();
        var mage = Near(game, "mage");
        var spells = Spells(game, mage);
        var kept = 0;
        for (var i = 0; i < 400; i++)
        {
            mage.KnownPlayer["conf"] = 1;
            if (game.WithoutKnownFailures(mage, spells).Any(s => s.Id == "CONF")) kept++;
        }
        Assert.InRange(kept, 5, 45);
    }

    [Fact]
    public void WhatAMonsterKnows_IsKeptInTheSave()
    {
        var game = Game();
        var mage = Near(game, "mage");
        mage.KnownPlayer["fire"] = 2;
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal(2, loaded.Level.Monsters.All.Single(m => m.Race.Id == "mage").KnownPlayer["fire"]);
    }
}
