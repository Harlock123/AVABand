using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>The journey window: depth over time, each depth, the history.</summary>
public class JourneyUiTests
{
    [AvaloniaFact]
    public void TheJourney_ShowsTheLevelsVisited_AndItsWindowOpens()
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
        vm.Game.MarkDebugUsed();
        foreach (var depth in new[] { 1, 3, 2, 5, 8, 6, 12 })
        {
            vm.Execute(new DebugJumpCommand(depth));
            for (var i = 0; i < 30; i++) vm.Execute(new HoldCommand());
        }

        var journey = vm.CreateJourney();
        Assert.Equal(12, journey.MaxDepth);
        Assert.Equal(8, journey.Points.Count - 1); // the town and seven levels (and now)
        Assert.Contains(journey.Rows, r => r.Depth == "600 ft (L12)");
        Assert.Contains("Began the quest", journey.History);
        Assert.StartsWith("7 levels entered, deepest 600 ft", journey.Summary);

        vm.ShowJourney();
        var journeyWindow = Assert.IsType<JourneyWindow>(window.OwnedWindows.Last());
        TileRenderingTests.Save(journeyWindow, "journey");
        journeyWindow.Close();
    }
}
