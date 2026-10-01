using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.World;
using Angband.Data;
using Angband.Data.Tiles;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>Sconces on the walls of lit rooms (AVABand's own): a lit room's walls tell from an unlit one's.</summary>
public class SconceUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open(bool tiles)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings { UseTiles = tiles, TilesetId = "dcss", TileScale = 1 };
        settings.Options[DisplayOptions.Hints] = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory),
            TilesetCatalog.Discover([TilesetCatalog.DefaultDirectory]), settings, save: null);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        return (window, vm);
    }

    /// <summary>A shallow level with lit and unlit rooms both (lit rooms are the rule near the surface).</summary>
    private static void ToARoomyLevel(MainWindowViewModel vm)
    {
        var game = vm.Game;
        game.MarkDebugUsed();
        for (var tries = 0; tries < 20; tries++)
        {
            vm.Execute(new DebugJumpCommand(2 + tries % 6));
            vm.SkipScenes();
            var level = game.Level;
            var lit = level.AllLocs().Any(p => level[p].Has(SquareFlags.Room) && level[p].Has(SquareFlags.Glow));
            var dark = level.AllLocs().Any(p => level[p].Has(SquareFlags.Room) && !level[p].Has(SquareFlags.Glow)
                                                && level.FeatureAt(p).Has(TerrainFlags.Wall));
            if (lit && dark) break;
        }
        vm.ShowWholeMap = true;
    }

    [AvaloniaFact]
    public void Lit_rooms_walls_have_sconces_and_unlit_ones_none()
    {
        var (window, vm) = Open(tiles: false);
        ToARoomyLevel(vm);
        var level = vm.Game.Level;
        var sconces = level.AllLocs().Where(p => vm.GetCell(p.X, p.Y).Sconce).ToList();
        Assert.NotEmpty(sconces);
        Assert.All(sconces, p =>
        {
            Assert.True(level.FeatureAt(p).Has(TerrainFlags.Wall));
            Assert.True(level[p].Has(SquareFlags.Glow));       // a lit room's wall
        });
        // Walls of unlit rooms: never.
        Assert.DoesNotContain(level.AllLocs(), p => vm.GetCell(p.X, p.Y).Sconce && !level[p].Has(SquareFlags.Glow));
        // Spaced along the wall, not every square.
        Assert.All(sconces, p => Assert.Equal(0, (p.X + p.Y) % 4));
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "sconces-ascii");

        vm.SetOption(DisplayOptions.Sconces, false);
        Assert.DoesNotContain(level.AllLocs(), p => vm.GetCell(p.X, p.Y).Sconce);
    }

    [AvaloniaFact]
    public void Sconces_are_drawn_over_tiles_too()
    {
        var (window, vm) = Open(tiles: true);
        ToARoomyLevel(vm);
        Assert.True(vm.UseTiles);
        Assert.Contains(vm.Game.Level.AllLocs(), p => vm.GetCell(p.X, p.Y).Sconce);
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "sconces-tiles");
    }
}
