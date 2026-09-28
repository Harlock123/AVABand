using System.Text.Json;

namespace Angband.Input;

/// <summary>
/// Which keys and gamepad buttons trigger which actions. Keyboard chords are key names with
/// optional modifiers (<c>Up</c>, <c>NumPad8</c>, <c>Ctrl+T</c>, <c>F7</c>) or typed characters
/// (<c>Char:&gt;</c>, <c>Char:G</c>), which follow the keyboard layout. Gamepad buttons use SDL's
/// game-controller names (<c>A</c>, <c>B</c>, <c>LeftShoulder</c>, <c>RightTrigger</c>...). Several
/// chords may trigger one action; each chord or button triggers one action.
/// </summary>
public sealed class InputBindings
{
    public Dictionary<string, InputAction> Keys { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, InputAction> Buttons { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Gamepad buttons SDL's game-controller API knows.</summary>
    public static readonly IReadOnlyList<string> ButtonNames =
    [
        "A", "B", "X", "Y", "Back", "Guide", "Start", "LeftStick", "RightStick", "LeftShoulder", "RightShoulder",
        "DPadUp", "DPadDown", "DPadLeft", "DPadRight", "LeftTrigger", "RightTrigger",
    ];

    public static InputBindings Defaults()
    {
        var b = new InputBindings();
        void K(InputAction a, params string[] chords) { foreach (var c in chords) b.Keys[c] = a; }
        K(InputAction.MoveNorth, "Up", "NumPad8", "Char:k");
        K(InputAction.MoveSouth, "Down", "NumPad2", "Char:j");
        K(InputAction.MoveWest, "Left", "NumPad4", "Char:h");
        K(InputAction.MoveEast, "Right", "NumPad6", "Char:l");
        K(InputAction.MoveNorthWest, "Home", "NumPad7", "Char:y");
        K(InputAction.MoveNorthEast, "PageUp", "NumPad9", "Char:u");
        K(InputAction.MoveSouthWest, "End", "NumPad1", "Char:b");
        K(InputAction.MoveSouthEast, "PageDown", "NumPad3", "Char:n");
        // Running: Shift with the arrows or keypad (Angband), Shift with the roguelike letters where
        // they are free (K and B already toggle ignoring and browse), or '.' and a direction.
        K(InputAction.RunNorth, "Shift+Up", "Shift+NumPad8");
        K(InputAction.RunSouth, "Shift+Down", "Shift+NumPad2", "Char:J");
        K(InputAction.RunWest, "Shift+Left", "Shift+NumPad4", "Char:H");
        K(InputAction.RunEast, "Shift+Right", "Shift+NumPad6", "Char:L");
        K(InputAction.RunNorthWest, "Shift+Home", "Shift+NumPad7", "Char:Y");
        K(InputAction.RunNorthEast, "Shift+PageUp", "Shift+NumPad9", "Char:U");
        K(InputAction.RunSouthWest, "Shift+End", "Shift+NumPad1");
        K(InputAction.RunSouthEast, "Shift+PageDown", "Shift+NumPad3", "Char:N");
        K(InputAction.Run, "Char:.");
        K(InputAction.Hold, "NumPad5", "Clear", "Char:5", "Char:,");
        K(InputAction.Steal, "Char:s");
        K(InputAction.Options, "Char:=");
        K(InputAction.Inscribe, "Char:{");
        K(InputAction.Ignore, "Ctrl+D");
        K(InputAction.ToggleIgnore, "Char:K");
        K(InputAction.Uninscribe, "Char:}");
        K(InputAction.Confirm, "Enter");
        K(InputAction.Cancel, "Escape");
        K(InputAction.StairsDown, "Char:>");
        K(InputAction.StairsUp, "Char:<");
        K(InputAction.Open, "Char:o");
        K(InputAction.Close, "Char:c");
        K(InputAction.Tunnel, "Char:T");
        K(InputAction.Disarm, "Char:D");
        K(InputAction.Fire, "Char:f");
        K(InputAction.Rest, "Char:R");
        K(InputAction.Wield, "Char:w");
        K(InputAction.TakeOff, "Char:t");
        K(InputAction.Quaff, "Char:q");
        K(InputAction.Read, "Char:r");
        K(InputAction.Eat, "Char:E");
        K(InputAction.Drop, "Char:d");
        K(InputAction.Throw, "Char:v");
        K(InputAction.AimWand, "Char:a");
        K(InputAction.UseStaff, "Char:Z");
        K(InputAction.ZapRod, "Char:z");
        K(InputAction.Activate, "Char:A");
        K(InputAction.Look, "Char:x");
        K(InputAction.Target, "Char:*", "Multiply");
        K(InputAction.MonsterKnowledge, "Char:~");
        K(InputAction.Pickup, "Char:g");
        K(InputAction.Refuel, "Char:F");
        K(InputAction.Inspect, "Char:I");
        K(InputAction.Cast, "Char:m");
        K(InputAction.Study, "Char:G");
        K(InputAction.Browse, "Char:B");
        K(InputAction.EnterStore, "Char:_");
        K(InputAction.SwitchPane, "Tab");
        K(InputAction.ToggleTiles, "Ctrl+T");
        K(InputAction.ToggleMute, "Ctrl+M");
        K(InputAction.OpenSettings, "F10");
        K(InputAction.ZoomIn, "Ctrl+OemPlus", "Ctrl+Add");
        K(InputAction.ZoomOut, "Ctrl+OemMinus", "Ctrl+Subtract");
        K(InputAction.NewGame, "Ctrl+N");
        K(InputAction.SaveGame, "Ctrl+S");
        K(InputAction.LoadGame, "Ctrl+O");
        K(InputAction.Retire, "Char:Q");
        K(InputAction.CharacterSheet, "Char:C");
        K(InputAction.HighScores, "Ctrl+H");
        K(InputAction.Feeling, "Ctrl+F");
        K(InputAction.MessageHistory, "Ctrl+P");
        K(InputAction.RepeatCommand, "Ctrl+V");
        K(InputAction.CommandCount, "Char:0");
        K(InputAction.OverviewMap, "Char:M"); // Angband's 'n' moves here (roguelike keys), so its roguelike key
        K(InputAction.MonsterList, "Char:[");
        K(InputAction.ObjectList, "Char:]");
        K(InputAction.ShowCommands, "Char:?", "F1");
        K(InputAction.RegenerateLevel, "F5");
        K(InputAction.JumpDeeper, "F6");
        K(InputAction.JumpNextLevel, "F8");
        K(InputAction.JumpToTown, "F9");
        K(InputAction.ShowWholeMap, "F7");

        void P(InputAction a, string button) => b.Buttons[button] = a;
        P(InputAction.MoveNorth, "DPadUp");
        P(InputAction.MoveSouth, "DPadDown");
        P(InputAction.MoveWest, "DPadLeft");
        P(InputAction.MoveEast, "DPadRight");
        P(InputAction.Confirm, "A");
        P(InputAction.Cancel, "B");
        P(InputAction.Fire, "X");
        P(InputAction.Cast, "Y");
        P(InputAction.Quaff, "LeftShoulder");
        P(InputAction.Read, "RightShoulder");
        P(InputAction.Throw, "LeftTrigger");
        P(InputAction.Wield, "RightTrigger");
        P(InputAction.Rest, "Back");
        P(InputAction.OpenSettings, "Start");
        P(InputAction.Hold, "LeftStick");
        P(InputAction.ToggleTiles, "RightStick");
        return b;
    }

    public InputAction ForKey(string chord) => Keys.GetValueOrDefault(chord);
    public InputAction ForButton(string button) => Buttons.GetValueOrDefault(button);

    public IEnumerable<string> KeysFor(InputAction action) => Keys.Where(kv => kv.Value == action).Select(kv => kv.Key).Order(StringComparer.Ordinal);
    public IEnumerable<string> ButtonsFor(InputAction action) => Buttons.Where(kv => kv.Value == action).Select(kv => kv.Key).Order(StringComparer.Ordinal);

    /// <summary>Binds a key chord to an action (taking it away from whatever it did before).</summary>
    public void BindKey(string chord, InputAction action) => Keys[chord] = action;

    /// <summary>Binds a button, replacing the action's previous button (one button per action on a pad).</summary>
    public void BindButton(string button, InputAction action)
    {
        foreach (var old in ButtonsFor(action).ToList()) Buttons.Remove(old);
        Buttons[button] = action;
    }

    public void ClearKeys(InputAction action)
    {
        foreach (var chord in KeysFor(action).ToList()) Keys.Remove(chord);
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVABand", "bindings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>Loads saved bindings, or the defaults if there are none (or the file is unreadable).</summary>
    public static InputBindings Load(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            if (!File.Exists(file)) return Defaults();
            var loaded = JsonSerializer.Deserialize<InputBindings>(File.ReadAllText(file), Json);
            if (loaded is null || loaded.Keys.Count == 0) return Defaults();
            var bindings = new InputBindings
            {
                Keys = new Dictionary<string, InputAction>(loaded.Keys, StringComparer.Ordinal),
                Buttons = new Dictionary<string, InputAction>(loaded.Buttons, StringComparer.Ordinal),
            };
            bindings.AddMissingDefaults();
            return bindings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return Defaults();
        }
    }

    /// <summary>
    /// Actions added since the bindings were saved get their default keys and buttons, unless the
    /// player has already used those for something else.
    /// </summary>
    public void AddMissingDefaults()
    {
        var defaults = Defaults();
        var boundKeys = Keys.Values.ToHashSet();
        foreach (var (key, action) in defaults.Keys)
            if (!boundKeys.Contains(action) && !Keys.ContainsKey(key)) Keys[key] = action;
        var boundButtons = Buttons.Values.ToHashSet();
        foreach (var (button, action) in defaults.Buttons)
            if (!boundButtons.Contains(action) && !Buttons.ContainsKey(button)) Buttons[button] = action;
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
            // Bindings are a convenience; failing to save must not break the game.
        }
    }
}
