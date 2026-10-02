using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Data;
using Angband.Input;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

/// <summary>Full screen (AVABand's own): on by default, F11 or View → Full screen toggles it, remembered.</summary>
public class FullScreenUiTests
{
    [AvaloniaFact]
    public void TheGameOpensFullScreen_AndF11TogglesIt_Remembered()
    {
        Assert.True(new AppSettings().FullScreen);                      // on unless turned off
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings();
        var saved = 0;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: _ => saved++);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        Assert.True(vm.IsFullScreen);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.ApplyFullScreen(vm.IsFullScreen);                        // (as the app does on opening)
        window.Show();
        Assert.Equal(WindowState.FullScreen, window.WindowState);

        window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
        Assert.False(vm.IsFullScreen);
        Assert.Equal(WindowState.Normal, window.WindowState);
        Assert.False(settings.FullScreen);
        Assert.True(saved > 0);

        vm.ToggleFullScreenCommand.Execute(null);                       // View → Full screen
        Assert.Equal(WindowState.FullScreen, window.WindowState);
        Assert.True(settings.FullScreen);
    }

    [AvaloniaFact]
    public void TurnedOff_TheGameOpensInAWindow()
    {
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings { FullScreen = false }, save: null);
        Assert.False(vm.IsFullScreen);
    }
}
