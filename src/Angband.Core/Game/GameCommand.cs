using Angband.Core.Geometry;

namespace Angband.Core.Game;

/// <summary>
/// Everything the player can ask for. Keyboard, mouse and gamepad input all end up as one of
/// these, so the engine never deals with raw input.
/// </summary>
public abstract record GameCommand;

/// <summary>Step in a direction; bumping a door opens it (Angband "easy_alter").</summary>
public sealed record WalkCommand(Direction Direction) : GameCommand;

/// <summary>Walk onto a square even if you know of a trap there (Angband jump, 'W' / roguelike '-').</summary>
public sealed record JumpCommand(Direction Direction) : GameCommand;

/// <summary>Spend a turn doing nothing.</summary>
public sealed record HoldCommand : GameCommand;

public sealed record OpenCommand(Direction Direction) : GameCommand;

/// <summary>Retire a winning character (ends the game with the victory on the score table).</summary>
public sealed record RetireCommand : GameCommand;

/// <summary>Disarm a chest or a known trap in a direction (<see cref="Direction.Here"/> for underfoot).</summary>
public sealed record DisarmCommand(Direction Direction) : GameCommand;

/// <summary>Tunnel into rubble, a vein or a wall (Angband 'T'), repeating until through or disturbed.</summary>
public sealed record TunnelCommand(Direction Direction) : GameCommand;

/// <summary>
/// A command given a count (Angband's '0' prefix, "Repeat: 20"): walking and holding repeat that
/// many times; tunnelling, opening and disarming try at most that many times (99 without a count).
/// Anything that disturbs the player stops the repetition.
/// </summary>
public sealed record CountedCommand(GameCommand Command, int Count) : GameCommand;

/// <summary>Inscribe an item (Angband '{'); an empty text removes the inscription. Takes no time.</summary>
public sealed record InscribeCommand(Items.Item Item, string Text) : GameCommand;

/// <summary>Remove an item's inscription (Angband '}'). Takes no time.</summary>
public sealed record UninscribeCommand(Items.Item Item) : GameCommand;

/// <summary>Repeat the level feeling (Angband Ctrl+F). Takes no time.</summary>
public sealed record FeelingCommand : GameCommand;

/// <summary>Steal from the adjacent monster in a direction (rogues; Angband 's').</summary>
public sealed record StealCommand(Direction Direction) : GameCommand;

public sealed record CloseCommand(Direction Direction) : GameCommand;

/// <summary>Take the staircase underfoot.</summary>
public sealed record TakeStairsCommand(bool Down) : GameCommand;

/// <summary>Debug: jump straight to a depth.</summary>
public sealed record DebugJumpCommand(int Depth) : GameCommand;

/// <summary>Debug: cure everything (Angband do_cmd_wiz_cure_all).</summary>
public sealed record DebugCureAllCommand : GameCommand;

/// <summary>Fire the launcher at a square (or the nearest visible monster), using given or first matching ammo.</summary>
public sealed record FireCommand(Loc? Target = null, Items.Item? Ammo = null) : GameCommand;

/// <summary>Pick up everything underfoot, or one item.</summary>
public sealed record PickupCommand(Items.Item? Item = null) : GameCommand;

public sealed record DropCommand(Items.Item Item, int Count = 1) : GameCommand;

public sealed record WieldCommand(Items.Item Item) : GameCommand;

public sealed record TakeOffCommand(Items.Item Item) : GameCommand;

/// <summary>Quaff a potion, read a scroll or eat food.</summary>
/// <summary>Activate a worn item (Angband 'A'), aiming it if its effect needs a target or direction.</summary>
public sealed record ActivateCommand(Items.Item Item, Loc? Target = null, Direction? Direction = null, char? Glyph = null) : GameCommand;

/// <summary>Return to the player's own shape.</summary>
public sealed record ResumeShapeCommand : GameCommand;

/// <param name="Glyph">For banishment: the monster letter to banish.</param>
/// <param name="Uncurse">For Remove Curse: the item and the curse on it to break.</param>
public sealed record UseCommand(Items.Item Item, Loc? Target = null, Direction? Direction = null, char? Glyph = null,
    CurseChoice? Uncurse = null) : GameCommand;

/// <summary>A curse chosen for Remove Curse to attack (Angband get_item, then get_curse).</summary>
public sealed record CurseChoice(Items.Item Item, string Curse);

/// <summary>Throw an item at a square, or the nearest visible monster when null.</summary>
public sealed record ThrowCommand(Items.Item Item, Loc? Target = null) : GameCommand;

/// <summary>Refill the wielded lantern from a flask of oil.</summary>
public sealed record RefuelCommand(Items.Item Fuel) : GameCommand;

/// <summary>Rest until healed or disturbed.</summary>
public sealed record RestCommand : GameCommand;

/// <summary>Learn a new spell (a specific one, or the first available).</summary>
/// <summary>
/// Learn a spell. Classes that choose (Angband CHOOSE_SPELLS) name it; the others name a
/// <paramref name="Book"/> and are given one of its spells at random, as 4.2's priests and paladins are.
/// </summary>
public sealed record StudyCommand(string? SpellId = null, string? Book = null) : GameCommand;

/// <summary>
/// Cast a learned spell at a target (or the nearest visible monster), or in a direction for spells
/// that need one. Casting without enough mana must be allowed explicitly: it may make you faint.
/// </summary>
public sealed record CastCommand(string SpellId, Loc? Target = null, Direction? Direction = null, bool AllowOverexert = false,
    CurseChoice? Uncurse = null) : GameCommand;

/// <summary>
/// Walk to a known square along the shortest known path (mouse click travel). Stops when a
/// monster is in view, the player is hurt, or the way is blocked.
/// </summary>
public sealed record TravelCommand(Loc Target) : GameCommand;

/// <summary>Run one way until something interesting happens (Angband Shift+direction or '.').</summary>
public sealed record RunCommand(Direction Direction) : GameCommand;

/// <summary>
/// AVABand: a run's first step, the run then taken a step at a time (<see cref="RunOnCommand"/>) so the
/// interface can draw each one. Each step takes a step's time.
/// </summary>
public sealed record RunStartCommand(Direction Direction) : GameCommand;

/// <summary>AVABand: the next step of the run under way (see <see cref="RunStartCommand"/>); false once it's over.</summary>
public sealed record RunOnCommand : GameCommand;

/// <summary>Enter the store whose entrance the player is standing on (Angband '_').</summary>
public sealed record EnterStoreCommand : GameCommand;

/// <summary>An answer to the last quest prompt (<see cref="QuestPromptEvent"/>). Takes no time.</summary>
public sealed record QuestChoiceCommand(string Choice) : GameCommand;

/// <summary>Buy from the current store (at home: take back). Takes no game time.</summary>
public sealed record BuyCommand(Items.Item Item, int Count = 1) : GameCommand;

/// <summary>Sell to the current store (at home: store). Takes no game time.</summary>
public sealed record SellCommand(Items.Item Item, int Count = 1) : GameCommand;

/// <summary>Leave the store screen (no game time; lets listeners react).</summary>
public sealed record LeaveStoreCommand : GameCommand;
