using System.Text.Json;
using Angband.Core.Game;

namespace Angband.Avalonia;

/// <summary>
/// Butterbur's strongroom (AVABand's own): the lockers every one of your characters shares, kept in
/// <c>&lt;AppData&gt;/AVABand/strongroom.json</c> beside the high scores and the graveyard — outside any
/// one character's save, so they outlive the character that filled them. Written whole each time, through
/// a temporary file, so a crash leaves the old file or the new one, never half of one.
/// </summary>
public sealed class StrongroomStore(string directory, Func<DateTime>? clock = null) : IStrongroom
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.Now);

    public string FilePath => Path.Combine(directory, "strongroom.json");

    public static StrongroomStore Default() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVABand"));

    public IReadOnlyList<StrongroomLocker> Lockers => Load();

    public void Deposit(StrongroomLocker locker)
    {
        var lockers = Load();
        lockers.Add(locker with { Id = Guid.NewGuid().ToString("N")[..12], Date = _clock().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) });
        Write(lockers);
    }

    public bool Remove(string id)
    {
        var lockers = Load();
        if (lockers.RemoveAll(l => l.Id == id) == 0) return false;
        Write(lockers);
        return true;
    }

    private List<StrongroomLocker> Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<StrongroomLocker>>(File.ReadAllText(FilePath), Json) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private void Write(List<StrongroomLocker> lockers)
    {
        Directory.CreateDirectory(directory);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(lockers, Json));
        File.Move(temp, FilePath, overwrite: true);
    }
}
