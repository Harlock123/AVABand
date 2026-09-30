using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Persistence;
using Angband.Data;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>Save backups: earlier saves kept as a character's save is replaced, and restored from the Load dialog.</summary>
public sealed class SaveBackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "avaband-backups-" + Guid.NewGuid());
    private DateTime _now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static readonly Angband.Core.Definitions.GameData Data = DataLoader.Load(DataLoader.DefaultDataDirectory);

    private SaveStore Store() => new(_dir) { UtcNow = () => _now };

    private static GameSession Game(ulong seed = 42) => GameSession.NewGame(Data, seed);

    [Fact]
    public void ReplacingASave_KeepsTheOneBefore_AtMostOneEveryTenMinutes_AndTheNewestFive()
    {
        var saves = Store();
        var game = Game();
        saves.Save(game);                                  // the first save: nothing to keep yet
        Assert.Empty(saves.Backups(saves.PathFor(game)));
        game.Player.Gold = 111;
        saves.Save(game);                                  // replaces it: the first is kept
        Assert.Single(saves.Backups(saves.PathFor(game)));
        _now += TimeSpan.FromMinutes(3);
        saves.Save(game);                                  // too soon for another
        Assert.Single(saves.Backups(saves.PathFor(game)));

        for (var i = 0; i < 8; i++)
        {
            _now += SaveStore.BackupInterval + TimeSpan.FromSeconds(1);
            game.Player.Gold = 1000 + i;
            saves.Save(game);
        }
        var backups = saves.Backups(saves.PathFor(game));
        Assert.Equal(SaveStore.BackupsKept, backups.Count);
        Assert.True(backups.Zip(backups.Skip(1)).All(p => p.First.Path.CompareTo(p.Second.Path) > 0)); // newest first
        Assert.Single(saves.List());                        // backups aren't characters of their own
    }

    [Fact]
    public void ADamagedSave_IsListedFromItsNewestBackup()
    {
        var saves = Store();
        var game = Game();
        saves.Save(game);
        _now += TimeSpan.FromHours(1);
        saves.Save(game);
        File.WriteAllText(saves.PathFor(game), "not a save any more");

        var entry = Assert.Single(saves.List());
        Assert.True(entry.MainDamaged);
        Assert.True(entry.IsBackup);
        Assert.Contains("save damaged", entry.Title);

        var restored = saves.Restore(entry);
        Assert.Equal(saves.PathFor(game), restored.Path);
        Assert.Equal(game.Seed, SaveGame.LoadFromFile(Data, restored.Path).Seed);
        Assert.False(Assert.Single(saves.List()).MainDamaged);
    }

    [Fact]
    public void DeletingACharacter_OrItsDeath_TakesItsBackupsToo()
    {
        var saves = Store();
        var game = Game();
        saves.Save(game);
        _now += TimeSpan.FromHours(1);
        saves.Save(game);
        Assert.NotEmpty(saves.Backups(saves.PathFor(game)));
        saves.DeleteFor(game);                              // permadeath: no way back
        Assert.Empty(saves.List());
        Assert.Empty(saves.Backups(saves.PathFor(game)));
    }

    /// <summary>The Load dialog lists a character's earlier saves; restoring one loads it, notes it in the history, and keeps the save it replaced.</summary>
    [AvaloniaFact]
    public void TheLoadDialog_RestoresAnEarlierSave()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42);
        var saves = Store();
        vm.UseSaves(saves, resume: false);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();

        vm.Game.Player.Gold = 500;
        saves.Save(vm.Game);
        _now += TimeSpan.FromHours(1);
        vm.Game.Player.Gold = 9999;                        // later: rich
        saves.Save(vm.Game);

        var dialog = vm.CreateLoadGame();
        Assert.True(dialog.HasBackups);
        var backup = Assert.Single(dialog.Backups);
        Assert.Contains("level 1", backup.BackupTitle);
        Assert.False(dialog.RestoreCommand.CanExecute(null));
        dialog.SelectedBackup = backup;
        var loaded = false;
        dialog.Loaded += () => loaded = true;
        dialog.RestoreCommand.Execute(null);

        Assert.True(loaded);
        Assert.Equal(500, vm.Game.Player.Gold);            // back as it was
        Assert.StartsWith("Restored from an earlier save", vm.LastMessage);
        Assert.Contains(vm.Game.History, h => h.Text.StartsWith("Restored from an earlier save"));
        Assert.Contains(saves.Backups(saves.PathFor(vm.Game)), b => SaveGame.LoadFromFile(Data, b.Path).Player.Gold == 9999);
    }

    [AvaloniaFact]
    public void TheLoadDialog_ShowsEarlierSaves()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42);
        var saves = Store();
        vm.UseSaves(saves, resume: false);
        for (var i = 0; i < 3; i++)
        {
            saves.Save(vm.Game);
            _now += TimeSpan.FromMinutes(30);
        }
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var dialog = window.OpenLoadGame();
        var model = (LoadGameViewModel)dialog.DataContext!;
        Assert.Equal(2, model.Backups.Count);
        Assert.Equal("Earlier saves of this character (2)", model.BackupsHeader);
        dialog.FindControl<Expander>("BackupsExpander")!.IsExpanded = true;
        dialog.UpdateLayout();
        TileRenderingTests.Save(dialog, "load-backups");
    }
}
