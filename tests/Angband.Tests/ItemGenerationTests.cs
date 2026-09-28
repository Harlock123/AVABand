using Angband.Core.Definitions;
using Angband.Core.Items;
using Angband.Core.Randomness;

namespace Angband.Tests;

public class ItemGenerationTests
{
    private static ObjectFactory Factory() => new(TestData.Game);

    [Fact]
    public void MagicBonus_StaysInRange_AndGrowsWithDepth()
    {
        var rng = new GameRandom(1);
        double Mean(int level) => Enumerable.Range(0, 5000).Average(_ => ObjectFactory.MagicBonus(rng, 10, level));
        for (var i = 0; i < 5000; i++) Assert.InRange(ObjectFactory.MagicBonus(rng, 10, rng.RandInt0(128)), 0, 10);
        Assert.True(Mean(10) < Mean(60));
        Assert.True(Mean(60) < Mean(120));
    }

    [Fact]
    public void PickKind_MostlyRespectsDepth()
    {
        var rng = new GameRandom(2);
        var f = Factory();
        var picks = Enumerable.Range(0, 5000).Select(_ => f.PickKind(rng, 3)!).ToList();
        Assert.True(picks.Count(k => k.Level > 3) < picks.Count / 10, "out-of-depth picks should be rare");
        Assert.DoesNotContain(picks, k => k.Commonness == 0);
    }

    [Fact]
    public void GreatWeapons_OftenBecomeEgos_WithValidBases()
    {
        var rng = new GameRandom(3);
        var f = Factory();
        var egos = 0;
        for (var i = 0; i < 300; i++)
        {
            var item = f.Create("long_sword");
            f.ApplyMagic(rng, item, 30, good: true, great: true);
            if (item.Ego is { } ego)
            {
                egos++;
                Assert.Contains("sword", ego.Bases);
            }
            Assert.True(item.ToHit > 0 || item.IsArtifact);
        }
        Assert.True(egos > 100, $"{egos}");
    }

    [Fact]
    public void BadRolls_CanBeCursed()
    {
        var rng = new GameRandom(4);
        var f = Factory();
        var cursed = 0;
        for (var i = 0; i < 3000; i++)
        {
            var item = f.Create("leather_boots");
            f.ApplyMagic(rng, item, 40);
            if (item.IsCursed) { cursed++; Assert.True(item.ToAc < 0); }
        }
        Assert.True(cursed > 0);
    }

    [Fact]
    public void Artifacts_AreCreatedAtMostOnce()
    {
        var f = Factory();
        var art = TestData.Game.Artifacts.Single(a => a.Id == "narthanc");
        var first = f.CreateArtifact(art);
        Assert.Equal(art, first.Artifact);
        Assert.Equal(6, first.ToDam);
        Assert.Single(first.Brands);

        var rng = new GameRandom(5);
        for (var i = 0; i < 2000; i++)
        {
            var dagger = f.Create("dagger");
            f.TryMakeArtifact(rng, dagger, 50);
            Assert.NotEqual("narthanc", dagger.Artifact?.Id);
        }
    }

    [Fact]
    public void Gold_ScalesWithDepth()
    {
        var rng = new GameRandom(6);
        var f = Factory();
        var shallow = Enumerable.Range(0, 2000).Average(_ => f.MakeGold(rng, 1).GoldValue);
        var deep = Enumerable.Range(0, 2000).Average(_ => f.MakeGold(rng, 50).GoldValue);
        Assert.True(deep > shallow * 2);

        // Angband money_kind: the treasure goes by the value, copper to adamantite across the largest
        // drop at the greatest depth, so shallow finds are nearly all copper and deep ones gems.
        var shallowKinds = Enumerable.Range(0, 1000).Select(_ => f.MakeGold(rng, 1).Kind.Id).ToList();
        Assert.True(shallowKinds.Count(k => k == "copper") > 950);
        var deepKinds = Enumerable.Range(0, 1000).Select(_ => f.MakeGold(rng, 100).Kind.Id).ToHashSet();
        Assert.Contains("garnets", deepKinds);
        Assert.Contains("rubies", deepKinds);
        Assert.DoesNotContain("copper", deepKinds);
    }

    [Fact]
    public void Missiles_GenerateInStacks()
    {
        var rng = new GameRandom(7);
        var f = Factory();
        for (var i = 0; i < 200; i++)
        {
            var item = f.Make(rng, 5);
            if (item is { IsAmmo: true }) Assert.InRange(item.Number, 2, 49); // up to 7d7, as Angband 4.2 piles them
        }
    }

    [Fact]
    public void Serials_AreUnique()
    {
        var f = Factory();
        var rng = new GameRandom(8);
        var serials = Enumerable.Range(0, 500).Select(_ => f.Make(rng, 10)!.Serial).ToList();
        Assert.Equal(serials.Count, serials.Distinct().Count());
    }
}
