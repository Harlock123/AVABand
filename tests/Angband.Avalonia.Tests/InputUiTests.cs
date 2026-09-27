using Angband.Avalonia.Controls;
using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Data;
using Angband.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

public class InputUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open(ulong seed = 42)
    {
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(seed, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    [AvaloniaFact]
    public void ArrowsAndEnter_NavigateMenus()
    {
        var (window, vm) = Open();
        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None); // drop menu: several items
        Assert.True(vm.PromptRows.Count > 2);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.Equal(2, vm.PromptSelectedIndex);
        var third = vm.PromptRows[2].Item;

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.False(vm.IsPrompting);
        Assert.True(vm.FloorRows.Any(r => r.Item == third),
            $"last message: {vm.LastMessage}; floor: {string.Join(",", vm.FloorRows.Select(r => r.Name))}; third: {third}");
    }

    [AvaloniaFact]
    public void GamepadActions_DriveMenusAndTheContextAction()
    {
        var (_, vm) = Open(7);
        vm.HandleAction(InputAction.Quaff);
        Assert.True(vm.IsPrompting);
        vm.HandleAction(InputAction.Cancel);
        Assert.False(vm.IsPrompting);

        // Stand on the stairs and press Confirm (the A button): down we go.
        var game = typeof(MainWindowViewModel).GetField("_game", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(vm) as Angband.Core.Game.GameSession;
        game!.Player.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();
        vm.HandleAction(InputAction.Confirm);
        Assert.Contains("250 ft", vm.StatusText.Replace("50 ft (L1)", "250 ft")); // at depth 1 (50 ft)
        Assert.Equal(1, game.Player.Depth);
    }

    [AvaloniaFact]
    public void Keys_CanBeRebound_InTheControlsTab()
    {
        var (window, vm) = Open();
        var settings = window.OpenSettings();
        settings.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 2;
        TileRenderingTests.Save(settings, "settings-controls");
        var row = vm.BindingRows.Single(r => r.Action == InputAction.Rest);

        vm.RebindKeyCommand.Execute(row);
        Assert.True(vm.IsCapturingKey);
        settings.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);

        Assert.False(vm.IsCapturingKey);
        Assert.Equal(InputAction.Rest, vm.Bindings.ForKey("F2"));
        Assert.Contains("F2", vm.BindingRows.Single(r => r.Action == InputAction.Rest).Keys);
    }

    [AvaloniaFact]
    public void ClickingTheMap_TravelsThere()
    {
        var (window, vm) = Open(3);
        var map = window.GetVisualDescendants().OfType<MapView>().First();
        window.CaptureRenderedFrame(); // lay out and render once so the camera is known

        var game = typeof(MainWindowViewModel).GetField("_game", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(vm) as Angband.Core.Game.GameSession;
        foreach (var m in game!.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m); // town folk would interrupt travel
        var start = game.Player.Position;
        var target = game.Level.AllLocs()
            .Where(p => game.Level.IsPassable(p) && game.Known.IsKnown(p) && p.DistanceTo(start) is > 2 and < 6)
            .OrderBy(p => p.Y).ThenBy(p => p.X).First();

        // Translate the target square into a point in the window.
        var cell = map.Renderer.CellSize;
        var fx = game.Player.Position; // the camera centres on the player when the map is larger than the view
        var cols = (int)(map.Bounds.Width / cell.Width);
        var rows = (int)(map.Bounds.Height / cell.Height);
        double Origin(int size, int view, double px, double c) => size <= view ? Math.Floor((px - size * c) / 2) : 0;
        int Offset(int size, int view, int focus) => size <= view ? 0 : Math.Clamp(focus - view / 2, 0, size - view);
        var x = Origin(game.Level.Width, cols, map.Bounds.Width, cell.Width) + (target.X - Offset(game.Level.Width, cols, fx.X) + 0.5) * cell.Width;
        var y = Origin(game.Level.Height, rows, map.Bounds.Height, cell.Height) + (target.Y - Offset(game.Level.Height, rows, fx.Y) + 0.5) * cell.Height;
        var point = map.TranslatePoint(new Point(x, y), window)!.Value;

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        Assert.Equal(target, game.Player.Position);
    }

    /// <summary>Shift+arrow runs: one key press takes the player the length of a corridor.</summary>
    [AvaloniaFact]
    public void ShiftArrow_RunsDownACorridor()
    {
        var (window, vm) = Open();
        var game = vm.Game;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);

        // Carve a known corridor running east from the player, ten squares long.
        var start = game.Player.Position;
        var terrain = game.Data.Terrain.Ids;
        for (var dx = -1; dx <= 11; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            var p = new Loc(start.X + dx, start.Y + dy);
            if (!game.Level.InBounds(p)) continue;
            game.Level[p].Feature = dy == 0 && dx is >= 0 and <= 10 ? terrain.Floor : terrain.Granite;
            game.Level[p].Trap = 0;
            foreach (var item in game.Level.Objects.At(p).ToList()) game.Level.Objects.Remove(p, item);
        }
        game.Known.RememberAll(game.Level);
        game.UpdateView();

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);

        Assert.Equal(start + new Loc(10, 0), game.Player.Position);
    }

    /// <summary>In a menu, Shift+arrow moves the selection as the arrow does, rather than closing the menu.</summary>
    [AvaloniaFact]
    public void ShiftArrow_InAMenu_MovesTheSelection()
    {
        var (window, vm) = Open();
        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
        Assert.True(vm.PromptRows.Count > 2);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Assert.True(vm.IsPrompting);
        Assert.Equal(1, vm.PromptSelectedIndex);
    }
}
