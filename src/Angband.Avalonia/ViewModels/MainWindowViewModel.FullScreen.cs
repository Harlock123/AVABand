using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

// AVABand's own: the main window opens full screen (where the system allows it; maximised where it
// doesn't), and F11 or View → Full screen turns it off and on again, remembered for next time.
public sealed partial class MainWindowViewModel
{
    /// <summary>Whether the main window should be full screen (the window follows it).</summary>
    [ObservableProperty] private bool _isFullScreen;

    [RelayCommand]
    private void ToggleFullScreen() => IsFullScreen = !IsFullScreen;

    partial void OnIsFullScreenChanged(bool value)
    {
        _settings.FullScreen = value;
        _saveSettings?.Invoke(_settings);
    }
}
