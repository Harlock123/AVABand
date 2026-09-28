using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Records;
using Angband.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

public sealed class RecordsUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "avaband-records-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private (MainWindow Window, MainWindowViewModel Vm, RecordStore Records) Open(ulong seed = 42)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(seed);
        var records = new RecordStore(_dir);
        vm.UseRecords(records);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm, records);
    }

    [AvaloniaFact]
    public void C_OpensTheCharacterSheet_WhichSavesADump()
    {
        var (window, vm, records) = Open();
        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Shift);

        var sheetWindow = Assert.IsType<CharacterSheetWindow>(window.OwnedWindows.Last());
        var sheet = (CharacterSheetViewModel)sheetWindow.DataContext!;
        Assert.Contains($"Name   {vm.Game.Player.Name}", sheet.Text);
        TileRenderingTests.Save(sheetWindow, "character-sheet");

        sheet.SaveCommand.Execute(null);
        var file = Assert.Single(Directory.GetFiles(records.DumpDirectory));
        Assert.Contains(file, sheet.Status);
        Assert.Contains("[Equipment]", File.ReadAllText(file));
    }

    [AvaloniaFact]
    public void Death_RecordsTheScore_WritesADump_AndShowsTheTable()
    {
        var (window, vm, records) = Open();
        vm.Game.Player.Experience = 500;
        vm.Game.Player.MaxDepth = 3;
        vm.Game.TakeHit(10_000, "a Cave orc");

        var entry = Assert.Single(records.LoadScores().Entries);
        Assert.Equal(800, entry.Points);
        Assert.Equal("a Cave orc", entry.KilledBy);
        Assert.Contains("Killed by a Cave orc", File.ReadAllText(Assert.Single(Directory.GetFiles(records.DumpDirectory))));

        // Death opens the game-over menu; its high-scores choice shows the table.
        var menuWindow = Assert.IsType<GameOverWindow>(window.OwnedWindows.Last());
        var menu = (GameOverMenuViewModel)menuWindow.DataContext!;
        Assert.Equal("You have died", menu.Title);
        Assert.Contains(menu.Lines, l => l.Contains("was killed by a Cave orc"));
        Assert.Contains(menu.Lines, l => l.Contains("#1 on the high-score table"));
        menu.Choose(menu.Choices.Single(c => c.Label == "View the high scores"));
        var scoresWindow = Assert.IsType<HighScoresWindow>(window.OwnedWindows.Last());
        var scores = (HighScoresViewModel)scoresWindow.DataContext!;
        var row = Assert.Single(scores.Rows);
        Assert.True(row.IsCurrent);
        Assert.StartsWith("Killed by a Cave orc", row.Fate);
        Assert.Contains(vm.Messages, m => m.Contains("#1 on the high-score table"));
    }

    [AvaloniaFact]
    public void CtrlH_ShowsTheLivingCharacterAmongTheScores()
    {
        var (window, vm, records) = Open();
        var board = new ScoreBoard();
        foreach (var points in new[] { 5000L, 50L })
            board.Add(new ScoreEntry { Name = $"Old{points}", Race = "Dwarf", Class = "Priest", Points = points, KilledBy = "a jackal", MaxDepth = 2, Depth = 2 });
        board.Save(records.ScoresPath);
        vm.Game.Player.Experience = 100;

        window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
        var scoresWindow = Assert.IsType<HighScoresWindow>(window.OwnedWindows.Last());
        var scores = (HighScoresViewModel)scoresWindow.DataContext!;
        Assert.Equal(["Old5000", vm.Game.Player.Name, "Old50"], scores.Rows.Select(r => r.Entry.Name));
        Assert.Equal([1, 2, 3], scores.Rows.Select(r => r.Rank));
        Assert.Equal("Alive (current game)", scores.Rows[1].Fate);
        Assert.Equal(2, records.LoadScores().Entries.Count); // looking doesn't add the living character
        TileRenderingTests.Save(scoresWindow, "high-scores");
    }

    [AvaloniaFact]
    public void Q_RetiresAWinner_AfterAskingFirst()
    {
        var (window, vm, records) = Open();
        window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.Shift);
        Assert.Equal("You can retire once you have defeated Morgoth.", vm.LastMessage);

        vm.Game.Player.IsWinner = true;
        window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.Shift);
        Assert.True(vm.IsConfirming);
        window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);

        Assert.True(vm.Game.Player.IsDead);
        Assert.StartsWith("*** RETIRED VICTORIOUS ***", vm.StatusText);
        var entry = Assert.Single(records.LoadScores().Entries);
        Assert.True(entry.Won);
        var menu = (GameOverMenuViewModel)window.OwnedWindows.OfType<GameOverWindow>().Last().DataContext!;
        Assert.Equal("Your adventure is over", menu.Title);
        menu.Choose(menu.Choices.Single(c => c.Label == "View the high scores"));
        var scores = (HighScoresViewModel)window.OwnedWindows.OfType<HighScoresWindow>().Last().DataContext!;
        Assert.Equal("Retired victorious", scores.Rows.Single().Fate);
    }
}
