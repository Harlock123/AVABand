using Angband.Core.Items;
using Angband.Core.Randomness;

namespace Angband.Tests;

/// <summary>Angband 4.2.5's make_object and apply_magic: how often things are good, great, cursed and deep.</summary>
public class GenerationData425Tests
{
    private static ObjectFactory Factory() => new(TestData.Game) { AllowArtifacts = false };

    [Fact]
    public void Kinds_are_made_below_the_deepest_allocation_level()
    {
        // 4.2.5 allocates no deeper than 100: a kind "to 100" is still found at 5000 ft and below.
        var f = Factory();
        var rng = new GameRandom(1);
        Assert.All(Enumerable.Range(0, 200), _ => Assert.NotNull(f.PickKind(rng, 120)));
    }

    [Fact]
    public void A_third_and_more_of_things_are_good_and_three_in_ten_of_those_great()
    {
        var f = Factory();
        var rng = new GameRandom(2);
        int good = 0, great = 0;
        const int n = 4000;
        for (var i = 0; i < n; i++)
        {
            var sword = f.Create("long_sword");
            f.ApplyMagic(rng, sword, 0);
            if (sword.ToHit > 0) good++;
            if (sword.Ego is not null) great++;
        }
        Assert.InRange(good, n * 28 / 100, n * 38 / 100); // 33% at the surface
        Assert.InRange(great, 1, good * 40 / 100);        // egos only from the great ones
    }

    [Fact]
    public void Good_drops_are_4_2_5s_good_kinds()
    {
        var f = Factory();
        Assert.True(f.IsGoodKind(TestData.Game.Object("ring_of_speed")!)); // GOOD
        Assert.True(f.IsGoodKind(TestData.Game.Object("arrow")!));
        Assert.True(f.IsGoodKind(TestData.Game.Object("long_sword")!));
        Assert.False(f.IsGoodKind(TestData.Game.Object("cure_light_wounds")!));
        Assert.False(f.IsGoodKind(TestData.Game.Object("ring_of_teleportation")!));
        var rng = new GameRandom(3);
        for (var i = 0; i < 300; i++)
            Assert.True(f.IsGoodKind(f.Make(rng, 30, good: true)!.Kind));
    }

    [Fact]
    public void A_ring_of_speed_can_be_super_charged()
    {
        var f = Factory();
        var rng = new GameRandom(4);
        var speeds = Enumerable.Range(0, 400).Select(_ =>
        {
            var ring = f.Create("ring_of_speed");
            f.ApplyMagic(rng, ring, 80);
            return ring.Modifier("speed");
        }).ToList();
        var basic = speeds.Min();
        Assert.Contains(speeds, s => s > basic + 1);
    }

    [Fact]
    public void Great_melee_weapons_sometimes_get_more_dice()
    {
        var f = Factory();
        var rng = new GameRandom(5);
        var kind = TestData.Game.Object("dagger")!;
        var bigger = 0;
        for (var i = 0; i < 3000; i++)
        {
            var dagger = f.Create(kind);
            f.ApplyMagic(rng, dagger, 50, good: true, great: true);
            if (dagger.Damage.Count * dagger.Damage.Sides > kind.Damage.Count * kind.Damage.Sides) bigger++;
        }
        Assert.True(bigger > 0);
    }
}
