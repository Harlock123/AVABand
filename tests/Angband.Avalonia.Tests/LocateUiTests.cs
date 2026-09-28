using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Geometry;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

/// <summary>Angband's locate ('L', here W): scroll the map half a screen at a time.</summary>
public class LocateUiTests
{
    [AvaloniaFact]
    public void W_ScrollsTheMap_ByHalfScreens_AndEscapeComesBack()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        vm.Game.MarkDebugUsed();
        vm.HandleAction(InputAction.JumpNextLevel); // a dungeon level, bigger than the screen
        var me = vm.Game.Player.Position;
        var (cols, rows) = vm.ViewportCells();
        Assert.True(cols > 10 && rows > 5);

        window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Shift);
        Assert.True(vm.IsLocating);
        Assert.StartsWith("Map sector [", vm.LastMessage);
        Assert.EndsWith("your sector.  Direction?", vm.LastMessage);

        var before = vm.Focus;
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Assert.True(vm.Focus.X > before.X || before.X >= vm.Game.Level.Width - 1 - cols / 2); // east, unless at the edge
        Assert.Equal(me, vm.Game.Player.Position);                                           // nobody moved

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(vm.IsLocating);
        Assert.Equal(me, vm.Focus);
    }
}
