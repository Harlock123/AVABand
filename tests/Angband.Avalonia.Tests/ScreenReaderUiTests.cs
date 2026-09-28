using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Data;
using Angband.Input;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

/// <summary>Screen reader support: off by default; when on, what is said is announced.</summary>
public class ScreenReaderUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open(bool on)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings();
        settings.Options[DisplayOptions.Hints] = false;
        if (on) settings.Options[DisplayOptions.ScreenReader] = true;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    [AvaloniaFact]
    public void ItIsOffByDefault_AndThenNothingIsAnnounced()
    {
        Assert.False(DisplayOptions.All.Single(o => o.Id == DisplayOptions.ScreenReader).Default);
        var (window, vm) = Open(on: false);
        vm.HandleAction(InputAction.Quaff);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", vm.Announcement);

        // Describe surroundings still works, on the message line.
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.StartsWith("Hit points 20 of 20. In the town", vm.LastMessage);
    }

    [AvaloniaFact]
    public void WhenOn_MessagesPromptsAndTheSurroundings_AreAnnounced()
    {
        var (window, vm) = Open(on: true);
        var game = vm.Game;

        vm.HandleAction(InputAction.Quaff); // a prompt: its question and every choice
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Quaff which potion?", vm.Announcement);
        Assert.Contains("Cure Light Wounds", vm.Announcement);
        vm.CancelPrompt();

        vm.Execute(new WalkCommand(Direction.North)); // whatever it says is said
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(vm.LastMessage.TrimEnd('​'), vm.Announcement);

        var at = game.Level.AllLocs().First(l => l.DistanceTo(game.Player.Position) == 3 && game.Level.IsEmptyFloor(l)
            && Angband.Core.Combat.ProjectionPath.Projectable(game.Level, game.Player.Position, l, 20));
        new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster("jackal")!, at, asleep: true);
        game.UpdateView();
        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Control | RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Hit points", vm.Announcement);
        Assert.Contains("jackal", vm.Announcement);
        Assert.Matches(@"jackal[^;.]*, \d+ (north|south|east|west)", vm.Announcement); // where it is, in words
        Assert.Equal(vm.BuildMapSummary(), vm.MapSummary);

        // What a screen reader sees: the live region, with the words, set to be read at once.
        var live = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "LiveRegion");
        Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(live));
        Assert.Equal(vm.Announcement, ControlAutomationPeer.CreatePeerForElement(live).GetName());
        var map = window.GetVisualDescendants().OfType<Angband.Avalonia.Controls.MapView>().First(m => m.Name == "Map");
        Assert.Contains("jackal", ControlAutomationPeer.CreatePeerForElement(map).GetName());
    }
}
