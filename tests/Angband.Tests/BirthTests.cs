using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Magic;
using Angband.Core.Randomness;

namespace Angband.Tests;

public class BirthTests
{
    private static Dictionary<string, int> Stats(int str, int intel, int wis, int dex, int con) =>
        new() { ["str"] = str, ["int"] = intel, ["wis"] = wis, ["dex"] = dex, ["con"] = con };

    private static GameSession Create(string race, string cls, Dictionary<string, int>? stats = null, ulong seed = 1) =>
        GameSession.NewGame(TestData.Game, seed, new CharacterSpec("Tester", race, cls, stats ?? Stats(15, 15, 15, 15, 15)));

    // --- Point-buy -----------------------------------------------------------------------------

    [Theory]
    [InlineData(10, 0)]
    [InlineData(11, 1)]
    [InlineData(14, 4)]
    [InlineData(16, 6)]
    [InlineData(17, 8)]
    [InlineData(18, 12)]
    public void PointBuy_CostsMatchAngband(int stat, int cost) => Assert.Equal(cost, Birth.Cost(stat));

    [Fact]
    public void PointBuy_RejectsOverspendingAndOutOfRangeStats()
    {
        Assert.Null(Birth.ValidatePointBuy(Stats(18, 17, 10, 10, 10), 20));   // 12 + 8 = 20
        Assert.Contains("costs 24", Birth.ValidatePointBuy(Stats(18, 18, 10, 10, 10), 20)); // 12 + 12
        Assert.Contains("between", Birth.ValidatePointBuy(Stats(19, 10, 10, 10, 10), 20));
        Assert.Contains("between", Birth.ValidatePointBuy(Stats(9, 10, 10, 10, 10), 20));
    }

    [Fact]
    public void UnspentPoints_BecomeGold()
    {
        var thrifty = Create("human", "warrior", Stats(10, 10, 10, 10, 10));
        var spender = Create("human", "warrior", Stats(18, 17, 10, 10, 10));
        Assert.Equal(200 + 20 * 50, thrifty.Player.Gold);
        Assert.Equal(200, spender.Player.Gold);
    }

    // --- Rolling -------------------------------------------------------------------------------

    [Fact]
    public void RolledStats_FollowAngbandsDice()
    {
        var rng = new GameRandom(9);
        for (var i = 0; i < 500; i++)
        {
            var stats = Birth.RollStats(rng);
            Assert.All(stats.Values, v => Assert.InRange(v, 8, 17));
            Assert.InRange(stats.Values.Sum() - 25, 36, 44); // dice total strictly between 35 and 45
        }
        Assert.Equal(Birth.RollStats(new GameRandom(3)), Birth.RollStats(new GameRandom(3)));
    }

    // --- Races and classes ---------------------------------------------------------------------

    [Fact]
    public void FinalStats_AddRaceAndClass()
    {
        var game = Create("half_troll", "warrior");
        Assert.Equal(15 + 4 + 3, game.Player.Stats["str"]);   // 18/40
        Assert.Equal(15 - 4 - 2, game.Player.Stats["int"]);
        Assert.Equal("18/40", StatTables.Format(game.Player.Stats["str"]));
    }

    [Fact]
    public void Races_ChangeHitDiceSkillsAndExperience()
    {
        var hobbit = Create("hobbit", "mage");
        var troll = Create("half_troll", "warrior");
        Assert.Equal(7 + 0, hobbit.Player.HitDie);
        Assert.Equal(12 + 9, troll.Player.HitDie);
        Assert.Equal(120, hobbit.Player.ExpFactor); // 4.2.5: the race's alone (classes have none)
        Assert.True(hobbit.Player.Stealth > troll.Player.Stealth);
        Assert.True(troll.Player.SkillMelee > hobbit.Player.SkillMelee);
        var highElf = Create("high_elf", "mage");
        var human = Create("human", "mage");
        Assert.True(highElf.ExperienceForLevel(1) > human.ExperienceForLevel(1)); // 145 against 100
    }

    [Theory]
    [InlineData("dwarf", "blind")]
    [InlineData("gnome", "free_act")]
    [InlineData("kobold", "pois")]
    [InlineData("elf", "light")]
    [InlineData("half_orc", "dark")]
    public void Races_BringInnateProtections(string race, string protection)
    {
        var game = Create(race, "warrior");
        Assert.True(game.Player.Resists.GetValueOrDefault(protection) > 0);
    }

    [Fact]
    public void HalfTrolls_Regenerate()
    {
        int HealedAfter(string race)
        {
            var game = Create(race, "warrior");
            var arena = Arena.Create(2);
            game.UseLevel(arena.Level, arena.Player.Position);
            foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
            game.Player.MaxHp = 500;
            game.Player.Hp = 1;
            TestGames.HoldUntil(game, game.GameTurn + 10 * 50);
            return game.Player.Hp;
        }
        Assert.True(HealedAfter("half_troll") > HealedAfter("human") * 3 / 2);
    }

    [Fact]
    public void Infravision_ShowsWarmMonstersInTheDark()
    {
        static bool Sees(string race)
        {
            var game = Create(race, "warrior");
            var arena = Arena.Create(3,
                "############",
                "#@.........#",
                "############");
            game.UseLevel(arena.Level, arena.Player.Position);
            var kobold = Arena.AddMonster(game, "kobold", new Loc(5, 1)); // four squares away, unlit
            game.UpdateView();
            return kobold.IsVisible;
        }
        Assert.True(Sees("dwarf"));   // infravision 5
        Assert.False(Sees("human"));  // none: the torch only reaches two squares
    }

    // --- Stats matter --------------------------------------------------------------------------

    [Fact]
    public void Strength_RaisesDamageAndCarryingLimit()
    {
        var weak = Create("human", "warrior", Stats(10, 10, 10, 10, 10));
        var strong = Create("human", "warrior", Stats(18, 10, 10, 10, 10));
        Assert.True(strong.Player.ToDam > weak.Player.ToDam);
        Assert.True(strong.Player.WeightLimit > weak.Player.WeightLimit);
    }

    [Fact]
    public void Dexterity_RaisesAccuracyAndArmour()
    {
        var clumsy = Create("human", "warrior", Stats(10, 10, 10, 10, 10));
        var nimble = Create("human", "warrior", Stats(10, 10, 10, 18, 10));
        Assert.True(nimble.Player.ToHit > clumsy.Player.ToHit);
        Assert.True(nimble.Player.Armour > clumsy.Player.Armour);
    }

    [Fact]
    public void Wisdom_RaisesSavingThrow_AndConstitution_HitPoints()
    {
        var a = Create("human", "priest", Stats(10, 10, 10, 10, 10));
        var b = Create("human", "priest", Stats(10, 10, 18, 10, 18));
        Assert.True(b.Player.SkillSave > a.Player.SkillSave);
        a.GainExperience(a.ExperienceForLevel(19));
        b.GainExperience(b.ExperienceForLevel(19));
        Assert.True(b.Player.MaxHp > a.Player.MaxHp);
        Assert.True(b.Player.MaxMana > a.Player.MaxMana);
    }

    [Fact]
    public void Characters_KeepTheirName()
    {
        Assert.Equal("Tester", Create("elf", "mage").Player.Name);
    }
}
