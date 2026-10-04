using Angband.Avalonia.Controls;
using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Data;
using Angband.Input;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

/// <summary>Looking around the map (AVABand's own): Ctrl+arrows, a middle-button drag; let go and the view comes back.</summary>
public class PeekUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings();
        settings.Options[DisplayOptions.Hints] = false;
        settings.Options[DisplayOptions.Scenes] = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        vm.Game.MarkDebugUsed();
        vm.Execute(new DebugJumpCommand(3));
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    [AvaloniaFact]
    public void CtrlArrows_LookAround_WithoutMovingOrTakingTime_AndLettingGoComesBack()
    {
        var (window, vm) = Open();
        var game = vm.Game;
        game.Known.RememberAll(game.Level);                                    // (an explored level, to look over)
        var me = game.Player.Position;
        var turn = game.GameTurn;

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Control);
        Assert.True(vm.IsPeeking);
        Assert.Equal(new Loc(Math.Min(game.Level.Width - 1, me.X + MainWindowViewModel.PeekStep), me.Y), vm.Focus);
        Assert.Equal(vm.Focus, vm.Cursor);                                   // the marker at the view's centre
        Assert.StartsWith("Looking around: ", vm.LastMessage);
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Control); // two arrows held: diagonally
        var centre = vm.PeekCentre;
        Assert.True(centre.Y > me.Y || centre.Y == game.Level.Height - 1);
        Assert.Equal(me, game.Player.Position);                              // nobody moved
        Assert.Equal(turn, game.GameTurn);                                   // and no time passed
        TileRenderingTests.Save(window, "look-around");

        window.KeyReleaseQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);
        Assert.False(vm.IsPeeking);
        Assert.Equal(me, vm.Focus);
        Assert.Null(vm.Cursor);
    }

    [AvaloniaFact]
    public void RememberedSquares_AreLitMoreWhileLooking()
    {
        var (_, vm) = Open();
        var game = vm.Game;
        var level = game.Level;
        var remembered = level.AllLocs().First(p => !level[p].Has(Angband.Core.World.SquareFlags.Seen) && p.DistanceTo(game.Player.Position) > 20);
        game.Known.Remember(level, remembered);                                // (seen once, out of sight now)
        var before = vm.ShadeAt(remembered.X, remembered.Y).Amount;
        vm.Peek(Direction.North);
        Assert.True(vm.ShadeAt(remembered.X, remembered.Y).Amount < before);
        vm.EndPeek();
        Assert.Equal(before, vm.ShadeAt(remembered.X, remembered.Y).Amount);
    }

    [AvaloniaFact]
    public void TheView_StopsAtTheLevelsEdge_AndAnyCommandBringsItBack()
    {
        var (_, vm) = Open();
        for (var i = 0; i < 200; i++) vm.Peek(Direction.NorthWest);
        Assert.Equal(new Loc(0, 0), vm.PeekCentre);
        vm.HandleAction(InputAction.Hold);
        Assert.False(vm.IsPeeking);
        Assert.Equal(vm.Game.Player.Position, vm.Focus);
    }

    [AvaloniaFact]
    public void AMiddleButtonDrag_PansTheMap_AndLettingGoComesBack()
    {
        var (window, vm) = Open();
        var map = window.GetVisualDescendants().OfType<MapView>().First();
        var cell = map.Renderer.CellSize;
        var start = map.TranslatePoint(new Point(map.Bounds.Width / 2, map.Bounds.Height / 2), window)!.Value;
        var me = vm.Game.Player.Position;
        window.MouseDown(start, MouseButton.Middle);
        window.MouseMove(new Point(start.X + cell.Width * 4.5, start.Y), RawInputModifiers.MiddleMouseButton);
        Assert.True(vm.IsPeeking);
        Assert.Equal(Math.Max(0, me.X - 4), vm.PeekCentre.X);                // dragged right: the view shows the west
        window.MouseUp(new Point(start.X + cell.Width * 4.5, start.Y), MouseButton.Middle);
        Assert.False(vm.IsPeeking);
        Assert.Equal(me, vm.Game.Player.Position);
    }

    /// <summary>Alt (Option on a Mac) works as Ctrl does — macOS keeps Ctrl+arrows, and a VM's Cmd is Super.</summary>
    [AvaloniaFact]
    public void AltArrows_LookAround_Too_AndTheHotbarStepsMoveToAltBrackets()
    {
        var (window, vm) = Open();
        var me = vm.Game.Player.Position;
        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Alt);
        Assert.True(vm.IsPeeking);
        Assert.Equal(Math.Max(0, me.X - MainWindowViewModel.PeekStep), vm.PeekCentre.X);
        Assert.DoesNotContain(vm.HotbarSlots, h => h.Selected);              // (no longer the hotbar's step)
        window.KeyReleaseQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Alt);
        window.KeyReleaseQwerty(PhysicalKey.AltLeft, RawInputModifiers.None);
        Assert.False(vm.IsPeeking);

        window.KeyPressQwerty(PhysicalKey.BracketRight, RawInputModifiers.Alt); // Alt+] steps along the hotbar now
        Assert.True(vm.HotbarSlots[1].Selected);
        window.KeyPressQwerty(PhysicalKey.BracketLeft, RawInputModifiers.Alt);  // and Alt+[ back
        Assert.True(vm.HotbarSlots[0].Selected);
    }

    /// <summary>Shift with the wheel — two fingers on a trackpad — looks around; letting go of Shift comes back.</summary>
    [AvaloniaFact]
    public void ShiftWheel_LooksAround_AndPlainWheelStillZooms()
    {
        var (window, vm) = Open();
        var map = window.GetVisualDescendants().OfType<MapView>().First();
        var middle = map.TranslatePoint(new Point(map.Bounds.Width / 2, map.Bounds.Height / 2), window)!.Value;
        var me = vm.Game.Player.Position;
        window.MouseWheel(middle, new Vector(0, -1), RawInputModifiers.Shift);  // a notch down: the view goes south
        Assert.True(vm.IsPeeking);
        Assert.Equal(Math.Min(vm.Game.Level.Height - 1, me.Y + 3), vm.PeekCentre.Y);
        window.MouseWheel(middle, new Vector(0, 0.2), RawInputModifiers.Shift); // a trackpad's small step: not yet a square
        Assert.Equal(Math.Min(vm.Game.Level.Height - 1, me.Y + 3), vm.PeekCentre.Y);
        window.MouseWheel(middle, new Vector(0.5, 0.2), RawInputModifiers.Shift); // they add up (and sideways works)
        Assert.Equal(Math.Min(vm.Game.Level.Height - 1, me.Y + 2), vm.PeekCentre.Y);
        Assert.Equal(Math.Max(0, me.X - 1), vm.PeekCentre.X);
        window.KeyReleaseQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.None);
        Assert.False(vm.IsPeeking);

        var zoom = vm.MapFontSize;
        window.MouseWheel(middle, new Vector(0, 1), RawInputModifiers.None);
        Assert.False(vm.IsPeeking);
        Assert.NotEqual(zoom, vm.MapFontSize);
    }
}
