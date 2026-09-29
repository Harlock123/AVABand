using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>The message area's three lines, and damage noted on the messages and in the history.</summary>
public class MessageAreaUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42, "warrior");
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    [AvaloniaFact]
    public void A_monsters_hit_shows_its_damage_on_the_line_and_in_the_history()
    {
        var (window, vm) = Open();
        var game = vm.Game;
        game.Player.Hp = game.Player.MaxHp = 100_000;
        var at = game.Level.AllLocs().First(l => l.ChebyshevTo(game.Player.Position) == 1 && game.Level.IsEmptyFloor(l));
        var orc = new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster("cave_orc")!, at, asleep: false)!;
        game.Scheduler.Add(orc);
        game.UpdateView();
        for (var i = 0; i < 30 && game.Player.Hp == game.Player.MaxHp; i++) vm.Execute(new HoldCommand());

        var hit = vm.History.Last(m => m.Text.Contains("hits you"));
        Assert.Matches(@"hits you \(\d+\)\.$", hit.Text);
        Assert.Contains(vm.Messages, m => m == hit.Text);
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "message-area");
    }

    [AvaloniaFact]
    public void Three_lines_show_the_two_before_the_newest_and_one_line_when_asked()
    {
        var (_, vm) = Open();
        foreach (var n in new[] { "one", "two", "three" }) vm.AddMessage(n);
        var shown = vm.EarlierMessages.Select(m => m.Text).Append(vm.LastMessage).ToList();
        Assert.Equal(["one", "two", "three"], shown);

        vm.SetOption(DisplayOptions.ThreeMessageLines, false);
        Assert.Empty(vm.EarlierMessages);
    }
}
