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
        InputAction.SwitchPane => "Buy / sell (stores)",
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
