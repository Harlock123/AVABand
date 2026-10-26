using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>AVABand's quests in the window: the Prancing Pony's menus, and the journal.</summary>
public class QuestUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        vm.Game.GainExperience(vm.Game.ExperienceForLevel(11));
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    private static void Pick(MainWindowViewModel vm, string label) =>
        vm.PromptKey(vm.ChoiceRows.First(r => r.Text.StartsWith(label)).Letter[0]);

    [AvaloniaFact]
    public void Walking_into_the_inn_offers_work_and_a_quest_shows_its_words()
    {
        var (window, vm) = Open();
        var game = vm.Game;
        var door = game.Level.AllLocs().First(l => game.Level.FeatureAt(l).Shop == "inn");
        var from = game.Level.AllLocs().First(l => l.ChebyshevTo(door) == 1 && game.Level.IsEmptyFloor(l));
        game.Player.Position = from;
        vm.Execute(new WalkCommand(Angband.Core.Geometry.DirectionExtensions.FromOffset(door.X - from.X, door.Y - from.Y)));

        Assert.True(vm.IsPrompting);
        Assert.Equal("The Prancing Pony", vm.PromptTitle);
        Assert.Contains("Butterbur", vm.PromptText);
        Pick(vm, "Ask who has work");
        Pick(vm, "Ask about: The Broken Blade");
        Assert.Contains("hilt, the blade and the point", vm.PromptText);
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "quest-offer");
        Pick(vm, "Take it on");
        Assert.False(vm.IsPrompting);
        Assert.NotNull(game.AvaQuests.Get("broken_blade"));

        var journal = vm.CreateKnowledge().Quests!;
        Assert.Equal(["The town", "The Broken Blade"], journal.Rows.Select(r => r.Name));
        Assert.Contains("You have 0 of 3", journal.Rows[1].Describe());
    }

    /// <summary>The town grown with your renown: on the map (in tiles and in letters, by day and by night), and on the Quests page.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_town_grows_on_the_map_and_the_quests_page_says_how_far(bool tiles)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var tilesets = Angband.Data.Tiles.TilesetCatalog.Discover([Angband.Data.Tiles.TilesetCatalog.DefaultDirectory]);
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), tilesets,
            new AppSettings { UseTiles = tiles, TilesetId = "adam-bolt", TileScale = 1 }, save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        var game = vm.Game;
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 760 };
        window.Show();
        vm.ShowWholeMap = true;
        var mode = tiles ? "tiles" : "ascii";
        TileRenderingTests.Save(window, $"town-stage0-{mode}");

        var town = vm.CreateKnowledge().Quests!.Rows[0];
        Assert.Equal("The town", town.Name);
        Assert.Equal("renown 0, stage 0", town.Note);
        Assert.Contains("Next, at renown 1: Lamps now light the square at night.", town.Describe());

        game.AvaQuests.JobsDone = 15;
        vm.Execute(new DebugJumpCommand(3));
        vm.Execute(new DebugJumpCommand(0));
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        Assert.Equal(5, game.TownStage);
        vm.ShowWholeMap = true;
        TileRenderingTests.Save(window, $"town-stage5-{mode}");
        town = vm.CreateKnowledge().Quests!.Rows[0];
        Assert.Equal("renown 15, stage 5", town.Note);
        Assert.Contains("The town can grow no more", town.Describe());

        // By night: the lamps light the square, and the walls they light carry sconces.
        game.Scheduler.SetGameTurn(game.Data.Constants.DayLength / 2 + 10);
        vm.Execute(new DebugJumpCommand(3));
        vm.Execute(new DebugJumpCommand(0));
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        Assert.False(game.IsDaytime);
        vm.ShowWholeMap = false;
        game.Player.Position = game.Level.AllLocs().First(p => game.Level.FeatureAt(p).Id == "town_fountain") + new Angband.Core.Geometry.Loc(0, 1);
        game.UpdateView();
        vm.Refresh();
        TileRenderingTests.Save(window, $"town-stage5-night-{mode}");
    }
}
