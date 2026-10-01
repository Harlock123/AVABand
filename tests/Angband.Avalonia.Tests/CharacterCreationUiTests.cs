using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

public class CharacterCreationUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, AppSettings Saved) Open()
    {
        var saved = new AppSettings();
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(),
            s => saved.LastCharacter = s.LastCharacter);
        vm.StartGame(42);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm, saved);
    }

    [AvaloniaFact]
    public void PointBuy_RespectsTheBudget()
    {
        var (window, _, _) = Open();
        var creation = (CharacterCreationViewModel)window.OpenCharacterCreation().DataContext!;
        creation.IsPointBuy = true;
        var str = creation.StatRows.Single(r => r.Id == "str");

        for (var i = 0; i < 20; i++) creation.IncreaseCommand.Execute(str);

        Assert.Equal(18, str.Base);
        Assert.True(Birth.PointsSpent(creation.StatRows.ToDictionary(r => r.Id, r => r.Base)) <= creation.Budget);
        Assert.Null(creation.Error);
        for (var i = 0; i < 20; i++) creation.DecreaseCommand.Execute(str);
        Assert.Equal(Birth.PointBuyMin, str.Base);
    }

    [AvaloniaFact]
    public void Rolling_GivesNewStats()
    {
        var (window, _, _) = Open();
        var creation = (CharacterCreationViewModel)window.OpenCharacterCreation().DataContext!;
        creation.IsPointBuy = false;
        var sets = Enumerable.Range(0, 5).Select(_ =>
        {
            creation.RerollCommand.Execute(null);
            return string.Join(",", creation.StatRows.Select(r => r.Base));
        }).ToHashSet();
        Assert.True(sets.Count > 1);
        Assert.All(creation.StatRows, r => Assert.InRange(r.Base, 8, 17));
    }

    [AvaloniaFact]
    public void Preview_FollowsRaceAndClass()
    {
        var (window, _, _) = Open();
        var creation = (CharacterCreationViewModel)window.OpenCharacterCreation().DataContext!;
        creation.SelectedRace = creation.Races.Single(r => r.Id == "dwarf");
        creation.SelectedClass = creation.Classes.Single(c => c.Id == "priest");

        Assert.Contains("the Dwarf Priest", creation.Preview);
        Assert.Contains("Divine magic (WIS)", creation.Preview);
        Assert.Contains("Blindness Immunity", creation.Preview);
        Assert.Equal("+2", creation.StatRows.Single(r => r.Id == "wis").RaceMod);
        Assert.Equal("+3", creation.StatRows.Single(r => r.Id == "wis").ClassMod);
        TileRenderingTests.Save(window.OwnedWindows[0], "character-creation");
    }

    /// <summary>Every race and class's abilities are named as 4.2.5's birth screen names them, never as a raw flag ("bravery_30").</summary>
    [AvaloniaFact]
    public void Preview_DescribesEveryAbilityInWords()
    {
        var (window, _, _) = Open();
        var creation = (CharacterCreationViewModel)window.OpenCharacterCreation().DataContext!;
        foreach (var race in creation.Races)
        foreach (var cls in creation.Classes)
        {
            creation.SelectedRace = race;
            creation.SelectedClass = cls;
            var abilities = creation.Preview.Split(Environment.NewLine).Single(l => l.Contains("abilities", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("_", abilities);
            Assert.DoesNotMatch("[A-Z]{3,}", abilities);
        }

        creation.SelectedRace = creation.Races.Single(r => r.Id == "human");
        creation.SelectedClass = creation.Classes.Single(c => c.Id == "warrior");
        Assert.Contains("Abilities: Relentless [30], No Magic, Shield Bash", creation.Preview);
        creation.SelectedClass = creation.Classes.Single(c => c.Id == "mage");
        Assert.Contains("Abilities: Full Spellcaster, Extra Spell Beaming, Spell Choice", creation.Preview); // player_property.txt order
    }

    [AvaloniaFact]
    public void Start_BeginsTheGame_AndRemembersTheCharacter()
    {
        var (window, vm, saved) = Open();
        var creationWindow = window.OpenCharacterCreation();
        var creation = (CharacterCreationViewModel)creationWindow.DataContext!;
        creation.Name = "Galadwen";
        creation.SelectedRace = creation.Races.Single(r => r.Id == "high_elf");
        creation.SelectedClass = creation.Classes.Single(c => c.Id == "mage");

        creation.StartCommand.Execute(null);

        Assert.StartsWith("Galadwen the High-Elf Mage L1", vm.StatusText);
        Assert.Equal("Galadwen", saved.LastCharacter?.Name);
        Assert.False(creationWindow.IsVisible);

        vm.QuickStartCommand.Execute(null); // same character again
        Assert.StartsWith("Galadwen the High-Elf Mage L1", vm.StatusText);
    }

    [AvaloniaFact]
    public void TheHeroicRoll_AndTheAutoroller_MakeSuperlativeStats()
    {
        var (window, _, _) = Open();
        var creation = (CharacterCreationViewModel)window.OpenCharacterCreation().DataContext!;
        creation.SelectedMethod = CharacterCreationViewModel.StatMethods.Single(m => m.Method == StatMethod.HeroicRoll);
        Assert.True(creation.IsRolled);
        Assert.All(creation.StatRows, r => Assert.InRange(r.Base, 14, HeroicBirth.HeroicMax));
        Assert.Contains("Heroic", creation.PointsText);

        // Minimums on the final stats: STR 18/50, CON 18/20 (indexes into "any, 8, 9, ... 18/220").
        var str = creation.StatRows.Single(r => r.Id == "str");
        var con = creation.StatRows.Single(r => r.Id == "con");
        str.MinimumIndex = CharacterCreationViewModel.MinimumChoices.ToList().IndexOf("18/50");
        con.MinimumIndex = CharacterCreationViewModel.MinimumChoices.ToList().IndexOf("18/20");
        Assert.Equal(23, str.Minimum);
        creation.AutorollCommand.Execute(null);
        Assert.StartsWith("Met every minimum", creation.AutorollText.Replace("The first roll met", "Met"));
        Assert.True(Birth.FinalStat(str.Base, creation.SelectedRace, creation.SelectedClass, "str") >= 23);
        Assert.True(Birth.FinalStat(con.Base, creation.SelectedRace, creation.SelectedClass, "con") >= 20);
        Assert.Equal(StatMethod.HeroicRoll, creation.Spec()!.Method);
        TileRenderingTests.Save(window.OwnedWindows[0], "character-creation-heroic");

        // A minimum this roll can never reach is refused at once.
        str.MinimumIndex = CharacterCreationViewModel.MinimumChoices.Count - 1; // 18/220
        creation.AutorollCommand.Execute(null);
        Assert.Contains("can't reach 18/220", creation.AutorollText);
    }

    [AvaloniaFact]
    public void HeroicPointBuy_GoesTo18Slash50_AndIsRememberedWithTheMinimums()
    {
        var (window, vm, saved) = Open();
        var creation = (CharacterCreationViewModel)window.OpenCharacterCreation().DataContext!;
        creation.SelectedMethod = CharacterCreationViewModel.StatMethods.Single(m => m.Method == StatMethod.HeroicPointBuy);
        Assert.True(creation.IsPointBuy);
        Assert.Equal(HeroicBirth.HeroicBudget, creation.Budget);
        var str = creation.StatRows.Single(r => r.Id == "str");
        for (var i = 0; i < 20; i++) creation.IncreaseCommand.Execute(str);
        Assert.Equal(HeroicBirth.HeroicMax, str.Base);
        Assert.Equal("18/50", str.BaseText);
        Assert.Null(creation.Error);
        creation.StatRows.Single(r => r.Id == "dex").MinimumIndex = 5;

        creation.StartCommand.Execute(null);
        Assert.True(vm.Game.Player.HeroicBirth);
        Assert.Equal(HeroicBirth.HeroicMax, vm.Game.Player.BaseStats["str"]);
        Assert.True(saved.LastCharacter!.Heroic);
        Assert.False(saved.LastCharacter.Rolled);
        Assert.Equal(CharacterCreationViewModel.LowestMinimum + 4, saved.LastCharacter.AutorollMinimums["dex"]);

        // The next creation screen starts from them.
        var again = (CharacterCreationViewModel)window.OpenCharacterCreation().DataContext!;
        Assert.Equal(StatMethod.HeroicPointBuy, again.Method);
        Assert.Equal(5, again.StatRows.Single(r => r.Id == "dex").MinimumIndex);
    }

    /// <summary>Compare races: every race side by side, the chosen one marked; a click chooses one; AVABand's abilities only when on.</summary>
    [AvaloniaFact]
    public void CompareRaces_ListsEveryRace_AndChoosesOne()
    {
        var (window, _, _) = Open();
        var creationWindow = window.OpenCharacterCreation();
        var creation = (CharacterCreationViewModel)creationWindow.DataContext!;
        Assert.Equal(creation.Races.Count, creation.RaceComparison.Count);
        Assert.Single(creation.RaceComparison, r => r.IsSelected);

        var dwarf = creation.RaceComparison.Single(r => r.Race.Id == "dwarf");
        Assert.Equal("+2", dwarf.Str);
        Assert.Equal("d11", dwarf.HitDie);
        Assert.Equal("120%", dwarf.Experience);
        Assert.StartsWith("Delver, carries 10% more", dwarf.Abilities);
        Assert.Contains("Strength +2", dwarf.Spoken);
        var human = creation.RaceComparison.Single(r => r.Race.Id == "human");
        Assert.Equal("0", human.Str);
        Assert.StartsWith("Two-weapon fighting", human.Abilities);
        Assert.StartsWith("Stone-thrower, second breakfast, carries 10% less",
            creation.RaceComparison.Single(r => r.Race.Id == "hobbit").Abilities);

        creation.ChooseRaceCommand.Execute(dwarf.Race);
        Assert.Equal("dwarf", creation.SelectedRace!.Id);
        Assert.True(creation.RaceComparison.Single(r => r.Race.Id == "dwarf").IsSelected);

        var panel = creationWindow.FindControl<global::Avalonia.Controls.Expander>("RaceComparisonPanel")!;
        panel.IsExpanded = true;
        creationWindow.CaptureRenderedFrame();
        panel.BringIntoView();
        creationWindow.CaptureRenderedFrame();
        TileRenderingTests.Save(creationWindow, "compare-races");

        // AVABand's racial abilities off: only Angband's own.
        creation.BirthOptionRows.Single(o => o.Id == OptionIds.AvaRaces).IsChecked = false;
        Assert.DoesNotContain("Delver", creation.RaceComparison.Single(r => r.Race.Id == "dwarf").Abilities);

    }

    /// <summary>Portraits: the race chosen in the class chosen (and each race, and each class on that race, in the lists); every one there.</summary>
    [AvaloniaFact]
    public void The_portrait_shows_the_race_in_the_class()
    {
        var (window, _, _) = Open();
        var creationWindow = window.OpenCharacterCreation();
        var creation = (CharacterCreationViewModel)creationWindow.DataContext!;
        creation.SelectedRace = creation.Races.Single(r => r.Id == "dwarf");
        creation.SelectedClass = creation.Classes.Single(c => c.Id == "warrior_priest");
        Assert.NotNull(creation.Portrait);
        Assert.Equal(128, creation.Portrait!.PixelSize.Width);
        Assert.Equal("Adventurer the Dwarf Warrior/Priest", creation.PortraitCaption);
        var before = creation.Portrait;
        creation.SelectedClass = creation.Classes.Single(c => c.Id == "mage");
        Assert.NotSame(before, creation.Portrait);

        // A picture for every race, and every race in every class (multiclasses too).
        foreach (var race in creation.Races)
        {
            Assert.NotNull(Angband.Avalonia.Controls.Portraits.For(race.Id));
            foreach (var cls in creation.Classes) Assert.NotNull(Angband.Avalonia.Controls.Portraits.For(race.Id, cls.Id));
        }
        Assert.Contains(creation.Classes, c => c.Id == "warrior_mage");

        creation.SelectedClass = creation.Classes.Single(c => c.Id == "warrior_druid");
        creationWindow.CaptureRenderedFrame();
        TileRenderingTests.Save(creationWindow, "creation-portrait");
    }
}
