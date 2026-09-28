using Angband.Core.Game;

namespace Angband.Tests;

/// <summary>
/// Angband 4.2's "of Flame" and "of Frost": a weapon ego (common, shallow, with the resistance) and
/// an ammunition ego (rarer, deeper, without) — once AVABand's single "of Burning" / "of Freezing".
/// </summary>
public class FlameFrostEgoTests
{
    [Theory]
    [InlineData("burning", "flame_ammo", "of Flame", "fire")]
    [InlineData("freezing", "frost_ammo", "of Frost", "cold")]
    public void WeaponsAndAmmunition_HaveTheirOwnVersion(string weaponId, string ammoId, string name, string element)
    {
        var weapon = TestData.Game.Egos.Single(e => e.Id == weaponId); // (the old id, so saves still load)
        var ammo = TestData.Game.Egos.Single(e => e.Id == ammoId);
        Assert.Equal(name, weapon.Name);
        Assert.Equal(name, ammo.Name);
        Assert.Equal(["sword", "hafted", "polearm"], weapon.Bases);
        Assert.Equal(["shot", "arrow", "bolt"], ammo.Bases);
        Assert.Equal((1, 50, 40), (weapon.Level, weapon.MaxDepth, weapon.Commonness));
        Assert.Equal((10, 100, 10), (ammo.Level, ammo.MaxDepth, ammo.Commonness));
        Assert.Contains(element, weapon.Resists);
        Assert.DoesNotContain(element, ammo.Resists);
        Assert.All(new[] { weapon, ammo }, e => Assert.Contains(element, e.Ignore));
        Assert.All(new[] { weapon, ammo }, e => Assert.Contains(e.Brands, b => b.Element == element && b.Multiplier == 3));
    }

    [Fact]
    public void AnArrowOfFlame_IsSafeFromFire_ButNotFromAcid()
    {
        var game = GameSession.NewGame(TestData.Game, 3);
        var arrow = game.Objects.Create("arrow", 10);
        arrow.Ego = TestData.Game.Egos.Single(e => e.Id == "flame_ammo");
        Assert.False(arrow.HarmedBy("fire"));
        Assert.True(arrow.HarmedBy("acid"));
        Assert.Contains("of Flame", game.Describe(arrow));
    }

    [Fact]
    public void TheOldNames_AreGone() =>
        Assert.DoesNotContain(TestData.Game.Egos, e => e.Name is "of Burning" or "of Freezing");
}
