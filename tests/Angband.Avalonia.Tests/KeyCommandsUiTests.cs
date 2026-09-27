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

/// <summary>The keyboard commands window ('?' / F1).</summary>
public class KeyCommandsUiTests
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

    private static CommandRow Row(KeyCommandsViewModel commands, string command) =>
        commands.Groups.SelectMany(g => g.Rows).Single(r => r.Command == command);

    [AvaloniaFact]
    public void QuestionMark_OpensTheCommands_WithTheirKeys()
    {
        var (window, vm) = Open();
        // Shift+/ as a real keyboard reports it: the '?' symbol (the headless keyboard doesn't shift punctuation).
        window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent, Key = Key.OemQuestion, PhysicalKey = PhysicalKey.Slash,
            KeyModifiers = KeyModifiers.Shift, KeySymbol = "?", Source = window,
        });
        var dialog = Assert.IsType<KeyCommandsWindow>(window.OwnedWindows.Last());
        var commands = (KeyCommandsViewModel)dialog.DataContext!;

        Assert.Equal("m", Row(commands, "Cast").Keys);
        Assert.Equal("w", Row(commands, "Wear / wield").Keys);
        Assert.Equal("x", Row(commands, "Look").Keys);
        Assert.Contains("Wear or wield", Row(commands, "Wear / wield").Description);
        Assert.Contains(commands.Groups, g => g.Title == "Magic");
        Assert.Contains(commands.Groups, g => g.Title == "Look and target mode"); // keys that work only there
        TileRenderingTests.Save(dialog, "key-commands");

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); // the main window doesn't close it...
        dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); // ...Escape in it does
        Assert.False(dialog.IsVisible);
    }

    [AvaloniaFact]
    public void F1_AndTheMenu_OpenItToo()
    {
        var (window, vm) = Open();
        window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
        Assert.IsType<KeyCommandsWindow>(window.OwnedWindows.Last()).Close();
        vm.ShowKeyCommandsCommand.Execute(null);
        Assert.IsType<KeyCommandsWindow>(window.OwnedWindows.Last());
    }

    [AvaloniaFact]
    public void EveryCommand_IsExplained()
    {
        var missing = Enum.GetValues<InputAction>().Where(a => a != InputAction.None).Except(KeyCommandsViewModel.Explained).ToList();
        Assert.Empty(missing);
    }

    [AvaloniaFact]
    public void TheKeysShown_FollowRebinding()
    {
        var (_, vm) = Open();
        vm.Bindings.BindKey("Char:M", InputAction.Cast);
        var commands = new KeyCommandsViewModel(vm.Bindings);
        Assert.Equal("M  ·  m", Row(commands, "Cast").Keys);
        Assert.Equal(",  ·  5  ·  Clear  ·  NumPad5", Row(commands, "Hold (stay put)").Keys);
        Assert.Equal("Y", Row(commands, "Cast").Button);
    }

    [AvaloniaFact]
    public void Find_NarrowsTheList()
    {
        var (_, vm) = Open();
        var commands = new KeyCommandsViewModel(vm.Bindings);
        commands.Filter = "spell";
        var rows = commands.Groups.SelectMany(g => g.Rows).ToList();
        Assert.Contains(rows, r => r.Command == "Cast");
        Assert.Contains(rows, r => r.Command == "Study");
        Assert.DoesNotContain(rows, r => r.Command == "Wear / wield");
        commands.Filter = "zzzz";
        Assert.True(commands.NothingFound);
        commands.Filter = "";
        Assert.True(commands.Groups.Count > 8);
    }
}
