using System.Text.Json;

namespace Angband.Core.Monsters;

/// <summary>What the player has learned about one monster race (Angband struct monster_lore).</summary>
public sealed class RaceLore
{
    /// <summary>Times it has been seen (each monster counts once).</summary>
    public int Sights { get; set; }
    /// <summary>Killed by any character.</summary>
    public int TotalKills { get; set; }
    /// <summary>Characters it has killed.</summary>
    public int Deaths { get; set; }
    /// <summary>How often each of its blows (by index) has been seen.</summary>
    public List<int> BlowsSeen { get; set; } = [];
    /// <summary>Spells (ids) it has been seen to use.</summary>
    public SortedSet<string> SpellsSeen { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Spells cast while watched (for the frequency estimate).</summary>
    public int CastsSeen { get; set; }
    /// <summary>Innate attacks (arrows, breaths...) seen (Angband cast_innate): over 50, their frequency is known.</summary>
    public int CastsInnate { get; set; }
    /// <summary>Other spells seen cast (Angband cast_spell): over 50, their frequency is known.</summary>
    public int CastsSpell { get; set; }
    /// <summary>Turns it was watched while able to act (for the frequency estimate).</summary>
    public int TurnsWatched { get; set; }
    /// <summary>Flags the player has worked out (resistances, "evil", breeding...).</summary>
    public SortedSet<string> FlagsKnown { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// Resistances and vulnerabilities the player has tested and found the race lacks (Angband keeps
    /// these in lore->flags too: known flags the race doesn't have). "It does not resist fire."
    /// </summary>
    public SortedSet<string> FlagsLacking { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Most objects / gold seen dropped by one of them.</summary>
    public int MaxItemsDropped { get; set; }
    public int MaxGoldDropped { get; set; }
    /// <summary>Probed: everything about the race is known.</summary>
    public bool Probed { get; set; }

    public int BlowSeen(int index) => index < BlowsSeen.Count ? BlowsSeen[index] : 0;

    public void SeeBlow(int index)
    {
        while (BlowsSeen.Count <= index) BlowsSeen.Add(0);
        BlowsSeen[index]++;
    }
}

/// <summary>
/// The player's monster memory, kept across characters as Angband 4.2 does (its lore.txt). Loaded
/// from and saved to a JSON file by the application.
/// </summary>
public sealed class MonsterLoreBook
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public Dictionary<string, RaceLore> Races { get; set; } = new(StringComparer.Ordinal);

    public RaceLore For(string raceId)
    {
        if (!Races.TryGetValue(raceId, out var lore)) Races[raceId] = lore = new RaceLore();
        return lore;
    }

    public RaceLore? Find(string raceId) => Races.GetValueOrDefault(raceId);

    public static MonsterLoreBook Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new MonsterLoreBook();
            var book = JsonSerializer.Deserialize<MonsterLoreBook>(File.ReadAllText(path), Json) ?? new MonsterLoreBook();
            book.Races = new Dictionary<string, RaceLore>(book.Races, StringComparer.Ordinal);
            return book;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new MonsterLoreBook();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, path, overwrite: true);
    }
}
