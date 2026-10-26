using Angband.Avalonia.Input;
using Angband.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

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

    /// <summary>The Mods tab lists what's in the mods folder now (it may have changed since the game started).</summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainWindowViewModel vm) vm.RefreshMods();
    }

    /// <summary>Opens the mods folder in the file manager (made first, if there are no mods yet).</summary>
    private async void OnOpenModsFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var folder = vm.ModsFolder;
        try
        {
            System.IO.Directory.CreateDirectory(folder);
            vm.ModsStatus = await Launcher.LaunchDirectoryInfoAsync(new System.IO.DirectoryInfo(folder))
                ? "" : $"Your mods folder is {folder}";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            vm.ModsStatus = $"Your mods folder is {folder}";
        }
    }

    /// <summary>Opens on the options page (Angband '=').</summary>
    public void ShowOptionsPage() => Tabs.SelectedIndex = Tabs.ItemCount - 1;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { IsCapturingKey: true } vm || KeyboardInput.IsModifierKey(e.Key)) return;
        vm.CaptureKey(e.Key == Key.Escape ? null : KeyboardInput.CaptureChord(e));
        e.Handled = true;
    }
}
