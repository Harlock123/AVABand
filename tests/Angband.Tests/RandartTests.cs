using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Persistence;
using Angband.Core.Randomness;

namespace Angband.Tests;

/// <summary>Random artifacts (Angband obj-randart.c, obj-power.c, randname.c).</summary>
public class RandartTests
{
    private static GameSession Randarts(ulong seed = 41)
    {
        var spec = CharacterSpec.Default("human", "warrior") with { Options = new Dictionary<string, bool> { [OptionIds.Randarts] = true } };
        return GameSession.NewGame(TestData.Game, seed, spec);
    }

    [Fact]
    public void ArtifactPower_RanksTheStandardSetSensibly()
    {
        int P(string id) => ArtifactPower.Of(TestData.Game.Artifacts.Single(a => a.Id == id), TestData.Game);
        Assert.True(P("ringil") > P("sting"));
        Assert.True(P("sting") > P("narthanc") || P("sting") >= P("narthanc"));
        Assert.True(P("of_morgoth") > P("galadriel"));
        // Only the harmful ones (Mormegil, Camlost) come out negative, as in Angband.
        Assert.Equal(["camlost", "mormegil"], TestData.Game.Artifacts.Where(a => ArtifactPower.Of(a, TestData.Game) <= 0).Select(a => a.Id).Order());
    }

    [Fact]
    public void ASet_IsTheSameSize_KeepsTheFixedArtifacts_AndIsNamedLikeAngbands()
    {
        var set = RandartGenerator.Generate(TestData.Game, 7);
        Assert.Equal(TestData.Game.Artifacts.Count, set.Count);
        foreach (var id in RandartGenerator.Fixed)
            Assert.Same(TestData.Game.Artifacts.Single(a => a.Id == id), set.Single(a => a.Id == id));
        var random = set.Where(a => !RandartGenerator.Fixed.Contains(a.Id)).ToList();
        Assert.All(random, a =>
        {
            Assert.Matches(@"^('[A-Z][a-z]{4,9}'|of [A-Z][a-z]{4,9})$", a.Name);
            Assert.NotNull(TestData.Game.Object(a.Kind));
            Assert.InRange(a.AllocChance, 1, 99);
            Assert.True(a.MaxDepth >= a.Level);
            Assert.InRange(a.Level, 1, 100);
            Assert.StartsWith("Random ", a.Description);
        });
        Assert.Equal(random.Count, random.Select(a => a.Id).Distinct().Count());
    }

    [Fact]
    public void TheSameSeed_MakesTheSameSet_AndAnotherADifferentOne()
    {
        static string Describe(IEnumerable<ArtifactDef> set) =>
            string.Join("|", set.Select(a => $"{a.Id}:{a.Name}:{a.Kind}:{a.ToHit}:{a.ToDam}:{a.ToAc}:{string.Join(",", a.Modifiers)}:{string.Join(",", a.Resists)}"));
        Assert.Equal(Describe(RandartGenerator.Generate(TestData.Game, 99)), Describe(RandartGenerator.Generate(TestData.Game, 99)));
        Assert.NotEqual(Describe(RandartGenerator.Generate(TestData.Game, 99)), Describe(RandartGenerator.Generate(TestData.Game, 100)));
    }

    [Fact]
    public void TheNewArtifacts_AreAsPowerfulAsTheOldOnesOfTheirKind()
    {
        var data = TestData.Game;
        var standard = data.Artifacts.Where(a => !RandartGenerator.Fixed.Contains(a.Id))
            .GroupBy(a => data.Object(a.Kind)!.Base)
            .ToDictionary(g => g.Key, g => g.Select(a => ArtifactPower.Of(a, data)).ToList());
        var set = RandartGenerator.Generate(data, 5).Where(a => !RandartGenerator.Fixed.Contains(a.Id)).ToList();
        var powers = set.Select(a => ArtifactPower.Of(a, data)).ToList();
        // On the whole, the same strength as the set they replace.
        var oldAverage = standard.Values.SelectMany(p => p).Average();
        Assert.InRange(powers.Average(), oldAverage * 0.7, oldAverage * 1.4);
        // Every kind of item is still represented (at least 80% as many, Angband create_artifact_set).
        foreach (var (b, list) in standard)
            Assert.True(set.Count(a => data.Object(a.Kind)!.Base == b) >= 4 * (list.Count + 1) / 5 - 1, b);
    }

    [Fact]
    public void RandomNames_LookLikeTolkiensNames()
    {
        var rng = new GameRandom(3);
        for (var i = 0; i < 50; i++)
        {
            var name = RandomName.Make(rng, TestData.Game.NameWords, 5, 9);
            Assert.InRange(name.Length, 5, 10);
            Assert.Matches("[aeiou]", name);
        }
    }

    [Fact]
    public void AGameWithRandarts_MakesThemInsteadOfTheStandardOnes_AndKeepsThemWhenSaved()
    {
        var game = Randarts();
        Assert.NotNull(game.RandartSeed);
        Assert.Contains(game.Artifacts, a => a.Id.StartsWith("randart_"));
        var rng = new GameRandom(5);
        Item? art = null;
        for (var i = 0; i < 20_000 && art is null; i++)
        {
            var item = game.Objects.Make(rng, 60, good: true, great: true);
            if (item?.IsArtifact == true) art = item;
        }
        Assert.NotNull(art);
        Assert.True(art!.Artifact!.Id.StartsWith("randart_") || RandartGenerator.Fixed.Contains(art.Artifact.Id));

        game.Player.Inventory.Add(art);
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal(game.RandartSeed, loaded.RandartSeed);
        var again = loaded.Player.Inventory.All.Single(i => i.Artifact?.Id == art.Artifact.Id);
        Assert.Equal(art.Artifact.Name, again.Artifact!.Name);
    }

    [Fact]
    public void WithoutTheOption_TheStandardArtifactsAreUsed()
    {
        var game = GameSession.NewGame(TestData.Game, 42);
        Assert.Null(game.RandartSeed);
        Assert.Same(TestData.Game.Artifacts, game.Artifacts);
    }
}
