using Angband.Core.Geometry;

namespace Angband.Input;

/// <summary>
/// Turns raw gamepad state (button presses, stick and trigger positions, time) into actions:
/// the D-pad and left stick give eight-way movement with hold-to-repeat, triggers act as buttons,
/// and other buttons fire their bound action once per press. A button used in a chord binding
/// (<c>LeftTrigger+A</c>, <c>LeftTrigger+DPad</c>) is a shift: held with another button it gives
/// the chord's action, held with a direction it runs, and pressed alone it fires its own action
/// when released. Pure logic, so it is testable
/// without hardware; <see cref="SdlGamepadProvider"/> feeds it from SDL.
/// </summary>
public sealed class GamepadMapper(InputBindings bindings)
{
    /// <summary>Stick deflection (0..1) needed to count as a direction.</summary>
    public const double StickDeadZone = 0.5;
    /// <summary>Trigger pull (0..1) that counts as a press.</summary>
    public const double TriggerThreshold = 0.5;
    public static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(320);
    public static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(140);
    /// <summary>
    /// How long a run waits for a second D-pad button, so pressing two a moment apart runs
    /// diagonally rather than straight first.
    /// </summary>
    public static readonly TimeSpan RunGrace = TimeSpan.FromMilliseconds(60);

    private readonly HashSet<string> _held = [];
    private double _stickX, _stickY;
    private Direction? _heldDirection;
    private TimeSpan _nextRepeat;
    /// <summary>Whether the shift button now held has been used in a chord (so its own action is skipped).</summary>
    private bool _chordUsed;
    /// <summary>When a run waiting out <see cref="RunGrace"/> starts (null: none waiting).</summary>
    private TimeSpan? _runAt;

    /// <summary>The key of a binding for a shift button held with a direction.</summary>
    public const string DirectionChord = "DPad";

    public InputBindings Bindings { get; set; } = bindings;

    public event Action<InputAction>? ActionTriggered;

    /// <summary>When set, the next button press is reported here instead (for rebinding), then cleared.</summary>
    public Action<string>? CaptureNextButton { get; set; }

    public void ButtonDown(string button, TimeSpan now)
    {
        if (!_held.Add(button)) return;
        if (CaptureNextButton is { } capture)
        {
            CaptureNextButton = null;
            capture(button);
            return;
        }
        if (IsMovementButton(button))
        {
            UpdateDirection(now);
            return;
        }
        if (HeldShift(except: button) is { } shift && Bindings.ForButton(shift + "+" + button) is var chord and not InputAction.None)
        {
            _chordUsed = true;
            ActionTriggered?.Invoke(chord);
            return;
        }
        if (IsShift(button))
        {
            _chordUsed = false; // its own action waits for the release
            return;
        }
        if (Bindings.ForButton(button) is var action and not InputAction.None) ActionTriggered?.Invoke(action);
    }

    public void ButtonUp(string button, TimeSpan now)
    {
        if (!_held.Remove(button)) return;
        if (IsMovementButton(button)) UpdateDirection(now);
        else if (IsShift(button) && !_chordUsed && Bindings.ForButton(button) is var action and not InputAction.None)
            ActionTriggered?.Invoke(action);
    }

    /// <summary>A button some chord binding starts with.</summary>
    private bool IsShift(string button)
    {
        var prefix = button + "+";
        return Bindings.Buttons.Keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal));
    }

    private string? HeldShift(string? except = null) => _held.FirstOrDefault(b => b != except && IsShift(b));

    /// <summary>Left stick position, each axis -1..1 (y down is positive, as SDL reports it).</summary>
    public void LeftStick(double x, double y, TimeSpan now)
    {
        (_stickX, _stickY) = (x, y);
        UpdateDirection(now);
    }

    /// <summary>A trigger's pull, 0..1; crossing the threshold presses or releases it like a button.</summary>
    public void Trigger(string name, double value, TimeSpan now)
    {
        var pressed = _held.Contains(name);
        if (value >= TriggerThreshold && !pressed) ButtonDown(name, now);
        else if (value < TriggerThreshold * 0.6 && pressed) ButtonUp(name, now); // hysteresis
    }

    /// <summary>Call regularly (e.g. every frame) so held directions repeat.</summary>
    public void Tick(TimeSpan now)
    {
        if (_peekDirection is { } look && now >= _nextPeek)
        {
            _nextPeek = now + PeekInterval;
            Peek?.Invoke(look);
        }
        if (_runAt is { } runAt && now >= runAt)
        {
            // The grace is over: run whichever way is held now (let go by now, and it's off).
            _runAt = null;
            if (CurrentDirection() is { } runDir) ActionTriggered?.Invoke(InputActions.RunFromDirection(runDir));
            return;
        }
        if (_heldDirection is not { } dir || now < _nextRepeat) return;
        _nextRepeat = now + RepeatInterval;
        ActionTriggered?.Invoke(InputActions.FromDirection(dir));
    }

    /// <summary>The direction currently held (D-pad takes priority over the stick).</summary>
    public Direction? CurrentDirection()
    {
        int dx = 0, dy = 0;
        foreach (var button in _held)
        {
            switch (Bindings.ForButton(button))
            {
                case InputAction.MoveNorth: dy--; break;
                case InputAction.MoveSouth: dy++; break;
                case InputAction.MoveWest: dx--; break;
                case InputAction.MoveEast: dx++; break;
            }
        }
        if (dx == 0 && dy == 0) return StickDirection(_stickX, _stickY);
        return DirectionExtensions.FromOffset(dx, dy);
    }

    /// <summary>A stick's direction, snapped to one of eight 45-degree sectors; null inside the dead zone.</summary>
    public static Direction? StickDirection(double x, double y)
    {
        if (Math.Sqrt(x * x + y * y) < StickDeadZone) return null;
        var sector = (int)Math.Round(Math.Atan2(y, x) / (Math.PI / 4));
        var (dx, dy) = (((sector % 8) + 8) % 8) switch
        {
            0 => (1, 0), 1 => (1, 1), 2 => (0, 1), 3 => (-1, 1),
            4 => (-1, 0), 5 => (-1, -1), 6 => (0, -1), _ => (1, -1),
        };
        return DirectionExtensions.FromOffset(dx, dy);
    }

    // --- The right stick looks around the map (AVABand's own) ------------------------------------

    /// <summary>How often a held right stick slides the view another step.</summary>
    public static readonly TimeSpan PeekInterval = TimeSpan.FromMilliseconds(110);

    private Direction? _peekDirection;
    private TimeSpan _nextPeek;

    /// <summary>The right stick slides the view this way (again and again while it's held).</summary>
    public event Action<Direction>? Peek;

    /// <summary>The right stick was let go: the view comes back.</summary>
    public event Action? PeekEnded;

    /// <summary>Right stick position, each axis -1..1 (y down is positive, as SDL reports it).</summary>
    public void RightStick(double x, double y, TimeSpan now)
    {
        var dir = StickDirection(x, y);
        if (dir == _peekDirection) return;
        var was = _peekDirection;
        _peekDirection = dir;
        if (dir is { } d)
        {
            Peek?.Invoke(d);
            _nextPeek = now + PeekInterval;
        }
        else if (was is not null) PeekEnded?.Invoke();
    }

    private bool IsMovementButton(string button) =>
        Bindings.ForButton(button) is InputAction.MoveNorth or InputAction.MoveSouth or InputAction.MoveEast or InputAction.MoveWest;

    private void UpdateDirection(TimeSpan now)
    {
        var dir = CurrentDirection();
        if (dir == _heldDirection) return;
        _heldDirection = dir;
        if (dir is null) return;
        if (HeldShift() is { } shift && Bindings.ForButton(shift + "+" + DirectionChord) == InputAction.Run)
        {
            // Shift and a direction: run, once (the run goes on by itself), after a moment's grace
            // for a second D-pad button; a change of direction meanwhile doesn't restart the wait.
            _chordUsed = true;
            _runAt ??= now + RunGrace;
            _nextRepeat = TimeSpan.MaxValue;
            return;
        }
        if (dir is not { } d) return;
        ActionTriggered?.Invoke(InputActions.FromDirection(d));
        _nextRepeat = now + RepeatDelay;
    }
}
