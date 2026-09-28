using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Data;
using Angband.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Avalonia.Headless;

namespace Angband.Avalonia.Tests;

public class OptionsUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, AppSettings Settings, List<AppSettings> Saves) Open(AppSettings? settings = null)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        settings ??= new AppSettings();
        var saves = new List<AppSettings>();
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, saves.Add);
        vm.StartGame(42);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm, settings, saves);
    }

    private static OptionRow Row(IEnumerable<OptionRow> rows, string id) => rows.Single(r => r.Id == id);

    [AvaloniaFact]
    public void Equals_OpensTheOptionsPage()
    {
        var (window, vm, _, _) = Open();
        vm.HandleAction(InputAction.Options);
        var settings = Assert.IsType<SettingsWindow>(window.OwnedWindows.Last());
        var tabs = settings.GetVisualDescendants().OfType<TabControl>().First();
        Assert.Equal("Options", ((TabItem)tabs.SelectedItem!).Header);

        // Every interface option (engine and display), the character's birth options (read-only) and the cheats.
        Assert.Equal(OptionCatalog.OfKind(OptionKind.Interface).Count() + DisplayOptions.All.Count, vm.InterfaceOptionRows.Count);
        Assert.All(vm.BirthOptionRows, r => Assert.False(r.IsEnabled));
        Assert.Equal(3, vm.CheatOptionRows.Count);
        settings.Width = 820;
        settings.Height = 620;
        TileRenderingTests.Save(settings, "settings-options");
    }

    [AvaloniaFact]
    public void InterfaceOptions_ApplyAtOnce_AndAreSaved()
    {
        var (_, vm, settings, saves) = Open();
        Row(vm.InterfaceOptionRows, OptionIds.ShowDamage).IsChecked = true;
        Assert.True(vm.Game.Options[OptionIds.ShowDamage]);
        Assert.True(settings.Options[OptionIds.ShowDamage]);
        Assert.NotEmpty(saves);

        // A new game (or a loaded one) takes the settings' options.
        vm.StartGame(43);
        Assert.True(vm.Game.Options[OptionIds.ShowDamage]);
    }

    [AvaloniaFact]
    public void DisplayOptions_ChangeTheMap()
    {
        var (_, vm, _, _) = Open();
        var wall = vm.Game.Level.AllLocs().First(p => vm.Game.Level.FeatureAt(p).Has(TerrainFlags.Wall)
                                                      && vm.Game.Known.IsKnown(p));
        Assert.Equal(MapCellBuilder.Black, vm.GetCell(wall.X, wall.Y).Background);
        Row(vm.InterfaceOptionRows, DisplayOptions.SolidWalls).IsChecked = true;
        var solid = vm.GetCell(wall.X, wall.Y);
        Assert.Equal(solid.Foreground, solid.Background);

        // hp_changes_color: the @ reddens as hit points fall.
        var p = vm.Game.Player.Position;
        var healthy = vm.GetCell(p.X, p.Y).Foreground;
        vm.Game.Player.Hp = 1;
        Assert.NotEqual(healthy, vm.GetCell(p.X, p.Y).Foreground);

        // center_player off hands the map view Angband's panel scrolling.
        Row(vm.InterfaceOptionRows, DisplayOptions.CenterPlayer).IsChecked = false;
        Assert.False(vm.CenterPlayer);
    }

    [AvaloniaFact]
    public void MouseMovement_Off_KeepsClicksFromMovingThePlayer()
    {
        var (_, vm, _, _) = Open();
        Row(vm.InterfaceOptionRows, DisplayOptions.MouseMovement).IsChecked = false;
        var start = vm.Game.Player.Position;
        var spot = vm.Game.Level.AllLocs().First(l => l.DistanceTo(start) is > 1 and < 4 && vm.Game.Level.IsEmptyFloor(l)
                                                      && vm.Game.Known.IsKnown(l));
        vm.ClickCell(spot, secondary: false);
        Assert.Equal(start, vm.Game.Player.Position);
    }

    [AvaloniaFact]
    public void EffectiveSpeed_ShowsAMultiplier()
    {
        var (_, vm, _, _) = Open();
        vm.Game.Player.BaseSpeed = 10;
        vm.Execute(new HoldCommand());
        Assert.Contains("Fast (+10)", vm.StatusText);
        Row(vm.InterfaceOptionRows, DisplayOptions.EffectiveSpeed).IsChecked = true;
        Assert.Contains("Fast (x2.0)", vm.StatusText);
    }

    [AvaloniaFact]
    public void Cheats_MarkTheCharacter()
    {
        var (_, vm, _, _) = Open();
        Assert.StartsWith("Switching a cheat on", vm.CheaterText);
        Row(vm.CheatOptionRows, OptionIds.CheatLive).IsChecked = true;
        Assert.True(vm.Game.IsCheater);
        Assert.StartsWith("This character has cheated", vm.CheaterText);
    }

    [AvaloniaFact]
    public void BirthOptions_AreChosenAtCreation_AndRemembered()
    {
        var (window, vm, settings, _) = Open();
        var creation = vm.CreateCharacterCreation();
        Row(creation.BirthOptionRows, OptionIds.StartKit).IsChecked = false;
        Row(creation.BirthOptionRows, OptionIds.ForceDescend).IsChecked = true;
        Assert.Contains("Starting gold", creation.Preview);

        var creationWindow = new CharacterCreationWindow { DataContext = creation };
        creationWindow.Show(window);
        creationWindow.FindControl<Expander>("BirthOptions")!.IsExpanded = true;
        TileRenderingTests.Save(creationWindow, "character-creation-birth-options");

        vm.StartCharacter(creation.Spec()!);
        Assert.False(vm.Game.Options[OptionIds.StartKit]);
        Assert.True(vm.Game.ForceDescend);
        Assert.False(settings.LastCharacter!.BirthOptions[OptionIds.StartKit]);

        // The next creation screen starts from the same choices.
        Assert.True(Row(vm.CreateCharacterCreation().BirthOptionRows, OptionIds.ForceDescend).IsChecked);
        Assert.False(Row(vm.BirthOptionRows, OptionIds.StartKit).IsChecked);
    }

    [AvaloniaFact]
    public void TheNewBirthOptions_AreInCharacterCreation_AndShownInSettings_AndTakeEffect()
    {
        var (window, vm, _, _) = Open();
        string[] added = [OptionIds.AiLearn, OptionIds.LevelsPersist, OptionIds.PercentDamage, OptionIds.Stacking];

        // Character creation: every birth option, each one to choose.
        var creation = vm.CreateCharacterCreation();
        foreach (var id in added) Assert.True(Row(creation.BirthOptionRows, id).IsEnabled);

        // Settings -> Options: shown, fixed for the character (as Angband's birth options are).
        var settings = window.OpenSettings();
        settings.ShowOptionsPage();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var texts = settings.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        foreach (var id in added)
        {
            Assert.False(Row(vm.BirthOptionRows, id).IsEnabled);
            Assert.Contains(OptionCatalog.Find(id)!.Description, texts);
        }

        // Chosen at creation, they are the new character's.
        Row(creation.BirthOptionRows, OptionIds.LevelsPersist).IsChecked = true;
        Row(creation.BirthOptionRows, OptionIds.PercentDamage).IsChecked = true;
        creation.StartCommand.Execute(null);
        Assert.True(vm.Game.PersistentLevels);
        Assert.True(vm.Game.PercentDamage);
        Assert.True(Row(vm.BirthOptionRows, OptionIds.LevelsPersist).IsChecked);
    }
}

public class FeelingUiTests
{
    [AvaloniaFact]
    public void TheStatusBar_ShowsTheFeeling_AndCtrlFRepeatsIt()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        Assert.DoesNotContain("LF:", vm.StatusText); // not in town

        vm.Game.Player.Position = vm.Game.Level.FindFeature(TerrainFlags.DownStair).First();
        vm.Execute(new TakeStairsCommand(Down: true));
        Assert.Matches(@"LF:\d-[?\d$*]", vm.StatusText);
        Assert.Contains(vm.Messages, m => Angband.Core.Game.LevelFeelings.MonsterTexts.Any(t => m.StartsWith(t)));

        vm.Messages.Clear();
        window.KeyPressQwerty(global::Avalonia.Input.PhysicalKey.F, global::Avalonia.Input.RawInputModifiers.Control);
        Assert.Contains(vm.Messages, m => Angband.Core.Game.LevelFeelings.MonsterTexts.Any(t => m.StartsWith(t)));
        TileRenderingTests.Save(window, "level-feeling");
    }
}
