using Angband.Core.Game;
using Angband.Core.Persistence;
using Angband.Core.Records;
using Angband.Data;

namespace Angband.Tests;

/// <summary>Mods made visible (AVABand's own): the loader's switch-off list, broken mods left out, mod.json and the diff counts, the example mod, and modded characters marked.</summary>
public class ModTests
{
    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("avaband-mods-").FullName;
        public string Mod(string name)
        {
            var dir = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(dir);
            return dir;
        }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private const string Newt = """
        [ { "id": "test_newt", "name": "test newt", "glyph": "R", "color": "Umber", "base": "reptile", "depth": 2,
            "rarity": 1, "hitPoints": 5, "armour": 4, "sleep": 10, "hearing": 10, "experience": 2,
            "blows": [ { "method": "bite", "effect": "hurt", "damage": "1d2" } ], "flags": [ "ANIMAL" ] } ]
        """;

    private const string Crystal = """
        [ { "id": "test_crystal", "name": "crystal wall", "glyph": "#", "color": "LightBlue", "flags": ["Wall", "Rock", "Permanent"] } ]
        """;

    [Fact]
    public void Loader_SkipsTheModsSwitchedOff()
    {
        using var root = new TempDir();
        File.WriteAllText(Path.Combine(root.Mod("alpha"), DataLoader.AvaMonstersFile), Newt);
        File.WriteAllText(Path.Combine(root.Mod("beta"), DataLoader.AvaTerrainFile), Crystal);

        var data = DataLoader.LoadWithMods(DataLoader.DefaultDataDirectory, [.. DataLoader.UserModDirectories(root.Path)], ["BETA"], out var results);

        Assert.NotNull(data.Monster("test_newt"));
        Assert.False(data.Terrain.TryGet("test_crystal", out _));
        Assert.Equal(["alpha"], data.Mods);
        Assert.Equal([("alpha", ModLoadState.Loaded), ("beta", ModLoadState.Disabled)], results.Select(r => (r.Name, r.State)));

        // With nothing switched off, both load, in name order.
        var both = DataLoader.LoadWithMods(DataLoader.DefaultDataDirectory, [.. DataLoader.UserModDirectories(root.Path)], [], out _);
        Assert.True(both.Terrain.TryGet("test_crystal", out _));
        Assert.Equal(["alpha", "beta"], both.Mods);
        Assert.Empty(TestData.Game.Mods);
    }

    [Fact]
    public void Loader_LeavesOutABrokenMod_AndKeepsTheRest()
    {
        using var root = new TempDir();
        File.WriteAllText(Path.Combine(root.Mod("a-good"), DataLoader.AvaMonstersFile), Newt);
        File.WriteAllText(Path.Combine(root.Mod("b-bad-json"), DataLoader.AvaMonstersFile), "[ { \"id\": ");
        File.WriteAllText(Path.Combine(root.Mod("c-bad-colour"), DataLoader.AvaTerrainFile),
            Crystal.Replace("LightBlue", "NoSuchColour"));
        File.WriteAllText(Path.Combine(root.Mod("d-good"), DataLoader.AvaTerrainFile), Crystal);

        var data = DataLoader.LoadWithMods(DataLoader.DefaultDataDirectory, [.. DataLoader.UserModDirectories(root.Path)], null, out var results);

        Assert.NotNull(data.Monster("test_newt"));
        Assert.True(data.Terrain.TryGet("test_crystal", out _));
        Assert.Equal(["a-good", "d-good"], data.Mods);
        var byName = results.ToDictionary(r => r.Name);
        Assert.Equal(ModLoadState.Failed, byName["b-bad-json"].State);
        Assert.Contains(DataLoader.AvaMonstersFile, byName["b-bad-json"].Error);
        Assert.Equal(ModLoadState.Failed, byName["c-bad-colour"].State);
        Assert.Contains("NoSuchColour", byName["c-bad-colour"].Error);
        Assert.Null(byName["d-good"].Error);
    }

    [Fact]
    public void Inspect_ReadsTheManifest_AndCountsWhatEachFileAddsAndReplaces()
    {
        using var root = new TempDir();
        var dir = root.Mod("my-mod");
        File.WriteAllText(Path.Combine(dir, ModCatalog.ManifestFile), """
            { "name": "My mod", "author": "Someone", "version": "0.3", "description": "Jackals, but more." }
            """);
        // One of the game's monsters replaced (jackal) and a new one added — in either monster file.
        File.WriteAllText(Path.Combine(dir, DataLoader.MonstersFile), """
            [ { "id": "jackal", "name": "jackal" }, { "id": "test_newt", "name": "test newt" } ]
            """);
        File.WriteAllText(Path.Combine(dir, DataLoader.AvaObjectsFile), """[ { "id": "dagger" } ]""");
        File.WriteAllText(Path.Combine(dir, DataLoader.ColorsFile), """{ "Green": "#00FF00", "Mauve": "#E0B0FF" }""");
        File.WriteAllText(Path.Combine(dir, DataLoader.TownFile), "{}");
        Directory.CreateDirectory(Path.Combine(dir, DataLoader.TemplatesFolder));
        File.WriteAllText(Path.Combine(dir, DataLoader.TemplatesFolder, DataLoader.VaultsFile), """[ { "id": "test_vault" } ]""");
        File.WriteAllText(Path.Combine(dir, "monster.json"), "[]"); // misspelt: never read
        File.WriteAllText(Path.Combine(dir, DataLoader.TrapsFile), "{ not json");

        var info = ModCatalog.Inspect(dir);

        Assert.Equal("my-mod", info.Name);
        Assert.Equal("My mod", info.Title);
        Assert.Equal("Someone", info.Manifest!.Author);
        Assert.Equal("0.3", info.Manifest.Version);
        Assert.Equal("Jackals, but more.", info.Manifest.Description);
        var files = info.Files.ToDictionary(f => f.File);
        Assert.Equal((1, 1), (files[DataLoader.MonstersFile].Added, files[DataLoader.MonstersFile].Replaced));
        Assert.Equal((0, 1), (files[DataLoader.AvaObjectsFile].Added, files[DataLoader.AvaObjectsFile].Replaced));
        Assert.Equal((1, 1), (files[DataLoader.ColorsFile].Added, files[DataLoader.ColorsFile].Replaced));
        Assert.True(files[DataLoader.TownFile].ReplacesWhole);
        Assert.Equal(1, files["templates/vaults.json"].Added);
        Assert.Equal("monsters.json: adds 1, replaces 1", files[DataLoader.MonstersFile].Summary);
        Assert.Contains(info.Errors, e => e.StartsWith(DataLoader.TrapsFile, StringComparison.Ordinal));
        Assert.Contains(info.Notes, n => n.Contains("monster.json", StringComparison.Ordinal));

        // No mod.json: the folder's name is its title.
        var bare = ModCatalog.Inspect(root.Mod("bare"));
        Assert.Null(bare.Manifest);
        Assert.Equal("bare", bare.Title);
        Assert.Contains(bare.Notes, n => n.Contains("changes nothing", StringComparison.Ordinal));
        Assert.Equal(["bare", "my-mod"], ModCatalog.Discover(root.Path).Select(m => m.Name));
    }

    [Fact]
    public void TheExampleMod_Loads_AndAddsAMonsterAndAnItem()
    {
        using var root = new TempDir();
        var dir = ModCatalog.WriteExample(root.Path);
        Assert.NotNull(dir);
        Assert.Null(ModCatalog.WriteExample(root.Path)); // never overwritten

        var data = DataLoader.LoadWithMods(DataLoader.DefaultDataDirectory, [dir!], [], out var results);
        Assert.Equal(ModLoadState.Loaded, results.Single().State);
        Assert.Equal("mud newt", data.Monster("example_mud_newt")!.Name);
        Assert.NotNull(data.Object("example_pick_knife"));

        var info = ModCatalog.Inspect(dir!);
        Assert.Empty(info.Errors);
        Assert.Equal("Example mod", info.Title);
        Assert.Equal(["ava_monsters.json: adds 1, replaces 0", "ava_objects.json: adds 1, replaces 0"], info.Files.Select(f => f.Summary));
    }

    [Fact]
    public void AModdedCharacter_IsMarked_InTheSave_TheScores_AndTheDump()
    {
        using var root = new TempDir();
        var dir = ModCatalog.WriteExample(root.Path)!;
        var data = DataLoader.LoadWithMods(DataLoader.DefaultDataDirectory, [dir], [], out _);
        var game = GameSession.NewGame(data, 5, CharacterSpec.Default("human", "warrior"));

        Assert.Equal([ModCatalog.ExampleName], game.Player.Mods);
        Assert.Equal([ModCatalog.ExampleName], ScoreEntry.For(game).Mods);
        Assert.Equal(" [Modded]", Scoring.ModdedTag(ScoreEntry.For(game).Mods));
        Assert.Contains($"Mods: {ModCatalog.ExampleName}", CharacterDump.Build(game));
        Assert.Equal([ModCatalog.ExampleName], SaveGame.Summary(game).Mods);

        // Loaded later without the mod, it stays modded (once modded, always).
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal([ModCatalog.ExampleName], loaded.Player.Mods);

        // The standard game: no mark anywhere.
        var plain = GameSession.NewGame(TestData.Game, 5, CharacterSpec.Default("human", "warrior"));
        Assert.Empty(plain.Player.Mods);
        Assert.Null(ScoreEntry.For(plain).Mods);
        Assert.Null(SaveGame.Summary(plain).Mods);
        Assert.Equal("", Scoring.ModdedTag(null));
        Assert.DoesNotContain("Mods:", CharacterDump.Build(plain));
    }

    [Fact]
    public void ModdableFiles_AreTheLoadersFiles()
    {
        // Every file name the loader declares is one a mod can carry (and the help lists).
        var declared = typeof(DataLoader).GetFields()
            .Where(f => f.IsLiteral && f.Name.EndsWith("File", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();
        var moddable = ModCatalog.ModdableFiles.Select(f => f.Split('/').Last()).ToHashSet();
        Assert.Equal(declared.Order(), moddable.Order());
    }
}
