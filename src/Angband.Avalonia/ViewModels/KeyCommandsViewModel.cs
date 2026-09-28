using System.Collections.ObjectModel;
using Angband.Avalonia.Input;
using Angband.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Angband.Avalonia.ViewModels;

/// <summary>One command: what it's called, what it does, and the keys and button that do it now.</summary>
public sealed record CommandRow(string Command, string Description, string Keys, string Button)
{
    public bool Matches(string filter) =>
        filter.Length == 0
        || Command.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Description.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Keys.Contains(filter, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A group of commands under a heading.</summary>
public sealed record CommandGroup(string Title, IReadOnlyList<CommandRow> Rows);

/// <summary>
/// The keyboard commands window ('?' or F1, like Angband's command help): every command, grouped,
/// with the keys and controller button currently bound to it (rebinding in Settings → Controls
/// shows up here), and the keys that work only in particular places — menus, look mode, stores.
/// </summary>
public sealed partial class KeyCommandsViewModel : ObservableObject
{
    // What each command does, by group, in the order a player meets them.
    private static readonly (string Title, (InputAction Action, string Description)[] Commands)[] Table =
    [
        ("Moving", [
            (InputAction.MoveNorth, "Step north, or attack a monster there"),
            (InputAction.MoveSouth, "Step south, or attack"),
            (InputAction.MoveWest, "Step west, or attack"),
            (InputAction.MoveEast, "Step east, or attack"),
            (InputAction.MoveNorthWest, "Step north-west, or attack"),
            (InputAction.MoveNorthEast, "Step north-east, or attack"),
            (InputAction.MoveSouthWest, "Step south-west, or attack"),
            (InputAction.MoveSouthEast, "Step south-east, or attack"),
            (InputAction.RunNorth, "Run north until something interesting happens"),
            (InputAction.RunSouth, "Run south until something interesting happens"),
            (InputAction.RunWest, "Run west until something interesting happens"),
            (InputAction.RunEast, "Run east until something interesting happens"),
            (InputAction.RunNorthWest, "Run north-west until something interesting happens"),
            (InputAction.RunNorthEast, "Run north-east until something interesting happens"),
            (InputAction.RunSouthWest, "Run south-west until something interesting happens"),
            (InputAction.RunSouthEast, "Run south-east until something interesting happens"),
            (InputAction.Run, "Run, asking which way. Runs follow corridors round bends; a monster, object, door or junction stops them"),
            (InputAction.Hold, "Stay in place for a turn (secret doors beside you are found, as on every step)"),
            (InputAction.Rest, "Rest until healed or disturbed"),
            (InputAction.StairsDown, "Take a staircase down"),
            (InputAction.StairsUp, "Take a staircase up"),
            (InputAction.Open, "Open a door or chest (asks for a direction)"),
            (InputAction.Close, "Close a door"),
            (InputAction.Tunnel, "Dig through rubble, veins or rock"),
            (InputAction.Disarm, "Disarm a trap or a trapped chest"),
            (InputAction.EnterStore, "Enter the store you are standing on"),
        ]),
        ("Items", [
            (InputAction.Wield, "Wear or wield an item"),
            (InputAction.TakeOff, "Take off something you are wearing"),
            (InputAction.Pickup, "Pick up items here"),
            (InputAction.Drop, "Drop an item"),
            (InputAction.Quaff, "Quaff (drink) a potion"),
            (InputAction.Read, "Read a scroll"),
            (InputAction.Eat, "Eat some food"),
            (InputAction.Refuel, "Refuel your light"),
            (InputAction.Inspect, "Inspect an item: what it does and what you know of it"),
            (InputAction.Inscribe, "Inscribe an item (e.g. @q1, !d)"),
            (InputAction.Uninscribe, "Remove an inscription"),
            (InputAction.Ignore, "Ignore an item, or everything like it"),
            (InputAction.ToggleIgnore, "Show or hide ignored items"),
        ]),
        ("Fighting and devices", [
            (InputAction.Fire, "Fire a missile from your launcher"),
            (InputAction.FireNearest, "Fire the first missile in the quiver at the nearest monster (Tab too, outside stores)"),
            (InputAction.TargetClosest, "Target the nearest monster"),
            (InputAction.Throw, "Throw an item (or your weapon)"),
            (InputAction.AimWand, "Aim a wand"),
            (InputAction.UseStaff, "Use a staff"),
            (InputAction.ZapRod, "Zap a rod"),
            (InputAction.Activate, "Activate a worn item"),
            (InputAction.UseItem, "Use any item: quaff, read, eat, aim, use, zap or activate it, as it needs"),
            (InputAction.Steal, "Steal from a monster next to you (rogues)"),
        ]),
        ("Repeating", [
            (InputAction.RepeatCommand, "Do the last command again: cast, fire, throw, use an item, dig, disarm... (aimed ones aim afresh)"),
            (InputAction.CommandCount, "Type a count, then walk, hold, tunnel, open or disarm that many times (until disturbed)"),
        ]),
        ("Magic", [
            (InputAction.Cast, "Cast a spell or pray"),
            (InputAction.Study, "Learn a new spell from a book"),
            (InputAction.Browse, "Browse a spell book"),
        ]),
        ("Looking and information", [
            (InputAction.Look, "Look around: step through what you can see"),
            (InputAction.Target, "Choose a target for missiles and spells"),
            (InputAction.Help, "Help: how to play, your character, fighting, magic, the birth options"),
            (InputAction.IdentifySymbol, "What a symbol on the map stands for (and the monsters you have met shown with it)"),
            (InputAction.OverviewMap, "The whole level at a glance"),
            (InputAction.Locate, "Scroll the map half a screen at a time; Esc comes back"),
            (InputAction.CenterMap, "Centre the map on you once (when it isn't kept centred)"),
            (InputAction.ContextMenu, "The right-click menu: for the square under the look cursor, or for you"),
            (InputAction.DescribeSurroundings, "Say what is around you: hit points, where, monsters and objects in view, stairs"),
            (InputAction.HotbarPrevious, "Hotbar: select the slot to the left"),
            (InputAction.HotbarNext, "Hotbar: select the slot to the right"),
            (InputAction.HotbarUse, "Hotbar: use the selected slot (Alt+1..Alt+0 use a slot directly)"),
            (InputAction.WalkIntoTrap, "Walk onto a trap you know of, on purpose (walking at one disarms it)"),
            (InputAction.MonsterList, "List the monsters you can see or sense"),
            (InputAction.ObjectList, "List the objects you know of on the level"),
            (InputAction.MonsterKnowledge, "Knowledge: monsters, objects, runes, egos, artifacts"),
            (InputAction.CharacterSheet, "Character sheet and paper doll"),
            (InputAction.Feeling, "Repeat the level feeling"),
            (InputAction.SaveAndQuit, "Save the character and close the game"),
            (InputAction.TakeNote, "Write a note in your history (it goes in the character dump)"),
            (InputAction.MessageHistory, "Message history: everything said this game, with a Find box"),
            (InputAction.HighScores, "High scores"),
            (InputAction.ShowCommands, "This list of commands"),
        ]),
        ("Menus and questions", [
            (InputAction.Confirm, "Choose; say yes; otherwise the obvious action here (stairs, pick up...)"),
            (InputAction.Cancel, "Back out of a menu or question; close a window"),
            (InputAction.SwitchPane, "In a store: switch between buying and selling"),
        ]),
        ("Game and display", [
            (InputAction.SaveGame, "Save the game"),
            (InputAction.LoadGame, "Load a saved character"),
            (InputAction.NewGame, "Create a new character"),
            (InputAction.Retire, "Retire (after winning)"),
            (InputAction.Options, "Game options"),
            (InputAction.OpenSettings, "Settings: display, sound, controls, options"),
            (InputAction.ToggleTiles, "Switch between tiles and letters"),
            (InputAction.ToggleMute, "Mute or unmute sound"),
            (InputAction.ZoomIn, "Zoom in"),
            (InputAction.ZoomOut, "Zoom out"),
        ]),
        ("Debug", [
            (InputAction.ShowWholeMap, "Show the whole map"),
            (InputAction.RegenerateLevel, "Make a new level"),
            (InputAction.JumpDeeper, "Go 5 levels deeper"),
            (InputAction.JumpNextLevel, "Go to the next level down, at a random spot"),
            (InputAction.JumpToTown, "Go straight back to town (Word of Recall without the wait)"),
        ]),
    ];

    /// <summary>Keys that only mean something in particular places, so they can't be rebound.</summary>
    private static readonly (string Title, (string Keys, string Description)[] Keys)[] ContextKeys =
    [
        ("In menus", [
            ("a-z", "Pick the item or spell with that letter"),
            ("0-9", "Pick the item inscribed for that digit (@q1, @v2...)"),
            ("any other key", "Close the menu"),
        ]),
        ("Look and target mode", [
            ("direction keys", "Move the cursor"),
            ("x / space / +", "Next thing to look at (- for the previous one)"),
            ("r", "Recall what you know of the monster"),
            ("t / 5 / Enter", "Target it"),
            ("p", "Put the cursor back on yourself and move it freely"),
            ("Esc", "Stop looking"),
        ]),
        ("When asked for a direction", [
            ("direction keys", "That way"),
            ("t / '", "Toward the current target"),
            ("Enter", "Toward the target, if there is one"),
        ]),
        ("In a store", [
            ("a-z", "Buy (or sell) one of that item"),
            ("Shift + letter", "The whole stack"),
            ("Tab", "Switch between buying and selling"),
            ("I", "Examine an item"),
            ("Esc", "Leave"),
        ]),
        ("Lists and windows", [
            ("x", "In the monster list: sort by experience"),
            ("Esc / controller B", "Close a window"),
            ("D-pad, A", "Move between and press a window's controls with a controller"),
        ]),
        ("Mouse", [
            ("left click", "Walk there (if Mouse movement is on in Options)"),
            ("right click", "Fire at a monster, or describe the square"),
            ("wheel", "Zoom"),
        ]),
    ];

    private readonly List<CommandGroup> _all;

    public KeyCommandsViewModel(InputBindings bindings)
    {
        var groups = Table.Select(g => new CommandGroup(g.Title, [.. g.Commands.Select(c => new CommandRow(
            c.Action.Label(), c.Description,
            Keys(bindings.KeysFor(c.Action)),
            string.Join(", ", bindings.ButtonsFor(c.Action))))]));
        var context = ContextKeys.Select(g => new CommandGroup(g.Title, [.. g.Keys.Select(k => new CommandRow("", k.Description, k.Keys, ""))]));
        _all = [.. groups, .. context];
        ApplyFilter();
    }

    public ObservableCollection<CommandGroup> Groups { get; } = [];

    /// <summary>Keys as typed — "m", "&gt;", "Ctrl+D", "NumPad5" — between dots, so "," reads as a key.</summary>
    private static string Keys(IEnumerable<string> chords)
    {
        var shown = chords.Select(c => c.StartsWith("Char:", StringComparison.Ordinal) ? c[5..] : KeyboardInput.Display(c)).ToList();
        return shown.Count == 0 ? "(none)" : string.Join(" · ", shown);
    }

    /// <summary>Only commands whose name, description or keys contain this.</summary>
    [ObservableProperty] private string _filter = "";

    public bool NothingFound => Groups.Count == 0;

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Groups.Clear();
        var text = Filter.Trim();
        foreach (var group in _all)
        {
            var rows = group.Rows.Where(r => r.Matches(text) || group.Title.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
            if (rows.Count > 0) Groups.Add(new CommandGroup(group.Title, rows));
        }
        OnPropertyChanged(nameof(NothingFound));
    }

    /// <summary>Every action the window explains (to check none is forgotten).</summary>
    public static IEnumerable<InputAction> Explained => Table.SelectMany(g => g.Commands).Select(c => c.Action);
}
