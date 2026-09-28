using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Records;
using Angband.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

/// <summary>The game-over menu: after a death, and on starting with no living character.</summary>
public sealed class GameOverUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "avaband-gameover-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private (MainWindow Window, MainWindowViewModel Vm) Open(AppSettings? settings = null)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings ?? new AppSettings(), save: null);
        vm.StartGame(42, "warrior");
        vm.UseRecords(new RecordStore(Path.Combine(_dir, "records")));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    private static (GameOverWindow Window, GameOverMenuViewModel Menu) MenuOf(MainWindow window)
    {
        var dialog = window.OwnedWindows.OfType<GameOverWindow>().Last();
        return (dialog, (GameOverMenuViewModel)dialog.DataContext!);
    }

    [AvaloniaFact]
    public void Dying_OffersPlayAgain_ANewCharacter_TheScores_TheSheet_OrExit()
    {
        var (window, vm) = Open();
        vm.Game.TakeHit(100_000, "a Cave orc");
        var (dialog, menu) = MenuOf(window);
        Assert.Equal("You have died", menu.Title);
        Assert.StartsWith("Play again as ", menu.Choices[0].Label);
        Assert.Equal(["Create a new character...", "View the high scores", "Look at the character sheet", "Exit"],
            menu.Choices.Skip(1).Select(c => c.Label));
        Assert.Equal(["a", "b", "c", "d", "e"], menu.Choices.Select(c => c.Letter));

        // a (by its letter): the same character again, alive, in town; the menu closes.
        var dead = vm.Game;
        dialog.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        Assert.False(dialog.IsVisible);
        Assert.NotSame(dead, vm.Game);
        Assert.False(vm.Game.Player.IsDead);
        Assert.Equal(0, vm.Game.Player.Depth);
    }

    [AvaloniaFact]
    public void ANewCharacter_OpensCreation_AndEscapeLeavesTheDeadWhereTheyLie()
    {
        var (window, vm) = Open();
        var asked = false;
        vm.NewCharacterRequested += () => asked = true;
        vm.Game.TakeHit(100_000, "a jackal");
        var (_, menu) = MenuOf(window);
        menu.ChooseLetter('b');
        Assert.True(asked);

        vm.Game.TakeHit(1, "a jackal"); // (already dead: nothing more happens)
        var (window2, vm2) = Open();
        vm2.Game.TakeHit(100_000, "a jackal");
        var (dialog2, _) = MenuOf(window2);
        dialog2.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(dialog2.IsVisible);
        Assert.True(vm2.Game.Player.IsDead);
        vm2.Execute(new Angband.Core.Game.HoldCommand()); // (the screen refreshes after any command)
        Assert.StartsWith("*** DEAD ***", vm2.StatusText); // Ctrl+N still there
    }

    [AvaloniaFact]
    public void StartingWithNoLivingCharacter_SaysHowTheLastEnded_AndAsksWhatNext()
    {
        var settings = new AppSettings { LastCharacter = new SavedCharacter { Name = "Borin", Race = "dwarf", Class = "warrior" } };
        var records = new RecordStore(Path.Combine(_dir, "records"));
        var board = new ScoreBoard();
        board.Add(new ScoreEntry { Name = "Borin", Race = "Dwarf", Class = "Warrior", KilledBy = "Grip, Farmer Maggot's Dog", Depth = 2,
            DateUtc = DateTime.UtcNow });
        board.Save(records.ScoresPath);

        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null);
        vm.UseRecords(records);
        vm.UseSaves(new SaveStore(Path.Combine(_dir, "saves")), resume: true); // nothing alive to continue
        Assert.True(vm.ShouldOfferStartMenu);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        vm.ShowStartMenu();
        var (_, menu) = MenuOf(window);
        Assert.Equal("Welcome back", menu.Title);
        Assert.Equal("Your last adventure ended: Borin the Dwarf Warrior was killed by Grip, Farmer Maggot's Dog at 100 ft (level 2).",
            Assert.Single(menu.Lines));
        Assert.Equal("Play again as Borin the Dwarf Warrior", menu.Choices[0].Label);
        Assert.False(vm.ShouldOfferStartMenu);
    }

    [AvaloniaFact]
    public void CtrlX_SavesTheCharacter_AndClosesTheGame()
    {
        var (window, vm) = Open();
        var saves = new SaveStore(Path.Combine(_dir, "saves"));
        vm.UseSaves(saves, resume: false);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Control);
        Assert.True(closed);
        var saved = Assert.Single(saves.List());
        Assert.False(saved.Summary.IsDead);
    }
}
