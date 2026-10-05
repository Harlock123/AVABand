using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Data;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>Map pins through the right-click menu: pinned, shown, read out, taken away.</summary>
public class PinUiTests
{
    [AvaloniaFact]
    public void RightClick_PinsANote_ThatTheMapAndLookShow()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var game = vm.Game;
        var at = game.Level.AllLocs().First(p => game.Level.IsEmptyFloor(p) && game.Known.IsKnown(p) && p.ChebyshevTo(game.Player.Position) > 3);
        vm.ClickCell(at, secondary: true);
        Assert.Contains("Pin a note here...", vm.MenuLabels);
        vm.PromptKey((char)('a' + vm.MenuLabels.ToList().IndexOf("Pin a note here...")));
        Assert.True(vm.IsInscribing);
        vm.InscriptionText = "the Alchemist owes me";
        vm.CommitInscription();
        Assert.True(vm.HasPin(at.X, at.Y));
        Assert.Equal("the Alchemist owes me", game.PinAt(at));
        vm.HoverCell(at);
        Assert.Contains("Pinned: \"the Alchemist owes me\"", vm.HoverText);
        TileRenderingTests.Save(window, "map-pin");

        vm.ClickCell(at, secondary: true);
        Assert.Contains("Remove the pin", vm.MenuLabels);
        vm.PromptKey((char)('a' + vm.MenuLabels.ToList().IndexOf("Remove the pin")));
        Assert.False(vm.HasPin(at.X, at.Y));
    }
}
