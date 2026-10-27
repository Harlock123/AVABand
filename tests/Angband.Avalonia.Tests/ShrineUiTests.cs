using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>AVABand's shrines in the window: on the map (in tiles and in letters), and what one asks.</summary>
public class ShrineUiTests
{
    private static void Pick(MainWindowViewModel vm, string label) =>
        vm.PromptKey(vm.ChoiceRows.First(r => r.Text.StartsWith(label)).Letter[0]);

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_shrine_is_drawn_on_the_map_asks_what_youll_do_and_goes_dark(bool tiles)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var tilesets = Angband.Data.Tiles.TilesetCatalog.Discover([Angband.Data.Tiles.TilesetCatalog.DefaultDirectory]);
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), tilesets,
            new AppSettings { UseTiles = tiles, TilesetId = "adam-bolt", TileScale = 2 }, save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        var game = vm.Game;
        game.Player.Hp = game.Player.MaxHp = 5000;
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var mode = tiles ? "tiles" : "ascii";

        // Down until a level has a shrine (one in six or seven, from 100 ft).
        Loc? shrine = null;
        for (var i = 0; i < 200 && shrine is null; i++)
        {
            vm.Execute(new DebugJumpCommand(12));
            shrine = game.Level.AllLocs().Where(p => GameSession.ShrinePowerOf(game.Level.FeatureAt(p).Id) is not null).Select(p => (Loc?)p).FirstOrDefault();
        }
        Assert.NotNull(shrine);
        var at = shrine.Value;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        var power = GameSession.ShrinePowerOf(game.Level.FeatureAt(at).Id)!;
        game.Player.Position = at + new Loc(-1, 0);
        game.UpdateView();
        vm.Refresh();
        TileRenderingTests.Save(window, $"shrine-map-{mode}");

        vm.Execute(new WalkCommand(Direction.East));
        Assert.True(vm.IsPrompting);
        Assert.Contains(power.Name == "Morgoth" ? "Dark altar of Morgoth" : $"Shrine of {power.Name}", vm.PromptTitle);
        Assert.Contains(power.Gift, vm.PromptText);
        Assert.Contains(vm.ChoiceRows, r => r.Text.StartsWith("Desecrate"));
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, $"shrine-prompt-{mode}");

        Pick(vm, "Pray to");
        Assert.False(vm.IsPrompting);
        Assert.Equal("shrine_spent", game.Level.FeatureAt(at).Id);
        Assert.Equal(1, game.Shrines.Used);
        vm.Execute(new WalkCommand(Direction.West));
        vm.Refresh();
        TileRenderingTests.Save(window, $"shrine-dark-{mode}");
    }
}
