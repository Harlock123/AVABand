using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Magic;
using Angband.Data;

namespace Angband.Tests;

public class MagicTests
{
    private static GameSession Caster(string cls, ulong seed = 1, params string[] rows)
    {
        var game = Arena.Create(seed, rows);
        var fresh = GameSession.NewGame(TestData.Game, seed, cls);
        // Arena.Create makes a warrior; rebuild as the requested class on the arena level.
        fresh.UseLevel(game.Level, game.Player.Position);
        return fresh;
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    private static void Learn(GameSession game, string spellId, int level = 1)
    {
        if (game.Player.Level < level) game.GainExperience(game.ExperienceForLevel(level - 1) - game.Player.Experience);
        Assert.True(game.Execute(new StudyCommand(spellId)), $"could not learn {spellId}");
    }

    // --- Classes and levels -----------------------------------------------------------------------

    [Theory]
    [InlineData("warrior", "dagger", 19, 0)]
    [InlineData("mage", "rapier", 10, 2)]
    [InlineData("priest", "mace", 12, 2)]
    [InlineData("ranger", "main_gauche", 15, 0)]
    [InlineData("druid", "whip", 12, 2)]
    public void Classes_StartWithTheirKitHitPointsAndMana(string cls, string? weapon, int hp, int mana)
    {
        var game = GameSession.NewGame(TestData.Game, 1, cls);
        Assert.Equal(cls, game.Player.Class!.Id);
        Assert.Equal(weapon, game.Player.Inventory.Weapon?.Kind.Id);
        Assert.Equal(hp, game.Player.MaxHp);
        Assert.Equal(mana, game.Player.MaxMana);
        Assert.Equal(mana, game.Player.Mana);
    }

    [Fact]
    public void Casters_CarryTheirRealmsBook()
    {
        // Angband 4.2's first books.
        Assert.Contains(GameSession.NewGame(TestData.Game, 1, "mage").Player.Inventory.Pack, i => i.Kind.Id == "first_spells");
        Assert.Contains(GameSession.NewGame(TestData.Game, 1, "priest").Player.Inventory.Pack, i => i.Kind.Id == "novices_handbook");
        Assert.Contains(GameSession.NewGame(TestData.Game, 1, "druid").Player.Inventory.Pack, i => i.Kind.Id == "lesser_charms");
    }

    [Fact]
    public void Experience_RaisesLevels_HitPoints_Skills_AndMana()
    {
        var game = GameSession.NewGame(TestData.Game, 2, "mage");
        var messages = Messages(game);
        var hp = game.Player.MaxHp;
        var melee = game.Player.SkillMelee;

        game.GainExperience(game.ExperienceForLevel(9)); // straight to level 10

        Assert.Equal(10, game.Player.Level);
        Assert.Contains("Welcome to level 10.", messages);
        Assert.True(game.Player.MaxHp >= hp);
        Assert.Equal(35 + 15 * 10 / 10, game.Player.SkillMelee); // base + 15 per 10 levels
        Assert.True(game.Player.SkillMelee > melee);
        Assert.Equal(1 + StatTables.ManaPerLevel[StatTables.Index(18)] * 10 / 100, game.Player.MaxMana);
    }

    [Fact]
    public void KillingMonsters_CanLevelYouUp()
    {
        var game = Arena.Create();
        var ogre = Arena.AddMonster(game, "wormtongue", new Loc(8, 2));
        game.DamageMonster(ogre, 100_000);
        Assert.True(game.Player.Level > 1);
    }

    // --- Studying ---------------------------------------------------------------------------------

    [Fact]
    public void Studying_IsLimitedByLevelAndBooks()
    {
        var game = GameSession.NewGame(TestData.Game, 3, "mage");
        // Angband 4.2's [First Spells]: three level-1 spells.
        Assert.Equal(["magic_missile", "light_room", "find_traps_doors"], game.StudyableSpells().Select(s => s.Id));

        Assert.False(game.Execute(new StudyCommand("detect_monsters"))); // level 3 spell
        Assert.True(game.Execute(new StudyCommand("magic_missile")));
        Assert.True(game.Execute(new StudyCommand("light_room")));
        Assert.True(game.Execute(new StudyCommand("find_traps_doors")));
        Assert.Empty(game.StudyableSpells());

        game.GainExperience(game.ExperienceForLevel(4));
        Assert.Contains(game.StudyableSpells(), s => s.Id == "detect_monsters");
        Assert.DoesNotContain(game.StudyableSpells(), s => s.Id == "frost_bolt"); // needs the second book
    }

    [Fact]
    public void Warriors_CannotStudy()
    {
        var game = GameSession.NewGame(TestData.Game, 3, "warrior");
        var messages = Messages(game);
        Assert.False(game.Execute(new StudyCommand()));
        Assert.Contains("You cannot learn any spells!", messages);
    }

    // --- Casting --------------------------------------------------------------------------------

    [Fact]
    public void FailChance_FollowsAngbandsFormula()
    {
        var game = GameSession.NewGame(TestData.Game, 4, "mage");
        var missile = TestData.Game.Spell("magic_missile")!;
        // base 22, INT 18 takes 2 off, level difference 0.
        Assert.Equal(20, game.SpellFailChance(missile));
        game.GainExperience(game.ExperienceForLevel(4)); // level 5
        Assert.Equal(22 - 12 - 2, game.SpellFailChance(missile));
        game.Player.Mana = 0;
        Assert.Equal(22 - 12 - 2 + 5, game.SpellFailChance(missile));
    }

    [Fact]
    public void MagicMissile_HitsTheNearestMonster_UsesMana_AndGivesFirstCastExperience()
    {
        var game = Caster("mage", 5);
        Learn(game, "magic_missile");
        var mold = Arena.AddMonster(game, "grey_mold", new Loc(9, 2));
        mold.Hp = mold.MaxHp = 10_000;
        game.GainExperience(0);
        var exp = game.Player.Experience;

        var hit = false;
        for (var i = 0; i < 20 && !hit; i++)
        {
            game.Player.Mana = game.Player.MaxMana;
            Assert.True(game.Execute(new CastCommand("magic_missile")));
            hit = mold.Hp < 10_000;
        }

        Assert.True(hit);
        Assert.Equal(exp + 4 * 1, game.Player.Experience);
        Assert.Contains("magic_missile", game.Player.CastSpells);
    }

    [Fact]
    public void CastingWithoutEnoughMana_NeedsConsent_AndCausesFainting()
    {
        var game = Caster("mage", 6);
        Learn(game, "magic_missile");
        Arena.AddMonster(game, "grey_mold", new Loc(9, 2)).Hp = 10_000;
        game.Player.Mana = 0;
        var messages = Messages(game);

        Assert.False(game.Execute(new CastCommand("magic_missile")));
        Assert.Contains(messages, m => m.StartsWith("You do not have enough mana"));

        Assert.True(game.Execute(new CastCommand("magic_missile", AllowOverexert: true)));
        Assert.Contains("You faint from the effort!", messages);
        Assert.Contains("You are paralysed!", messages);
    }

    [Fact]
    public void BlindOrConfusedCasters_CannotCast()
    {
        var game = Caster("mage", 7);
        Learn(game, "magic_missile");
        game.IncreaseTimed(TimedIds.Blind, 10);
        Assert.False(game.Execute(new CastCommand("magic_missile")));
        game.Player.Timed.Clear();
        game.IncreaseTimed(TimedIds.Confused, 10);
        Assert.False(game.Execute(new CastCommand("magic_missile")));
    }

    [Fact]
    public void StinkingCloud_HitsAGroup_WithFalloff_AndPoisonImmunityHelps()
    {
        // In Angband 4.2 it's the druid's: a radius-2 ball of level/2 + 10.
        var game = Caster("druid", 8,
            "###############",
            "#,,,,,,,,,,,,,#",
            "#,,@,,,,,,,,,,#",
            "#,,,,,,,,,,,,,#",
            "###############");
        game.Player.Level = 11;
        game.Player.LearnedSpells.Add("stinking_cloud");
        game.Player.Mana = game.Player.MaxMana = 50;
        game.Player.NaturalStats["wis"] = game.Player.Stats["wis"] = 40; // never fail
        var centre = Arena.AddMonster(game, "soldier_ant", new Loc(10, 2)); // (4.2.5's kobolds resist poison)
        var side = Arena.AddMonster(game, "soldier_ant", new Loc(11, 2));
        var immune = Arena.AddMonster(game, "grey_mold", new Loc(10, 3)); // IM_POIS
        foreach (var m in new[] { centre, side, immune }) m.Hp = m.MaxHp = 10_000;
        var hits = new Dictionary<int, int>();
        game.Events.Subscribe<PlayerAttackEvent>(e => hits[e.MonsterId] = e.Damage);

        // Level 11 still has a small failure chance: cast until it goes off.
        for (var i = 0; i < 10 && !hits.ContainsKey(centre.Id); i++)
        {
            game.Player.Mana = game.Player.MaxMana;
            Assert.True(game.Execute(new CastCommand("stinking_cloud", centre.Position)));
        }

        var full = 11 / 2 + 10;
        Assert.Equal(full, hits[centre.Id]);
        Assert.Equal(full / 2, hits[side.Id]);
        Assert.Equal(full / 2 / 9, hits[immune.Id]);
    }

    [Fact]
    public void CallLight_LightsTheRoom_AndBurnsLightSensitiveMonsters()
    {
        var game = Caster("priest", 9,
            "###############",
            "#.............#",
            "#..@..........#",
            "#.............#",
            "###############");
        foreach (var p in game.Level.AllLocs()) game.Level[p].Flags |= Angband.Core.World.SquareFlags.Room;
        foreach (var p in game.Level.AllLocs()) game.Level[p].Flags &= ~Angband.Core.World.SquareFlags.Glow;
        Learn(game, "call_light");
        game.Player.Level = 10; // Angband 4.2: 2d(level/2) to light-sensitive monsters
        game.Player.NaturalStats["wis"] = game.Player.Stats["wis"] = 40;
        var orc = Arena.AddMonster(game, "cave_orc", new Loc(10, 2)); // HURT_LIGHT
        orc.Hp = orc.MaxHp = 10_000;

        game.Execute(new CastCommand("call_light"));

        Assert.True(game.Level[new Loc(13, 3)].Has(Angband.Core.World.SquareFlags.Glow));
        Assert.True(orc.Hp < 10_000);
    }

    [Fact]
    public void StoneToMud_DigsThroughRock_ButNotPermanentWalls()
    {
        // In Angband 4.2 it's the druid's (and ranger's) Turn Stone to Mud.
        var game = Caster("druid", 10,
            "#########",
            "#,@,#,,,#",
            "#########");
        game.Player.Level = 30; // well above the spell's level: never fails
        game.Player.LearnedSpells.Add("earth_to_dust");
        game.Player.Mana = game.Player.MaxMana = 50;
        game.Player.NaturalStats["wis"] = game.Player.Stats["wis"] = 40;
        game.Player.Inventory.Add(game.Objects.Create("gifts_of_nature"));

        Assert.False(game.Execute(new CastCommand("earth_to_dust"))); // needs a direction
        Assert.True(game.Execute(new CastCommand("earth_to_dust", Direction: Direction.East)));
        Assert.True(game.Level.IsFloor(new Loc(4, 1)));

        var messages = Messages(game);
        game.Level[new Loc(2, 0)].Feature = TestData.Game.Terrain.Ids.Permanent;
        game.Execute(new CastCommand("earth_to_dust", Direction: Direction.North));
        Assert.Contains("The wall resists.", messages);
        Assert.False(game.Level.IsFloor(new Loc(2, 0)));
    }

    // --- Buffs ----------------------------------------------------------------------------------

    [Fact]
    public void Bless_ImprovesArmourAndAccuracy_UntilItExpires()
    {
        var game = Caster("priest", 11);
        game.Player.Level = 5;
        game.Player.LearnedSpells.Add("bless");
        game.Player.NaturalStats["wis"] = game.Player.Stats["wis"] = 40;
        game.Player.Mana = game.Player.MaxMana = 20;
        var armour = game.Player.Armour;
        var toHit = game.Player.ToHit;

        game.Execute(new CastCommand("bless"));
        Assert.Equal(armour + 5, game.Player.Armour);
        Assert.Equal(toHit + 10, game.Player.ToHit);

        TestGames.HoldUntil(game, game.GameTurn + 10 * 30);
        Assert.Equal(armour, game.Player.Armour);
    }

    [Fact]
    public void Heroes_AreNotAfraid()
    {
        var game = Arena.Create();
        game.IncreaseTimed(TimedIds.Afraid, 20);
        game.IncreaseTimed(TimedIds.Hero, 20);
        Assert.False(game.Player.Timed.Has(TimedIds.Afraid));
        game.IncreaseTimed(TimedIds.Afraid, 20);
        Assert.False(game.Player.Timed.Has(TimedIds.Afraid));
    }

    [Fact]
    public void TemporaryResist_StacksWithPermanent()
    {
        var game = Arena.Create();
        game.IncreaseTimed("oppose_pois", 20);
        Assert.Equal(1, game.Player.Resists["pois"]);
        game.Player.IntrinsicResists["pois"] = 1;
        game.RecalculateBonuses();
        Assert.Equal(2, game.Player.Resists["pois"]);
    }

    [Fact]
    public void Regeneration_And_ManaRegen_Work()
    {
        int HealedAfter(bool regen)
        {
            var game = Arena.Create();
            game.Player.MaxHp = 500;
            game.Player.Hp = 1;
            if (regen) game.IncreaseTimed(TimedIds.Regen, 1000);
            TestGames.HoldUntil(game, game.GameTurn + 10 * 50);
            return game.Player.Hp;
        }
        Assert.True(HealedAfter(true) > HealedAfter(false) * 3 / 2);

        var mage = GameSession.NewGame(TestData.Game, 12, "mage");
        mage.GainExperience(mage.ExperienceForLevel(19));
        mage.Player.Mana = 0;
        mage.Player.Hp = mage.Player.MaxHp = 100_000; // standing still in town for 200 turns invites trouble
        TestGames.HoldUntil(mage, mage.GameTurn + 10 * 200);
        Assert.True(mage.Player.Mana > 0);
    }

    [Fact]
    public void DetectEvil_OnlyFindsEvil()
    {
        var game = Caster("priest", 13, "#############################", "#,,,,,,,,,,,,,,,,,,,,,,,,,,,#", "#,@,,,,,,,,,,,,,,,,,,,,,,,,,#", "#############################");
        Learn(game, "detect_evil");
        game.Player.NaturalStats["wis"] = game.Player.Stats["wis"] = 40;
        var kobold = Arena.AddMonster(game, "kobold", new Loc(20, 1));    // EVIL
        var lizard = Arena.AddMonster(game, "rock_lizard", new Loc(22, 1)); // not evil
        game.Level[new Loc(20, 1)].Flags &= ~(Angband.Core.World.SquareFlags.Glow);
        foreach (var p in game.Level.AllLocs()) game.Level[p].Flags &= ~Angband.Core.World.SquareFlags.Glow;
        game.Player.Inventory.Light!.Fuel = 0;
        game.RecalculateBonuses();

        game.Execute(new CastCommand("detect_evil"));

        Assert.True(kobold.IsDetected);
        Assert.False(lizard.IsDetected);
    }

    [Fact]
    public void Portal_RangeGrowsWithLevel()
    {
        var game = Caster("priest", 14, "##################################################",
            "#,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,#",
            "#,@,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,#",
            "##################################################");
        game.Player.Level = 10;
        game.Player.LearnedSpells.Add("portal");
        game.Player.Inventory.Add(game.Objects.Create("healing_and_sanctuary")); // a dungeon book in 4.2
        game.Player.NaturalStats["wis"] = game.Player.Stats["wis"] = 40;
        game.Player.Mana = game.Player.MaxMana = 20;
        var start = game.Player.Position;
        for (var i = 0; i < 10 && game.Player.Position == start; i++) // the spell can fail (7%)
        {
            game.Player.Mana = 20;
            game.Execute(new CastCommand("portal"));
        }
        Assert.InRange(start.DistanceTo(game.Player.Position), 17, 35); // L/2 + 30
    }

    // --- Expressions and data -------------------------------------------------------------------

    [Theory]
    [InlineData("3+(L-1)/5", 1, 3)]
    [InlineData("3+(L-1)/5", 11, 5)]
    [InlineData("L*3", 7, 21)]
    [InlineData("10+L*3/2", 13, 29)]
    [InlineData("-L+20", 5, 15)]
    public void SpellExpressions_Evaluate(string expr, int level, int expected) =>
        Assert.Equal(expected, SpellExpr.Eval(expr, level));

    [Fact]
    public void BadSpellData_IsReported()
    {
        var dir = Directory.CreateTempSubdirectory("avaband-magic-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, DataLoader.SpellsFile), """
                [ { "id": "x", "name": "X", "realm": "arcane", "book": "novices_handbook", "effect": "zap:1" },
                  { "id": "y", "name": "Y", "realm": "arcane", "book": "magic_for_beginners", "effect": "bolt:none:(L:4",
                    "classes": { "bard": {} } } ]
                """);
            var ex = Assert.Throws<GameDataException>(() => DataLoader.Load(DataLoader.DefaultDataDirectory, dir));
            foreach (var expected in new[] { "zap", "not a Arcane book", "bard", "bad expression" })
                Assert.Contains(expected, ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Casting_IsDeterministic()
    {
        static string Run()
        {
            var game = Caster("mage", 15);
            Learn(game, "magic_missile");
            var mold = Arena.AddMonster(game, "grey_mold", new Loc(9, 2));
            for (var i = 0; i < 10; i++) { game.Player.Mana = 2; game.Execute(new CastCommand("magic_missile")); }
            return $"{mold.Hp} {game.Player.Experience} {game.GameTurn}";
        }
        Assert.Equal(Run(), Run());
    }
}
