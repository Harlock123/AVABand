using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Records;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>The notice board's bounty on a unique in the window: its poster and picture, and the museum's mark.</summary>
public class BountyUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open(bool tiles)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var tilesets = tiles ? Angband.Data.Tiles.TilesetCatalog.Discover([Angband.Data.Tiles.TilesetCatalog.DefaultDirectory]) : [];
        var settings = tiles ? new AppSettings { UseTiles = true, TilesetId = "dcss", TileScale = 1 } : new AppSettings();
        settings.Options[DisplayOptions.Hints] = false;
        settings.Options[DisplayOptions.Scenes] = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), tilesets, settings, save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        vm.Game.GainExperience(vm.Game.ExperienceForLevel(14));
        vm.Game.Player.MaxDepth = 12;
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_board_shows_the_bounty_poster_with_the_uniques_picture(bool tiles)
    {
        var (window, vm) = Open(tiles);
        var game = vm.Game;
        game.Player.Position = game.Level.AllLocs().First(l => game.Level.FeatureAt(l).Shop == "inn");
        vm.Execute(new EnterStoreCommand());
        vm.Execute(new QuestChoiceCommand("inn:board"));

        var bounty = Assert.IsType<Angband.Core.Quests.UniqueBounty>(game.Bounty);
        var race = game.Data.Monster(bounty.Race)!;
        Assert.Equal("The notice board", vm.PromptTitle);
        Assert.Contains($"WANTED — {race.Name}, said to haunt the levels around", vm.PromptText);
        var row = vm.ChoiceRows.First();
        Assert.Equal($"Take: Bounty: {race.Name} ({bounty.Reward} gold and a trophy)", row.Text);
        Assert.True(row.HasPicture);
        Assert.Equal(tiles, row.UseTiles);
        Assert.Contains(vm.ChoiceRows, r => r.Text.StartsWith("Take: Hunt", StringComparison.Ordinal) && r.HasPicture);
        Assert.False(vm.ChoiceRows.Last().HasPicture); // (Back)
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, tiles ? "board-bounty-tiles" : "board-bounty");

        vm.PromptKey(row.Letter[0]);
        Assert.True(bounty.Taken);
        var log = vm.CreateQuestLog();
        Assert.Contains(log.Cards, c => c.Name == $"Bounty: {race.Name}" && c.Reward == $"{bounty.Reward} gold and a trophy");
    }

    [AvaloniaFact]
    public void The_museum_marks_a_unique_slain_for_a_bounty()
    {
        var (_, vm) = Open(tiles: false);
        var unique = Museum.UniqueRaces(vm.Game.Data).OrderBy(r => r.Depth).First();
        vm.Museum.Uniques[unique.Id] = new MuseumEntry { WhenUtc = DateTime.UtcNow, Character = "Bob the Human Warrior", Depth = 5, Bounty = true };
        var piece = vm.CreateMuseum().Uniques.Pieces.Single(p => p.Name == unique.Name);
        Assert.EndsWith("· Bounty claimed", piece.Note);
        Assert.Contains("Bounty claimed.", piece.Detail);
    }
}
