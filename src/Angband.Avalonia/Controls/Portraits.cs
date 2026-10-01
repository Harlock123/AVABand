using System.Globalization;
using Angband.Core.Definitions;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Angband.Avalonia.Controls;

/// <summary>
/// The creation screen's portraits (art/portraits, made by tools/portrait_art.py from Dungeon Crawl Stone
/// Soup's CC0 player tiles): a race in a class (<c>dwarf_warrior_priest.png</c>), or a race alone in plain
/// clothes (<c>dwarf.png</c>). Loaded once each; null when the file isn't there.
/// </summary>
public static class Portraits
{
    /// <summary>Where they're looked for (tests point it elsewhere).</summary>
    public static string Folder { get; set; } = Path.Combine(AppContext.BaseDirectory, "art", "portraits");

    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

    public static Bitmap? For(string? raceId, string? classId = null)
    {
        if (raceId is null) return null;
        var name = classId is null ? raceId : $"{raceId}_{classId}";
        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var cached)) return cached;
            var path = Path.Combine(Folder, name + ".png");
            Bitmap? bitmap = null;
            try
            {
                if (File.Exists(path)) bitmap = new Bitmap(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                bitmap = null; // (a damaged picture shows nothing, not an error)
            }
            Cache[name] = bitmap;
            return bitmap;
        }
    }

    /// <summary>A race (the race list): its portrait in plain clothes.</summary>
    public static readonly IValueConverter Race = new FuncValueConverter<RaceDef?, Bitmap?>(race => For(race?.Id));

    /// <summary>A class, with the race chosen (the class list): that race in that class.</summary>
    public static readonly IMultiValueConverter RaceInClass = new RaceInClassConverter();

    private sealed class RaceInClassConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
            values.Count >= 2 && values[0] is ClassDef cls ? For((values[1] as RaceDef)?.Id ?? "human", cls.Id) : null;
    }
}
