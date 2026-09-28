using Angband.Avalonia.Controls;
using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Geometry;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

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

    [AvaloniaFact]
    public void CtrlL_CentresTheMapOnce_WhenItIsNotKeptCentred()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        vm.Game.MarkDebugUsed();
        vm.HandleAction(InputAction.JumpNextLevel);
        vm.CenterPlayer = false; // "Center map continuously" off: the view moves by panels
        var map = window.GetVisualDescendants().OfType<MapView>().First(m => m.Name == "Map");
        var level = vm.Game.Level;

        void Put(Loc at)
        {
            vm.Game.Player.Position = at;
            vm.Revision++;
            window.CaptureRenderedFrame();
        }

        Put(new Loc(level.Width / 2, level.Height / 2));
        var (cols, rows) = map.ViewCells;
        Assert.True(cols > 20 && rows > 10 && cols < level.Width && rows < level.Height);
        var panel = map.ViewOffset;

        var moved = vm.Game.Player.Position + new Loc(5, 2); // still well inside the panel
        Put(moved);
        Assert.Equal(panel, map.ViewOffset);

        window.KeyPressQwerty(PhysicalKey.L, RawInputModifiers.Control);
        window.CaptureRenderedFrame();
        var centred = new Loc(moved.X - cols / 2, moved.Y - rows / 2);
        Assert.Equal(centred, map.ViewOffset);

        Put(moved + new Loc(1, 0)); // just once: the panel stays put again
        Assert.Equal(centred, map.ViewOffset);
    }

    [Fact]
    public void CenterMap_IsCtrlL_AndAtInTheRoguelikeKeys()
    {
        Assert.Equal(InputAction.CenterMap, InputBindings.Defaults().ForKey("Ctrl+L"));
        Assert.Equal(InputAction.CenterMap, InputBindings.Preset(InputBindings.Keyset.Original).ForKey("Ctrl+L"));
        var rogue = InputBindings.Preset(InputBindings.Keyset.Roguelike);
        Assert.Equal(InputAction.CenterMap, rogue.ForKey("Char:@"));
        Assert.Equal(InputAction.CenterMap, rogue.ForKey("Ctrl+L"));
    }
}
