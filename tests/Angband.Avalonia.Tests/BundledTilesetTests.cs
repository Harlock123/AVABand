using Angband.Data;
using Angband.Data.Tiles;

namespace Angband.Avalonia.Tests;

/// <summary>Checks the tilesets shipped with the game against the game data and their images.</summary>
public class BundledTilesetTests
{
    private static readonly IReadOnlyList<TilesetManifest> Bundled = LoadBundled();

    private static IReadOnlyList<TilesetManifest> LoadBundled()
    {
        var problems = new List<string>();
        var sets = TilesetCatalog.Discover([TilesetCatalog.DefaultDirectory], problems);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        return sets;
    }

    public static TheoryData<string> Ids()
    {
        var data = new TheoryData<string>();
        foreach (var s in Bundled) data.Add(s.Id);
        return data;
    }

    [Fact]
    public void NineTilesetsShip() =>
        Assert.Equal(["adam-bolt", "angband-nomad", "angband-old", "dawnlike", "dcss", "gervais", "hexany", "rltiles", "tangaria"],
            Bundled.Select(s => s.Id).Order());

    /// <summary>AVABand's quest monsters have pictures in every set (borrowed from the nearest Angband monster's).</summary>
    [Theory]
    [MemberData(nameof(Ids))]
    public void TheQuestMonsters_HaveTiles(string id)
    {
        var set = Bundled.Single(s => s.Id == id);
        var data = DataLoader.Load(DataLoader.DefaultDataDirectory);
        var quest = data.Monsters.Where(m => m.Id is "durgash_the_keybearer" or "hathol_lord_of_the_barrow" or "fengel_the_fence"
            or "nar_the_red_handed" or "skarn_quickfingers" or "ilse_shadowcloak" or "the_shade_of_the_stair").ToList();
        Assert.Equal(7, quest.Count);
        Assert.All(quest, m => Assert.True(set.Tiles.ContainsKey("monster:" + m.Id), $"{id}: no tile for {m.Id}"));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void SheetCells_LieInsideTheirImages(string id)
    {
        var set = Bundled.Single(s => s.Id == id);
        var sizes = set.Sheets.ToDictionary(kv => kv.Key, kv => PngSize(set.ResolvePath(kv.Value)));
        foreach (var (key, entry) in set.Tiles)
        foreach (var r in entry.Variants.Values.Append(entry.Default).SelectMany(l => l).Where(r => r.Sheet is not null))
        {
            var (w, h) = sizes[r.Sheet!];
            Assert.True((r.Column + 1) * set.TileWidth <= w && (r.Row + 1) * set.TileHeight <= h,
                $"{id} {key}: cell {r.Column},{r.Row} is outside {w}x{h}");
        }
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void EveryTerrainTrapMonsterAndObjectBase_HasArt(string id)
    {
        var set = Bundled.Single(s => s.Id == id);
        var data = DataLoader.Load(DataLoader.DefaultDataDirectory);
        var wanted = data.Terrain.All.Where(t => t.Id != "none" && t.Mimic is null).Select(t => "terrain:" + t.Id)
            .Concat(data.Traps.Select(t => "trap:" + t.Id))
            .Concat(data.ObjectBases.Select(b => "object-base:" + b.Id))
            .Append("object:pile")
            .Append("player");
        var missing = wanted.Where(k => !set.Tiles.ContainsKey(k)).ToList();
        // Monsters: their own art, their Angband name's art, or the art for their glyph.
        missing.AddRange(data.Monsters
            .Where(m => !set.Tiles.ContainsKey("monster:" + m.Id)
                        && !set.Tiles.ContainsKey("monster-name:" + m.Name.ToLowerInvariant())
                        && !set.Tiles.ContainsKey(Angband.Avalonia.Rendering.TileRenderer.GlyphKey(m.Glyph)))
            .Select(m => "monster:" + m.Id));
        Assert.True(missing.Count == 0, $"{id} has no art for: {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void HasCreditsAndLicence(string id)
    {
        var set = Bundled.Single(s => s.Id == id);
        Assert.False(string.IsNullOrWhiteSpace(set.Author));
        Assert.False(string.IsNullOrWhiteSpace(set.License));
        Assert.True(File.Exists(set.ResolvePath("LICENSE.txt")));
    }

    /// <summary>Width and height from a PNG's IHDR chunk.</summary>
    private static (int W, int H) PngSize(string path)
    {
        using var f = File.OpenRead(path);
        var header = new byte[24];
        f.ReadExactly(header);
        return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16)),
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20)));
    }
}
