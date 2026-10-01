using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Persistence;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>Every game is recorded; Game → Watch a replay… plays one back in the window.</summary>
public sealed class ReplayUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "avaband-replayui-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [AvaloniaFact]
    public void AGame_IsRecorded_AndWatchingItPlaysItBack_ThenEscReturnsToTheCharacter()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings();
        settings.Options[DisplayOptions.Hints] = false;
        settings.Options[DisplayOptions.Scenes] = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null)
        {
            ReplayDirectory = Path.Combine(_dir, "replays"),
        };
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.UseSaves(new SaveStore(Path.Combine(_dir, "saves")), resume: false);
        vm.StartGame(21, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();

        var game = vm.Game;
        vm.Execute(new WalkCommand(Direction.East));
        vm.HandleAction(InputAction.TargetClosest); // a choice outside a command
        for (var i = 0; i < 20; i++) vm.Execute(new WalkCommand(i % 2 == 0 ? Direction.South : Direction.West));
        vm.Execute(new HoldCommand());
        var turn = game.GameTurn;
        var at = game.Player.Position;
        Assert.True(vm.TrySave());
        // (The character the window started with was saved, and recorded, when this one began.)
        var replays = vm.ListReplays();
        var (path, title) = Assert.Single(replays, r => Path.GetFileName(r.Path).Contains($"-{game.Seed:x}-", StringComparison.Ordinal));
        Assert.Contains("Human Warrior", title);

        vm.WatchReplay();
        Assert.Equal("Open a replay file…", vm.MenuLabels[^1]);
        vm.PromptKey((char)('a' + vm.MenuLabels.ToList().IndexOf(title)));
        Assert.True(vm.IsReplaying);
        Assert.True(vm.Game.IsReplay);
        Assert.StartsWith("Replay: ", vm.ReplayStatus);
        // Paused at once: playing, its clock steps it on in real time, and a slow machine could reach the end.
        vm.HandleAction(InputAction.Confirm);
        Assert.True(vm.ReplayPaused);
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "replay");
        vm.HandleAction(InputAction.MoveNorth); // faster
        Assert.Equal(8, vm.ReplaySpeed);
        for (var i = 0; i < 500 && !vm.LastMessage.StartsWith("The replay has ended", StringComparison.Ordinal); i++) vm.StepReplay();
        Assert.StartsWith("The replay has ended: it played out exactly as recorded.", vm.LastMessage);
        Assert.Equal(turn, vm.Game.GameTurn);
        Assert.Equal(at, vm.Game.Player.Position);
        Assert.False(vm.TrySave()); // a replay is never saved

        vm.HandleAction(InputAction.Cancel); // Esc: back to the character
        Assert.False(vm.IsReplaying);
        Assert.False(vm.Game.IsReplay);
        Assert.Equal(turn, vm.Game.GameTurn);
        Assert.Contains(vm.ListReplays(), r => r.Path == path);
    }
}
