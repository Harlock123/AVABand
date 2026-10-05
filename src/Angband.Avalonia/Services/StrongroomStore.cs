using System.Text.Json;
using Angband.Core.Game;

namespace Angband.Avalonia;

/// <summary>
/// Butterbur's strongroom (AVABand's own): the lockers every one of your characters shares, kept in
/// <c>&lt;AppData&gt;/AVABand/strongroom.json</c> beside the high scores and the graveyard — outside any
/// one character's save, so they outlive the character that filled them. Written whole each time, through
/// a temporary file, so a crash leaves the old file or the new one, never half of one.
/// <para>
/// And kept in step with the character's save, in two steps: leaving or taking something is first written
/// as pending (with which save and when), then the character is saved, then it's made final
/// (<see cref="Commit"/>). If the game stops between the two, <see cref="Recover"/> on the next start
/// looks at that save: written since, and the change is made final; not, and it's undone — so an item is
/// never both in a locker and in a save, nor in neither.
/// </para>
/// </summary>
public sealed class StrongroomStore(string directory, Func<DateTime>? clock = null) : IStrongroom
{
    /// <summary>A locker as the file keeps it: committed, or pending a deposit or a taking.</summary>
    public sealed record Entry(StrongroomLocker Locker, string? Pending = null, string? Save = null, DateTime At = default);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.Now);

    public string FilePath => Path.Combine(directory, "strongroom.json");

    /// <summary>The save file of the character now playing (to check after a crash whether it was written); null for none.</summary>
    public Func<string?> CurrentSave { get; set; } = () => null;

    public static StrongroomStore Default() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVABand"));

    /// <summary>The lockers in use (a deposit not yet final doesn't count; one being taken has gone).</summary>
    public IReadOnlyList<StrongroomLocker> Lockers => [.. Load().Where(e => e.Pending is null).Select(e => e.Locker)];

    public void Deposit(StrongroomLocker locker)
    {
        var entries = Load();
        var made = locker with { Id = Guid.NewGuid().ToString("N")[..12], Date = _clock().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) };
        entries.Add(new Entry(made, "deposit", CurrentSave(), DateTime.UtcNow));
        Write(entries);
    }

    public bool Remove(string id)
    {
        var entries = Load();
        var i = entries.FindIndex(e => e.Locker.Id == id && e.Pending is null);
        if (i < 0) return false;
        entries[i] = entries[i] with { Pending = "take", Save = CurrentSave(), At = DateTime.UtcNow };
        Write(entries);
        return true;
    }

    /// <summary>The character is saved: what was pending is final.</summary>
    public void Commit()
    {
        var entries = Load();
        if (entries.All(e => e.Pending is null)) return;
        Write([.. entries.Where(e => e.Pending != "take").Select(e => e with { Pending = null, Save = null, At = default })]);
    }

    /// <summary>
    /// On starting: anything left pending by a game that stopped — made final if its character's save was
    /// written after it, undone if not. Returns how many were sorted out.
    /// </summary>
    public int Recover()
    {
        var entries = Load();
        var pending = entries.Count(e => e.Pending is not null);
        if (pending == 0) return 0;
        bool Saved(Entry e) => e.Save is { } path && File.Exists(path) && File.GetLastWriteTimeUtc(path) > e.At;
        var kept = new List<Entry>();
        foreach (var e in entries)
        {
            if (e.Pending is null) kept.Add(e);
            else if (e.Pending == "deposit" && Saved(e)) kept.Add(e with { Pending = null, Save = null, At = default }); // left, and saved without it
            else if (e.Pending == "take" && !Saved(e)) kept.Add(e with { Pending = null, Save = null, At = default });   // never reached the save: back in its locker
        }
        Write(kept);
        return pending;
    }

    private List<Entry> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            var text = File.ReadAllText(FilePath);
            // (a file from before the two steps: plain lockers)
            if (JsonDocument.Parse(text).RootElement is { ValueKind: JsonValueKind.Array } root && root.GetArrayLength() > 0
                && !root[0].TryGetProperty(nameof(Entry.Locker), out _))
                return [.. (JsonSerializer.Deserialize<List<StrongroomLocker>>(text, Json) ?? []).Select(l => new Entry(l))];
            return JsonSerializer.Deserialize<List<Entry>>(text, Json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private void Write(List<Entry> entries)
    {
        Directory.CreateDirectory(directory);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entries, Json));
        File.Move(temp, FilePath, overwrite: true);
    }
}
