using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Randomness;

namespace Angband.Tests;

/// <summary>
/// The artifacts brought into line with Angband 4.2.5's artifact.txt, and what came with them:
/// allocation by chance and depth range, the special artifacts made as themselves, their own
/// weights, immunities, the STICKY One Ring, damage reduction and extra moves.
/// </summary>
public class ArtifactData425Tests
{
    private static ArtifactDef Art(string id) => TestData.Game.Artifacts.Single(a => a.Id == id);

    private static Item Wear(GameSession game, string artifact)
    {
        var item = game.Player.Inventory.Add(game.Objects.CreateArtifact(Art(artifact)))!;
        game.Execute(new WieldCommand(item));
        Assert.Contains(item, game.Player.Inventory.Equipped);
        return item;
    }

    [Fact]
    public void Artifacts_have_4_2_5s_values()
    {
        var narthanc = Art("narthanc");
        Assert.Equal((9, 12, 10), (narthanc.ToHit, narthanc.ToDam, narthanc.ToAc));
        Assert.Equal((1, 40), (narthanc.Level, narthanc.AllocChance)); // alloc:40:1 to 50
        Assert.Equal(1000, new ObjectFactory(TestData.Game).CreateArtifact(Art("grond")).Weight); // 100 lb
    }

    [Fact]
    public void An_artifact_is_never_made_beyond_its_depths()
    {
        var art = Art("of_wormtongue"); // boots, 15 to 80
        Assert.Equal((15, 80), (art.Level, art.MaxDepth));
        var factory = new ObjectFactory(TestData.Game);
        var rng = new GameRandom(3);
        for (var i = 0; i < 3000; i++)
        {
            var boots = factory.Create(art.Kind);
            factory.TryMakeArtifact(rng, boots, 90);
            Assert.NotEqual(art.Id, boots.Artifact?.Id);
        }
        var made = false;
        for (var i = 0; i < 3000 && !made; i++)
        {
            var boots = factory.Create(art.Kind);
            factory.TryMakeArtifact(rng, boots, 40);
            made = boots.Artifact?.Id == art.Id;
        }
        Assert.True(made);
    }

    [Fact]
    public void Special_artifacts_are_made_as_themselves()
    {
        var factory = new ObjectFactory(TestData.Game);
        var rng = new GameRandom(11);
        var made = new List<Item>();
        for (var i = 0; i < 200; i++)
            if (factory.MakeSpecialArtifact(rng, 30) is { } item) made.Add(item);
        Assert.NotEmpty(made);
        Assert.All(made, i => Assert.True(i.Kind.IsSpecialArtifactKind));
        Assert.Contains(made, i => i.Artifact!.Id == "galadriel");
        Assert.Equal(made.Count, made.Select(i => i.Artifact!.Id).Distinct().Count()); // each once
        Assert.Null(factory.MakeSpecialArtifact(rng, 0)); // never in the town
    }

    [Fact]
    public void Immunity_makes_an_element_harmless()
    {
        var game = Arena.Create(3);
        Wear(game, "of_azaghal");
        Assert.Equal(3, game.Player.Resists["fire"]);
        var hp = game.Player.Hp;
        game.ElementalHit("fire", 200, "a test");
        Assert.Equal(hp, game.Player.Hp);
    }

    [Fact]
    public void The_One_Ring_will_not_come_off()
    {
        var game = Arena.Create(4);
        var ring = Wear(game, "the_one_ring");
        game.Execute(new TakeOffCommand(ring));
        Assert.Contains(ring, game.Player.Inventory.Equipped);

        // Another ring goes on the other hand; a third can't displace the One Ring.
        var other = game.Player.Inventory.Add(game.Objects.Create("ring_of_protection"))!;
        game.Execute(new WieldCommand(other));
        Assert.Contains(other, game.Player.Inventory.Equipped);
        var third = game.Player.Inventory.Add(game.Objects.Create("ring_of_open_wounds"))!;
        game.Execute(new WieldCommand(third));
        Assert.Contains(ring, game.Player.Inventory.Equipped);
    }

    [Fact]
    public void Belegennon_turns_aside_some_of_every_hurt()
    {
        var game = Arena.Create(5);
        Wear(game, "belegennon");
        Assert.Equal(5, game.DamageReduction);
        game.Player.Hp = game.Player.MaxHp = 1000;
        game.TakeHit(12, "a test");
        Assert.Equal(993, game.Player.Hp);
        game.TakeHit(4, "a test");
        Assert.Equal(993, game.Player.Hp);
    }

    [Fact]
    public void Wormtongues_boots_give_an_extra_move()
    {
        var game = Arena.Create(6);
        var full = game.MoveEnergyPerStep;
        Wear(game, "of_wormtongue");
        Assert.Equal(full / 2, game.MoveEnergyPerStep);
    }
}
