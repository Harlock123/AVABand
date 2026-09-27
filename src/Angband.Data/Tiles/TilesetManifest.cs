using System.Globalization;
using System.Text.Json;
using Angband.Core.Definitions;

namespace Angband.Data.Tiles;

/// <summary>How a square is lit, so tilesets can supply different art for each state.</summary>
public enum TileLighting
{
    /// <summary>In view and lit by a permanent light (glowing room, daylight).</summary>
    Lit,
    /// <summary>In view and lit only by the player's light source.</summary>
    Torch,
    /// <summary>Remembered but not currently seen.</summary>
    Dark,
}

/// <summary>One image: a cell of a named sheet, or a whole individual PNG file.</summary>
public sealed record TileImageRef(string? Sheet, int Column, int Row, string? File)
{
    public static TileImageRef FromSheet(string sheet, int column, int row) => new(sheet, column, row, null);
    public static TileImageRef FromFile(string file) => new(null, 0, 0, file);
}

/// <summary>A tile: one or more images drawn on top of each other, per lighting state.</summary>
public sealed class TileEntry
{
    public required IReadOnlyList<TileImageRef> Default { get; init; }
    public IReadOnlyDictionary<TileLighting, IReadOnlyList<TileImageRef>> Variants { get; init; } =
        new Dictionary<TileLighting, IReadOnlyList<TileImageRef>>();

    /// <summary>The layers for a lighting state, and whether the art was drawn for that state.</summary>
    public (IReadOnlyList<TileImageRef> Layers, bool Exact) For(TileLighting lighting) =>
        Variants.TryGetValue(lighting, out var layers) ? (layers, true) : (Default, lighting == TileLighting.Lit);
}

/// <summary>
/// A tileset folder's <c>tileset.json</c>. Tile keys are <c>terrain:&lt;id&gt;</c>, <c>trap:&lt;id&gt;</c>,
/// <c>monster:&lt;id&gt;</c>, <c>monster-name:&lt;lower-case name&gt;</c> and <c>player</c>. A tile value is
/// <list type="bullet">
/// <item>a reference: <c>"sheet:col,row"</c> for a cell of a sheet, or <c>"path/file.png"</c>;</item>
/// <item>a list of references, drawn bottom to top (paper-doll layering);</item>
/// <item>an object with <c>lit</c>/<c>torch</c>/<c>dark</c> variants, each a reference or list.</item>
/// </list>
/// Keys a tileset does not define are drawn as ASCII, so partial tilesets still work.
/// </summary>
public sealed class TilesetManifest
{
    public const string FileName = "tileset.json";

    /// <summary>Folder name; the id stored in settings.</summary>
    public required string Id { get; init; }
    public required string Directory { get; init; }
    public required string Name { get; init; }
    public string Author { get; init; } = "";
    public string License { get; init; } = "";
    public string Source { get; init; } = "";
    public int TileWidth { get; init; }
    public int TileHeight { get; init; }
    /// <summary>Sheet name to image file (relative to <see cref="Directory"/>).</summary>
    public IReadOnlyDictionary<string, string> Sheets { get; init; } = new Dictionary<string, string>();
    /// <summary>Tiles paint their own background, so terrain need not be drawn under creatures.</summary>
    public bool Opaque { get; init; }
    public IReadOnlyDictionary<string, TileEntry> Tiles { get; init; } = new Dictionary<string, TileEntry>();

    public string ResolvePath(string relative) => Path.Combine(Directory, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Every image file this tileset needs (sheets and individual tiles).</summary>
    public IEnumerable<string> ReferencedFiles() =>
        Sheets.Values.Concat(Tiles.Values
                .SelectMany(t => t.Variants.Values.Append(t.Default))
                .SelectMany(layers => layers)
                .Where(r => r.File is not null)
                .Select(r => r.File!))
            .Distinct(StringComparer.Ordinal);

    public static TilesetManifest Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        var errors = new List<string>();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(path),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            throw new GameDataException($"{path}: {ex.Message}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            string Str(string name, string fallback = "") =>
                root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : fallback;
            int Int(string name) =>
                root.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : 0;

            var sheets = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("sheets", out var sheetsEl) && sheetsEl.ValueKind == JsonValueKind.Object)
                foreach (var s in sheetsEl.EnumerateObject())
                    sheets[s.Name] = s.Value.GetString() ?? "";

            var tiles = new Dictionary<string, TileEntry>(StringComparer.Ordinal);
            if (root.TryGetProperty("tiles", out var tilesEl) && tilesEl.ValueKind == JsonValueKind.Object)
                foreach (var t in tilesEl.EnumerateObject())
                    if (ParseEntry(t.Value, t.Name, sheets, errors) is { } entry) tiles[t.Name] = entry;

            var manifest = new TilesetManifest
            {
                Id = System.IO.Path.GetFileName(directory.TrimEnd(System.IO.Path.DirectorySeparatorChar)),
                Directory = directory,
                Name = Str("name", System.IO.Path.GetFileName(directory)),
                Author = Str("author"),
                License = Str("license"),
                Source = Str("source"),
                TileWidth = Int("tileWidth"),
                TileHeight = Int("tileHeight"),
                Sheets = sheets,
                Opaque = root.TryGetProperty("opaque", out var o) && o.ValueKind == JsonValueKind.True,
                Tiles = tiles,
            };

            if (manifest.TileWidth <= 0 || manifest.TileHeight <= 0)
                errors.Add("tileWidth and tileHeight must be positive.");
            foreach (var file in manifest.ReferencedFiles())
                if (!File.Exists(manifest.ResolvePath(file)))
                    errors.Add($"missing image '{file}'.");

            if (errors.Count > 0)
                throw new GameDataException($"Tileset '{directory}' is invalid:{Environment.NewLine}" +
                                            string.Join(Environment.NewLine, errors.Select(e => "  - " + e)));
            return manifest;
        }
    }

    private static TileEntry? ParseEntry(JsonElement value, string key, IReadOnlyDictionary<string, string> sheets, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            var layers = ParseLayers(value, key, sheets, errors);
            return layers is null ? null : new TileEntry { Default = layers };
        }

        var variants = new Dictionary<TileLighting, IReadOnlyList<TileImageRef>>();
        foreach (var prop in value.EnumerateObject())
        {
            if (!Enum.TryParse<TileLighting>(prop.Name, ignoreCase: true, out var lighting))
            {
                errors.Add($"{key}: unknown lighting '{prop.Name}' (use lit, torch or dark).");
                continue;
            }
            if (ParseLayers(prop.Value, key, sheets, errors) is { } layers) variants[lighting] = layers;
        }
        if (!variants.TryGetValue(TileLighting.Lit, out var lit))
        {
            errors.Add($"{key}: a 'lit' variant is required.");
            return null;
        }
        return new TileEntry { Default = lit, Variants = variants };
    }

    private static IReadOnlyList<TileImageRef>? ParseLayers(JsonElement value, string key,
        IReadOnlyDictionary<string, string> sheets, List<string> errors)
    {
        var items = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : [value];
        var result = new List<TileImageRef>();
        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                errors.Add($"{key}: tile references must be strings.");
                return null;
            }
            if (ParseRef(item.GetString()!, sheets) is { } r) result.Add(r);
            else
            {
                errors.Add($"{key}: bad tile reference '{item.GetString()}' (use \"sheet:col,row\" or \"file.png\").");
                return null;
            }
        }
        return result.Count == 0 ? null : result;
    }

    /// <summary>Parses <c>"sheet:col,row"</c> or a relative PNG path.</summary>
    public static TileImageRef? ParseRef(string text, IReadOnlyDictionary<string, string> sheets)
    {
        var colon = text.IndexOf(':');
        if (colon > 0 && sheets.ContainsKey(text[..colon]))
        {
            var parts = text[(colon + 1)..].Split(',');
            if (parts.Length == 2
                && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var col)
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var row))
                return TileImageRef.FromSheet(text[..colon], col, row);
            return null;
        }
        return text.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !System.IO.Path.IsPathRooted(text) && !text.Contains("..")
            ? TileImageRef.FromFile(text)
            : null;
    }
}

/// <summary>Finds tilesets: each subfolder with a <c>tileset.json</c> in the given roots.</summary>
public static class TilesetCatalog
{
    /// <summary>The <c>tilesets</c> folder next to the executable.</summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "tilesets");

    /// <summary>Per-user tilesets: <c>&lt;AppData&gt;/AVABand/tilesets</c>.</summary>
    public static string UserDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVABand", "tilesets");

    /// <summary>
    /// Loads every tileset found. Broken ones are reported in <paramref name="problems"/> rather than
    /// failing the game; a user tileset with the same folder name replaces a bundled one.
    /// </summary>
    public static IReadOnlyList<TilesetManifest> Discover(IEnumerable<string> roots, List<string>? problems = null)
    {
        var found = new Dictionary<string, TilesetManifest>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(System.IO.Directory.Exists))
        foreach (var dir in System.IO.Directory.GetDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(Path.Combine(dir, TilesetManifest.FileName))) continue;
            try
            {
                var manifest = TilesetManifest.Load(dir);
                found[manifest.Id] = manifest;
            }
            catch (GameDataException ex)
            {
                problems?.Add(ex.Message);
            }
        }
        return found.Values.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<TilesetManifest> DiscoverDefault(List<string>? problems = null) =>
        Discover([DefaultDirectory, UserDirectory], problems);
}
