using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Geometry;
using Angband.Data;
using Angband.Data.Tiles;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>The town's shopfronts (AVABand's own): tinted buildings, windows with wares, awnings, lanterns at night, names.</summary>
public class ShopfrontUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open(bool tiles)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings { UseTiles = tiles, TilesetId = "dcss", TileScale = 1 };
        settings.Options[DisplayOptions.Hints] = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory),
            TilesetCatalog.Discover([TilesetCatalog.DefaultDirectory]), settings, save: null);
        vm.StartGame(42, "warrior");
        vm.ShowWholeMap = true;
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        return (window, vm);
    }

    [AvaloniaFact]
    public void EveryShop_HasItsFront_Windows_AndAName()
    {
        var (window, vm) = Open(tiles: true);
        var level = vm.Game.Level;
        var doors = level.AllLocs().Where(p => level.FeatureAt(p).Shop is not null).ToList();
        Assert.Equal(vm.Game.Data.Town.Shops.Count, doors.Count);
        foreach (var door in doors)
        {
            var part = vm.FacadeAt(door.X, door.Y);
            Assert.NotNull(part);
            Assert.Equal(FacadeKind.Door, part.Kind);
            Assert.True(level.IsPassable(door + new Loc(part.StreetX, part.StreetY)));   // the awning hangs over the street
            var windows = level.AllLocs().Where(p => vm.FacadeAt(p.X, p.Y) is { Kind: FacadeKind.Window } w && w.ShopId == part.ShopId).ToList();
            Assert.InRange(windows.Count, 1, 4);
            Assert.All(windows, w => Assert.InRange(w.ChebyshevTo(door), 1, 2));                 // either side of the door
            Assert.Contains(level.AllLocs(), p => vm.FacadeAt(p.X, p.Y) is { Kind: FacadeKind.Wall } w && w.ShopId == part.ShopId);
        }
        // Wares in the windows (but the Home's are curtained), and every shop named.
        Assert.Contains(level.AllLocs(), p => vm.FacadeAt(p.X, p.Y) is { Kind: FacadeKind.Window, ShopId: "alchemist", Ware: not null });
        Assert.All(level.AllLocs().Select(p => vm.FacadeAt(p.X, p.Y)).Where(f => f is { Kind: FacadeKind.Window, ShopId: "home" }), f => Assert.Null(f!.Ware));
        Assert.Equal(doors.Count, vm.ShopSigns.Count);
        Assert.Contains(vm.ShopSigns, s => s.Name == "Alchemy shop");
        // The town's own wall isn't a shop's.
        Assert.Null(vm.FacadeAt(0, 0));
        TileRenderingTests.Save(window, "town-shopfronts-day");

        Assert.True(vm.Game.IsDaytime);
        Assert.False(vm.IsNight);
        vm.Game.Scheduler.SetGameTurn(vm.Game.Data.Constants.DayLength * 3 / 4);
        Assert.True(vm.IsNight);
        vm.Revision++;
        TileRenderingTests.Save(window, "town-shopfronts-night");

        vm.SetOption(DisplayOptions.Shopfronts, false);
        vm.SetOption(DisplayOptions.ShopNames, false);
        Assert.Null(vm.FacadeAt(doors[0].X, doors[0].Y));
        Assert.Empty(vm.ShopSigns);
    }

    [AvaloniaFact]
    public void InLetters_AShopsNumber_StandsOnItsColour()
    {
        var (window, vm) = Open(tiles: false);
        var level = vm.Game.Level;
        var door = level.AllLocs().First(p => level.FeatureAt(p).Shop == "alchemist");
        var cell = vm.GetCell(door.X, door.Y);
        Assert.NotEqual(0u, cell.Background & 0x00FFFFFF);
        Assert.Equal(0xFFFFF4D8u, cell.Foreground);
        TileRenderingTests.Save(window, "town-shopfronts-ascii");
        vm.SetOption(DisplayOptions.Shopfronts, false);
        Assert.Equal(0u, vm.GetCell(door.X, door.Y).Background & 0x00FFFFFF);
    }

    /// <summary>A merchant caravan in the square: its wagon (a tile of every set) and its name.</summary>
    [AvaloniaFact]
    public void ACaravan_IsDrawnAsAWagon_WithItsName()
    {
        var (window, vm) = Open(tiles: true);
        var game = vm.Game;
        game.MarkDebugUsed();
        game.GainExperience(game.ExperienceForLevel(14));
        for (var i = 0; i < 60 && game.AvaQuests.Caravan is null; i++)
        {
            vm.Execute(new Angband.Core.Game.DebugJumpCommand(5));
            vm.Execute(new Angband.Core.Game.DebugJumpCommand(0));
            vm.SkipScenes();
        }
        Assert.NotNull(game.AvaQuests.Caravan);
        Assert.Contains(vm.ShopSigns, s => s.Name == "Merchant caravan");
        vm.ShowWholeMap = true;
        vm.Revision++;
        TileRenderingTests.Save(window, "town-caravan");
    }
}
