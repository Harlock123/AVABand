using Angband.Core.Geometry;

namespace Angband.Input;

/// <summary>Everything an input device can ask for. Devices produce actions; the UI decides what they mean now.</summary>
public enum InputAction
{
    None,
    MoveNorth, MoveNorthEast, MoveEast, MoveSouthEast, MoveSouth, MoveSouthWest, MoveWest, MoveNorthWest,
    Hold,
    /// <summary>Accept: choose in a menu, "yes" to a question, otherwise the context action (stairs, pick up...).</summary>
    Confirm,
    /// <summary>Back out of a menu or question.</summary>
    Cancel,
    StairsDown, StairsUp, Open, Close, Tunnel, Disarm, Fire, Rest,
    Wield, TakeOff, Quaff, Read, Eat, Drop, Throw, Pickup, Refuel, Inspect,
    Cast, Study, Browse,
    /// <summary>Aim a wand, use a staff, zap a rod (Angband a / u / z; u is taken by movement here).</summary>
    AimWand, UseStaff, ZapRod,
    /// <summary>Look at visible monsters (Angband roguelike 'x'; l moves here).</summary>
    Look,
    /// <summary>Pick a target (Angband '*').</summary>
    Target,
    /// <summary>The knowledge browser (Angband '~'); the name is kept so saved bindings still load.</summary>
    MonsterKnowledge,
    /// <summary>Activate a worn item (Angband 'A').</summary>
    Activate,
    /// <summary>Ignore an item (Angband 'k'; here Ctrl+D, the roguelike key, as k moves) / show ignored items ('K').</summary>
    Ignore, ToggleIgnore,
    /// <summary>Inscribe an item (Angband '{') / remove an inscription ('}').</summary>
    Inscribe, Uninscribe,
    /// <summary>Repeat the level feeling (Angband Ctrl+F).</summary>
    Feeling,
    /// <summary>The monster list (Angband '[') / the object list (Angband ']').</summary>
    MonsterList, ObjectList,
    /// <summary>The options menu (Angband '=').</summary>
    Options,
    /// <summary>Steal from an adjacent monster (rogues; Angband 's'). Other classes hold instead.</summary>
    Steal,
    /// <summary>Enter the store underfoot (Angband '_').</summary>
    EnterStore,
    /// <summary>In a store: switch between buying and selling.</summary>
    SwitchPane,
    ToggleTiles, ToggleMute, OpenSettings, ZoomIn, ZoomOut, NewGame, SaveGame, LoadGame, Retire,
    CharacterSheet, HighScores,
    ShowWholeMap, RegenerateLevel, JumpDeeper,
    /// <summary>The list of commands and their keys (Angband '?' help). Added last so saved bindings keep their meaning.</summary>
    ShowCommands,
    /// <summary>
    /// Run until something interesting happens (Angband Shift+direction, roguelike Shift+letter): one
    /// per direction, and <see cref="Run"/> ('.') asks which way. Added last so saved bindings keep their meaning.
    /// </summary>
    RunNorth, RunNorthEast, RunEast, RunSouthEast, RunSouth, RunSouthWest, RunWest, RunNorthWest, Run,
    /// <summary>Debug: jump to the next level down / straight back to town. Added last so saved bindings keep their meaning.</summary>
    JumpNextLevel, JumpToTown,
    /// <summary>The message history (Angband Ctrl+P). Added last so saved bindings keep their meaning.</summary>
    MessageHistory,
    /// <summary>Repeat the last command (Angband 'n' / roguelike Ctrl+V). Added last so saved bindings keep their meaning.</summary>
    RepeatCommand,
    /// <summary>The whole level at a glance (Angband 'M'). Added last so saved bindings keep their meaning.</summary>
    OverviewMap,
    /// <summary>
    /// Give the next command a count (Angband '0': "Repeat: 20", then the command). Added last so
    /// saved bindings keep their meaning.
    /// </summary>
    CommandCount,
    /// <summary>The help pages (Angband '?'). Added last so saved bindings keep their meaning.</summary>
    Help,
    /// <summary>
    /// Fire the first missile at the nearest monster (Angband h, or Tab) and target the nearest
    /// monster (Angband apostrophe). Added last so saved bindings keep their meaning.
    /// </summary>
    FireNearest, TargetClosest,
    /// <summary>Identify a symbol (Angband '/'). Added last so saved bindings keep their meaning.</summary>
    IdentifySymbol,
    /// <summary>Scroll the map about (Angband locate, 'L' / roguelike 'W'). Added last so saved bindings keep their meaning.</summary>
    Locate,
    /// <summary>
    /// Walk onto a trap on purpose (Angband 'W' / roguelike '-') and use any item (Angband 'U' /
    /// roguelike 'X'). Added last so saved bindings keep their meaning.
    /// </summary>
    WalkIntoTrap, UseItem,
    /// <summary>Write a note in the history (Angband ':'). Added last so saved bindings keep their meaning.</summary>
    TakeNote,
    /// <summary>Save and close the game (Angband Ctrl+X). Added last so saved bindings keep their meaning.</summary>
    SaveAndQuit,
    /// <summary>Centre the map on the player once (Angband Ctrl+L / roguelike '@'). Added last so saved bindings keep their meaning.</summary>
    CenterMap,
    /// <summary>The hotbar and the map menu from a gamepad (AVABand's own). Added last so saved bindings keep their meaning.</summary>
    HotbarNext, HotbarPrevious, HotbarUse, ContextMenu,
    /// <summary>Say what is around you (for screen readers; AVABand's own). Added last so saved bindings keep their meaning.</summary>
    DescribeSurroundings,
    /// <summary>
    /// Pick an item from the pack (Angband 'i') or what you wear ('e') for its menu, and AVABand's
    /// "Clear out junk". Added last so saved bindings keep their meaning.
    /// </summary>
    Inventory, Equipment, ClearJunk,
    /// <summary>Show where you've been on this level (AVABand's own). Added last so saved bindings keep their meaning.</summary>
    ShowTrail,
    /// <summary>The quest log (AVABand's own). Added last so saved bindings keep their meaning.</summary>
    QuestLog,
    /// <summary>Full screen on or off (AVABand's own). Added last so saved bindings keep their meaning.</summary>
    ToggleFullScreen,
    /// <summary>A careful search of the squares around you (AVABand's own). Added last so saved bindings keep their meaning.</summary>
    Search,
    /// <summary>Auto-explore (AVABand's own). Added last so saved bindings keep their meaning.</summary>
    Explore,
}

public enum InputDevice { Keyboard, Mouse, Gamepad }

public static class InputActions
{
    public static readonly InputAction[] Movement =
    [
        InputAction.MoveNorth, InputAction.MoveNorthEast, InputAction.MoveEast, InputAction.MoveSouthEast,
        InputAction.MoveSouth, InputAction.MoveSouthWest, InputAction.MoveWest, InputAction.MoveNorthWest,
    ];

    public static Direction? ToDirection(this InputAction action) => action switch
    {
        InputAction.MoveNorth => Direction.North,
        InputAction.MoveNorthEast => Direction.NorthEast,
        InputAction.MoveEast => Direction.East,
        InputAction.MoveSouthEast => Direction.SouthEast,
        InputAction.MoveSouth => Direction.South,
        InputAction.MoveSouthWest => Direction.SouthWest,
        InputAction.MoveWest => Direction.West,
        InputAction.MoveNorthWest => Direction.NorthWest,
        _ => null,
    };

    /// <summary>The direction of a run action (Shift+direction).</summary>
    public static Direction? ToRunDirection(this InputAction action) => action switch
    {
        InputAction.RunNorth => Direction.North,
        InputAction.RunNorthEast => Direction.NorthEast,
        InputAction.RunEast => Direction.East,
        InputAction.RunSouthEast => Direction.SouthEast,
        InputAction.RunSouth => Direction.South,
        InputAction.RunSouthWest => Direction.SouthWest,
        InputAction.RunWest => Direction.West,
        InputAction.RunNorthWest => Direction.NorthWest,
        _ => null,
    };

    /// <summary>The run action for a direction.</summary>
    public static InputAction RunFromDirection(Direction dir) => dir switch
    {
        Direction.North => InputAction.RunNorth,
        Direction.NorthEast => InputAction.RunNorthEast,
        Direction.East => InputAction.RunEast,
        Direction.SouthEast => InputAction.RunSouthEast,
        Direction.South => InputAction.RunSouth,
        Direction.SouthWest => InputAction.RunSouthWest,
        Direction.West => InputAction.RunWest,
        Direction.NorthWest => InputAction.RunNorthWest,
        _ => InputAction.Run,
    };

    public static InputAction FromDirection(Direction dir) => dir switch
    {
        Direction.North => InputAction.MoveNorth,
        Direction.NorthEast => InputAction.MoveNorthEast,
        Direction.East => InputAction.MoveEast,
        Direction.SouthEast => InputAction.MoveSouthEast,
        Direction.South => InputAction.MoveSouth,
        Direction.SouthWest => InputAction.MoveSouthWest,
        Direction.West => InputAction.MoveWest,
        Direction.NorthWest => InputAction.MoveNorthWest,
        _ => InputAction.Hold,
    };

    /// <summary>A readable label for settings screens ("MoveNorthEast" → "Move north-east").</summary>
    public static string Label(this InputAction action) => action switch
    {
        InputAction.MoveNorthEast => "Move north-east",
        InputAction.MoveSouthEast => "Move south-east",
        InputAction.MoveSouthWest => "Move south-west",
        InputAction.MoveNorthWest => "Move north-west",
        InputAction.RunNorthEast => "Run north-east",
        InputAction.RunSouthEast => "Run south-east",
        InputAction.RunSouthWest => "Run south-west",
        InputAction.RunNorthWest => "Run north-west",
        InputAction.Run => "Run (asks which way)",
        InputAction.Confirm => "Confirm / context action",
        InputAction.Cancel => "Cancel",
        InputAction.TakeOff => "Take off",
        InputAction.Wield => "Wear / wield",
        InputAction.Pickup => "Pick up",
        InputAction.MonsterKnowledge => "Knowledge",
        InputAction.Hold => "Hold (stay put)",
        InputAction.Inscribe => "Inscribe",
        InputAction.SwitchPane => "Buy / sell (stores); fire at nearest",
        InputAction.FireNearest => "Fire at nearest",
        InputAction.TargetClosest => "Target closest",
        InputAction.IdentifySymbol => "Identify a symbol",
        InputAction.Locate => "Locate (scroll the map)",
        InputAction.WalkIntoTrap => "Walk into a trap",
        InputAction.UseItem => "Use an item",
        InputAction.TakeNote => "Take a note",
        InputAction.SaveAndQuit => "Save and quit",
        InputAction.CenterMap => "Center map",
        InputAction.HotbarNext => "Hotbar: next slot",
        InputAction.HotbarPrevious => "Hotbar: previous slot",
        InputAction.HotbarUse => "Hotbar: use the selected slot",
        InputAction.ContextMenu => "Menu for the square (the look cursor's, or yours)",
        InputAction.DescribeSurroundings => "Describe surroundings",
        InputAction.ShowTrail => "Show where you've been",
        InputAction.QuestLog => "Quest log",
        InputAction.ToggleFullScreen => "Full screen",
        InputAction.Search => "Search carefully",
        InputAction.Explore => "Explore",
        InputAction.AimWand => "Aim a wand",
        InputAction.UseStaff => "Use a staff",
        InputAction.ZapRod => "Zap a rod",
        InputAction.Activate => "Activate an item",
        InputAction.MonsterList => "Monster list",
        InputAction.ObjectList => "Object list",
        InputAction.StairsDown => "Go down stairs",
        InputAction.StairsUp => "Go up stairs",
        InputAction.ToggleTiles => "Toggle tiles/ASCII",
        InputAction.ShowWholeMap => "Show whole map (debug)",
        InputAction.RegenerateLevel => "Regenerate level (debug)",
        InputAction.JumpDeeper => "Jump 5 levels (debug)",
        InputAction.JumpNextLevel => "Jump to the next level (debug)",
        InputAction.JumpToTown => "Jump back to town (debug)",
        InputAction.ShowCommands => "Keyboard commands",
        InputAction.RepeatCommand => "Repeat last command",
        InputAction.OverviewMap => "Level map",
        InputAction.CommandCount => "Repeat count",
        _ => System.Text.RegularExpressions.Regex.Replace(action.ToString(), "(?<=[a-z])([A-Z])", " $1").ToLowerInvariant() is var s
            ? char.ToUpperInvariant(s[0]) + s[1..] : action.ToString(),
    };
}

/// <summary>Something that turns a device's input into <see cref="InputAction"/>s.</summary>
public interface IInputProvider : IDisposable
{
    InputDevice Device { get; }
    /// <summary>Human-readable status, e.g. "Xbox Wireless Controller" or "No controller".</summary>
    string Status { get; }
    event Action<InputAction>? ActionTriggered;
}
