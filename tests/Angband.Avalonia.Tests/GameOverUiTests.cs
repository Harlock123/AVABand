using Angband.Core.Game;
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

    [AvaloniaFact]
    public void TheGame_SavesItself_EveryFiveMinutesOfPlay()
    {
        var (_, vm) = Open();
        var saves = new SaveStore(Path.Combine(_dir, "autosaves"));
        vm.UseSaves(saves, resume: false);
        var now = new DateTime(2025, 1, 1, 12, 0, 0);
        vm.Clock = () => now;

        vm.Execute(new Angband.Core.Game.HoldCommand());   // the clock starts
        now = now.AddMinutes(4);
        vm.Execute(new Angband.Core.Game.HoldCommand());
        Assert.Empty(saves.List());
        now = now.AddMinutes(2);
        vm.Execute(new Angband.Core.Game.HoldCommand());   // six minutes: saved
        Assert.Single(saves.List());

        // Off, it leaves saving to the level changes and exit.
        var (_, quiet) = Open();
        var none = new SaveStore(Path.Combine(_dir, "none"));
        quiet.UseSaves(none, resume: false);
        quiet.SetOption(DisplayOptions.Autosave, false);
        var t = new DateTime(2025, 1, 1, 12, 0, 0);
        quiet.Clock = () => t;
        quiet.Execute(new Angband.Core.Game.HoldCommand());
        t = t.AddHours(1);
        quiet.Execute(new Angband.Core.Game.HoldCommand());
        Assert.Empty(none.List());
    }

    /// <summary>A death is laid in the graveyard, with its dump; the graveyard window shows its stone and its story.</summary>
    [AvaloniaFact]
    public void ADeath_IsBuried_AndItsStoneRead_InTheGraveyard()
    {
        var (window, vm) = Open();
        vm.Game.Player.Name = "Bramble";
        vm.Game.TakeHit(100_000, "a Cave orc");
        var records = new RecordStore(Path.Combine(_dir, "records"));
        var fallen = Assert.Single(records.LoadGraveyard().Fallen);
        Assert.Equal("Bramble", fallen.Name);
        Assert.Equal("a Cave orc", fallen.KilledBy);
        Assert.True(File.Exists(fallen.DumpPath));

        var yard = vm.CreateGraveyard();
        Assert.Equal("One adventurer rests here.", yard.Summary);
        var stone = Assert.Single(yard.Stones);
        Assert.Equal("Slain by a Cave orc in the town", stone.End);
        Assert.Contains("Bramble", yard.Story);
        Assert.Contains("Killed by a Cave orc", yard.Story);              // the dump follows the stone's words
        Assert.True(yard.Story.Length > 400);

        var graveyard = new GraveyardWindow { DataContext = yard };
        DialogFit.Show(graveyard, window);
        TileRenderingTests.Save(graveyard, "graveyard");
        vm.ShowTitle();
        Assert.Contains(vm.TitleChoices, c => c.Label == "The graveyard");
    }

    /// <summary>A headstone keeps its game's replay: "Watch their last moments" plays it from the level they fell on.</summary>
    [AvaloniaFact]
    public void TheGraveyard_Replays_TheirLastMoments()
    {
        var (window, vm) = Open();
        vm.ReplayDirectory = Path.Combine(_dir, "replays");
        vm.StartGame(77, "warrior");                        // (recorded from the start)
        var game = vm.Game;
        // (Nothing set by hand before the fall — the replay must play the same game — and only a few turns
        // in town: a townsperson's blows, few as they are, add up.)
        for (var i = 0; i < 5; i++) vm.Execute(new HoldCommand());
        game.MarkDebugUsed();                               // (a debug jump: it would bar the grave, so take it off again)
        vm.Execute(new DebugJumpCommand(3));
        game.ToggleDebugMark();
        vm.SkipScenes();
        for (var i = 0; i < 20; i++) vm.Execute(new HoldCommand());
        game.Player.Hp = 1;
        game.TakeHit(100_000, "a cave spider");

        var yard = vm.CreateGraveyard();
        var stone = yard.Stones.First();
        Assert.True(stone.HasReplay);
        Assert.True(yard.WatchCommand.CanExecute(null));
        yard.WatchCommand.Execute(null);
        Assert.True(vm.IsReplaying);
        Assert.Equal(3, vm.Game.Player.Depth);              // on the level they fell on, not back in town
        Assert.Contains("last moments", vm.LastMessage);
        vm.StopReplay();
    }

    /// <summary>Character cards: the living character in play, and a fallen one from the graveyard, saved as pictures.</summary>
    [AvaloniaFact]
    public void CharacterCards_AreSaved_ForTheLiving_AndTheFallen()
    {
        MainWindow.OpenSavedCards = false;
        var (_, vm) = Open();
        vm.Game.Player.Name = "Wren";
        vm.SaveCharacterCardCommand.Execute(null);
        Assert.StartsWith("Character card saved: ", vm.LastMessage);
        var living = vm.LastMessage["Character card saved: ".Length..];
        Assert.True(File.Exists(living));
        using (var png = new global::Avalonia.Media.Imaging.Bitmap(living)) Assert.Equal(1200, png.PixelSize.Width);

        vm.Game.TakeHit(100_000, "a Cave orc");
        var yard = vm.CreateGraveyard();
        yard.SaveCardCommand.Execute(null);
        var fallen = Directory.GetFiles(vm.CardDirectory!, "*.png").Single(f => f != living);
        Assert.StartsWith("Wren-", Path.GetFileName(fallen));
        var shots = Environment.GetEnvironmentVariable("AVABAND_SCREENSHOTS");
        if (shots is not null)
        {
            File.Copy(living, Path.Combine(shots, "card-living.png"), true);
            File.Copy(fallen, Path.Combine(shots, "card-fallen.png"), true);
        }
    }

    /// <summary>The daily dungeon: today's seed and character (not the last character's), and a try goes in the daily table with its replay.</summary>
    [AvaloniaFact]
    public void TheDailyDungeon_IsPlayed_AndItsTriesKept()
    {
        var (window, vm) = Open();
        vm.ReplayDirectory = Path.Combine(_dir, "replays");
        var daily = vm.CreateDaily();
        Assert.StartsWith("Today: a ", daily.Character);
        Assert.True(daily.IsEmpty);
        daily.PlayCommand.Execute(null);
        var today = DailyDungeon.Today();
        Assert.Equal(DailyDungeon.SeedFor(today), vm.Game.Seed);
        Assert.Equal(DailyDungeon.Stamp(today), vm.Game.Player.DailyDate);
        var spec = DailyDungeon.SpecFor(vm.Game.Data, today, "x");
        Assert.Equal(spec.ClassId, vm.Game.Player.Class!.Id);

        for (var i = 0; i < 5; i++) vm.Execute(new HoldCommand());
        vm.Game.TakeHit(100_000, "a jackal");
        var tries = new RecordStore(Path.Combine(_dir, "records")).LoadDaily().For(DailyDungeon.Stamp(today));
        var first = Assert.Single(tries);
        Assert.Equal(1, first.Attempt);
        Assert.StartsWith("killed by a jackal", first.Fate);
        Assert.True(File.Exists(first.ReplayPath));
        Assert.True(vm.FeatBook.Earned.ContainsKey("daily")); // a first try is a feat
        Assert.True(new RecordStore(Path.Combine(_dir, "records")).LoadFeats().Earned.ContainsKey("daily"));

        var again = vm.CreateDaily();
        var row = Assert.Single(again.Rows);
        Assert.True(row.IsToday);
        Assert.Same(row, again.Selected); // today's best, ready to share
        Assert.True(again.WatchCommand.CanExecute(null));

        // Sharing: a line to copy, and the replay under a name that says which day and try.
        string? copied = null;
        (string From, string Name)? saved = null;
        again.CopyRequested += line => copied = line;
        again.SaveReplayRequested += (from, name) => saved = (from, name);
        again.CopyResultCommand.Execute(null);
        Assert.StartsWith($"AVABand daily {DailyDungeon.Stamp(today)}: ", copied);
        Assert.Contains("killed by a jackal", copied);
        Assert.Equal("Copied: " + copied, again.ShareNote);
        again.SaveReplayCommand.Execute(null);
        Assert.Equal((first.ReplayPath!, $"AVABand-daily-{DailyDungeon.Stamp(today)}-try1.avareplay"), saved);

        var dialog = new DailyWindow { DataContext = again };
        DialogFit.Show(dialog, window);
        TileRenderingTests.Save(dialog, "daily");
    }
}
