using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Persistence;
using Angband.Data;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>Moving a character to another computer: exporting its save, and importing one copied over.</summary>
public sealed class SaveTransferTests : IDisposable
{
    // Two computers' save folders, and a USB stick between them (all temporary).
    private readonly string _root = Path.Combine(Path.GetTempPath(), "avaband-transfer-" + Guid.NewGuid());
    private string Here => Path.Combine(_root, "here");
    private string There => Path.Combine(_root, "there");
    private string Stick => Path.Combine(_root, "stick");
    private DateTime _now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    public SaveTransferTests() => Directory.CreateDirectory(Stick);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static readonly Angband.Core.Definitions.GameData Data = DataLoader.Load(DataLoader.DefaultDataDirectory);

    private SaveStore Store(string dir) => new(dir) { UtcNow = () => _now };

    private static GameSession Game(ulong seed = 42, long gold = 500)
    {
        var game = GameSession.NewGame(Data, seed);
        game.Player.Gold = gold;
        return game;
    }

    [Fact]
    public void A_save_exported_here_imports_there_and_plays_on()
    {
        var here = Store(Here);
        var game = Game(gold: 1234);
        here.Save(game);
        var dialog = new LoadGameViewModel(here, Data);
        var stick = Path.Combine(Stick, "my-hero.avasave");                       // (renamed on the way: it doesn't matter)
        dialog.ExportTo(stick);
        Assert.True(File.Exists(stick));
        Assert.Contains("Copied", dialog.Status);
        Assert.True(File.Exists(here.PathFor(game)));                             // a copy: the save stays

        var there = Store(There);
        var other = new LoadGameViewModel(there, Data);
        Assert.True(other.IsEmpty);
        other.BeginImport(stick);
        Assert.Equal("", other.Error);
        Assert.False(other.ConfirmingImport);
        var entry = Assert.Single(other.Saves);
        Assert.Equal(other.Selected, entry);
        Assert.Equal(Path.GetFileName(here.PathFor(game)), Path.GetFileName(entry.Path)); // its own name again
        Assert.StartsWith("Imported", other.Status);
        Assert.Equal(1234, there.Load(Data, entry.Path).Player.Gold);
    }

    [Fact]
    public void Importing_over_the_same_character_asks_and_the_one_replaced_is_kept_as_an_earlier_save()
    {
        var there = Store(There);
        var older = Game(gold: 100);
        there.Save(older);

        _now += TimeSpan.FromHours(2);
        var here = Store(Here);
        var newer = Game(gold: 9000);                                              // the same character (name and seed), played on
        here.Save(newer);
        var stick = Path.Combine(Stick, "hero.avasave");
        here.Export(here.PathFor(newer), stick);

        var dialog = new LoadGameViewModel(there, Data);
        dialog.BeginImport(stick);
        Assert.True(dialog.ConfirmingImport);
        Assert.Contains("is already here", dialog.ImportQuestion);
        Assert.Contains("is newer", dialog.ImportQuestion);
        Assert.Equal(100, there.Load(Data, there.PathFor(older)).Player.Gold); // nothing changed yet

        // "Don't": as it was.
        dialog.CancelImportCommand.Execute(null);
        Assert.False(dialog.ConfirmingImport);
        Assert.Equal(100, there.Load(Data, there.PathFor(older)).Player.Gold);

        // "Replace": the import is the save, and the one it replaced can be restored.
        dialog.BeginImport(stick);
        dialog.ReplaceOnImportCommand.Execute(null);
        Assert.False(dialog.ConfirmingImport);
        Assert.Equal(9000, there.Load(Data, there.PathFor(older)).Player.Gold);
        var kept = Assert.Single(there.Backups(there.PathFor(older)));
        Assert.Equal(100, there.Load(Data, kept.Path).Player.Gold);
        Assert.Single(dialog.Saves);
    }

    [Fact]
    public void What_cant_be_imported_says_why_and_changes_nothing()
    {
        var there = Store(There);
        var dialog = new LoadGameViewModel(there, Data);

        var junk = Path.Combine(Stick, "junk.avasave");
        File.WriteAllBytes(junk, [1, 2, 3, 4, 5]);
        dialog.BeginImport(junk);
        Assert.Contains("Can't import junk.avasave", dialog.Error);
        Assert.Contains("damaged", dialog.Error);

        var dead = Game();
        dead.Player.IsDead = true;
        var grave = Path.Combine(Stick, "dead.avasave");
        SaveGame.SaveToFile(dead, grave);
        dialog.BeginImport(grave);
        Assert.Contains("has died", dialog.Error);

        dialog.BeginImport(Path.Combine(Stick, "missing.avasave"));
        Assert.Contains("Can't import", dialog.Error);
        Assert.True(dialog.IsEmpty);
        Assert.False(Directory.Exists(There) && Directory.EnumerateFiles(There, "*" + SaveGame.Extension).Any());
    }

    [Fact]
    public void Importing_the_save_already_in_the_folder_just_selects_it()
    {
        var here = Store(Here);
        var game = Game();
        here.Save(game);
        var dialog = new LoadGameViewModel(here, Data);
        dialog.BeginImport(here.PathFor(game));
        Assert.Equal("", dialog.Error);
        Assert.False(dialog.ConfirmingImport);
        Assert.Contains("already in your saves", dialog.Status);
        Assert.Empty(here.Backups(here.PathFor(game)));
    }

    [AvaloniaFact]
    public void Export_this_character_saves_first_then_copies()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(Data);
        vm.StartGame(42);
        var saves = Store(Here);
        vm.UseSaves(saves, resume: false);
        vm.Game.Player.Gold = 4321;                                                // (since the last save)
        var stick = Path.Combine(Stick, "out.avasave");
        Assert.True(vm.ExportCharacterTo(stick));
        Assert.Contains("Copied", vm.LastMessage);
        Assert.Equal(4321, saves.Load(Data, stick).Player.Gold);
        Assert.Equal(4321, saves.Load(Data, saves.PathFor(vm.Game)).Player.Gold);  // and saved here too
        vm.ClosedCleanly();
    }

    [AvaloniaFact]
    public void The_title_offers_an_import_and_the_dialog_shows_its_buttons_and_question()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(Data);
        vm.StartGame(42);
        vm.UseSaves(Store(Here), resume: false);
        vm.ShowTitle();
        Assert.Contains(vm.TitleChoices, c => c.Label == "Import a save from another computer");
        vm.ClosedCleanly();

        // A character here, and a newer copy of it to import: the dialog asks.
        var here = Store(Here);
        here.Save(Game(gold: 1));
        _now += TimeSpan.FromHours(1);
        var elsewhere = Store(There);
        var newer = Game(gold: 2);
        elsewhere.Save(newer);
        var dialog = new LoadGameViewModel(here, Data);
        var window = new LoadGameWindow { DataContext = dialog };
        window.Show();
        Assert.True(window.FindControl<global::Avalonia.Controls.Button>("ImportButton")!.IsVisible);
        Assert.True(window.FindControl<global::Avalonia.Controls.Button>("ExportButton")!.IsEffectivelyEnabled);
        Assert.False(window.FindControl<global::Avalonia.Controls.Border>("ImportConfirm")!.IsVisible);
        dialog.BeginImport(elsewhere.PathFor(newer));
        Assert.True(window.FindControl<global::Avalonia.Controls.Border>("ImportConfirm")!.IsVisible);
        TileRenderingTests.Save(window, "import-replace");
        window.Close();
    }
}
