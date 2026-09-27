using Angband.Core.Game;
using Angband.Core.Records;

namespace Angband.Avalonia;

/// <summary>
/// Where the high-score table (<c>scores.json</c>) and character dumps (<c>dumps/</c>) live,
/// normally <c>&lt;AppData&gt;/AVABand</c>.
/// </summary>
public sealed class RecordStore(string directory)
{
    public string Directory { get; } = directory;
    public string ScoresPath => Path.Combine(Directory, "scores.json");
    public string DumpDirectory => Path.Combine(Directory, "dumps");
    /// <summary>Monster memory, shared by every character (as Angband 4.2's lore.txt).</summary>
    public string LorePath => Path.Combine(Directory, "lore.json");

    public Angband.Core.Monsters.MonsterLoreBook LoadLore() => Angband.Core.Monsters.MonsterLoreBook.Load(LorePath);

    public void SaveLore(Angband.Core.Monsters.MonsterLoreBook lore)
    {
        try
        {
            lore.Save(LorePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Monster memory is a convenience; losing an update must not stop the game.
        }
    }

    public static RecordStore Default() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVABand"));

    public ScoreBoard LoadScores() => ScoreBoard.Load(ScoresPath);

    /// <summary>Adds a finished character to the table; returns its rank (0 if it didn't place).</summary>
    public int RecordScore(ScoreEntry entry)
    {
        var board = LoadScores();
        var rank = board.Add(entry);
        if (rank > 0) board.Save(ScoresPath);
        return rank;
    }

    /// <summary>Writes a dump to <c>dumps/&lt;name&gt;-&lt;date&gt;.txt</c> and returns its path.</summary>
    public string WriteDump(GameSession game, string text)
    {
        System.IO.Directory.CreateDirectory(DumpDirectory);
        var safe = string.Concat(game.Player.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsWhiteSpace(c) ? '_' : c));
        var path = Path.Combine(DumpDirectory, $"{(safe.Length == 0 ? "character" : safe)}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, text);
        return path;
    }
}
