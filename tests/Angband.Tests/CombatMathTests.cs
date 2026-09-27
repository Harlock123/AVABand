using Angband.Core.Combat;
using Angband.Core.Randomness;

namespace Angband.Tests;

public class CombatMathTests
{
    private static double HitRate(int chance, int armour, bool visible, ulong seed = 1, int samples = 40_000)
    {
        var rng = new GameRandom(seed);
        var hits = 0;
        for (var i = 0; i < samples; i++)
            if (CombatMath.TestHit(rng, chance, armour, visible)) hits++;
        return hits / (double)samples;
    }

    [Fact]
    public void TestHit_Always12PercentHit_And5PercentMiss()
    {
        Assert.InRange(HitRate(0, 10_000, visible: true), 0.11, 0.13);
        Assert.InRange(HitRate(1_000_000, 0, visible: true), 0.94, 0.96);
    }

    [Theory]
    [InlineData(70, 10, true)]
    [InlineData(70, 60, true)]
    [InlineData(100, 30, false)]
    [InlineData(40, 90, true)]
    public void TestHit_MatchesAnalyticProbability(int chance, int armour, bool visible)
    {
        Assert.InRange(HitRate(chance, armour, visible), CombatMath.HitProbability(chance, armour, visible) - 0.01,
            CombatMath.HitProbability(chance, armour, visible) + 0.01);
    }

    [Fact]
    public void UnseenTargets_AreHarderToHit() =>
        Assert.True(CombatMath.HitProbability(80, 40, visible: false) < CombatMath.HitProbability(80, 40, visible: true));

    [Fact]
    public void Chances_IncludeBonusesAndRange()
    {
        Assert.Equal(70 + 5 * 3, CombatMath.MeleeChance(70, 5));
        Assert.Equal(55 + 2 * 3 - 7, CombatMath.MissileChance(55, 2, 7));
    }

    [Fact]
    public void CriticalMelee_NeverHappensWithHopelessOdds()
    {
        var rng = new GameRandom(3);
        for (var i = 0; i < 5000; i++)
        {
            Assert.Equal(10, CombatMath.CriticalMelee(rng, 0, -100, 0, 10, out var grade));
            Assert.Equal(CriticalGrade.None, grade);
        }
    }

    [Fact]
    public void CriticalMelee_DamageMatchesGrade()
    {
        var rng = new GameRandom(4);
        var seen = new HashSet<CriticalGrade>();
        for (var i = 0; i < 20_000; i++)
        {
            var dmg = CombatMath.CriticalMelee(rng, 6000, 0, 60, 10, out var grade);
            seen.Add(grade);
            var expected = grade switch
            {
                CriticalGrade.Good => 25,
                CriticalGrade.Great => 30,
                CriticalGrade.Superb => 45,
                CriticalGrade.HighGreat => 50,
                CriticalGrade.HighSuperb => 60,
                _ => 10,
            };
            Assert.Equal(expected, dmg);
        }
        // A 600 lb "weapon" always crits; power = 6000 + d650 lands in the top band.
        Assert.Equal([CriticalGrade.HighSuperb], seen);
    }

    [Fact]
    public void CriticalShot_TopsOutAtSuperb()
    {
        var rng = new GameRandom(5);
        for (var i = 0; i < 2000; i++)
        {
            var dmg = CombatMath.CriticalShot(rng, 6000, 0, 1, 10, out var grade);
            Assert.Equal(CriticalGrade.Superb, grade);
            Assert.Equal(45, dmg);
        }
    }

    [Theory]
    [InlineData(100, 0, 100)]
    [InlineData(100, 100, 75)]
    [InlineData(100, 240, 40)]
    [InlineData(100, 1000, 40)]
    public void ArmourReduce_SoaksUpToSixtyPercent(int damage, int armour, int expected) =>
        Assert.Equal(expected, CombatMath.ArmourReduce(damage, armour));

    [Fact]
    public void ResistElement_BaseElements()
    {
        var fire = TestData.Game.Element("fire")!;
        var rng = new GameRandom(1);
        Assert.Equal(30, CombatMath.ResistElement(rng, fire, 30, 0));
        Assert.Equal(10, CombatMath.ResistElement(rng, fire, 30, 1));
        Assert.Equal(3, CombatMath.ResistElement(rng, fire, 30, 2));
        Assert.Equal(0, CombatMath.ResistElement(rng, fire, 30, 3));
        Assert.Equal(40, CombatMath.ResistElement(rng, fire, 30, -1));
    }

    [Fact]
    public void ResistElement_HighElementsUseRandomDivisor()
    {
        var chaos = TestData.Game.Element("chaos")!;
        var rng = new GameRandom(2);
        var results = Enumerable.Range(0, 2000).Select(_ => CombatMath.ResistElement(rng, chaos, 120, 1)).ToHashSet();
        Assert.All(results, r => Assert.InRange(r, 120 * 6 / 12, 120 * 6 / 7));
        Assert.True(results.Count > 3);
    }

    [Fact]
    public void MonsterCritical_OnlyForNearMaximumBlows()
    {
        var rng = new GameRandom(6);
        var dice = new Dice(6, 8); // max 48
        Assert.Equal(0, CombatMath.MonsterCritical(rng, dice, 30));
        for (var i = 0; i < 200; i++) Assert.InRange(CombatMath.MonsterCritical(rng, dice, 48), 7, 20);
        Assert.InRange(CombatMath.MonsterCritical(rng, dice, 46), 6, 20);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(200, 50)]
    [InlineData(250, 40)]
    [InlineData(350, 28)]
    public void BlowEnergy_SupportsFractionalBlows(int blows, int energy) =>
        Assert.Equal(energy, CombatMath.BlowEnergy(blows, 100));

    [Fact]
    public void KillExperience_DividesByPlayerLevelWithFraction()
    {
        Assert.Equal((150, 0), CombatMath.KillExperience(30, 5, 1));
        var (whole, frac) = CombatMath.KillExperience(30, 5, 7);
        Assert.Equal(21, whole);
        Assert.Equal(3 * 0x10000 / 7, frac);
    }

    [Fact]
    public void MonsterFear_MoreLikelyWhenBadlyHurt()
    {
        var rng = new GameRandom(7);
        var healthy = Enumerable.Range(0, 5000).Count(_ => CombatMath.MonsterFearOnHit(rng, 1, 99, 100) > 0);
        var dying = Enumerable.Range(0, 5000).Count(_ => CombatMath.MonsterFearOnHit(rng, 30, 5, 100) > 0);
        Assert.Equal(0, healthy);
        Assert.True(dying > 4000, $"{dying}");
    }
}
