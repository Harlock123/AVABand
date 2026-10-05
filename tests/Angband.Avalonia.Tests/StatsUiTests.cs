using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Data;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>The statistics page (Game → Statistics…).</summary>
public class StatsUiTests
{
    [AvaloniaFact]
    public void TheStatisticsPage_ShowsTheTallies_AndTheKills()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        vm.Game.CharacterKills["jackal"] = 7;
        vm.Game.CharacterKills["cave_spider"] = 12;
        vm.Game.Stats.DamageDealt = 1234;
        vm.ShowStats();
        var page = Assert.IsType<StatsWindow>(window.OwnedWindows.Last());
        var stats = Assert.IsType<StatsViewModel>(page.DataContext);
        Assert.Contains(stats.Rows, r => r.What == "Damage dealt" && r.Value == "1,234");
        Assert.Contains(stats.Rows, r => r.What == "Monsters killed" && r.Value == "19");
        Assert.Equal(("cave spider", "12"), (stats.Kills[0].What, stats.Kills[0].Value));
        TileRenderingTests.Save(page, "statistics");
    }
}
