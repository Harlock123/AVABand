using Angband.Data.Tiles;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Angband.Avalonia.Rendering;

/// <summary>A drawable piece of an image.</summary>
public readonly record struct TileSprite(Bitmap Image, Rect Source);

/// <summary>
/// Loads a tileset's images (sheets and individual PNGs) and resolves tile keys to sprites.
/// Images load on first use and are shared for the atlas's lifetime.
/// </summary>
public sealed class TileAtlas : IDisposable
{
    private readonly Dictionary<string, Bitmap?> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, TileLighting), (TileSprite[] Layers, bool Exact)?> _resolved = [];

    public TileAtlas(TilesetManifest manifest) => Manifest = manifest;

    public TilesetManifest Manifest { get; }

    /// <summary>Looks up a tile. <c>exact</c> is false when a lighting variant was missing (the renderer shades it).</summary>
    public bool TryResolve(string key, TileLighting lighting, out TileSprite[] layers, out bool exact)
    {
        if (!_resolved.TryGetValue((key, lighting), out var hit))
        {
            hit = Resolve(key, lighting);
            _resolved[(key, lighting)] = hit;
        }
        layers = hit?.Layers ?? [];
        exact = hit?.Exact ?? false;
        return hit is not null;
    }

    private (TileSprite[], bool)? Resolve(string key, TileLighting lighting)
    {
        if (!Manifest.Tiles.TryGetValue(key, out var entry)) return null;
        var (refs, exact) = entry.For(lighting);
        var sprites = new List<TileSprite>(refs.Count);
        foreach (var r in refs)
        {
            if (r.File is { } file)
            {
                if (Image(file) is { } img) sprites.Add(new TileSprite(img, new Rect(0, 0, img.PixelSize.Width, img.PixelSize.Height)));
            }
            else if (r.Sheet is { } sheet && Manifest.Sheets.TryGetValue(sheet, out var sheetFile) && Image(sheetFile) is { } img)
            {
                var src = new Rect(r.Column * Manifest.TileWidth, r.Row * Manifest.TileHeight, Manifest.TileWidth, Manifest.TileHeight);
                if (src.Right <= img.PixelSize.Width && src.Bottom <= img.PixelSize.Height) sprites.Add(new TileSprite(img, src));
            }
        }
        return sprites.Count == 0 ? null : (sprites.ToArray(), exact);
    }

    private Bitmap? Image(string relative)
    {
        if (_images.TryGetValue(relative, out var img)) return img;
        try
        {
            img = new Bitmap(Manifest.ResolvePath(relative));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            img = null; // missing or corrupt: those tiles fall back to ASCII
        }
        _images[relative] = img;
        return img;
    }

    public void Dispose()
    {
        foreach (var img in _images.Values) img?.Dispose();
        _images.Clear();
        _resolved.Clear();
    }
}
