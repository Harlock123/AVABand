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
        TestKit.Give(vm);
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
        Assert.StartsWith("Hit points 19 of 19. In the town", vm.LastMessage);
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

    /// <summary>A shop, when on: where you are, what's for sale with each note, the service on offer, each row as the arrows reach it.</summary>
    [AvaloniaFact]
    public void WhenOn_AShopIsSaid_RowsWithTheirNotes_AndItsService()
    {
        var (window, vm) = Open(on: true);
        var game = vm.Game;
        // Something not yet known, so the Alchemist has a service to offer.
        game.Player.Inventory.Add(game.Objects.Create("speed"));
        game.Player.Position = game.Level.AllLocs().Single(l => game.Level.FeatureAt(l).Shop == "alchemist");
        vm.Execute(new EnterStoreCommand());
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsInStore);
        Assert.Contains(vm.StoreTitle, vm.Announcement);
        Assert.Contains("Service on offer: Identify something, press !.", vm.Announcement);
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "StoreServiceButton");
        Assert.Equal("Shop service: Identify something (key !)", ControlAutomationPeer.CreatePeerForElement(button).GetName());
        vm.LeaveStore();

        // The Armoury's rows carry their notes; the arrows say the row they reach.
        game.Player.Position = game.Level.AllLocs().Single(l => game.Level.FeatureAt(l).Shop == "armoury");
        vm.Execute(new EnterStoreCommand());
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        var noted = vm.StoreRows.First(r => r.HasAdvice);
        Assert.EndsWith(". " + noted.Advice, noted.Spoken);
        Assert.Contains(window.GetVisualDescendants().OfType<Grid>(),
            g => ReferenceEquals(g.DataContext, noted) && AutomationProperties.GetName(g) == noted.Spoken);
        vm.HandleAction(InputAction.MoveSouth);
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith(vm.StoreRows[1].Spoken, vm.Announcement);
    }

    /// <summary>The weight and slots line in words, and "Your pack is full." said once as it fills.</summary>
    [AvaloniaFact]
    public void WhenOn_AFullPackIsSaid_Once()
    {
        var (window, vm) = Open(on: true);
        var game = vm.Game;
        var line = window.GetVisualDescendants().OfType<Border>().Single(t => t.Name == "BurdenLine");
        Assert.Contains("pack slots free", ControlAutomationPeer.CreatePeerForElement(line).GetName());
        var kinds = game.Data.Objects.Where(k => k.Base is "food" or "flask" or "scroll" or "potion").Select(k => k.Id).ToList();
        for (var i = 0; i < kinds.Count && game.Player.Inventory.SlotsUsed < game.Player.Inventory.PackSize; i++)
            game.Player.Inventory.Add(game.Objects.Create(kinds[i]));
        var said = new List<string>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Announcement)) said.Add(vm.Announcement); };
        vm.Refresh();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Your pack is full.", vm.Announcement);
        Assert.Contains("your pack is full", ControlAutomationPeer.CreatePeerForElement(line).GetName());
        for (var i = 0; i < 3; i++)
        {
            vm.Execute(new HoldCommand());
            vm.Refresh();
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Single(said, a => a.Contains("Your pack is full.")); // (not again while it stays full)
    }

    /// <summary>Describing the surroundings says which monsters your knowledge of says could kill you.</summary>
    [AvaloniaFact]
    public void WhenOn_TheSurroundingsSayWhatIsADangerToYou()
    {
        var (_, vm) = Open(on: true);
        var game = vm.Game;
        var dragon = game.Data.Monster("ancient_white_dragon")!;
        var lore = game.Lore.For(dragon.Id);
        lore.SpellsSeen.Add("BR_COLD");
        lore.TotalKills = 1;
        var at = game.Level.AllLocs().First(l => l.DistanceTo(game.Player.Position) == 3 && game.Level.IsEmptyFloor(l)
            && Angband.Core.Combat.ProjectionPath.Projectable(game.Level, game.Player.Position, l, 20));
        new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, dragon, at, asleep: true);
        game.UpdateView();
        vm.DescribeSurroundings();
        Dispatcher.UIThread.RunJobs();
        Assert.Matches(@"ancient white dragon[^;.]*\(could kill you\), \d+ (north|south|east|west)", vm.Announcement);
    }
}
