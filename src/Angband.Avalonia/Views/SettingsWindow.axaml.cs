using Angband.Avalonia.Input;
using Angband.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Angband.Avalonia.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        // While the Controls tab waits for a key, the next key press becomes the binding.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        // Escape cancels a controller-button capture if one is waiting, otherwise closes the window.
        DialogKeys.CloseOnEscape(this, escapeFirst: () => DataContext is MainWindowViewModel vm && vm.CancelCapture(),
            busy: () => DataContext is MainWindowViewModel { IsCapturingKey: true });
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Opens on the options page (Angband '=').</summary>
    public void ShowOptionsPage() => Tabs.SelectedIndex = Tabs.ItemCount - 1;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { IsCapturingKey: true } vm || KeyboardInput.IsModifierKey(e.Key)) return;
        vm.CaptureKey(e.Key == Key.Escape ? null : KeyboardInput.CaptureChord(e));
        e.Handled = true;
    }
}
