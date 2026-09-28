using System.Text.Json;

namespace Angband.Avalonia;

/// <summary>Display preferences, saved to <c>&lt;AppData&gt;/AVABand/settings.json</c>.</summary>
public sealed class AppSettings
{
    public bool UseTiles { get; set; }
    /// <summary>Folder name of the chosen tileset.</summary>
    public string? TilesetId { get; set; } = "adam-bolt";
    public double TileScale { get; set; } = 2.0;
    /// <summary>The size of everything but the map: sidebar, message and status lines, menus, dialogs (1 = 100%).</summary>
    public double InterfaceScale { get; set; } = 1.0;
    public double FontSize { get; set; } = 16;
    public bool EffectsEnabled { get; set; } = true;
    public bool MusicEnabled { get; set; } = true;
    public bool Muted { get; set; }
    /// <summary>Sidebar lists (Angband's monster and object list subwindows).</summary>
    public bool ShowMonsterPanel { get; set; }
    public bool ShowObjectPanel { get; set; }
    /// <summary>Volumes 0..100.</summary>
    public double MasterVolume { get; set; } = 80;
    public double EffectsVolume { get; set; } = 80;
    public double MusicVolume { get; set; } = 50;
    public string? SoundPackId { get; set; } = "angband-dubtrain";
    public string? MusicPackId { get; set; } = "cc0-dungeon-music";
    /// <summary>Audio buffer size (<see cref="Angband.Audio.AudioBuffer"/>): applied when the game starts.</summary>
    public string AudioBuffer { get; set; } = Angband.Audio.AudioBuffer.Automatic;

    /// <summary>The last character created, reused by quick start (null until the first one).</summary>
    public SavedCharacter? LastCharacter { get; set; }

    /// <summary>Interface options (Angband's '=' menu), by Angband name; missing ones take their defaults.</summary>
    public Dictionary<string, bool> Options { get; set; } = [];

    /// <summary>Class used by "New game" (Ctrl+N).</summary>
    public string LastClass { get; set; } = "warrior";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVABand", "settings.json");

    /// <summary>Loads settings, falling back to defaults if the file is missing or unreadable.</summary>
    public static AppSettings Load(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            return File.Exists(file) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; failing to save must not break the game.
        }
    }
}

/// <summary>A character spec as stored in settings.</summary>
public sealed class SavedCharacter
{
    public string Name { get; set; } = "Adventurer";
    public string Race { get; set; } = "human";
    public string Class { get; set; } = "warrior";
    public Dictionary<string, int> Stats { get; set; } = [];
    public bool Rolled { get; set; }
    /// <summary>The birth options chosen with this character.</summary>
    public Dictionary<string, bool> BirthOptions { get; set; } = [];
}
