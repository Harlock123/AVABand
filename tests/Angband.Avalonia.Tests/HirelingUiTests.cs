using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Data;
using Angband.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

/// <summary>AVABand's hireling in the interface: the sidebar, the map, the orders and the Companions page.</summary>
public class HirelingUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, GameSession Game) Open()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings();
        settings.Options[DisplayOptions.Hints] = false;
        settings.Options[DisplayOptions.Scenes] = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var game = vm.Game;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        game.Join(new Hireling { Name = "Bergil", Look = "human", Role = HirelingRole.Archer, Level = 1, Share = 12, Fee = 50 });
        vm.Refresh();
        return (window, vm, game);
    }

    [AvaloniaFact]
    public void The_sidebar_shows_their_health_level_and_orders()
    {
        var (window, vm, game) = Open();
        Assert.True(vm.HirelingVisible);
        Assert.True(vm.HasHireling);
        Assert.Equal("Bergil the archer (level 1, following)", vm.HirelingName);
        Assert.Equal("**********", vm.HirelingStars);
        var bar = window.GetVisualDescendants().OfType<StackPanel>().First(p => p.Name == "HirelingBar");
        Assert.True(bar.IsVisible);

        game.HirelingHere!.Hp = game.HirelingHere.MaxHp / 2;
        vm.HandleAction(InputAction.HirelingHold);
        Assert.Equal(HirelingOrder.Hold, game.Hireling!.Order);
        Assert.Equal("Bergil the archer (level 1, holding)", vm.HirelingName);
        Assert.InRange(vm.HirelingStars.Length, 5, 6);
        TileRenderingTests.Save(window, "hireling-sidebar");
    }

    [AvaloniaFact]
    public void On_the_map_they_are_green_outlined_and_drawn_as_one_of_their_folk()
    {
        var (_, vm, game) = Open();
        var at = game.HirelingHere!.Position;
        Assert.Equal(at, vm.Ally);
        var cell = vm.GetCell(at.X, at.Y);
        Assert.Equal('p', cell.Glyph);
        Assert.Equal("monster:ranger", cell.TileKey);
        Assert.Equal(new MapCellBuilder(vm.Game.Data).Color("LightGreen"), cell.Foreground);
        Assert.Contains("your hireling", game.LookDescription(game.HirelingHere));
    }

    [AvaloniaFact]
    public void In_tiles_they_are_drawn_too()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var tilesets = Angband.Data.Tiles.TilesetCatalog.Discover([Angband.Data.Tiles.TilesetCatalog.DefaultDirectory]);
        var settings = new AppSettings { UseTiles = true, TilesetId = tilesets[0].Id, TileScale = 2 };
        settings.Options[DisplayOptions.Hints] = false;
        settings.Options[DisplayOptions.Scenes] = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), tilesets, settings, save: null);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        vm.Game.MarkDebugUsed();
        vm.Execute(new DebugJumpCommand(3));
        vm.Game.Join(new Hireling { Name = "Frerin", Look = "dwarf", Role = HirelingRole.Warrior, Level = 1 });
        vm.Refresh();
        Assert.True(vm.UseTiles);
        Assert.Equal(vm.Game.HirelingHere!.Position, vm.Ally);
        Assert.Equal("monster:stonefoot_warrior", vm.GetCell(vm.Ally!.Value.X, vm.Ally.Value.Y).TileKey);
        TileRenderingTests.Save(window, "hireling-tiles");
    }

    [AvaloniaFact]
    public void Their_orders_have_keys_in_every_keyset_and_a_place_in_the_menus()
    {
        foreach (var keyset in Enum.GetValues<InputBindings.Keyset>())
        {
            var keys = InputBindings.Preset(keyset).Keys;
            Assert.Equal(InputAction.HirelingFollow, keys["Ctrl+Shift+F"]);
            Assert.Equal(InputAction.HirelingHold, keys["Ctrl+Shift+H"]);
            Assert.Equal(InputAction.HirelingAttack, keys["Ctrl+Shift+A"]);
        }
        var (_, vm, game) = Open();
        vm.HirelingHoldCommand.Execute(null);
        Assert.Equal(HirelingOrder.Hold, game.Hireling!.Order);
        vm.HirelingFollowCommand.Execute(null);
        Assert.Equal(HirelingOrder.Follow, game.Hireling.Order);

        // Your own right-click menu has their orders; theirs offers to change places, not to attack.
        vm.OpenContextMenu(game.Player.Position);
        Assert.Contains("Orders for Bergil", vm.MenuLabels);
        vm.PromptKey((char)('a' + vm.MenuLabels.ToList().IndexOf("Orders for Bergil")));
        Assert.Equal(["Follow close", "Hold here", "Attack my target", "Send them home"], vm.MenuLabels); // (in town)
        vm.PromptKey('b');
        Assert.Equal(HirelingOrder.Hold, game.Hireling.Order);

        vm.OpenContextMenu(game.HirelingHere!.Position);
        Assert.Contains("Change places", vm.MenuLabels);
        Assert.DoesNotContain("Attack", vm.MenuLabels);
        Assert.DoesNotContain("Steal", vm.MenuLabels);
    }

    [AvaloniaFact]
    public void The_Companions_page_lists_the_hireling_and_the_fallen()
    {
        var (_, vm, game) = Open();
        game.FallenCompanions.Add(new FallenCompanion { Name = "Hirgon", Look = "dwarf", Role = HirelingRole.Warrior, Level = 6, Depth = 9, KilledBy = "a cave orc" });
        var page = vm.CreateKnowledge().Companions!;
        Assert.Equal(["Bergil the archer", "Hirgon the warrior"], page.Rows.Select(r => r.Name));
        Assert.Equal("fell at 450 ft", page.Rows[1].Note);
        Assert.Contains("killed by a cave orc", page.Rows[1].Describe());
        Assert.Contains("12% of the gold you find", page.Rows[0].Describe());
    }
}
