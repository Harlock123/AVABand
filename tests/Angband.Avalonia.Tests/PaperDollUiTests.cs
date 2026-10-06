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
        Assert.Equal(13, doll.Slots.Count);
        Assert.Equal(13, doll.Slots.Count(s => s.Label.Length > 0));
        Assert.Equal("Arms", doll.Arms.Label);                    // AVABand's bracers' slot
        Assert.True(doll.Arms.IsEmpty);

        // Each slot draws its item as the map does: the weapon's letter, in its colour.
        Assert.Equal(gear.Weapon!.Base.Glyph, doll.Weapon.Picture.GetCell(0, 0).Glyph);
        Assert.Equal('@', doll.Player.Picture.GetCell(0, 0).Glyph);
        Assert.True(doll.Head.Picture.GetCell(0, 0).IsUnknown); // an empty slot is left blank
        Assert.Equal("Human Warrior", doll.Player.Label);
        var grid = sheet.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PaperDoll");
        Assert.True(grid.IsVisible);
        Assert.Equal(14, grid.GetVisualDescendants().OfType<MapView>().Count());
        TileRenderingTests.Save(sheet, "paper-doll-ascii");
    }

    [AvaloniaFact]
    public void TheArmsSlot_ShowsTheBracers_AndTheirSocketsInTheTooltip()
    {
        var (window, vm) = Open(tiles: false);
        var game = vm.Game;
        var bracers = game.Objects.Create("iron_bracers");
        game.Knowledge.LearnKind(bracers.Kind);
        game.Player.Inventory.Add(bracers);
        Assert.True(game.Execute(new Angband.Core.Game.WieldCommand(bracers)));
        bracers = game.Player.Inventory.Equipped.First(i => i.Kind.Id == "iron_bracers");
        var ruby = game.Objects.Create("ruby");
        game.Knowledge.LearnKind(ruby.Kind);
        Assert.True(game.Execute(new Angband.Core.Game.SetGemCommand(bracers, game.Player.Inventory.Add(ruby)!)));
        vm.HandleAction(InputAction.CharacterSheet);
        var sheet = Assert.IsType<CharacterSheetWindow>(window.OwnedWindows.Last());
        var doll = ((CharacterSheetViewModel)sheet.DataContext!).PaperDoll!;
        Assert.Same(bracers, doll.Arms.Item);
        Assert.Contains("Socket 1: Ruby (+3 to-dam, resist fire)", doll.Arms.Description);
        Assert.Contains("Socket 2: empty", doll.Arms.Description);
        Assert.Equal("Sockets: Ruby · empty", doll.Arms.Sockets);
        Assert.False(doll.Weapon.HasSockets);
        Assert.Contains("Socket 2: empty", ((CharacterSheetViewModel)sheet.DataContext!).Text);
        TileRenderingTests.Save(sheet, "paper-doll-arms");
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

    [AvaloniaFact]
    public void TheDoll_FollowsTheEquipment_WhileTheSheetIsOpen()
    {
        var (window, vm) = Open(tiles: false);
        vm.HandleAction(InputAction.CharacterSheet);
        var sheetWindow = Assert.IsType<CharacterSheetWindow>(window.OwnedWindows.Last());
        var sheet = (CharacterSheetViewModel)sheetWindow.DataContext!;
        var game = vm.Game;
        Assert.True(sheet.PaperDoll!.Head.IsEmpty);

        // Put a cap on: the head slot fills in, on the open sheet.
        var cap = game.Objects.Create("hard_leather_cap");
        game.Player.Inventory.Add(cap);
        vm.Execute(new Angband.Core.Game.WieldCommand(cap));
        Assert.Same(cap, sheet.PaperDoll!.Head.Item);
        Assert.Equal(game.Describe(cap), sheet.PaperDoll.Head.Name);
        var shown = sheetWindow.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(game.Describe(cap), shown);
        TileRenderingTests.Save(sheetWindow, "paper-doll-updated");

        // Take the weapon off: its slot empties.
        vm.Execute(new Angband.Core.Game.TakeOffCommand(game.Player.Inventory.Weapon!));
        Assert.True(sheet.PaperDoll.Weapon.IsEmpty);

        // Switching to tiles redraws it with the tileset.
        vm.ToggleTilesCommand.Execute(null);
        Assert.Equal(vm.UseTiles, sheet.PaperDoll.Weapon.UseTiles);

        // A turn passing burns the torch, and the doll shows its fuel going down...
        var torch = game.Player.Inventory.Light!;
        vm.Execute(new Angband.Core.Game.HoldCommand());
        Assert.Equal(game.Describe(torch), sheet.PaperDoll.Light.Name);

        // ...but when nothing it shows has changed, the same doll stays (no needless redraws).
        var doll = sheet.PaperDoll;
        vm.HandleAction(InputAction.Feeling); // takes no game time
        Assert.Same(doll, sheet.PaperDoll);

        // Once the sheet is closed it is no longer kept up to date.
        sheetWindow.Close();
        var boots = game.Objects.Create("leather_sandals");
        game.Player.Inventory.Add(boots);
        vm.Execute(new Angband.Core.Game.WieldCommand(boots));
        Assert.True(sheet.PaperDoll.Feet.IsEmpty);
    }
}
