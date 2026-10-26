using Angband.Audio;
using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Persistence;
using Angband.Core.Records;
using Angband.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

/// <summary>The title screen: its picture, its music, and continuing, playing again or starting afresh.</summary>
public sealed class TitleScreenUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "avaband-title-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private sealed class Engine : NullAudioEngine
    {
        public override bool IsAvailable => true;
    }

    private (MainWindow Window, MainWindowViewModel Vm, Engine Audio) Open(AppSettings settings, Action<MainWindowViewModel>? before = null)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null);
        var engine = new Engine();
        vm.UseAudio(new AudioServices(engine, new SoundDirector(engine), SoundPackCatalog.Discover([SoundPackCatalog.DefaultDirectory])));
        vm.UseRecords(new RecordStore(Path.Combine(_dir, "records")));
        before?.Invoke(vm);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        vm.ShowTitle();
        window.CaptureRenderedFrame();
        return (window, vm, engine);
    }

    [AvaloniaFact]
    public void A_first_run_offers_the_first_adventure_to_the_title_music()
    {
        var (window, vm, audio) = Open(new AppSettings());
        Assert.True(vm.IsShowingTitle);
        Assert.NotNull(vm.TitleBackground);
        Assert.NotNull(vm.TitleLogo);
        Assert.Equal(["Play now", "Begin your first adventure", "High scores", "Exit"], vm.TitleChoices.Select(c => c.Label));
        Assert.Equal("title_theme.ogg", Path.GetFileName(audio.CurrentMusic));
        TileRenderingTests.Save(window, "title-first-run");
    }

    [AvaloniaFact]
    public void On_a_first_run_Enter_plays_now_with_a_friendly_character_in_the_town()
    {
        var settings = new AppSettings();
        var (window, vm, _) = Open(settings);
        Assert.Equal(0, vm.TitleSelectedIndex); // Play now is highlighted...
        Assert.Equal("Play now", vm.TitleChoices[0].Label);
        Assert.Equal("b", vm.TitleChoices.Single(c => c.Label == "Begin your first adventure").Letter); // ...the full screen a letter away
        Assert.Equal(vm.TitleChoices.Count, vm.TitleChoices.Select(c => c.Letter).Distinct().Count());

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.False(vm.IsShowingTitle);
        var p = vm.Game.Player;
        Assert.Contains(PlayNow.Combos, c => c.RaceId == p.Race?.Id && c.ClassId == p.Class?.Id);
        Assert.Equal(0, p.Depth);
        Assert.False(p.IsSeededRun);
        Assert.Equal("classic", Challenges.PresetFor(id => vm.Game.Options[id])?.Id);
        Assert.Empty(Challenges.Names(id => vm.Game.Options[id]));
        Assert.False(string.IsNullOrWhiteSpace(p.Name));
        Assert.NotEqual("Adventurer", p.Name);
        var who = $"{p.Race?.Name} {p.Class?.Name}";
        Assert.Contains(vm.Messages, m => m.StartsWith($"You are {p.Name}, {PlayNow.Article(who)} {who}: ", StringComparison.Ordinal));
        Assert.Contains(vm.Messages, m => m.Contains("Create a new character", StringComparison.Ordinal));
        // Remembered like a character from the creation screen: point-buy, the standard kit.
        Assert.Equal(p.Name, settings.LastCharacter?.Name);
        Assert.False(settings.LastCharacter!.Rolled);
        Assert.Null(settings.LastCharacter.Kit);
        Assert.Null(Angband.Core.Game.Birth.ValidatePointBuy(settings.LastCharacter.Stats, vm.Game.Data.Constants.BirthPoints));
        TileRenderingTests.Save(window, "play-now-town");
    }

    [AvaloniaFact]
    public void Play_now_starts_the_pair_chosen_with_points_on_what_it_needs()
    {
        var settings = new AppSettings { LastCharacter = new SavedCharacter { Name = "Borin", Race = "dwarf", Class = "warrior" } };
        var (window, vm, _) = Open(settings, v => v.UseSaves(new SaveStore(Path.Combine(_dir, "saves")), resume: true));
        var labels = vm.TitleChoices.Select(c => c.Label).ToList();
        Assert.Equal(labels.IndexOf("Create a new character") + 1, labels.IndexOf("Play now"));
        TileRenderingTests.Save(window, "title-play-now");

        var hobbit = PlayNow.Combos.Single(c => c.RaceId == "hobbit" && c.ClassId == "rogue");
        vm.PlayNowPick = _ => hobbit;
        var choice = vm.TitleChoices.Single(c => c.Label == "Play now");
        window.KeyPressQwerty(Enum.Parse<PhysicalKey>(choice.Letter.ToUpperInvariant()), RawInputModifiers.None);
        Assert.Equal("Hobbit", vm.Game.Player.Race?.Name);
        Assert.Equal("Rogue", vm.Game.Player.Class?.Name);
        Assert.Equal(16, settings.LastCharacter?.Stats["dex"]);
        Assert.NotEqual("Borin", vm.Game.Player.Name);
        Assert.Contains(vm.Messages, m => m.StartsWith($"You are {vm.Game.Player.Name}, a Hobbit Rogue: ", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void The_character_saved_last_is_continued_and_the_game_music_takes_over()
    {
        var saves = new SaveStore(Path.Combine(_dir, "saves"));
        var maker = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        maker.StartGame(42, "mage");
        maker.UseSaves(saves, resume: false);
        maker.SaveGame();
        var name = maker.Game.Player.Name;

        var (window, vm, audio) = Open(new AppSettings(), v => v.UseSaves(saves, resume: true));
        var first = vm.TitleChoices[0];
        Assert.Equal($"Continue {name}", first.Label);
        Assert.Equal("the level 1 Human Mage, in the town", first.Detail);
        Assert.Contains(vm.TitleChoices, c => c.Label == "Load a saved character");
        TileRenderingTests.Save(window, "title-continue");

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); // the first choice is highlighted
        Assert.False(vm.IsShowingTitle);
        Assert.Equal(name, vm.Game.Player.Name);
        Assert.NotEqual("title_theme.ogg", Path.GetFileName(audio.CurrentMusic)); // the town's music now
    }

    [AvaloniaFact]
    public void After_a_death_the_title_offers_to_play_the_character_again()
    {
        var settings = new AppSettings { LastCharacter = new SavedCharacter { Name = "Borin", Race = "dwarf", Class = "warrior" } };
        var records = new RecordStore(Path.Combine(_dir, "records"));
        var board = new ScoreBoard();
        board.Add(new ScoreEntry { Name = "Borin", Race = "Dwarf", Class = "Warrior", KilledBy = "Grip, Farmer Maggot's Dog", Depth = 2,
            DateUtc = DateTime.UtcNow });
        board.Save(records.ScoresPath);

        var (window, vm, _) = Open(settings, v => v.UseSaves(new SaveStore(Path.Combine(_dir, "saves")), resume: true));
        Assert.Equal("Play again as Borin", vm.TitleChoices[0].Label);
        Assert.Equal("the Dwarf Warrior, starting afresh — last killed by Grip, Farmer Maggot's Dog", vm.TitleChoices[0].Detail);
        Assert.Equal("Create a new character", vm.TitleChoices[1].Label);
        TileRenderingTests.Save(window, "title-play-again");

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        Assert.False(vm.IsShowingTitle);
    }

    [AvaloniaFact]
    public void The_title_offers_this_weeks_challenge_after_the_daily_dungeon_and_plays_it()
    {
        var settings = new AppSettings { LastCharacter = new SavedCharacter { Name = "Borin", Race = "dwarf", Class = "warrior" } };
        var (window, vm, _) = Open(settings, v => v.UseSaves(new SaveStore(Path.Combine(_dir, "saves")), resume: true));
        var labels = vm.TitleChoices.Select(c => c.Label).ToList();
        var week = Angband.Core.Game.WeeklyChallenge.ThisWeek();
        var twist = Angband.Core.Game.WeeklyChallenge.TwistFor(week);
        var choice = vm.TitleChoices.Single(c => c.Label == "This week's challenge");
        Assert.Equal(labels.IndexOf("Today's daily dungeon") + 1, labels.IndexOf(choice.Label));
        Assert.Equal($"the same for everyone all week, with a twist: {twist.Name}", choice.Detail);
        TileRenderingTests.Save(window, "title-weekly");

        // Its letter opens the week's window; playing starts the week's game.
        window.KeyPressQwerty(Enum.Parse<PhysicalKey>(choice.Letter.ToUpperInvariant()), RawInputModifiers.None);
        var dialog = window.OwnedWindows.OfType<DailyWindow>().Last();
        var weekly = Assert.IsType<DailyViewModel>(dialog.DataContext);
        Assert.True(weekly.IsWeekly);
        weekly.PlayCommand.Execute(null);
        Assert.False(vm.IsShowingTitle);
        Assert.Equal(week, vm.Game.Player.WeeklyWeek);
        Assert.Equal("Borin", vm.Game.Player.Name);
    }

    [AvaloniaFact]
    public void Arrows_move_the_highlight_and_the_title_stays_until_a_game_starts()
    {
        var (window, vm, _) = Open(new AppSettings());
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.Equal(1, vm.TitleSelectedIndex);
        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        Assert.Equal(vm.TitleChoices.Count - 1, vm.TitleSelectedIndex); // it wraps

        // A key that isn't a choice does nothing, and game keys don't reach the game.
        var turn = vm.Game.GameTurn;
        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.Shift);
        Assert.True(vm.IsShowingTitle);
        Assert.Equal(turn, vm.Game.GameTurn);

        vm.StartGame(7, "warrior"); // as the creation screen does
        Assert.False(vm.IsShowingTitle);
    }
}
