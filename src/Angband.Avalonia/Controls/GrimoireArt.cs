using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;

namespace Angband.Avalonia.Controls;

/// <summary>
/// The Grimoire's pictures (art/grimoire, made by tools/grimoire_art.py from Dungeon Crawl Stone Soup's
/// CC0 tiles): a spell's icon (by its name), a book's cover, a realm's sigil, and the parchment. Loaded
/// once each; null when the file isn't there.
/// </summary>
public static partial class GrimoireArt
{
    /// <summary>Where they're looked for (tests point it elsewhere).</summary>
    public static string Folder { get; set; } = Path.Combine(AppContext.BaseDirectory, "art", "grimoire");

    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

    /// <summary>A spell's file name: its name in lower case, the words joined by _ ("Find Traps, Doors &amp; Stairs" → find_traps_doors_stairs).</summary>
    public static string SpellFile(string name) => NotWord().Replace(name.ToLowerInvariant().Replace('ë', 'e'), "_").Trim('_');

    public static Bitmap? Spell(string name) => Load(Path.Combine("spells", SpellFile(name) + ".png"));
    public static Bitmap? Book(string id) => Load(Path.Combine("books", id + ".png"));
    public static Bitmap? Realm(string id) => Load(Path.Combine("realms", id + ".png"));
    public static Bitmap? Parchment => Load("parchment.png");

    private static Bitmap? Load(string relative)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(relative, out var cached)) return cached;
            Bitmap? bitmap = null;
            try
            {
                var path = Path.Combine(Folder, relative);
                if (File.Exists(path)) bitmap = new Bitmap(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                bitmap = null; // (a damaged picture shows nothing, not an error)
            }
            Cache[relative] = bitmap;
            return bitmap;
        }
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotWord();
}
