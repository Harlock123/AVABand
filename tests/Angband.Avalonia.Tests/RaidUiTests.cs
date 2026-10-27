using Angband.Avalonia.Controls;
using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Randomness;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

/// <summary>Raids on the town in the interface: the scene, the raiders on the map, and the Quests page's town row.</summary>
public sealed class RaidUiTests : IDisposable
{
    private readonly string _bundled = Path.Combine(Path.GetTempPath(), "avaband-bundled-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_bundled)) Directory.Delete(_bundled, true);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_raid_has_its_scene_shows_on_the_map_and_on_the_quests_page(bool tiles)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var tilesets = Angband.Data.Tiles.TilesetCatalog.Discover([Angband.Data.Tiles.TilesetCatalog.DefaultDirectory]);
        var settings = new AppSettings { UseTiles = tiles, TilesetId = "adam-bolt", TileScale = 1 };
        settings.Options[DisplayOptions.Hints] = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), tilesets, settings, save: null)
        {
            ArtDirectory = _bundled,
            BundledArtDirectory = _bundled, // (empty: the painted scene)
        };
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.AfterQueuedInput = a => a();
        vm.StartGame(42, "warrior");
        var game = vm.Game;
        game.GainExperience(game.ExperienceForLevel(19));
        game.Player.Hp = game.Player.MaxHp;
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 760 };
        window.Show();
        var view = window.GetVisualDescendants().OfType<SceneView>().Single();
        view.RunsByItself = false;

        game.AvaQuests.JobsDone = 3;
        vm.Execute(new DebugJumpCommand(3));
        vm.Execute(new DebugJumpCommand(0));
        vm.SkipScenes();
        Assert.Equal(2, game.TownStage);
        Assert.Null(game.Raid);

        // The raid: its scene holds until Space — the leader's letter before the burning town.
        var raid = Assert.IsType<Angband.Core.Quests.TownRaid>(game.StartRaid(new GameRandom(5)));
        vm.Execute(new HoldCommand());
        var leader = game.Raiders(raid).First();
        Assert.Equal(SceneKind.TownRaid, vm.Scene!.Kind);
        Assert.True(SceneKinds.HoldsForKey(SceneKind.TownRaid));
        Assert.True(vm.Scene.WaitForKey);
        Assert.Equal("Raiders in the town!", vm.Scene.Caption);
        Assert.Equal(leader.Race.Glyph.ToString(), vm.Scene.Glyph);
        Assert.Contains($"from the {raid.From}", vm.Scene.Subtitle);
        Assert.Null(vm.Scene.Picture);
        var mode = tiles ? "tiles" : "ascii";
        if (!tiles)
        {
            view.Advance(SceneView.DurationFor(SceneKind.TownRaid) * 0.6);
            window.CaptureRenderedFrame();
            TileRenderingTests.Save(window, "scene-town-raid");
        }
        vm.SkipScenes();

        // On the map: you, a few squares from the band coming in.
        game.Player.Position = game.Level.AllLocs().Where(l => game.Level.IsEmptyFloor(l) && l.DistanceTo(leader.Position) is >= 4 and <= 6
                && Angband.Core.Combat.ProjectionPath.Projectable(game.Level, l, leader.Position, 20))
            .OrderBy(l => l.DistanceTo(leader.Position)).First();
        game.UpdateView();
        vm.Refresh();
        Assert.All(game.Raiders(raid), m => Assert.True(m.IsVisible || !game.IsDaytime));
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, $"town-raid-{mode}");

        // The Quests page: the town raided.
        var town = vm.CreateKnowledge().Quests!.Rows[0];
        Assert.Equal("The town", town.Name);
        Assert.Equal("renown 3, stage 2, raided!", town.Note);
        Assert.Contains("Raiders are in the town now", town.Describe());

        // Driven off: counted on the page.
        game.DamageMonster(leader, 1_000_000);
        town = vm.CreateKnowledge().Quests!.Rows[0];
        Assert.Equal("renown 4, stage 2, raids 1/1 repelled", town.Note);
        Assert.Contains("Raids on the town: 1 driven off, 0 lost.", town.Describe());
    }
}
