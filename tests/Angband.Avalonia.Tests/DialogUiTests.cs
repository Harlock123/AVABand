using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Data;
using Angband.Input;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

/// <summary>
/// Every dialog can be left from the keyboard and has a button that closes it: tiling window
/// managers (Hyprland/Omarchy) draw no title bar to close them with.
/// </summary>
public class DialogUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.StartGame(42);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    /// <summary>Focuses the first control of this type in the dialog, as a user working in it would.</summary>
    private static T FocusFirst<T>(Window dialog) where T : Control
    {
        var control = dialog.GetVisualDescendants().OfType<T>().First(c => c.IsEffectivelyVisible && c.IsEffectivelyEnabled);
        control.Focus();
        return control;
    }

    private static void Escape(Window dialog) => dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

    [AvaloniaFact]
    public void Settings_CloseWithEscape_WhileAControlHasTheFocus()
    {
        var (window, _) = Open();
        var settings = window.OpenSettings();
        FocusFirst<Slider>(settings);
        Escape(settings);
        Assert.False(settings.IsVisible);
    }

    [AvaloniaFact]
    public void Settings_HaveACloseButton()
    {
        var (window, _) = Open();
        var settings = window.OpenSettings();
        var close = settings.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CloseButton");
        Assert.True(close.IsEffectivelyVisible);
        TileRenderingTests.Save(settings, "settings-close");
        close.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.False(settings.IsVisible);
    }

    [AvaloniaFact]
    public void Escape_CancelsAKeyCapture_BeforeItClosesTheSettings()
    {
        var (window, vm) = Open();
        var settings = window.OpenSettings();
        vm.RebindKeyCommand.Execute(vm.BindingRows.First());
        Assert.True(vm.IsCapturingKey);
        Escape(settings);
        Assert.False(vm.IsCapturingKey);
        Assert.True(settings.IsVisible);

        // The same for a controller button (here: "no gamepad", which also waits for a dismissal).
        vm.RebindButtonCommand.Execute(vm.BindingRows.First());
        Assert.NotNull(vm.CaptureHint);
        Escape(settings);
        Assert.Null(vm.CaptureHint);
        Assert.True(settings.IsVisible);

        Escape(settings);
        Assert.False(settings.IsVisible);
    }

    [AvaloniaFact]
    public void Escape_ClosesAnOpenDropDown_BeforeTheWindow()
    {
        var (window, _) = Open();
        var settings = window.OpenSettings();
        var tabs = settings.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedIndex = tabs.ItemCount - 1; // options: the ignore settings have drop-downs
        settings.UpdateLayout();
        var combo = FocusFirst<ComboBox>(settings);
        combo.IsDropDownOpen = true;
        Escape(settings);
        Assert.False(combo.IsDropDownOpen);
        Assert.True(settings.IsVisible);
        Escape(settings);
        Assert.False(settings.IsVisible);
    }

    [AvaloniaFact]
    public void Knowledge_CharacterSheet_AndScores_CloseWithEscape()
    {
        var (window, vm) = Open();

        vm.HandleAction(InputAction.MonsterKnowledge);
        var knowledge = Assert.IsType<KnowledgeWindow>(window.OwnedWindows.Last());
        FocusFirst<ListBox>(knowledge);
        Escape(knowledge);
        Assert.False(knowledge.IsVisible);

        vm.HandleAction(InputAction.CharacterSheet);
        var sheet = Assert.IsType<CharacterSheetWindow>(window.OwnedWindows.Last());
        Escape(sheet);
        Assert.False(sheet.IsVisible);

        vm.HandleAction(InputAction.HighScores);
        var scores = Assert.IsType<HighScoresWindow>(window.OwnedWindows.Last());
        Escape(scores);
        Assert.False(scores.IsVisible);
    }

    [AvaloniaFact]
    public void LoadAndNewCharacter_CloseWithEscape()
    {
        var (window, vm) = Open();
        var dir = Directory.CreateTempSubdirectory("avaband-dialog-").FullName;
        try
        {
            var load = new LoadGameWindow { DataContext = new LoadGameViewModel(new SaveStore(dir)) };
            load.Show(window);
            Escape(load);
            Assert.False(load.IsVisible);
        }
        finally { Directory.Delete(dir, true); }

        var creation = new CharacterCreationWindow { DataContext = vm.CreateCharacterCreation() };
        creation.Show(window);
        FocusFirst<TextBox>(creation); // typing the name
        Escape(creation);
        Assert.False(creation.IsVisible);
    }

    [AvaloniaFact]
    public void TheControllersB_ClosesEachDialog()
    {
        var (window, vm) = Open();
        void BCloses(Window dialog)
        {
            Assert.Same(dialog, window.OpenDialog);
            window.HandleGamepadAction(InputAction.Cancel);
            Assert.False(dialog.IsVisible);
        }

        BCloses(window.OpenSettings());
        vm.HandleAction(InputAction.MonsterKnowledge);
        BCloses(window.OwnedWindows.Last());
        vm.HandleAction(InputAction.CharacterSheet);
        BCloses(window.OwnedWindows.Last());
        vm.HandleAction(InputAction.HighScores);
        BCloses(window.OwnedWindows.Last());
        BCloses(window.OpenCharacterCreation());
        Assert.Null(window.OpenDialog);
    }

    [AvaloniaFact]
    public void TheGameIgnoresTheController_WhileADialogIsOpen()
    {
        var (window, vm) = Open();
        var game = vm.Game;
        var start = game.Player.Position;
        var dir = Enumerable.Range(0, 8).Select(i => (Angband.Core.Geometry.Direction)i)
            .First(d => game.Level.IsPassable(start.Step(d)) && game.Level.Monsters.At(start.Step(d)) is null);
        var move = InputActions.FromDirection(dir);

        var settings = window.OpenSettings();
        window.HandleGamepadAction(move);
        Assert.Equal(start, game.Player.Position); // not behind the dialog
        Assert.True(settings.IsVisible);

        window.HandleGamepadAction(InputAction.Cancel);
        window.HandleGamepadAction(move);
        Assert.Equal(start.Step(dir), game.Player.Position); // back to the game
    }

    [AvaloniaFact]
    public void B_ClosesADropDown_OrCancelsARebind_First()
    {
        var (window, vm) = Open();
        var settings = window.OpenSettings();
        var tabs = settings.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedIndex = tabs.ItemCount - 1;
        settings.UpdateLayout();
        var combo = FocusFirst<ComboBox>(settings);
        combo.IsDropDownOpen = true;
        window.HandleGamepadAction(InputAction.Cancel);
        Assert.False(combo.IsDropDownOpen);
        Assert.True(settings.IsVisible);

        vm.RebindKeyCommand.Execute(vm.BindingRows.First());
        window.HandleGamepadAction(InputAction.Cancel);
        Assert.Null(vm.CaptureHint);
        Assert.True(settings.IsVisible);

        window.HandleGamepadAction(InputAction.Cancel);
        Assert.False(settings.IsVisible);
    }

    [AvaloniaFact]
    public void TheDPad_MovesBetweenControls_AndAPressesTheCloseButton()
    {
        var (window, _) = Open();
        var settings = window.OpenSettings();
        Assert.Null(DialogPad.Focused(settings));

        window.HandleGamepadAction(InputAction.MoveSouth); // the first press finds the first control
        var first = DialogPad.Focused(settings);
        Assert.NotNull(first);

        var seen = new List<Control> { first! };
        for (var i = 0; i < 40 && DialogPad.Focused(settings)?.Name != "CloseButton"; i++)
        {
            window.HandleGamepadAction(InputAction.MoveSouth);
            seen.Add(DialogPad.Focused(settings)!);
        }
        Assert.Equal("CloseButton", DialogPad.Focused(settings)?.Name);
        TileRenderingTests.Save(settings, "settings-pad-focus");
        Assert.True(seen.Distinct().Count() > 2, string.Join(" > ", seen.Select(c => c.GetType().Name + ":" + c.Name)));

        window.HandleGamepadAction(InputAction.MoveNorth);
        Assert.NotEqual("CloseButton", DialogPad.Focused(settings)?.Name); // and back

        window.HandleGamepadAction(InputAction.MoveSouth);
        window.HandleGamepadAction(InputAction.Confirm);
        Assert.False(settings.IsVisible);
    }

    [AvaloniaFact]
    public void A_TogglesAnOption_AndLeftRightMoveASlider()
    {
        var (window, vm) = Open();
        var settings = window.OpenSettings();

        var slider = FocusFirst<Slider>(settings);
        var before = slider.Value;
        window.HandleGamepadAction(InputAction.MoveEast);
        Assert.True(slider.Value > before);
        window.HandleGamepadAction(InputAction.MoveWest);
        Assert.Equal(before, slider.Value);

        settings.ShowOptionsPage();
        settings.UpdateLayout();
        var box = FocusFirst<CheckBox>(settings);
        var row = (OptionRow)box.DataContext!;
        var was = row.IsChecked;
        window.HandleGamepadAction(InputAction.Confirm);
        Assert.Equal(!was, row.IsChecked);
        window.HandleGamepadAction(InputAction.Confirm);
        Assert.Equal(was, row.IsChecked);
    }

    [AvaloniaFact]
    public void A_OpensADropDown_TheDPadChooses_AndAClosesIt()
    {
        var (window, _) = Open();
        var settings = window.OpenSettings();
        settings.ShowOptionsPage();
        settings.UpdateLayout();
        var combo = FocusFirst<ComboBox>(settings);
        var index = combo.SelectedIndex;

        window.HandleGamepadAction(InputAction.Confirm);
        Assert.True(combo.IsDropDownOpen);
        window.HandleGamepadAction(InputAction.MoveSouth);
        Assert.Equal(Math.Min(combo.ItemCount - 1, index + 1), combo.SelectedIndex);
        window.HandleGamepadAction(InputAction.Confirm);
        Assert.False(combo.IsDropDownOpen);
        Assert.True(settings.IsVisible);

        // Closed, Up/Down pass the focus on instead of changing the choice.
        var chosen = combo.SelectedIndex;
        window.HandleGamepadAction(InputAction.MoveSouth);
        Assert.Equal(chosen, combo.SelectedIndex);
        Assert.NotSame(combo, DialogPad.Focused(settings));
    }

    [AvaloniaFact]
    public void TheDPad_ChangesTabs_AndWorksLists()
    {
        var (window, vm) = Open();
        var settings = window.OpenSettings();
        var tabs = settings.GetVisualDescendants().OfType<TabControl>().First();
        var tab = settings.GetVisualDescendants().OfType<TabItem>().First();
        tab.Focus(NavigationMethod.Tab);
        window.HandleGamepadAction(InputAction.MoveEast);
        Assert.Equal(1, tabs.SelectedIndex);
        settings.Close();

        // The knowledge browser's lists move their selection with Up/Down.
        var game = vm.Game;
        foreach (var race in new[] { "jackal", "cave_orc", "floating_eye" }) game.Lore.For(race).Sights = 1;
        vm.HandleAction(InputAction.MonsterKnowledge);
        var knowledge = window.OwnedWindows.OfType<KnowledgeWindow>().Last();
        var list = knowledge.GetVisualDescendants().OfType<ListBox>().First(l => l.ItemCount >= 3);
        list.SelectedIndex = 0;
        knowledge.UpdateLayout();
        list.ContainerFromIndex(0)!.Focus(NavigationMethod.Tab);
        window.HandleGamepadAction(InputAction.MoveSouth);
        Assert.Equal(1, list.SelectedIndex);
    }

    [AvaloniaFact]
    public void WhileAKeyIsBeingBound_OnlyBWorks()
    {
        var (window, vm) = Open();
        var settings = window.OpenSettings();
        var slider = FocusFirst<Slider>(settings);
        var value = slider.Value;
        vm.RebindKeyCommand.Execute(vm.BindingRows.First());
        window.HandleGamepadAction(InputAction.MoveEast);
        window.HandleGamepadAction(InputAction.Confirm);
        Assert.Equal(value, slider.Value);
        Assert.True(vm.IsCapturingKey); // A didn't get bound as a key
        window.HandleGamepadAction(InputAction.Cancel);
        Assert.False(vm.IsCapturingKey);
        Assert.True(settings.IsVisible);
    }

    [AvaloniaFact]
    public void TheLoadDialog_StartsOnTheList_AndDeleteNeedsTwoPresses()
    {
        var (window, vm) = Open();
        var dir = Directory.CreateTempSubdirectory("avaband-dialog-").FullName;
        try
        {
            var store = new SaveStore(dir);
            vm.UseSaves(store, resume: false);
            vm.SaveGame();
            var load = window.OpenLoadGame();
            var dialog = (LoadGameViewModel)load.DataContext!;

            window.HandleGamepadAction(InputAction.MoveSouth);
            Assert.IsType<ListBoxItem>(DialogPad.Focused(load)); // the characters come first
            var order = new List<string>();
            for (var i = 0; i < 3; i++)
            {
                window.HandleGamepadAction(InputAction.MoveEast); // Left/Right leave a list
                order.Add(((Button)DialogPad.Focused(load)!).Content?.ToString() ?? "");
            }
            Assert.Equal(["Load", "Cancel", "Delete"], order);

            window.HandleGamepadAction(InputAction.Confirm);
            Assert.Equal("Really delete?", dialog.DeleteText);
            Assert.Single(store.List());
            window.HandleGamepadAction(InputAction.Confirm);
            Assert.Empty(store.List());
            window.HandleGamepadAction(InputAction.Cancel);
            Assert.False(load.IsVisible);
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public void NewCharacter_GoesNameThenRaceThenClass()
    {
        var (window, _) = Open();
        var creation = window.OpenCharacterCreation();
        var vm = (CharacterCreationViewModel)creation.DataContext!;
        window.HandleGamepadAction(InputAction.MoveSouth);
        Assert.Equal("NameBox", DialogPad.Focused(creation)?.Name);
        window.HandleGamepadAction(InputAction.MoveSouth); // Random name
        window.HandleGamepadAction(InputAction.MoveSouth);
        Assert.IsType<ListBoxItem>(DialogPad.Focused(creation));
        var race = vm.SelectedRace;
        window.HandleGamepadAction(InputAction.MoveSouth); // Down picks the next race
        Assert.NotEqual(race, vm.SelectedRace);
        window.HandleGamepadAction(InputAction.MoveEast); // Right goes on to the classes
        var cls = vm.SelectedClass;
        window.HandleGamepadAction(InputAction.MoveSouth);
        Assert.NotEqual(cls, vm.SelectedClass);
        TileRenderingTests.Save(creation, "creation-gamepad");
        creation.Close();
    }
}
