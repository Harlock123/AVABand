using System.Diagnostics;
using Silk.NET.SDL;

namespace Angband.Input;

/// <summary>
/// Reads game controllers through SDL2's GameController API (Xbox, PlayStation, Switch Pro and
/// most others, with SDL's mapping database). Call <see cref="Poll"/> regularly on the UI thread.
/// Background events are enabled because the game window belongs to Avalonia, not SDL.
/// </summary>
public sealed unsafe class SdlGamepadProvider : IInputProvider
{
    private readonly Sdl _sdl;
    private readonly Dictionary<int, nint> _controllers = [];   // joystick instance id -> GameController*
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private SdlGamepadProvider(Sdl sdl, InputBindings bindings)
    {
        _sdl = sdl;
        Mapper = new GamepadMapper(bindings);
        Mapper.ActionTriggered += a => ActionTriggered?.Invoke(a);
        for (var i = 0; i < _sdl.NumJoysticks(); i++) Open(i);
    }

    public GamepadMapper Mapper { get; }
    public InputDevice Device => InputDevice.Gamepad;
    public event Action<InputAction>? ActionTriggered;

    public string Status
    {
        get
        {
            if (_controllers.Count == 0) return "No controller connected.";
            var names = _controllers.Values.Select(c => _sdl.GameControllerNameS((GameController*)c) ?? "Controller");
            return "Connected: " + string.Join(", ", names);
        }
    }

    /// <summary>Starts SDL's controller support, or returns null if SDL is unavailable.</summary>
    public static SdlGamepadProvider? TryCreate(InputBindings bindings)
    {
        try
        {
            var sdl = Sdl.GetApi();
            sdl.SetHint(Sdl.HintJoystickAllowBackgroundEvents, "1");
            if (sdl.Init(Sdl.InitGamecontroller | Sdl.InitEvents) < 0)
            {
                Trace.WriteLine($"SDL controller init failed: {sdl.GetErrorS()}");
                return null;
            }
            return new SdlGamepadProvider(sdl, bindings);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or FileNotFoundException
                                       or TypeInitializationException or InvalidOperationException)
        {
            Trace.WriteLine($"Gamepad support unavailable: {ex.Message}");
            return null;
        }
    }

    /// <summary>Drains SDL's event queue and advances key repeat.</summary>
    public void Poll()
    {
        var now = _clock.Elapsed;
        Event e;
        while (_sdl.PollEvent(&e) != 0)
        {
            switch ((EventType)e.Type)
            {
                case EventType.Controllerdeviceadded:
                    Open(e.Cdevice.Which);
                    break;
                case EventType.Controllerdeviceremoved:
                    if (_controllers.Remove(e.Cdevice.Which, out var gone)) _sdl.GameControllerClose((GameController*)gone);
                    break;
                case EventType.Controllerbuttondown:
                    Mapper.ButtonDown(ButtonName((GameControllerButton)e.Cbutton.Button), now);
                    break;
                case EventType.Controllerbuttonup:
                    Mapper.ButtonUp(ButtonName((GameControllerButton)e.Cbutton.Button), now);
                    break;
                case EventType.Controlleraxismotion:
                    Axis((GameControllerAxis)e.Caxis.Axis, e.Caxis.Value, now);
                    break;
            }
        }
        Mapper.Tick(now);
    }

    private double _lx, _ly;

    private void Axis(GameControllerAxis axis, short value, TimeSpan now)
    {
        var v = value / 32767.0;
        switch (axis)
        {
            case GameControllerAxis.Leftx: _lx = v; Mapper.LeftStick(_lx, _ly, now); break;
            case GameControllerAxis.Lefty: _ly = v; Mapper.LeftStick(_lx, _ly, now); break;
            case GameControllerAxis.Triggerleft: Mapper.Trigger("LeftTrigger", Math.Max(0, v), now); break;
            case GameControllerAxis.Triggerright: Mapper.Trigger("RightTrigger", Math.Max(0, v), now); break;
        }
    }

    private void Open(int deviceIndex)
    {
        if (_sdl.IsGameController(deviceIndex) != SdlBool.True) return;
        var controller = _sdl.GameControllerOpen(deviceIndex);
        if (controller == null) return;
        var joystick = _sdl.GameControllerGetJoystick(controller);
        _controllers[_sdl.JoystickInstanceID(joystick)] = (nint)controller;
    }

    /// <summary>Maps SDL's button enum to the names used in <see cref="InputBindings"/>.</summary>
    public static string ButtonName(GameControllerButton button) => button switch
    {
        GameControllerButton.A => "A",
        GameControllerButton.B => "B",
        GameControllerButton.X => "X",
        GameControllerButton.Y => "Y",
        GameControllerButton.Back => "Back",
        GameControllerButton.Guide => "Guide",
        GameControllerButton.Start => "Start",
        GameControllerButton.Leftstick => "LeftStick",
        GameControllerButton.Rightstick => "RightStick",
        GameControllerButton.Leftshoulder => "LeftShoulder",
        GameControllerButton.Rightshoulder => "RightShoulder",
        GameControllerButton.DpadUp => "DPadUp",
        GameControllerButton.DpadDown => "DPadDown",
        GameControllerButton.DpadLeft => "DPadLeft",
        GameControllerButton.DpadRight => "DPadRight",
        _ => button.ToString(),
    };

    public void Dispose()
    {
        foreach (var c in _controllers.Values) _sdl.GameControllerClose((GameController*)c);
        _controllers.Clear();
        _sdl.QuitSubSystem(Sdl.InitGamecontroller);
    }
}
