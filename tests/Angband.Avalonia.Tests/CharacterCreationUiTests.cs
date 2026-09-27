using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
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
        Assert.Contains("cannot be blinded", creation.Preview);
        Assert.Equal("+2", creation.StatRows.Single(r => r.Id == "wis").RaceMod);
        Assert.Equal("+3", creation.StatRows.Single(r => r.Id == "wis").ClassMod);
        TileRenderingTests.Save(window.OwnedWindows[0], "character-creation");
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
}
