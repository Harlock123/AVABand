using System.Text;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Persistence;

namespace Angband.Avalonia;

/// <summary>A save file on disk and what it contains.</summary>
public sealed record SaveEntry(string Path, SaveSummary Summary)
{
    public string Title => $"{Summary.Name}, level {Summary.Level} {Summary.Race} {Summary.Class}";

    public string Details => string.Create(System.Globalization.CultureInfo.CurrentCulture,
        $"{(Summary.Depth == 0 ? "in the town" : $"{Summary.Depth * 50} ft")}  ·  max {Summary.MaxDepth * 50} ft  ·  turn {Summary.GameTurn}  ·  saved {Summary.SavedAtUtc.ToLocalTime():g}");
}

/// <summary>
/// The folder of saved characters (<c>&lt;AppData&gt;/AVABand/saves</c>). One file per character, named
/// after the character and its seed, written atomically so a crash mid-save never loses the old one.
/// </summary>
public sealed class SaveStore(string directory)
{
    public string Directory { get; } = directory;

    public static string DefaultDirectory =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVABand", "saves");

    public static SaveStore Default() => new(DefaultDirectory);

    public string PathFor(GameSession game) =>
        System.IO.Path.Combine(Directory, $"{Sanitize(game.Player.Name)}-{game.Seed:x}{SaveGame.Extension}");

    public string Save(GameSession game)
    {
        var path = PathFor(game);
        SaveGame.SaveToFile(game, path);
        return path;
    }

    public GameSession Load(GameData data, string path) => SaveGame.LoadFromFile(data, path);

    /// <summary>Every readable save, newest first (damaged files are skipped).</summary>
    public IReadOnlyList<SaveEntry> List()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        var entries = new List<SaveEntry>();
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*" + SaveGame.Extension))
        {
            try
            {
                using var stream = File.OpenRead(path);
                entries.Add(new SaveEntry(path, SaveGame.ReadSummary(stream)));
            }
            catch (Exception ex) when (ex is SaveGameException or IOException or UnauthorizedAccessException)
            {
                // Unreadable saves aren't offered.
            }
        }
        return entries.OrderByDescending(e => e.Summary.SavedAtUtc).ToList();
    }

    public void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Permadeath: a dead character's save is removed.</summary>
    public void DeleteFor(GameSession game) => Delete(PathFor(game));

    private static string Sanitize(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in name.Trim()) sb.Append(invalid.Contains(c) || char.IsWhiteSpace(c) ? '_' : c);
        return sb.Length == 0 ? "character" : sb.ToString();
    }
}
