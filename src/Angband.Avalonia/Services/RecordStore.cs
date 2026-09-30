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

    /// <summary>Every character that died or retired (AVABand's own).</summary>
    public string GraveyardPath => Path.Combine(Directory, "graveyard.json");

    /// <summary>The graveyard; the first time, with the deaths the high scores remember brought in.</summary>
    public Graveyard LoadGraveyard()
    {
        var yard = Graveyard.Load(GraveyardPath);
        if (!yard.Backfilled)
        {
            yard.Backfill(LoadScores().Entries);
            SaveGraveyard(yard);
        }
        return yard;
    }

    public void SaveGraveyard(Graveyard yard)
    {
        try
        {
            yard.Save(GraveyardPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The graveyard is a memorial, not a record the game needs.
        }
    }

    /// <summary>A character's end, laid in the graveyard.</summary>
    public void Bury(FallenRecord record)
    {
        var yard = LoadGraveyard();
        yard.Add(record);
        SaveGraveyard(yard);
    }

    /// <summary>The feats done across every character (AVABand's own).</summary>
    public string FeatsPath => Path.Combine(Directory, "feats.json");

    public FeatBook LoadFeats() => FeatBook.Load(FeatsPath);

    public void SaveFeats(FeatBook feats)
    {
        try
        {
            feats.Save(FeatsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Feats are a pleasure, not a record the game needs: a failed write is forgotten.
        }
    }

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
