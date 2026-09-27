using Angband.Core.Definitions;
using Angband.Core.Items;

namespace Angband.Tests;

public class ItemKnowledgeTests
{
    private static (ObjectFactory F, PlayerKnowledge K) Setup(ulong seed = 1) =>
        (new ObjectFactory(TestData.Game), new PlayerKnowledge(TestData.Game, seed));

    [Theory]
    [InlineData("Ration~ of Food", false, "Ration of Food")]
    [InlineData("Ration~ of Food", true, "Rations of Food")]
    [InlineData("Wooden Torch~", true, "Wooden Torches")]
    [InlineData("Set~ of Leather Gloves", true, "Sets of Leather Gloves")]
    [InlineData("Iron Shot~", true, "Iron Shots")]
    public void Plurals_UseTheTildeMarker(string name, bool plural, string expected) =>
        Assert.Equal(expected, ItemNaming.Plain(name, plural));

    [Fact]
    public void Weapon_HidesBonusesUntilRunesAreKnown()
    {
        var (f, k) = Setup();
        var dagger = f.Create("dagger");
        dagger.ToHit = 3;
        dagger.ToDam = 4;

        Assert.Equal("a Dagger (1d4) {??}", ItemNaming.Describe(dagger, k));
        k.LearnRune(RuneIds.ToHit);
        k.LearnRune(RuneIds.ToDam);
        Assert.Equal("a Dagger (1d4) (+3,+4)", ItemNaming.Describe(dagger, k));
    }

    [Fact]
    public void EgoName_AppearsOnlyWhenFullyKnown()
    {
        var (f, k) = Setup();
        var sword = f.Create("long_sword");
        sword.Ego = TestData.Game.Egos.Single(e => e.Id == "slay_animal");
        sword.Slays.AddRange(sword.Ego.Slays);
        k.LearnRune(RuneIds.ToHit);
        k.LearnRune(RuneIds.ToDam);

        Assert.DoesNotContain("Slay Animal", ItemNaming.Describe(sword, k));
        k.LearnRune(RuneIds.Slay("ANIMAL"));
        Assert.Equal("a Long Sword of Slay Animal (2d5) (+0,+0)", ItemNaming.Describe(sword, k));
    }

    [Fact]
    public void Potions_ShowFlavourUntilLearned()
    {
        var (f, k) = Setup();
        var potion = f.Create("cure_light_wounds", 2);
        var flavor = k.Flavor(potion.Kind)!.Name;

        Assert.Equal($"2 {flavor} Potions", ItemNaming.Describe(potion, k));
        k.LearnKind(potion.Kind);
        Assert.Equal("2 Potions of Cure Light Wounds", ItemNaming.Describe(potion, k));
    }

    [Fact]
    public void Scrolls_HaveRandomTitles()
    {
        var (f, k) = Setup();
        var scroll = f.Create("phase_door");
        Assert.Matches("^a Scroll titled \"[a-z ]+\"$", ItemNaming.Describe(scroll, k));
    }

    [Fact]
    public void Flavours_AreFixedPerSeed_UniqueWithinAGroup_AndDifferBetweenGames()
    {
        var potions = TestData.Game.Objects.Where(o => o.Base == "potion").ToList();
        string Names(ulong seed)
        {
            var k = new PlayerKnowledge(TestData.Game, seed);
            return string.Join(",", potions.Select(p => k.Flavor(p)!.Name));
        }

        Assert.Equal(Names(5), Names(5));
        Assert.NotEqual(Names(5), Names(6));
        Assert.Equal(potions.Count, Names(5).Split(',').Distinct().Count());
    }

    [Fact]
    public void Curses_ShowOnceKnown()
    {
        var (f, k) = Setup();
        var ring = f.Create("ring_of_teleportation");
        k.LearnKind(ring.Kind);
        Assert.DoesNotContain("{cursed}", ItemNaming.Describe(ring, k));
        k.LearnRune(RuneIds.Curse("teleportation"));
        k.LearnRune(RuneIds.Modifier("speed"));
        Assert.Equal("a Ring of Teleportation <+2> {cursed}", ItemNaming.Describe(ring, k));
    }

    [Fact]
    public void Lights_ShowTheirFuel()
    {
        var (f, k) = Setup();
        var torches = f.Create("wooden_torch", 2);
        k.LearnRune(RuneIds.Modifier("light"));
        Assert.Equal("2 Wooden Torches <+2> (5000 turns)", ItemNaming.Describe(torches, k));
    }

    [Fact]
    public void Stacking_RequiresIdenticalProperties()
    {
        var (f, _) = Setup();
        var a = f.Create("iron_shot", 10);
        var b = f.Create("iron_shot", 5);
        Assert.True(a.CanStackWith(b));
        b.ToDam = 1;
        Assert.False(a.CanStackWith(b));

        var part = a.Split(f.NextSerial++, 4);
        Assert.Equal(6, a.Number);
        Assert.Equal(4, part.Number);
        Assert.NotEqual(a.Serial, part.Serial);
    }
}
