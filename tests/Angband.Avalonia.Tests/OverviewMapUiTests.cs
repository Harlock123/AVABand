using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Data;
using Angband.Data.Tiles;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

/// <summary>The level map (Angband 'M', display_map).</summary>
public class OverviewMapUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open(bool tiles)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var tilesets = TilesetCatalog.Discover([TilesetCatalog.DefaultDirectory]);
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), tilesets,
            new AppSettings { UseTiles = tiles, TilesetId = "gervais" }, save: null);
        vm.StartGame(42, "warrior");
        vm.Game.MarkDebugUsed();
        vm.Execute(new Angband.Core.Game.DebugJumpCommand(5));
        vm.Game.Known.RememberAll(vm.Game.Level); // as if the level were explored
        vm.Game.UpdateView();
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        return (window, vm);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void M_ShowsTheWholeLevel_SqueezedToFit(bool tiles)
    {
        var (window, vm) = Open(tiles);
        window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.Shift);
        var dialog = Assert.IsType<OverviewMapWindow>(window.OwnedWindows.Last());
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var overview = (OverviewMapSource)dialog.DataContext!;

        // The whole level fits: every block is on screen.
        var map = dialog.FindControl<Angband.Avalonia.Controls.MapView>("Map")!;
        var cell = map.Renderer.CellSize;
        Assert.True(overview.Width * cell.Width <= map.Bounds.Width + 0.5, $"{overview.Width}×{cell.Width} > {map.Bounds.Width}");
        Assert.True(overview.Height * cell.Height <= map.Bounds.Height + 0.5);
        Assert.True(overview.Block >= 1);

        // The player's block shows the player, and stairs outrank the floor around them.
        Assert.Equal("player", overview.GetCell(overview.Focus.X, overview.Focus.Y).TileKey);
        var stair = vm.Game.Level.FindFeature(TerrainFlags.Stair).First(s => s.ChebyshevTo(vm.Game.Player.Position) > overview.Block * 2);
        var shown = overview.GetCell(stair.X / overview.Block, stair.Y / overview.Block);
        Assert.Contains("stair", shown.TileKey);
        TileRenderingTests.Save(dialog, tiles ? "level-map-tiles" : "level-map-ascii");

        dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(dialog.IsVisible);
    }
}
