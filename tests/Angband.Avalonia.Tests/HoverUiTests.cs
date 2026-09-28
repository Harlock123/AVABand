using Angband.Avalonia.Controls;
using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Geometry;
using Angband.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

/// <summary>Hover to look: the square under the mouse, described at the foot of the map.</summary>
public class HoverUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        vm.Game.UpdateView();
        return (window, vm);
    }

    [AvaloniaFact]
    public void TheSquareUnderTheMouse_IsDescribed_WithoutTouchingTheMessages()
    {
        var (_, vm) = Open();
        var game = vm.Game;
        var message = vm.LastMessage;
        var stairs = game.Level.FindFeature(Angband.Core.Definitions.TerrainFlags.DownStair).First();
        vm.HoverCell(stairs);
        Assert.Equal("A down staircase", vm.HoverText);
        Assert.Equal(message, vm.LastMessage);

        var at = game.Level.AllLocs().First(l => l.ChebyshevTo(game.Player.Position) == 2 && game.Level.IsEmptyFloor(l));
        var jackal = new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster("jackal")!, at, asleep: true)!;
        game.UpdateView();
        vm.HoverCell(at);
        Assert.StartsWith("The jackal", vm.HoverText);

        vm.HoverCell(null);
        Assert.Null(vm.HoverText);

        vm.SetOption(DisplayOptions.HoverLook, false);
        vm.HoverCell(stairs);
        Assert.Null(vm.HoverText);
    }

    [AvaloniaFact]
    public void MovingTheMouse_OverTheMap_Hovers()
    {
        var (window, vm) = Open();
        var map = window.GetVisualDescendants().OfType<MapView>().First(m => m.Name == "Map");
        var centre = map.TranslatePoint(new Point(map.Bounds.Width / 2, map.Bounds.Height / 2), window)!.Value;
        window.MouseMove(centre);
        Assert.NotNull(vm.HoverText); // the middle of the map is you, in town (known)
        window.MouseMove(new Point(2, 2)); // over the menu bar, off the map
        Assert.Null(vm.HoverText);
    }

    [AvaloniaFact]
    public void TheInterfaceSize_ScalesEverythingButTheMap_AndDialogs()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings();
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        var sidebar = window.GetVisualDescendants().OfType<Border>().First(b => b.Width == 340);
        var map = window.GetVisualDescendants().OfType<MapView>().First(m => m.Name == "Map");
        var mapFont = map.CellFontSize;
        Assert.Equal(340, sidebar.Bounds.Width, 1);

        vm.InterfaceScale = 1.5;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var scaled = sidebar.TransformToVisual(window)!.Value.Transform(new Point(sidebar.Bounds.Width, 0)).X
                     - sidebar.TransformToVisual(window)!.Value.Transform(new Point(0, 0)).X;
        Assert.Equal(510, scaled, 1);                   // the sidebar, half as big again
        Assert.Equal(mapFont, map.CellFontSize);         // the map is left to its own zoom
        Assert.Equal(1.5, settings.InterfaceScale);      // and kept for next time

        vm.ShowKeyCommands();
        var dialog = window.OwnedWindows.OfType<KeyCommandsWindow>().Last();
        Assert.Equal(860 * 1.5, dialog.Width, 1);

        vm.InterfaceScale = 5;                           // out of range: held at 200%
        Assert.Equal(2.0, vm.InterfaceScale);
        vm.InterfaceScale = 1.0;
    }

    [AvaloniaFact]
    public void TheStatusBar_WrapsBetweenFields_WhenItDoesNotFit()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.StartGame(42, "mage");
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        var bar = window.GetVisualDescendants().OfType<ItemsControl>().First(c => c.Name == "StatusBar");
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var oneLine = bar.Bounds.Height;
        Assert.Contains(vm.StatusFields, f => f.StartsWith("HP ", StringComparison.Ordinal));
        Assert.Equal(vm.StatusText.Split("  |  ").Length, vm.StatusFields.Count);

        vm.InterfaceScale = 1.6;
        window.Width = 1000;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(bar.Bounds.Height > oneLine * 1.5, $"{bar.Bounds.Height} vs {oneLine}"); // a second line
        var shown = bar.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.All(vm.StatusFields, f => Assert.Contains(f, shown));                       // every field whole
        vm.InterfaceScale = 1.0;
    }
}
