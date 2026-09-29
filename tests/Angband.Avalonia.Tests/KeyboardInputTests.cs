using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

public class KeyboardInputTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(7);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm);
    }

    [AvaloniaFact]
    public void HoldKey_AdvancesTheGame()
    {
        var (window, vm) = Open();
        var before = vm.StatusText;

        window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.None); // 's' = hold

        Assert.NotEqual(before, vm.StatusText);
        Assert.Contains("Turn 1 ", vm.StatusText);
    }

    [AvaloniaFact]
    public void ArrowKeys_MoveOrBump()
    {
        var (window, vm) = Open();
        var revision = vm.Revision;
        foreach (var key in new[] { PhysicalKey.ArrowUp, PhysicalKey.ArrowDown, PhysicalKey.ArrowLeft, PhysicalKey.ArrowRight })
        {
            window.KeyPressQwerty(key, RawInputModifiers.None);
            window.KeyReleaseQwerty(key, RawInputModifiers.None); // (a tap: a held arrow waits a moment for a second one)
        }
        Assert.True(vm.Revision > revision);
    }

    [AvaloniaFact]
    public void F7_TogglesWholeMap()
    {
        var (window, vm) = Open();
        vm.Game.MarkDebugUsed(); // (the first debug command asks; see DebugUiTests)
        window.KeyPressQwerty(PhysicalKey.F7, RawInputModifiers.None);
        Assert.True(vm.ShowWholeMap);
        window.KeyPressQwerty(PhysicalKey.F7, RawInputModifiers.None);
        Assert.False(vm.ShowWholeMap);
    }
}
