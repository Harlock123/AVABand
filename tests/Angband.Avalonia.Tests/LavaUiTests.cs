using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

/// <summary>Angband 4.2.5 asks before a step into lava that would cost more than a third of your hit points.</summary>
public class LavaUiTests
{
    [AvaloniaFact]
    public void A_scalding_step_asks_and_no_keeps_you_out()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var game = vm.Game;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        var dir = DirectionExtensions.Compass.First(d => game.Level.IsEmptyFloor(game.Player.Position.Step(d)));
        var lava = game.Player.Position.Step(dir);
        game.Level[lava].Feature = game.Data.Terrain.Ids.Lava;
        var start = game.Player.Position;

        vm.Execute(new WalkCommand(dir));
        Assert.True(vm.IsConfirming);
        Assert.Equal("The lava will scald you!  Really step in? (y/n)", vm.LastMessage);
        window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);
        Assert.Equal(start, game.Player.Position);

        vm.Execute(new WalkCommand(dir));
        window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
        Assert.True(game.Player.Position == lava || game.Player.IsDead);
    }
}
