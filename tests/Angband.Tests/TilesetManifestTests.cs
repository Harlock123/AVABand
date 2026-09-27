using Angband.Core.Definitions;
using Angband.Data.Tiles;

namespace Angband.Tests;

public class TilesetManifestTests
{
    private static readonly Dictionary<string, string> Sheets = new() { ["main"] = "sheet.png" };

    [Theory]
    [InlineData("main:3,7", "main", 3, 7, null)]
    [InlineData("monsters/orc.png", null, 0, 0, "monsters/orc.png")]
    public void ParseRef_ReadsSheetCellsAndFiles(string text, string? sheet, int col, int row, string? file) =>
        Assert.Equal(new TileImageRef(sheet, col, row, file), TilesetManifest.ParseRef(text, Sheets));

    [Theory]
    [InlineData("other:1,2")]      // unknown sheet, not a png either
    [InlineData("main:1")]         // missing row
    [InlineData("main:-1,2")]      // negative
    [InlineData("../escape.png")]  // leaves the tileset folder
    [InlineData("/abs/path.png")]
    public void ParseRef_RejectsBadReferences(string text) => Assert.Null(TilesetManifest.ParseRef(text, Sheets));

    [Fact]
    public void Load_ParsesLayersAndLightingVariants()
    {
        using var dir = new TempTileset();
        dir.Png("sheet.png");
        dir.Png("a.png");
        dir.Png("b.png");
        dir.Manifest("""
            {
              "name": "Test set", "author": "Me", "license": "CC0", "tileWidth": 8, "tileHeight": 8,
              "sheets": { "main": "sheet.png" }, "opaque": true,
              "tiles": {
                "terrain:floor": { "lit": "main:1,0", "dark": "main:2,0" },
                "player": ["a.png", "b.png"],
                "monster:orc": "main:0,1"
              }
            }
            """);

        var m = TilesetManifest.Load(dir.Path);

        Assert.Equal("Test set", m.Name);
        Assert.True(m.Opaque);
        Assert.Equal(3, m.Tiles.Count);
        Assert.Equal(2, m.Tiles["player"].Default.Count);

        var floor = m.Tiles["terrain:floor"];
        Assert.Equal((TileImageRef.FromSheet("main", 2, 0), true), (floor.For(TileLighting.Dark).Layers[0], floor.For(TileLighting.Dark).Exact));
        // No torch art: falls back to the lit art, flagged inexact so the renderer tints it.
        Assert.Equal((TileImageRef.FromSheet("main", 1, 0), false), (floor.For(TileLighting.Torch).Layers[0], floor.For(TileLighting.Torch).Exact));
    }

    [Fact]
    public void Load_ReportsEveryProblem()
    {
        using var dir = new TempTileset();
        dir.Manifest("""
            {
              "name": "Broken", "tileWidth": 0, "tileHeight": 8, "sheets": { "main": "nope.png" },
              "tiles": { "a": "main:1", "b": { "dark": "main:0,0" }, "c": "missing.png", "d": { "shiny": "main:0,0", "lit": "main:0,0" } }
            }
            """);

        var ex = Assert.Throws<GameDataException>(() => TilesetManifest.Load(dir.Path));
        foreach (var expected in new[] { "tileWidth", "nope.png", "missing.png", "'lit' variant", "shiny", "bad tile reference" })
            Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Catalog_SkipsBrokenTilesets_AndLetsUserSetsOverride()
    {
        using var bundled = new TempTileset();
        using var user = new TempTileset();
        MakeSet(bundled.Path, "classic", "Bundled");
        MakeSet(bundled.Path, "broken", null);
        MakeSet(user.Path, "classic", "User version");

        var problems = new List<string>();
        var sets = TilesetCatalog.Discover([bundled.Path, user.Path], problems);

        Assert.Equal(["User version"], sets.Select(s => s.Name));
        Assert.Single(problems);
    }

    private static void MakeSet(string root, string id, string? name)
    {
        var dir = System.IO.Path.Combine(root, id);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(System.IO.Path.Combine(dir, "t.png"), TempTileset.TinyPng);
        File.WriteAllText(System.IO.Path.Combine(dir, TilesetManifest.FileName), name is null
            ? "{ not json"
            : $$"""{ "name": "{{name}}", "tileWidth": 1, "tileHeight": 1, "tiles": { "player": "t.png" } }""");
    }

    private sealed class TempTileset : IDisposable
    {
        /// <summary>A valid 1x1 PNG.</summary>
        public static readonly byte[] TinyPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

        public string Path { get; } = Directory.CreateTempSubdirectory("avaband-tiles-").FullName;
        public void Png(string name) => File.WriteAllBytes(System.IO.Path.Combine(Path, name), TinyPng);
        public void Manifest(string json) => File.WriteAllText(System.IO.Path.Combine(Path, TilesetManifest.FileName), json);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
