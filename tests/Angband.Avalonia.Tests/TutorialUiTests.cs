using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>The tutorial: guided step by step, never saved, finished by the stairs.</summary>
public sealed class TutorialUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "avaband-tutorial-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [AvaloniaFact]
    public void TheTutorial_GuidesEachStep_IsNeverSaved_AndTheStairsFinishIt()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        var saves = new SaveStore(_dir);
        vm.UseSaves(saves, resume: false);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        GameOverMenuViewModel? menu = null;
        vm.GameOverMenuRequested += m => menu = m;

        vm.StartTutorialCommand.Execute(null);
        var game = vm.Game;
        Assert.True(game.IsTutorial);
        Assert.StartsWith("Welcome to the tutorial!", vm.HintText);
        Assert.Contains("pick it up with g", vm.HintText); // the keys you have
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "tutorial");

        // Dismissed, the step's text stays away until the next step.
        vm.DismissHint();
        vm.Execute(new HoldCommand());
        Assert.False(vm.HasHint);
        foreach (var (at, item) in game.Level.Objects.All.ToList()) game.Level.Objects.Remove(at, item);
        vm.Execute(new HoldCommand());
        Assert.StartsWith("Now head east. Walk into the door", vm.HintText);
        for (var i = 0; i < 12; i++) vm.Execute(new HoldCommand());
        Assert.StartsWith("Now head east.", vm.HintText); // no countdown in the tutorial

        // Nothing is saved: not by hand, not on its own.
        Assert.False(vm.TrySave());
        Assert.DoesNotContain(saves.List(), e => e.Summary.Name == "Pupil"); // (the warrior in play was saved first)
        Assert.Single(saves.List());

        // The stairs finish it.
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        game.Player.Position = game.Level.AllLocs().Single(p => game.Level.Has(p, TerrainFlags.DownStair));
        vm.Execute(new TakeStairsCommand(Down: true));
        Assert.Equal(1, game.Player.Depth); // no second level
        Assert.NotNull(menu);
        Assert.Equal("Tutorial complete", menu.Title);
        Assert.Equal("Play the tutorial again", menu.Choices[0].Label);
        Assert.Contains(menu.Choices, c => c.Label == "Create a new character...");
    }
}
