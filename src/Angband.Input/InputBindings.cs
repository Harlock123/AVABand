using System.Text.Json;

namespace Angband.Input;

/// <summary>
/// Which keys and gamepad buttons trigger which actions. Keyboard chords are key names with
/// optional modifiers (<c>Up</c>, <c>NumPad8</c>, <c>Ctrl+T</c>, <c>F7</c>) or typed characters
/// (<c>Char:&gt;</c>, <c>Char:G</c>), which follow the keyboard layout. Gamepad buttons use SDL's
/// game-controller names (<c>A</c>, <c>B</c>, <c>LeftShoulder</c>, <c>RightTrigger</c>...), or two
/// of them held together (<c>LeftTrigger+A</c>); <c>LeftTrigger+DPad</c> bound to
/// <see cref="InputAction.Run"/> makes that button plus a direction run. Several chords may trigger
/// one action; each chord or button triggers one action.
/// </summary>
public sealed class InputBindings
{
    public Dictionary<string, InputAction> Keys { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, InputAction> Buttons { get; init; } = new(StringComparer.Ordinal);
    /// <summary>Keymaps (Angband): a key chord and the keys it types (see <see cref="KeymapText"/>).</summary>
    public Dictionary<string, string> Keymaps { get; init; } = new(StringComparer.Ordinal);

    /// <summary>The keymap for the first of these chords that has one, if any.</summary>
    public string? KeymapFor(IEnumerable<string> chords) =>
        chords.Select(c => Keymaps.GetValueOrDefault(c)).FirstOrDefault(a => !string.IsNullOrEmpty(a));

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
        K(InputAction.ShowCommands, "F1");
        K(InputAction.Help, "Char:?");
        K(InputAction.TargetClosest, "Char:'");
        K(InputAction.IdentifySymbol, "Char:/");
        K(InputAction.Locate, "Char:W"); // Angband's L runs east here
        K(InputAction.WalkIntoTrap, "Char:-");
        K(InputAction.UseItem, "Char:X"); // Angband's U runs north-east here
        K(InputAction.TakeNote, "Char::");
        K(InputAction.SaveAndQuit, "Ctrl+X");
        K(InputAction.CenterMap, "Ctrl+L");
        // The map menu and the hotbar cursor, for keyboards too (Alt+digit uses a slot directly).
        K(InputAction.ContextMenu, "Char:&");
        K(InputAction.HotbarPrevious, "Alt+Left");
        K(InputAction.HotbarNext, "Alt+Right");
        K(InputAction.HotbarUse, "Alt+Enter");
        K(InputAction.FireNearest, "Shift+Tab"); // and Tab itself outside stores (Angband's key)
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
        // The left trigger is also a shift: with a direction it runs, with A it repeats the last command
        // (a tap on its own still throws).
        P(InputAction.Run, "LeftTrigger+DPad");
        P(InputAction.RepeatCommand, "LeftTrigger+A");
        // ...with the shoulders it steps through the hotbar, with Y uses the slot, with X opens the map menu.
        P(InputAction.HotbarPrevious, "LeftTrigger+LeftShoulder");
        P(InputAction.HotbarNext, "LeftTrigger+RightShoulder");
        P(InputAction.HotbarUse, "LeftTrigger+Y");
        P(InputAction.ContextMenu, "LeftTrigger+X");
        return b;
    }

    /// <summary>The keyboard layouts on offer: AVABand's own, and Angband 4.2's two command keysets.</summary>
    public enum Keyset { Avaband, Original, Roguelike }

    /// <summary>
    /// A keyboard layout (gamepad buttons as in <see cref="Defaults"/>): AVABand's defaults, or one of
    /// Angband 4.2's keysets (ui-game.c's command table, original key / roguelike key). Arrows, the
    /// keypad, Enter, Escape, the function keys and the menus' Ctrl keys work in all of them.
    /// </summary>
    public static InputBindings Preset(Keyset keyset)
    {
        var b = Defaults();
        if (keyset == Keyset.Avaband) return b;
        // Drop the letters and the few symbols the keysets use differently; keep everything else.
        foreach (var chord in b.Keys.Keys.Where(k => k.StartsWith("Char:", StringComparison.Ordinal)
                     && (char.IsLetter(k[5]) || k[5..] is "." or "," or ";")).ToList())
            b.Keys.Remove(chord);
        void K(InputAction a, params string[] chords) { foreach (var c in chords) b.Keys[c] = a; }
        var rogue = keyset == Keyset.Roguelike;
        if (rogue)
        {
            K(InputAction.MoveWest, "Char:h"); K(InputAction.MoveSouth, "Char:j"); K(InputAction.MoveNorth, "Char:k");
            K(InputAction.MoveEast, "Char:l"); K(InputAction.MoveNorthWest, "Char:y"); K(InputAction.MoveNorthEast, "Char:u");
            K(InputAction.MoveSouthWest, "Char:b"); K(InputAction.MoveSouthEast, "Char:n");
            K(InputAction.RunWest, "Char:H"); K(InputAction.RunSouth, "Char:J"); K(InputAction.RunNorth, "Char:K");
            K(InputAction.RunEast, "Char:L"); K(InputAction.RunNorthWest, "Char:Y"); K(InputAction.RunNorthEast, "Char:U");
            K(InputAction.RunSouthWest, "Char:B"); K(InputAction.RunSouthEast, "Char:N");
            b.Keys.Remove("Ctrl+T");
            K(InputAction.Tunnel, "Ctrl+T");
            K(InputAction.ToggleTiles, "Ctrl+Shift+T"); // Ctrl+T digs in the roguelike keys
        }
        else K(InputAction.Tunnel, "Char:T");
        K(InputAction.Run, rogue ? "Char:," : "Char:.");
        K(InputAction.Hold, rogue ? "Char:." : "Char:,");
        K(InputAction.Look, rogue ? "Char:x" : "Char:l");
        K(InputAction.TakeOff, rogue ? "Char:T" : "Char:t");
        K(InputAction.Fire, rogue ? "Char:t" : "Char:f");
        K(InputAction.AimWand, rogue ? "Char:z" : "Char:a");
        K(InputAction.ZapRod, rogue ? "Char:a" : "Char:z");
        K(InputAction.UseStaff, rogue ? "Char:Z" : "Char:u");
        K(InputAction.Browse, rogue ? "Char:P" : "Char:b");
        K(InputAction.ToggleIgnore, rogue ? "Char:O" : "Char:K");
        if (!rogue) K(InputAction.Ignore, "Char:k");
        if (!rogue) K(InputAction.RepeatCommand, "Char:n");
        if (!rogue) K(InputAction.FireNearest, "Char:h"); // (and Tab, in both, outside stores)
        K(InputAction.Locate, rogue ? "Char:W" : "Char:L");
        if (!rogue) K(InputAction.WalkIntoTrap, "Char:W"); // ('-' in both)
        K(InputAction.UseItem, rogue ? "Char:X" : "Char:U");
        if (rogue) K(InputAction.CenterMap, "Char:@"); // (and Ctrl+L, in both)
        K(InputAction.Steal, "Char:s");
        K(InputAction.Open, "Char:o"); K(InputAction.Close, "Char:c"); K(InputAction.Disarm, "Char:D");
        K(InputAction.Rest, "Char:R"); K(InputAction.Wield, "Char:w"); K(InputAction.Quaff, "Char:q");
        K(InputAction.Read, "Char:r"); K(InputAction.Eat, "Char:E"); K(InputAction.Drop, "Char:d");
        K(InputAction.Throw, "Char:v"); K(InputAction.Activate, "Char:A"); K(InputAction.Pickup, "Char:g");
        K(InputAction.Refuel, "Char:F"); K(InputAction.Inspect, "Char:I"); K(InputAction.Cast, "Char:m");
        K(InputAction.Study, "Char:G"); K(InputAction.Retire, "Char:Q"); K(InputAction.CharacterSheet, "Char:C");
        K(InputAction.OverviewMap, "Char:M");
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
                Keymaps = new Dictionary<string, string>(loaded.Keymaps ?? [], StringComparer.Ordinal),
            };
            bindings.MoveHelpToQuestionMark();
            bindings.AddMissingDefaults();
            return bindings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return Defaults();
        }
    }

    /// <summary>
    /// Bindings saved before the help existed have '?' on the command list (with F1). As long as
    /// that is still so — nobody chose it — '?' goes to the help, as in Angband, and F1 keeps the list.
    /// </summary>
    public void MoveHelpToQuestionMark()
    {
        if (Keys.GetValueOrDefault("Char:?") == InputAction.ShowCommands && Keys.GetValueOrDefault("F1") == InputAction.ShowCommands
            && !Keys.ContainsValue(InputAction.Help))
            Keys["Char:?"] = InputAction.Help;
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
