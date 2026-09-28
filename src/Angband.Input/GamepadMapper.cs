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

    private readonly HashSet<string> _held = [];
    private double _stickX, _stickY;
    private Direction? _heldDirection;
    private TimeSpan _nextRepeat;
    /// <summary>Whether the shift button now held has been used in a chord (so its own action is skipped).</summary>
    private bool _chordUsed;

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
        if (dx == 0 && dy == 0)
        {
            var magnitude = Math.Sqrt(_stickX * _stickX + _stickY * _stickY);
            if (magnitude < StickDeadZone) return null;
            // Snap the stick angle to one of eight 45-degree sectors.
            var angle = Math.Atan2(_stickY, _stickX);
            var sector = (int)Math.Round(angle / (Math.PI / 4));
            var octant = ((sector % 8) + 8) % 8;
            (dx, dy) = octant switch
            {
                0 => (1, 0), 1 => (1, 1), 2 => (0, 1), 3 => (-1, 1),
                4 => (-1, 0), 5 => (-1, -1), 6 => (0, -1), _ => (1, -1),
            };
        }
        return dx == 0 && dy == 0 ? null : DirectionExtensions.FromOffset(dx, dy);
    }

    private bool IsMovementButton(string button) =>
        Bindings.ForButton(button) is InputAction.MoveNorth or InputAction.MoveSouth or InputAction.MoveEast or InputAction.MoveWest;

    private void UpdateDirection(TimeSpan now)
    {
        var dir = CurrentDirection();
        if (dir == _heldDirection) return;
        _heldDirection = dir;
        if (dir is not { } d) return;
        if (HeldShift() is { } shift && Bindings.ForButton(shift + "+" + DirectionChord) == InputAction.Run)
        {
            // Shift and a direction: run that way, once (the run goes on by itself).
            _chordUsed = true;
            ActionTriggered?.Invoke(InputActions.RunFromDirection(d));
            _nextRepeat = TimeSpan.MaxValue;
            return;
        }
        ActionTriggered?.Invoke(InputActions.FromDirection(d));
        _nextRepeat = now + RepeatDelay;
    }
}
