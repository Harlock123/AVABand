using Angband.Core.Definitions;
using Angband.Data;

namespace Angband.Tests;

public class DataLoaderTests
{
    [Fact]
    public void ShippedData_Loads()
    {
        var data = TestData.Game;
        Assert.True(data.Terrain.Count > 10);
        Assert.NotEmpty(data.Traps);
        Assert.Contains(data.Profiles, p => p.Name == "town");
        Assert.Contains(data.Vaults, v => v.Type == "Greater vault");
        Assert.Equal(10, data.Shops.Count); // the eight of 4.2.5, and AVABand's Prancing Pony and Arcane Artificer
        Assert.Equal('#', data.Terrain[data.Terrain.Ids.Granite].Glyph);
    }

    [Fact]
    public void Terrain_FlagsAreParsed()
    {
        var floor = TestData.Game.Terrain["floor"];
        Assert.True(floor.Has(TerrainFlags.Passable | TerrainFlags.Los | TerrainFlags.Floor));
        Assert.False(floor.Has(TerrainFlags.Wall));
    }

    [Fact]
    public void Mod_OverridesAndExtendsBaseData()
    {
        using var mod = new TempDir();
        File.WriteAllText(Path.Combine(mod.Path, DataLoader.TerrainFile), """
            [
              { "id": "granite_wall", "name": "mossy granite", "glyph": "▒", "color": "Green",
                "flags": ["Wall", "Rock", "Granite"] },
              { "id": "crystal_wall", "name": "crystal wall", "glyph": "◆", "color": "LightBlue",
                "flags": ["Wall", "Rock", "Permanent"] }
            ]
            """);
        File.WriteAllText(Path.Combine(mod.Path, DataLoader.ColorsFile), """{ "Green": "#00FF00" }""");

        var data = DataLoader.Load(DataLoader.DefaultDataDirectory, mod.Path);

        var granite = data.Terrain["granite_wall"];
        Assert.Equal("mossy granite", granite.Name);
        Assert.Equal('▒', granite.Glyph);
        Assert.Equal(TestData.Game.Terrain["granite_wall"].Index, granite.Index); // replaced in place
        Assert.True(data.Terrain.TryGet("crystal_wall", out _));
        Assert.Equal("#00FF00", data.Colors["Green"]);
    }

    [Fact]
    public void BadData_ReportsEveryProblem()
    {
        using var mod = new TempDir();
        File.WriteAllText(Path.Combine(mod.Path, DataLoader.TerrainFile), """
            [ { "id": "floor", "name": "floor", "glyph": "..", "color": "NoSuchColour", "flags": ["Bogus"] } ]
            """);

        var ex = Assert.Throws<GameDataException>(() => DataLoader.Load(DataLoader.DefaultDataDirectory, mod.Path));
        Assert.Contains("glyph", ex.Message);
        Assert.Contains("NoSuchColour", ex.Message);
        Assert.Contains("Bogus", ex.Message);
    }

    [Fact]
    public void UnknownRoomType_IsRejected()
    {
        using var mod = new TempDir();
        File.WriteAllText(Path.Combine(mod.Path, DataLoader.ProfilesFile), """
            [ { "id": "classic", "name": "classic", "rooms": [ { "name": "ballroom" } ] } ]
            """);

        var ex = Assert.Throws<GameDataException>(() => DataLoader.Load(DataLoader.DefaultDataDirectory, mod.Path));
        Assert.Contains("ballroom", ex.Message);
    }

    [Fact]
    public void UnknownTemplateSymbol_IsRejected()
    {
        using var mod = new TempDir();
        Directory.CreateDirectory(Path.Combine(mod.Path, DataLoader.TemplatesFolder));
        File.WriteAllText(Path.Combine(mod.Path, DataLoader.TemplatesFolder, DataLoader.RoomTemplatesFile), """
            [ { "id": "bad", "type": 1, "rating": 1, "rows": [ "%%%", "%?%", "%%%" ] } ]
            """);

        var ex = Assert.Throws<GameDataException>(() => DataLoader.Load(DataLoader.DefaultDataDirectory, mod.Path));
        Assert.Contains("'?'", ex.Message);
    }

    [Fact]
    public void BadItemData_IsReported()
    {
        using var mod = new TempDir();
        File.WriteAllText(Path.Combine(mod.Path, DataLoader.ObjectsFile), """
            [ { "id": "bad_potion", "name": "Oops", "base": "potion", "effect": "explode:10; timed:nope:5" },
              { "id": "orphan", "name": "Orphan", "base": "no_such_base", "curses": ["no_such_curse"] } ]
            """);
        File.WriteAllText(Path.Combine(mod.Path, DataLoader.ArtifactsFile), """
            [ { "id": "ghost", "name": "'Ghost'", "kind": "no_such_kind" } ]
            """);

        var ex = Assert.Throws<GameDataException>(() => DataLoader.Load(DataLoader.DefaultDataDirectory, mod.Path));
        foreach (var expected in new[] { "explode", "nope", "no_such_base", "no_such_curse", "no_such_kind" })
            Assert.Contains(expected, ex.Message);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("avaband-test-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
