using System.Collections.ObjectModel;
using Angband.Avalonia.Input;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>What the gamepad layer offers the UI (status, and capturing a button for rebinding).</summary>
public interface IGamepadService
{
    string Status { get; }
    void CaptureNextButton(Action<string> onButton);
    void UseBindings(InputBindings bindings);
}

/// <summary>A keymap on the Controls tab: its key, and the keys it types (editable).</summary>
public sealed partial class KeymapRow(string chord, string action, Action<string> changed) : ObservableObject
{
    public string Chord { get; } = chord;
    public string Key { get; } = KeyboardInput.Display(chord);
    [ObservableProperty] private string _action = action;
    partial void OnActionChanged(string value) => changed(value);
}

/// <summary>A row of the Controls settings tab.</summary>
public sealed partial class BindingRow(InputAction action) : ObservableObject
{
    public InputAction Action { get; } = action;
    public string Label { get; } = action.Label();
    [ObservableProperty] private string _keys = "";
    [ObservableProperty] private string _button = "";
}

/// <summary>
/// The input router: keyboard, mouse and gamepad all arrive here as <see cref="InputAction"/>s and
/// are interpreted for the current situation (menu, question, direction prompt, or play).
/// </summary>
public sealed partial class MainWindowViewModel
{
    private enum DirectionFor { None, Open, Close, Device, Tunnel, Disarm, Steal, Run, Jump }
    private DirectionFor _pendingDirection;
    private Angband.Core.Items.Item? _pendingDevice;
    private bool _pendingActivation;

    /// <summary>Stone to mud and other line devices (or activations) ask for a direction first.</summary>
    private void AskDeviceDirection(Angband.Core.Items.Item device, bool activate = false)
    {
        _pendingDevice = device;
        _pendingActivation = activate;
        AskDirection(DirectionFor.Device);
    }
    private IGamepadService? _gamepad;
    private Action<InputBindings>? _saveBindings;

    public InputBindings Bindings { get; private set; } = InputBindings.Defaults();
    public ObservableCollection<BindingRow> BindingRows { get; } = [];

    [ObservableProperty] private int _promptSelectedIndex;
    [ObservableProperty] private string _gamepadStatus = "Gamepad support is off.";
    /// <summary>Set while the Controls tab waits for a key or button to bind.</summary>
    [ObservableProperty] private string? _captureHint;

    private InputAction _capturingAction;
    private bool _capturingKey;

    /// <summary>Called once by the app with the saved bindings and (optionally) the gamepad service.</summary>
    public void UseInput(InputBindings bindings, IGamepadService? gamepad, Action<InputBindings>? save)
    {
        Bindings = bindings;
        _gamepad = gamepad;
        _saveBindings = save;
        GamepadStatus = gamepad?.Status ?? "Gamepad support is unavailable.";
        RefreshBindingRows();
    }

    public void RefreshGamepadStatus() => GamepadStatus = _gamepad?.Status ?? GamepadStatus;

    /// <summary>Routes an action from any device.</summary>
    public void HandleAction(InputAction action)
    {
        if (action == InputAction.None) return;
        EndPeek(); // (any command brings the view back first)
        if (HandleWaitingScene(action)) return;
        if (IsShowingTitle)
        {
            switch (action)
            {
                case InputAction.MoveNorth: MoveTitleSelection(-1); break;
                case InputAction.MoveSouth: MoveTitleSelection(+1); break;
                case InputAction.Confirm: ChooseSelectedTitle(); break;
            }
            return;
        }
        if (ReplayAction(action)) return;
        if (IsEnteringNumber)
        {
            NumberAction(action);
            return;
        }
        // A question comes first, even in a store (Enter/A must answer it, not buy or sell).
        if (IsConfirming)
        {
            if (action is InputAction.Confirm or InputAction.Cancel) Confirm(action == InputAction.Confirm);
            return;
        }
        // Outside the map (menus, stores, lists, look mode) Shift+direction just moves, as the direction would.
        if ((IsInStore || IsShowingList || IsConfirming || IsLooking || IsPrompting) && action.ToRunDirection() is { } runDir)
            action = InputActions.FromDirection(runDir);
        if (HandleStoreAction(action)) return;
        if (HandleListAction(action)) return;

        if (IsConfirming)
        {
            if (action is InputAction.Confirm or InputAction.Cancel) Confirm(action == InputAction.Confirm);
            return;
        }

        // The map menu from look mode is for the square under the cursor (so before look mode ends).
        if (action == InputAction.ContextMenu && IsLooking)
        {
            OpenContextMenuHere();
            return;
        }
        if (HandleLookAction(action)) return;
        if (IsLocating)
        {
            LocateAction(action);
            return;
        }

        if (IsPrompting)
        {
            switch (action)
            {
                case InputAction.MoveNorth: MovePromptSelection(-1); break;
                case InputAction.MoveSouth: MovePromptSelection(+1); break;
                case InputAction.Confirm: ChooseSelectedPromptRow(); break;
                case InputAction.Cancel: CancelPrompt(); break;
            }
            return;
        }

        if (OfferToResumeShape(action)) return;

        var dir = action.ToDirection();
        if (PendingSpellDirection is not null)
        {
            if (dir is null && action == InputAction.Confirm) dir = _game.DirectionToTarget();
            if (dir is not null || action == InputAction.Cancel) CastInDirection(dir);
            return;
        }
        if (_pendingDirection != DirectionFor.None)
        {
            var what = _pendingDirection;
            // "Hold" means the square underfoot, for a chest you're standing on.
            if (action == InputAction.Hold && what is DirectionFor.Open or DirectionFor.Disarm) dir = Direction.Here;
            _pendingDirection = DirectionFor.None;
            dir ??= action.ToRunDirection(); // Shift+direction answers too
            if (dir is null && action == InputAction.Confirm) dir = _game.DirectionToTarget();
            if (dir is not { } d)
            {
                LastMessage = "Cancelled.";
                ClearCount();
            }
            else if (what == DirectionFor.Device && _pendingDevice is { } device)
                Execute(_pendingActivation ? new ActivateCommand(device, Direction: d) : new UseCommand(device, Direction: d));
            else if (what == DirectionFor.Tunnel) Execute(new TunnelCommand(d));
            else if (what == DirectionFor.Run) StartRun(d, action);
            else if (what == DirectionFor.Jump) Execute(new JumpCommand(d));
            else if (what == DirectionFor.Disarm) Execute(new DisarmCommand(d));
            else if (what == DirectionFor.Steal) Execute(new StealCommand(d));
            else Execute(what == DirectionFor.Open ? new OpenCommand(d) : new CloseCommand(d));
            _pendingDevice = null;
            return;
        }

        if (dir is { } move)
        {
            Execute(new WalkCommand(move));
            return;
        }
        if (action.ToRunDirection() is { } run)
        {
            StartRun(run, action);
            return;
        }

        switch (action)
        {
            case InputAction.Hold: Execute(new HoldCommand()); break;
            case InputAction.Search: Execute(new SearchCommand()); break;
            case InputAction.Confirm: ContextAction(); break;
            case InputAction.Cancel: ClearCount(); break;
            case InputAction.StairsDown: StairsKey(down: true); break;
            case InputAction.StairsUp: StairsKey(down: false); break;
            case InputAction.Open: AskDirection(DirectionFor.Open); break;
            case InputAction.Close: AskDirection(DirectionFor.Close); break;
            case InputAction.Tunnel: AskDirection(DirectionFor.Tunnel); break;
            case InputAction.Run: AskDirection(DirectionFor.Run); break;
            case InputAction.Disarm: AskDirection(DirectionFor.Disarm); break;
            case InputAction.Steal:
                if (_game.ClassHas(ClassFlags.Steal)) AskDirection(DirectionFor.Steal);
                else Execute(new HoldCommand()); // 's' is just "hold" for everyone else
                break;
            case InputAction.Fire: FireDefault(); break;
            // Tab switches buying and selling in a store and, as in Angband, fires at the nearest monster elsewhere.
            case InputAction.FireNearest or InputAction.SwitchPane: FireAtNearest(); break;
            case InputAction.TargetClosest:
                _game.TargetClosest();
                Refresh();
                break;
            case InputAction.Inscribe: BeginItemPrompt(ItemPromptKind.Inscribe); break;
            case InputAction.Ignore: BeginItemPrompt(ItemPromptKind.Ignore); break;
            case InputAction.ToggleIgnore: Execute(new ToggleUnignoreCommand()); break;
            case InputAction.Uninscribe: BeginItemPrompt(ItemPromptKind.Uninscribe); break;
            case InputAction.Rest: Execute(new RestCommand()); break;
            case InputAction.Wield: BeginItemPrompt(ItemPromptKind.Wield); break;
            case InputAction.TakeOff: BeginItemPrompt(ItemPromptKind.TakeOff); break;
            case InputAction.Quaff: BeginItemPrompt(ItemPromptKind.Quaff); break;
            case InputAction.Read:
                // Angband player_can_read_prereq: said before the scrolls are offered.
                if (_game.CannotRead() is { } why) AddMessage(why);
                else BeginItemPrompt(ItemPromptKind.Read);
                break;
            case InputAction.Eat: BeginItemPrompt(ItemPromptKind.Eat); break;
            case InputAction.Drop: BeginItemPrompt(ItemPromptKind.Drop); break;
            case InputAction.Throw: BeginItemPrompt(ItemPromptKind.Throw); break;
            case InputAction.Pickup: BeginItemPrompt(ItemPromptKind.Pickup); break;
            case InputAction.Refuel: BeginItemPrompt(ItemPromptKind.Refuel); break;
            case InputAction.Inspect: BeginItemPrompt(ItemPromptKind.Inspect); break;
            case InputAction.Inventory: BeginItemPrompt(ItemPromptKind.InventoryMenu); break;
            case InputAction.Equipment: BeginItemPrompt(ItemPromptKind.EquipmentMenu); break;
            case InputAction.ClearJunk: BeginClearJunk(); break;
            case InputAction.AimWand: BeginItemPrompt(ItemPromptKind.Aim); break;
            case InputAction.UseStaff: BeginItemPrompt(ItemPromptKind.UseStaff); break;
            case InputAction.ZapRod: BeginItemPrompt(ItemPromptKind.Zap); break;
            case InputAction.Activate: BeginItemPrompt(ItemPromptKind.Activate); break;
            case InputAction.Look: Look(); break;
            case InputAction.Target: TargetMode(); break;
            case InputAction.MonsterKnowledge: ShowKnowledge(); break;
            case InputAction.ShowCommands: ShowKeyCommands(); break;
            case InputAction.Help: ShowHelp(); break;
            case InputAction.IdentifySymbol: BeginIdentifySymbol(); break;
            case InputAction.Locate: BeginLocate(); break;
            case InputAction.CenterMap: CenterMap(); break;
            case InputAction.HotbarNext: MoveHotbarCursor(+1); break;
            case InputAction.HotbarPrevious: MoveHotbarCursor(-1); break;
            case InputAction.HotbarUse: UseHotbar(HotbarCursor); break;
            case InputAction.ContextMenu: OpenContextMenuHere(); break;
            case InputAction.DescribeSurroundings: DescribeSurroundings(); break;
            case InputAction.ShowTrail: ShowTrail(); break;
            case InputAction.QuestLog: ShowQuestLog(); break;
            case InputAction.WalkIntoTrap: AskDirection(DirectionFor.Jump); break;
            case InputAction.UseItem: BeginItemPrompt(ItemPromptKind.UseAny); break;
            case InputAction.TakeNote: BeginNote(); break;
            case InputAction.SaveAndQuit: SaveAndQuit(); break;
            case InputAction.MessageHistory: ShowMessageHistory(); break;
            case InputAction.RepeatCommand: RepeatLastCommand(); break;
            case InputAction.CommandCount: BeginCount(); break;
            case InputAction.OverviewMap: ShowOverviewMap(); break;
            case InputAction.MonsterList: ShowMonsterList(); break;
            case InputAction.ObjectList: ShowObjectList(); break;
            case InputAction.Cast: BeginSpellPrompt(SpellPromptKind.Cast); break;
            case InputAction.Study: BeginSpellPrompt(SpellPromptKind.Study); break;
            case InputAction.Browse: BeginSpellPrompt(SpellPromptKind.Browse); break;
            case InputAction.EnterStore: Execute(new EnterStoreCommand()); break;
            case InputAction.ToggleTiles: ToggleTiles(); break;
            case InputAction.ToggleFullScreen: ToggleFullScreen(); break;
            case InputAction.ToggleMute: ToggleMute(); break;
            case InputAction.OpenSettings: SettingsRequested?.Invoke(); break;
            case InputAction.Options: ShowOptions(); break;
            case InputAction.Feeling: ShowFeeling(); break;
            case InputAction.ZoomIn: ZoomIn(); break;
            case InputAction.ZoomOut: ZoomOut(); break;
            case InputAction.NewGame: NewGame(); break;
            case InputAction.SaveGame: SaveGame(); break;
            case InputAction.LoadGame: LoadGame(); break;
            case InputAction.Retire: Retire(); break;
            case InputAction.CharacterSheet: ShowCharacterSheet(); break;
            case InputAction.HighScores: ShowHighScores(); break;
            case InputAction.ShowWholeMap: ToggleWholeMap(); break;
            case InputAction.RegenerateLevel: RegenerateLevel(); break;
            case InputAction.JumpDeeper: JumpDeeper(); break;
            case InputAction.JumpNextLevel: JumpNextLevel(); break;
            case InputAction.JumpToTown: JumpToTown(); break;
        }
    }

    /// <summary>Raised when an input asks for the settings window (the view opens it).</summary>
    public event Action? SettingsRequested;

    /// <summary>Confirm outside menus: take the stairs underfoot, pick up items, otherwise wait a turn.</summary>
    private void ContextAction()
    {
        var p = _game.Player.Position;
        if (_game.StoreHere is not null) Execute(new EnterStoreCommand());
        else if (_game.Level.Has(p, TerrainFlags.DownStair)) Execute(new TakeStairsCommand(Down: true));
        else if (_game.Level.Has(p, TerrainFlags.UpStair)) Execute(new TakeStairsCommand(Down: false));
        else if (_game.Level.Objects.Any(p)) BeginItemPrompt(ItemPromptKind.Pickup);
        else Execute(new HoldCommand());
    }

    /// <summary>
    /// '>' or '<': on a staircase of that kind, take it; anywhere else (AVABand's own), walk to the nearest
    /// one you know of — press again there to take it.
    /// </summary>
    private void StairsKey(bool down)
    {
        var flag = down ? Angband.Core.Definitions.TerrainFlags.DownStair : Angband.Core.Definitions.TerrainFlags.UpStair;
        if (_game.Level.Has(_game.Player.Position, flag) || _game.NearestKnownStairs(down) is not { } stairs)
        {
            Execute(new TakeStairsCommand(Down: down));
            return;
        }
        AddMessage($"You head for the {(down ? "down" : "up")} staircase.");
        Execute(new TravelCommand(stairs));
        if (_game.Player.Position == stairs) AddMessage($"You are on the {(down ? "down" : "up")} staircase: press {(down ? ">" : "<")} again to take it.");
    }

    private void AskDirection(DirectionFor what)
    {
        _pendingDirection = what;
        LastMessage = "Direction?";
    }

    // --- Prompt selection (for gamepads and arrow keys) ----------------------------------------------

    private int PromptRowCount => ChoiceRows.Count > 0 ? ChoiceRows.Count : SpellPromptRows.Count > 0 ? SpellPromptRows.Count : PromptRows.Count;

    private void MovePromptSelection(int delta)
    {
        var count = PromptRowCount;
        if (count == 0) return;
        PromptSelectedIndex = ((PromptSelectedIndex + delta) % count + count) % count;
    }

    private void ChooseSelectedPromptRow()
    {
        var letter = ChoiceRows.Count > 0 ? ChoiceRows.ElementAtOrDefault(PromptSelectedIndex)?.Letter
            : SpellPromptRows.Count > 0
            ? SpellPromptRows.ElementAtOrDefault(PromptSelectedIndex)?.Letter
            : PromptRows.ElementAtOrDefault(PromptSelectedIndex)?.Letter;
        if (letter is { Length: > 0 }) PromptKey(letter[0]);
        else CancelPrompt();
    }

    partial void OnIsPromptingChanged(bool value)
    {
        if (!value)
        {
            PromptText = "";
            return;
        }
        PromptSelectedIndex = 0;
        AnnouncePrompt();
    }

    // --- Mouse ---------------------------------------------------------------------------------------

    /// <summary>Left click: travel there (or attack if adjacent). Right click: shoot a monster, or look.</summary>
    public void ClickCell(Loc loc, bool secondary)
    {
        if (IsPrompting || IsConfirming || IsInStore) return;
        if (!_game.Level.InBounds(loc)) return;
        if (ClickInCursorMode(loc)) return;

        if (secondary)
        {
            OpenContextMenu(loc); // Angband's right-click menus
            return;
        }
        // Angband mouse_movement: clicks may be kept from moving the player.
        if (OptionValue(DisplayOptions.MouseMovement)) Execute(new TravelCommand(loc));
    }

    private string DescribeSquare(Loc p)
    {
        if (!_game.Known.IsKnown(p)) return "You know nothing about that place.";
        if (_game.IsHallucinating) return "You see something strange."; // Angband aux_hallucinate
        if (p == _game.Player.Position && _game.ObjectShownAt(p) is null && _game.VisibleTrapAt(p) is null)
        {
            var here = _data.Terrain[_game.Level[p].Feature];
            return $"You are on {(("aeiou".Contains(here.Name[0])) ? "an" : "a")} {here.Name}.";
        }
        if (_game.VisibleTrapAt(p) is { } trap)
            return $"You see {(("aeiou".Contains(char.ToLowerInvariant(trap.Name[0]))) ? "an" : "a")} {trap.Name}.";
        if (_game.ObjectShownAt(p) is { } shown && _game.Level[p].Has(Angband.Core.World.SquareFlags.Seen))
            return $"You see {_game.Describe(shown)}." + (AdviceNote(shown) is { } note ? " " + note : "");
        var feature = _data.Terrain[_game.Known.Feature(p)];
        if (feature.Mimic is { } mimic) feature = _data.Terrain[mimic];
        return $"You see {(("aeiou".Contains(feature.Name[0])) ? "an" : "a")} {feature.Name}.";
    }

    // --- Rebinding (Controls tab) ------------------------------------------------------------------

    private void RefreshBindingRows()
    {
        KeymapRows.Clear();
        foreach (var (chord, action) in Bindings.Keymaps.OrderBy(k => k.Key, StringComparer.Ordinal))
            KeymapRows.Add(new KeymapRow(chord, action, text =>
            {
                Bindings.Keymaps[chord] = text;
                _saveBindings?.Invoke(Bindings);
            }));
        BindingRows.Clear();
        foreach (var action in Enum.GetValues<InputAction>().Where(a => a != InputAction.None))
        {
            BindingRows.Add(new BindingRow(action)
            {
                Keys = string.Join(", ", Bindings.KeysFor(action).Select(KeyboardInput.Display)),
                Button = string.Join(", ", Bindings.ButtonsFor(action)),
            });
        }
    }

    [RelayCommand]
    private void RebindKey(BindingRow row)
    {
        _capturingAction = row.Action;
        _capturingKey = true;
        CaptureHint = $"Press a key for \"{row.Label}\" (Esc cancels)...";
    }

    [RelayCommand]
    private void RebindButton(BindingRow row)
    {
        if (_gamepad is null)
        {
            CaptureHint = "No gamepad support is available.";
            return;
        }
        _capturingAction = row.Action;
        _capturingKey = false;
        CaptureHint = $"Press a controller button for \"{row.Label}\"...";
        _gamepad.CaptureNextButton(button =>
        {
            if (CaptureHint is null || _capturingKey) return; // cancelled meanwhile
            Bindings.BindButton(button, _capturingAction);
            FinishCapture();
        });
    }

    [RelayCommand]
    private void ClearKeys(BindingRow row)
    {
        Bindings.ClearKeys(row.Action);
        FinishCapture();
    }

    [RelayCommand]
    private void ResetBindings() => UseKeyset(InputBindings.Keyset.Avaband);

    [RelayCommand]
    private void UseOriginalKeys() => UseKeyset(InputBindings.Keyset.Original);

    [RelayCommand]
    private void UseRoguelikeKeys() => UseKeyset(InputBindings.Keyset.Roguelike);

    /// <summary>Replaces the keyboard layout with a preset (the gamepad buttons as they were).</summary>
    public void UseKeyset(InputBindings.Keyset keyset)
    {
        var preset = InputBindings.Preset(keyset);
        if (keyset != InputBindings.Keyset.Avaband)
        {
            preset.Buttons.Clear();
            foreach (var (button, action) in Bindings.Buttons) preset.Buttons[button] = action;
        }
        foreach (var (chord, action) in Bindings.Keymaps) preset.Keymaps[chord] = action; // keymaps stay
        Bindings = preset;
        _gamepad?.UseBindings(Bindings);
        if (keyset != InputBindings.Keyset.Avaband && OptionValue(DisplayOptions.RoguelikeKeys) != (keyset == InputBindings.Keyset.Roguelike))
        {
            _settings.Options[DisplayOptions.RoguelikeKeys] = keyset == InputBindings.Keyset.Roguelike;
            SaveSettings();
            RefreshOptionRows();
        }
        FinishCapture();
    }

    /// <summary>True while waiting for a key to bind; the settings window feeds <see cref="CaptureKey"/>.</summary>
    public bool IsCapturingKey => CaptureHint is not null && _capturingKey;

    public void CaptureKey(string? chord)
    {
        if (!IsCapturingKey) return;
        if (_capturingKeymap)
        {
            _capturingKeymap = false;
            if (chord is not null) Bindings.Keymaps.TryAdd(chord, "");
        }
        else if (chord is not null) Bindings.BindKey(chord, _capturingAction);
        FinishCapture();
    }

    // --- Keymaps (Controls tab) ----------------------------------------------------------------------

    private bool _capturingKeymap;

    public ObservableCollection<KeymapRow> KeymapRows { get; } = [];

    [RelayCommand]
    private void AddKeymap()
    {
        _capturingKeymap = true;
        _capturingKey = true;
        CaptureHint = "Press the key the new keymap is for (Esc cancels)...";
    }

    [RelayCommand]
    private void RemoveKeymap(KeymapRow row)
    {
        Bindings.Keymaps.Remove(row.Chord);
        FinishCapture();
    }

    /// <summary>
    /// Whether the game is waiting for a command (not inside a menu, question, prompt or look mode):
    /// the only time a keymap is expanded, as in Angband.
    /// </summary>
    public bool IsAtCommandPrompt => !IsPrompting && !IsConfirming && !IsLooking && !IsInStore && !IsShowingList
                                     && !IsEnteringCount && !IsEnteringNumber && !IsAwaitingDirection && !IsInscribing
                                     && !IsChoosingGlyph && !IsLocating && PendingSpellDirection is null;

    /// <summary>Stops waiting for a key or controller button; true if it was waiting.</summary>
    public bool CancelCapture()
    {
        if (CaptureHint is null) return false;
        _capturingKey = false;
        CaptureHint = null;
        return true;
    }

    private void FinishCapture()
    {
        CaptureHint = null;
        _capturingKeymap = false;
        _saveBindings?.Invoke(Bindings);
        RefreshBindingRows();
    }
}
