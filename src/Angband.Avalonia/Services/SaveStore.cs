using System.Text;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Persistence;

namespace Angband.Avalonia;

/// <summary>
/// A save file on disk and what it contains. <see cref="IsBackup"/>: an earlier save kept in
/// saves/backups; <see cref="MainDamaged"/>: listed from its newest backup because the save itself
/// is missing or can't be read; <see cref="RestoredFrom"/>: loaded by restoring that backup.
/// </summary>
public sealed record SaveEntry(string Path, SaveSummary Summary, bool IsBackup = false, bool MainDamaged = false,
    DateTime? RestoredFrom = null)
{
    public string Title => $"{Summary.Name}, level {Summary.Level} {Summary.Race} {Summary.Class}"
                           + (MainDamaged ? " — save damaged: restore an earlier one" : "");

    /// <summary>A backup's line in the list of earlier saves.</summary>
    public string BackupTitle => string.Create(System.Globalization.CultureInfo.CurrentCulture,
        $"{Summary.SavedAtUtc.ToLocalTime():g}  ·  level {Summary.Level}  ·  {(Summary.Depth == 0 ? "in the town" : $"{Summary.Depth * 50} ft")}  ·  turn {Summary.GameTurn}");

    public string Details => string.Create(System.Globalization.CultureInfo.CurrentCulture,
        $"{(Summary.Depth == 0 ? "in the town" : $"{Summary.Depth * 50} ft")}  ·  max {Summary.MaxDepth * 50} ft  ·  turn {Summary.GameTurn}  ·  saved {Summary.SavedAtUtc.ToLocalTime():g}");
}

/// <summary>
/// The folder of saved characters (<c>&lt;AppData&gt;/AVABand/saves</c>). One file per character, named
/// after the character and its seed, written atomically so a crash mid-save never loses the old one.
/// Backups (AVABand's own): as a save is replaced, the one before it is kept in <c>saves/backups</c>
/// (at most one every <see cref="BackupInterval"/>, the newest <see cref="BackupsKept"/> kept), so a
/// damaged or unwanted save can be rolled back from the Load dialog. A dead character's backups go
/// with its save: permadeath stays permanent.
/// </summary>
public sealed class SaveStore(string directory)
{
    public string Directory { get; } = directory;

    /// <summary>How many earlier saves are kept per character.</summary>
    public const int BackupsKept = 5;

    /// <summary>The least time between backups, so they span a session rather than its last few minutes.</summary>
    public static readonly TimeSpan BackupInterval = TimeSpan.FromMinutes(10);

    public string BackupDirectory => System.IO.Path.Combine(Directory, "backups");

    /// <summary>The clock (tests move it).</summary>
    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    public static string DefaultDirectory =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVABand", "saves");

    public static SaveStore Default() => new(DefaultDirectory);

    /// <summary>A file that exists while the game runs; left behind, it means the last session ended in a crash.</summary>
    public string SessionMarker => System.IO.Path.Combine(Directory, "session.lock");

    /// <summary>Whether the last session didn't close cleanly (its marker is still there).</summary>
    public bool LastSessionCrashed => File.Exists(SessionMarker);

    /// <summary>Marks this session as running (call at start-up, after checking <see cref="LastSessionCrashed"/>).</summary>
    public void MarkSessionOpen()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(SessionMarker, DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without the marker a crash just goes unnoticed next time.
        }
    }

    /// <summary>Marks this session as closed cleanly.</summary>
    public void MarkSessionClosed()
    {
        try
        {
            File.Delete(SessionMarker);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Next start-up will ask once, harmlessly.
        }
    }

    public string PathFor(GameSession game) =>
        System.IO.Path.Combine(Directory, $"{Sanitize(game.Player.Name)}-{game.Seed:x}{SaveGame.Extension}");

    public string Save(GameSession game)
    {
        var path = PathFor(game);
        if (File.Exists(path)) KeepBackup(path, force: false);
        SaveGame.SaveToFile(game, path);
        return path;
    }

    private static string Stem(string path) => System.IO.Path.GetFileNameWithoutExtension(path);

    /// <summary>The backup files of the save at this path, newest first (by the time in their names).</summary>
    private List<string> BackupFiles(string stem) =>
        !System.IO.Directory.Exists(BackupDirectory) ? []
            : System.IO.Directory.EnumerateFiles(BackupDirectory, $"{stem}~*{SaveGame.Extension}")
                .Where(f => StemOfBackup(f) == stem)
                .OrderByDescending(f => f, StringComparer.Ordinal).ToList();

    /// <summary>A backup's character: its name up to the "~time".</summary>
    private static string StemOfBackup(string backup)
    {
        var name = Stem(backup);
        var tilde = name.LastIndexOf('~');
        return tilde < 0 ? name : name[..tilde];
    }

    /// <summary>Copies the save as it stands into backups (unless one was kept lately), and prunes the oldest.</summary>
    private void KeepBackup(string path, bool force)
    {
        try
        {
            var stem = Stem(path);
            var existing = BackupFiles(stem);
            var now = UtcNow();
            if (!force && existing.Count > 0 && now - File.GetLastWriteTimeUtc(existing[0]) < BackupInterval) return;
            System.IO.Directory.CreateDirectory(BackupDirectory);
            var stamp = now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            // Named by time and a sequence number, so their names sort newest last, and two in the same
            // second (a restore right after a save) never overwrite each other.
            string backup;
            var n = 1;
            do backup = System.IO.Path.Combine(BackupDirectory, $"{stem}~{stamp}-{n++:D2}{SaveGame.Extension}");
            while (File.Exists(backup));
            File.Copy(path, backup);
            File.SetLastWriteTimeUtc(backup, now);
            foreach (var old in BackupFiles(stem).Skip(BackupsKept)) File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A backup that can't be kept mustn't stop the save itself.
        }
    }

    /// <summary>The earlier saves of the character saved at this path, newest first (unreadable ones skipped).</summary>
    public IReadOnlyList<SaveEntry> Backups(string savePath)
    {
        var stem = savePath.StartsWith(BackupDirectory, StringComparison.Ordinal) ? StemOfBackup(savePath) : Stem(savePath);
        var entries = new List<SaveEntry>();
        foreach (var file in BackupFiles(stem))
            if (ReadEntry(file) is { } entry) entries.Add(entry with { IsBackup = true });
        return entries;
    }

    /// <summary>
    /// Puts an earlier save back as the character's save (keeping the one it replaces as a backup, so
    /// a restore can be undone), and returns it ready to load.
    /// </summary>
    public SaveEntry Restore(SaveEntry backup)
    {
        var main = System.IO.Path.Combine(Directory, StemOfBackup(backup.Path) + SaveGame.Extension);
        var earlier = File.ReadAllBytes(backup.Path);        // first: keeping the current one may prune it
        if (File.Exists(main) && ReadEntry(main) is not null) KeepBackup(main, force: true);
        var temp = main + ".restoring";
        File.WriteAllBytes(temp, earlier);
        File.Move(temp, main, overwrite: true);
        return new SaveEntry(main, backup.Summary, RestoredFrom: backup.Summary.SavedAtUtc);
    }

    private static SaveEntry? ReadEntry(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return new SaveEntry(path, SaveGame.ReadSummary(stream));
        }
        catch (Exception ex) when (ex is SaveGameException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public GameSession Load(GameData data, string path) => SaveGame.LoadFromFile(data, path);

    /// <summary>
    /// Every readable save, newest first — and each character whose save is missing or damaged but
    /// has backups, from its newest backup (marked, so it can be restored).
    /// </summary>
    public IReadOnlyList<SaveEntry> List()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        var entries = new List<SaveEntry>();
        var stems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*" + SaveGame.Extension))
            if (ReadEntry(path) is { } entry)
            {
                entries.Add(entry);
                stems.Add(Stem(path));
            }
        if (System.IO.Directory.Exists(BackupDirectory))
            foreach (var stem in System.IO.Directory.EnumerateFiles(BackupDirectory, "*" + SaveGame.Extension)
                         .Select(StemOfBackup).Distinct(StringComparer.Ordinal).Where(s => !stems.Contains(s)))
                if (BackupFiles(stem).Select(ReadEntry).FirstOrDefault(e => e is not null) is { } newest)
                    entries.Add(newest with { IsBackup = true, MainDamaged = true });
        return entries.OrderByDescending(e => e.Summary.SavedAtUtc).ToList();
    }

    /// <summary>Deletes a save and its backups (from the Load dialog, asked twice; or at death).</summary>
    public void Delete(string path)
    {
        var stem = path.StartsWith(BackupDirectory, StringComparison.Ordinal) ? StemOfBackup(path) : Stem(path);
        var main = System.IO.Path.Combine(Directory, stem + SaveGame.Extension);
        if (File.Exists(main)) File.Delete(main);
        foreach (var backup in BackupFiles(stem)) File.Delete(backup);
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
