using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>What the map cursor is doing (Angband's look and target modes).</summary>
public enum CursorMode { None, Look, Target }

// The map cursor (Angband target.c): 'x' looks and '*' targets. Both step through the visible
// monsters (space/+ next, - previous); 'p' or a direction key frees the cursor to roam any square.
// 't', '5' or Enter targets what is under the cursor, 'r' recalls the monster there, Esc leaves.
// Firing, throwing, aimed spells and devices then use the target while it stays valid.
public sealed partial class MainWindowViewModel
{
    private List<Loc> _cursorSpots = [];
    private int _cursorIndex;
    private bool _cursorFree;
    private Loc _cursor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLooking), nameof(IsTargeting))]
    private CursorMode _cursorMode;

    /// <summary>The cursor is on the map (look or target mode).</summary>
    public bool IsLooking => CursorMode != CursorMode.None;
    public bool IsTargeting => CursorMode == CursorMode.Target;

    /// <summary>The monster under the cursor, if any.</summary>
    public Monster? LookedAt => IsLooking && _game.Level.Monsters.At(_cursor) is { IsVisible: true } m ? m : null;

    /// <summary>Where the cursor is (for drawing), when looking or targeting.</summary>
    public Loc? Cursor => IsPeeking ? _peekCentre : IsLooking ? _cursor : null;

    /// <summary>The current target (for drawing), when it is valid.</summary>
    public Loc? Target => OptionValue(DisplayOptions.ShowTarget) ? _game.TargetPosition() : null;

    /// <summary>A direction prompt is waiting (so 't' can mean "toward the target").</summary>
    public bool IsAwaitingDirection => _pendingDirection != DirectionFor.None || PendingSpellDirection is not null;

    /// <summary>x: look at the visible monsters.</summary>
    [RelayCommand]
    public void Look() => EnterCursor(CursorMode.Look);

    /// <summary>*: pick a target.</summary>
    [RelayCommand]
    public void TargetMode() => EnterCursor(CursorMode.Target);

    private void EnterCursor(CursorMode mode)
    {
        if (CursorMode == mode)
        {
            StepCursor(+1);
            return;
        }
        // Target mode steps through the monsters that can be shot; look mode through everything of
        // interest nearby (Angband target_set_interactive), and with nothing there it starts free.
        _cursorSpots = mode == CursorMode.Target
            ? [.. _game.TargetableMonsters().Select(m => m.Position)]
            : [.. _game.LookSpots()];
        CursorMode = mode;
        _cursorIndex = 0;
        _cursorFree = _cursorSpots.Count == 0;
        _cursor = _cursorFree ? _game.Player.Position : _cursorSpots[0];
        ShowCursor();
    }

    private void StepCursor(int delta)
    {
        if (_cursorSpots.Count == 0) return;
        _cursorFree = false;
        _cursorIndex = ((_cursorIndex + delta) % _cursorSpots.Count + _cursorSpots.Count) % _cursorSpots.Count;
        _cursor = _cursorSpots[_cursorIndex];
        ShowCursor();
    }

    private void MoveCursor(Direction dir)
    {
        _cursorFree = true;
        var next = _cursor.Step(dir);
        if (_game.Level.InBounds(next)) _cursor = next;
        ShowCursor();
    }

    private void ShowCursor()
    {
        var hints = IsTargeting ? "[t] target, [space] next, [p] free, [Esc] done"
            : LookedAt is not null ? "[r] recall, [t] target, [space] next, [-] back, [Esc] done"
            : "[t] target, [space] next, [-] back, [Esc] done";
        string what;
        if (LookedAt is { } m)
        {
            _game.TrackHealth(m); // Angband: looking at a monster tracks its health
            UpdateHealthBar();
            var text = _game.LookDescription(m);
            what = char.ToUpperInvariant(text[0]) + text[1..];
        }
        else what = DescribeSquare(_cursor).TrimEnd('.');
        LastMessage = $"{what}.  {hints}";
        OnPropertyChanged(nameof(LookedAt));
        OnPropertyChanged(nameof(Cursor));
        RefreshRecallPanel();
        UpdatePath();
        Revision++;
    }

    /// <summary>Selects what is under the cursor as the target and leaves cursor mode.</summary>
    private void TargetUnderCursor()
    {
        if (LookedAt is { } m)
        {
            _game.SetTarget(m);
            AddMessage($"You target {_game.MonsterName(m)}.");
        }
        else
        {
            _game.SetTarget(_cursor);
            AddMessage("You target that spot.");
        }
        StopLooking();
        Refresh();
    }

    /// <summary>Letters in cursor mode (from the keyboard handler). True if the key was used.</summary>
    public bool CursorKey(string symbol)
    {
        if (!IsLooking) return false;
        switch (symbol)
        {
            case "t" or "5":
                TargetUnderCursor();
                return true;
            case "+" or " ":
                StepCursor(+1);
                return true;
            case "-":
                StepCursor(-1);
                return true;
            case "p":
                _cursorFree = true;
                _cursor = _game.Player.Position;
                ShowCursor();
                return true;
            case "r":
                if (LookedAt is { } m && !_game.IsHallucinating) ShowRecall(m.Race);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Handles an action during cursor mode; false lets it through (and ends cursor mode).</summary>
    private bool HandleLookAction(InputAction action)
    {
        if (!IsLooking) return false;
        if (action.ToDirection() is { } dir && dir != Direction.Here)
        {
            MoveCursor(dir);
            return true;
        }
        switch (action)
        {
            case InputAction.Look when CursorMode == CursorMode.Look:
            case InputAction.Target when CursorMode == CursorMode.Target:
                StepCursor(+1);
                return true;
            case InputAction.Read:
                if (LookedAt is { } m && !_game.IsHallucinating) ShowRecall(m.Race);
                return true;
            case InputAction.Confirm or InputAction.Hold:
                TargetUnderCursor();
                return true;
            case InputAction.Cancel:
                StopLooking();
                LastMessage = "";
                return true;
            default:
                StopLooking();
                return false;
        }
    }

    private void StopLooking()
    {
        CursorMode = CursorMode.None;
        _cursorSpots = [];
        OnPropertyChanged(nameof(LookedAt));
        OnPropertyChanged(nameof(Cursor));
        RefreshRecallPanel();
        UpdatePath();
        Revision++;
    }

    /// <summary>A click during cursor mode targets the square clicked.</summary>
    private bool ClickInCursorMode(Loc loc)
    {
        if (!IsLooking) return false;
        _cursor = loc;
        _cursorFree = true;
        TargetUnderCursor();
        return true;
    }

    /// <summary>'t' in a direction prompt: aim toward the current target.</summary>
    public bool DirectionTowardTarget()
    {
        if (!IsAwaitingDirection) return false;
        if (_game.DirectionToTarget() is not { } dir)
        {
            LastMessage = "You have no target.";
            return true;
        }
        HandleAction(InputActions.FromDirection(dir));
        return true;
    }
}
