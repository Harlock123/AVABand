using Angband.Avalonia.ViewModels;
using Angband.Input;

namespace Angband.Avalonia.Input;

/// <summary>Adapts the SDL provider to what the view model needs.</summary>
public sealed class GamepadService(SdlGamepadProvider provider) : IGamepadService
{
    public string Status => provider.Status;
    public void CaptureNextButton(Action<string> onButton) => provider.Mapper.CaptureNextButton = onButton;
    public void UseBindings(InputBindings bindings) => provider.Mapper.Bindings = bindings;
}
