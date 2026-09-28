using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

/// <summary>The message history (Angband Ctrl+P).</summary>
public class MessageHistoryUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    private static void Say(MainWindowViewModel vm, string text) => vm.Game.Events.Publish(new MessageEvent(text));

    [AvaloniaFact]
    public void CtrlP_ShowsEveryMessage_NewestLast_WithRepeatsCounted()
    {
        var (window, vm) = Open();
        Say(vm, "You hit the jackal.");
        Say(vm, "You miss the jackal.");
        Say(vm, "You miss the jackal.");
        Say(vm, "You miss the jackal.");
        Say(vm, "The jackal dies.");

        window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);

        var dialog = Assert.IsType<MessageHistoryWindow>(window.OwnedWindows.Last());
        var history = (MessageHistoryViewModel)dialog.DataContext!;
        var rows = history.Rows.Select(r => r.Display).TakeLast(3).ToList();
        Assert.Equal(["You hit the jackal.", "You miss the jackal. <x3>", "The jackal dies."], rows);
        TileRenderingTests.Save(dialog, "message-history");

        history.Filter = "miss";
        Assert.Equal(["You miss the jackal. <x3>"], history.Rows.Select(r => r.Display));
        Assert.StartsWith("1 of ", history.Summary);

        dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(dialog.IsVisible);
    }

    [AvaloniaFact]
    public void TheLog_KeepsAngbandsTwoThousandMessages_AndIsClearedForANewGame()
    {
        var (_, vm) = Open();
        for (var i = 0; i < MainWindowViewModel.MaxHistory + 100; i++) Say(vm, $"Message {i}");
        Assert.Equal(MainWindowViewModel.MaxHistory, vm.History.Count);
        Assert.Equal($"Message {MainWindowViewModel.MaxHistory + 99}", vm.History[^1].Text);

        vm.StartGame(7, "mage");
        Assert.DoesNotContain(vm.History, m => m.Text.StartsWith("Message "));
    }

    [AvaloniaFact]
    public void TheGameMenu_HasIt()
    {
        var (_, vm) = Open();
        MessageHistoryViewModel? shown = null;
        vm.MessageHistoryRequested += h => shown = h;
        vm.ShowMessageHistoryCommand.Execute(null);
        Assert.NotNull(shown);
    }
}
