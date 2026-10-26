using Angband.Avalonia.Controls;
using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>Seasonal events in the app: born in the event running by the player's date, the Settings switch, and the title screen dressed for it.</summary>
public sealed class SeasonUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "avaband-seasons-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static readonly DateTime Halloween = new(2026, 10, 26, 20, 0, 0);
    private static readonly DateTime Midwinter = new(2027, 1, 2, 20, 0, 0);

    private (MainWindow Window, MainWindowViewModel Vm, AppSettings Settings) Open(DateTime today)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings();
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null)
        {
            ArtDirectory = Path.Combine(_dir, "art"), // (none of the player's own)
            BundledArtDirectory = Path.Combine(AppContext.BaseDirectory, "art"),
            SeasonClock = () => today,
        };
        vm.UseRecords(new RecordStore(Path.Combine(_dir, "records")));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm, settings);
    }

    private static string Said(MainWindowViewModel vm) => vm.LastMessage + " " + string.Join(" ", vm.EarlierMessages.Select(m => m.Text));

    [AvaloniaFact]
    public void ACharacterMadeInAnEvent_IsBornInIt_AndToldSo()
    {
        var (_, vm, settings) = Open(Halloween);
        Assert.True(settings.SeasonalEvents);                                 // (on by default)
        Assert.Same(Seasons.AllHallowsEve, vm.SeasonToday);
        vm.StartCharacter(CharacterSpec.Default("human", "warrior") with { Name = "Jack" });
        Assert.Equal("halloween", vm.Game.Player.Season);
        Assert.Contains("You are born at All Hallows' Eve", Said(vm));

        // Out of season, or with the switch off: born in none.
        var (_, plain, _) = Open(new DateTime(2026, 6, 15));
        plain.StartCharacter(CharacterSpec.Default("human", "warrior"));
        Assert.Null(plain.Game.Player.Season);
        Assert.DoesNotContain("You are born at", Said(plain));

        vm.SeasonalEvents = false;
        Assert.False(settings.SeasonalEvents);
        Assert.Null(vm.SeasonToday);
        vm.StartCharacter(CharacterSpec.Default("human", "warrior"));
        Assert.Null(vm.Game.Player.Season);
    }

    [AvaloniaFact]
    public void TheDailyDungeon_AndTheWeeklyChallenge_AreNeverBornInAnEvent()
    {
        var (_, vm, _) = Open(Midwinter);
        vm.StartDaily();
        Assert.Null(vm.Game.Player.Season);
        Assert.Null(vm.Game.Season);
        vm.StartWeekly();
        Assert.Null(vm.Game.Player.Season);
        vm.QuickStartCommand.Execute(null);
        Assert.Equal("midwinter", vm.Game.Player.Season);                     // (an ordinary game is)
    }

    [AvaloniaFact]
    public void TheTitleScreen_IsPaintedForTheEvent_WhileItRuns()
    {
        var (window, vm, _) = Open(Halloween);
        vm.ShowTitle();
        Assert.Same(Seasons.AllHallowsEve, vm.TitleSeason);
        Assert.Equal("halloween", vm.TitleSeasonPainted);
        Assert.NotNull(vm.TitleBackground);
        var overlay = window.FindControl<SeasonalTitleOverlay>("TitleSeasonOverlay")!;
        Assert.Equal("halloween", overlay.Season);
        TileRenderingTests.Save(window, "title-halloween");
        vm.CloseTitle();

        var (winter, wvm, _) = Open(Midwinter);
        wvm.ShowTitle();
        Assert.Equal("midwinter", winter.FindControl<SeasonalTitleOverlay>("TitleSeasonOverlay")!.Season);
        TileRenderingTests.Save(winter, "title-midwinter");

        // The player's own picture of it, if they have one, is shown instead of the painting.
        Directory.CreateDirectory(wvm.ArtDirectory);
        File.Copy(wvm.PictureFor("deep-descent")!, Path.Combine(wvm.ArtDirectory, "title-midwinter.jpg"));
        wvm.ShowTitle();
        Assert.Null(wvm.TitleSeasonPainted);
        Assert.Same(Seasons.Midwinter, wvm.TitleSeason);

        // Out of season, or switched off: Thangorodrim as ever.
        var (_, summer, _) = Open(new DateTime(2026, 6, 15));
        summer.ShowTitle();
        Assert.Null(summer.TitleSeason);
        Assert.Null(summer.TitleSeasonPainted);
        vm.SeasonalEvents = false;
        vm.ShowTitle();
        Assert.Null(vm.TitleSeasonPainted);
        Assert.Null(window.FindControl<SeasonalTitleOverlay>("TitleSeasonOverlay")!.Season);
    }

    [AvaloniaFact]
    public void Settings_HasTheSeasonalEventsSwitch()
    {
        var (_, vm, settings) = Open(Halloween);
        var window = new SettingsWindow { DataContext = vm };
        window.Show();
        var toggle = window.FindControl<ToggleSwitch>("SeasonalSwitch")!;
        Assert.True(toggle.IsChecked);
        toggle.IsChecked = false;
        Assert.False(settings.SeasonalEvents);
        window.Close();
    }
}
