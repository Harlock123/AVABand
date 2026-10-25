using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>AVABand's ghosts in the app: a death keeps its gear, the next character meets its ghost, and laying it to rest marks the stone.</summary>
public sealed class GhostUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "avaband-ghosts-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [AvaloniaFact]
    public void AFallenCharacter_WalksAsAGhost_ForTheNext_UntilLaidToRest_ButNotInAReplay()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.StartGame(42, "warrior");
        var records = new RecordStore(Path.Combine(_dir, "records"));
        vm.UseRecords(records);
        vm.ReplayDirectory = Path.Combine(_dir, "replays");

        // Bramble falls at 150 ft, wearing their kit (a debug jump there, its mark taken off again).
        vm.StartGame(77, "warrior");
        Assert.Empty(vm.Game.Ghosts.Fallen);                          // (no one has fallen yet)
        vm.Game.Player.Name = "Bramble";
        vm.Game.MarkDebugUsed();
        vm.Execute(new DebugJumpCommand(3));
        vm.Game.ToggleDebugMark();
        vm.SkipScenes();
        vm.Game.TakeHit(100_000, "a cave spider");
        var stone = Assert.Single(records.LoadGraveyard().Fallen);
        Assert.Equal(3, stone.Depth);
        Assert.NotEmpty(stone.Equipment!);

        // Bramble's own game, played back, has no ghosts: a replay is never handed the graveyard.
        var replay = Assert.Single(vm.ListReplays());
        Assert.True(vm.PlayReplay(replay.Path));
        Assert.True(vm.Game.IsReplay);
        Assert.Empty(vm.Game.Ghosts.Fallen);
        vm.StopReplay();

        // The next character may meet Bramble's ghost at 150 ft.
        vm.StartGame(78, "warrior");
        var game = vm.Game;
        Assert.Equal("Bramble", Assert.Single(game.Ghosts.Fallen).Name);
        Angband.Core.Monsters.Monster? ghost = null;
        game.MarkDebugUsed();
        for (var i = 0; i < 80 && ghost is null; i++)
        {
            vm.Execute(new DebugJumpCommand(3));
            vm.SkipScenes();
            ghost = game.Level.Monsters.All.FirstOrDefault(m => game.GhostOf(m) is not null);
        }
        game.ToggleDebugMark();
        Assert.NotNull(ghost);
        Assert.Equal("The Ghost of Bramble", ghost.Race.Name);
        game.ProjectileHitsMonster(ghost, "test", null, 1_000_000);

        // Laid to rest: the stone says by whom, and no one meets it again.
        stone = Assert.Single(records.LoadGraveyard().Fallen);
        Assert.Equal($"{game.Player.Name} the Human Warrior", stone.LaidToRestBy);
        Assert.NotNull(stone.LaidToRestUtc);
        Assert.Contains($"Their ghost was laid to rest by {game.Player.Name} the Human Warrior", vm.CreateGraveyard().Story);
        vm.StartGame(79, "warrior");
        Assert.Empty(vm.Game.Ghosts.Fallen);
    }
}
