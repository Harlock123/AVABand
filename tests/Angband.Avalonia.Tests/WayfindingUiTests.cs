using Angband.Avalonia.ViewModels;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Data;
using Angband.Input;
using Angband.Avalonia.Views;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>
/// Finding your way and keeping track: where you've been (Ctrl+B), the run pace, notes written from
/// the History page, and the Upgrades page.
/// </summary>
public class WayfindingUiTests
{
    private static (MainWindowViewModel Vm, AppSettings Settings) Start()
    {
        var settings = new AppSettings();
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        TestKit.Give(vm);
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        return (vm, settings);
    }

    /// <summary>Walks the player east (open ground) a few squares; returns the start.</summary>
    private static Loc WalkEast(MainWindowViewModel vm, int steps)
    {
        TestKit.OpenGround(vm);
        var start = vm.Game.Player.Position;
        for (var i = 0; i < steps; i++) vm.Execute(new WalkCommand(Direction.East));
        return start;
    }

    [AvaloniaFact]
    public void CtrlB_DotsWhereYouveBeen_AndSaysWhereTheTrailBegan()
    {
        var (vm, _) = Start();
        Assert.Equal(InputAction.ShowTrail, InputBindings.Defaults().Keys["Ctrl+B"]);
        vm.HandleAction(InputAction.ShowTrail);
        Assert.Equal("You haven't moved yet on this level.", vm.LastMessage);

        var start = WalkEast(vm, 4);
        Assert.Equal(start + new Loc(4, 0), vm.Game.Player.Position);
        vm.HandleAction(InputAction.ShowTrail);
        Assert.True(vm.Effects.IsActive);
        Assert.Equal(4, vm.Effects.Glyphs.Count);
        Assert.Equal(start, vm.Effects.Glyphs[0].Loc);
        Assert.Equal("Umber", vm.Effects.Glyphs[0].Color);  // the older half dim
        Assert.Equal("Yellow", vm.Effects.Glyphs[^1].Color); // the newer bright
        Assert.Contains("the trail began 4 west of here", vm.LastMessage);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "trail");
        vm.Effects.Advance(MapEffects.TrailMs + 1);
        Assert.False(vm.Effects.IsActive);

        // Only the last forty squares; a new level starts afresh.
        for (var i = 0; i < 50; i++) vm.Execute(new WalkCommand(i % 2 == 0 ? Direction.West : Direction.East));
        Assert.Equal(MainWindowViewModel.TrailLength, vm.Trail.Count);
        vm.Execute(new DebugJumpCommand(1));
        Assert.Empty(vm.Trail);
    }

    [AvaloniaFact]
    public void TheRunPace_IsChosen_AndKept()
    {
        var (vm, settings) = Start();
        Assert.Equal(TimeSpan.FromMilliseconds(MainWindowViewModel.RunStepMs), vm.RunStepDelay);
        Assert.Equal(1, vm.RunPaceIndex); // steady
        vm.RunPaceIndex = 3;
        Assert.Equal(90, settings.RunStepMs);
        Assert.Equal(TimeSpan.FromMilliseconds(90), vm.RunStepDelay);
        Assert.Equal(4, vm.RunPaceChoices.Count);
    }

    [AvaloniaFact]
    public void TheHistoryPage_TakesANote()
    {
        var (vm, _) = Start();
        var knowledge = vm.CreateKnowledge();
        knowledge.NoteText = "left the Ring of Free Action at home";
        knowledge.AddNoteCommand.Execute(null);
        Assert.Equal("", knowledge.NoteText);
        Assert.Contains("-- Note: left the Ring of Free Action at home", knowledge.History);
        Assert.Contains(vm.Game.History, h => h.Text == "-- Note: left the Ring of Free Action at home");

        // An empty note isn't kept, and stays in the box for nothing.
        var lines = vm.Game.History.Count;
        knowledge.NoteText = "";
        knowledge.AddNoteCommand.Execute(null);
        Assert.Equal(lines, vm.Game.History.Count);
    }

    [AvaloniaFact]
    public void TheUpgradesPage_ListsWhatWouldSuitYouBetter()
    {
        var (vm, _) = Start(); // wielding a dagger, in soft leather armour
        var game = vm.Game;
        Assert.Contains("Nothing you carry", vm.CreateKnowledge().Upgrades!.EmptyText);
        Assert.Empty(vm.CreateKnowledge().Upgrades!.Rows);

        void Carry(string kind)
        {
            var item = game.Objects.Create(kind);
            game.Knowledge.LearnKind(item.Kind);
            game.Player.Inventory.Add(item);
        }
        game.Knowledge.LearnRune(Angband.Core.Definitions.RuneIds.ToHit);
        game.Knowledge.LearnRune(Angband.Core.Definitions.RuneIds.ToDam);
        Carry("main_gauche");   // better than the dagger
        Carry("metal_cap");     // for the empty helm slot
        Carry("soft_leather_armour"); // the same as worn: not listed
        var page = vm.CreateKnowledge().Upgrades!;
        Assert.Equal(2, page.Rows.Count);
        Assert.Contains(page.Rows, r => r.Name.Contains("Main Gauche") && r.Note == "better");
        Assert.Contains(page.Rows, r => r.Name.Contains("Metal Cap"));
        page.Selected = page.Rows.First(r => r.Name.Contains("Main Gauche"));
        Assert.StartsWith("Better — vs your Dagger", page.Text);
    }
}
