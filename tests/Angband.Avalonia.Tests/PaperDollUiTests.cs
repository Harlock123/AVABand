using Angband.Avalonia.Controls;
using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Data;
using Angband.Data.Tiles;
using Angband.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

/// <summary>The character sheet's paper doll: the equipment shown where it is worn.</summary>
public class PaperDollUiTests
{
    private static readonly IReadOnlyList<TilesetManifest> Tilesets = TilesetCatalog.Discover([TilesetCatalog.DefaultDirectory]);

    private static (MainWindow Window, MainWindowViewModel Vm) Open(bool tiles)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings { UseTiles = tiles, TilesetId = "adam-bolt", TileScale = 2 };
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), Tilesets, settings, save: null);
        vm.StartGame(42);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    [AvaloniaFact]
    public void TheCharacterSheet_ShowsWhatIsWornWhere()
    {
        var (window, vm) = Open(tiles: false);
        vm.HandleAction(InputAction.CharacterSheet);
        var sheet = Assert.IsType<CharacterSheetWindow>(window.OwnedWindows.Last());
        var doll = ((CharacterSheetViewModel)sheet.DataContext!).PaperDoll!;

        var gear = vm.Game.Player.Inventory;
        Assert.Same(gear.Weapon, doll.Weapon.Item);
        Assert.Equal(vm.Game.Describe(gear.Weapon!), doll.Weapon.Name);
        Assert.Contains(doll.Slots, s => s.Item is { } i && i.Base.Slot == Angband.Core.Definitions.EquipSlot.Body);
        Assert.True(doll.Head.IsEmpty);
        Assert.Equal("(nothing)", doll.Head.Name);
        Assert.Equal(12, doll.Slots.Count);
        Assert.Equal(12, doll.Slots.Count(s => s.Label.Length > 0));

        // Each slot draws its item as the map does: the weapon's letter, in its colour.
        Assert.Equal(gear.Weapon!.Base.Glyph, doll.Weapon.Picture.GetCell(0, 0).Glyph);
        Assert.Equal('@', doll.Player.Picture.GetCell(0, 0).Glyph);
        Assert.True(doll.Head.Picture.GetCell(0, 0).IsUnknown); // an empty slot is left blank
        Assert.Equal("Human Warrior", doll.Player.Label);
        var grid = sheet.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PaperDoll");
        Assert.True(grid.IsVisible);
        Assert.Equal(13, grid.GetVisualDescendants().OfType<MapView>().Count());
        TileRenderingTests.Save(sheet, "paper-doll-ascii");
    }

    [AvaloniaFact]
    public void InTileMode_TheDollUsesTheTileset()
    {
        var (window, vm) = Open(tiles: true);
        vm.HandleAction(InputAction.CharacterSheet);
        var sheet = Assert.IsType<CharacterSheetWindow>(window.OwnedWindows.Last());
        var doll = ((CharacterSheetViewModel)sheet.DataContext!).PaperDoll!;
        Assert.True(doll.Weapon.UseTiles);
        Assert.Equal("adam-bolt", doll.Weapon.Tileset?.Id);
        Assert.StartsWith("object:", doll.Weapon.Picture.GetCell(0, 0).TileKey);
        TileRenderingTests.Save(sheet, "paper-doll-tiles");
    }

    [AvaloniaFact]
    public void MonsterRecall_HasNoDoll()
    {
        var (window, vm) = Open(tiles: false);
        vm.ShowRecall(vm.Game.Data.Monster("jackal")!);
        var recall = Assert.IsType<CharacterSheetWindow>(window.OwnedWindows.Last());
        Assert.False(((CharacterSheetViewModel)recall.DataContext!).HasPaperDoll);
        Assert.False(recall.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PaperDoll").IsVisible);
    }
}
