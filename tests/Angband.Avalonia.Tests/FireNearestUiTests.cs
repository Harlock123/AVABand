using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

/// <summary>Angband's fire at nearest (h / Tab) and target closest (').</summary>
public class FireNearestUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior"); // a sling and iron shots
        TestKit.Give(vm);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        return (window, vm);
    }

    private static Angband.Core.Monsters.Monster Jackal(MainWindowViewModel vm, int distance)
    {
        var game = vm.Game;
        var at = game.Level.AllLocs().First(l => l.DistanceTo(game.Player.Position) == distance && game.Level.IsEmptyFloor(l)
            && Angband.Core.Combat.ProjectionPath.Projectable(game.Level, game.Player.Position, l, 20));
        var m = new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster("jackal")!, at, asleep: true)!;
        m.Hp = m.MaxHp = 10_000;
        game.UpdateView();
        return m;
    }

    [AvaloniaFact]
    public void Tab_FiresAtTheNearestMonster()
    {
        var (window, vm) = Open();
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Assert.Equal("No Available Target.", vm.LastMessage);

        var far = Jackal(vm, 5);
        var near = Jackal(vm, 2);
        var shots = new List<MissileFiredEvent>();
        vm.Game.Events.Subscribe<MissileFiredEvent>(shots.Add);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Assert.Single(shots);
        Assert.Same(near, vm.Game.TargetMonster);
        Assert.NotSame(far, vm.Game.TargetMonster);
    }

    [AvaloniaFact]
    public void WithoutALauncher_ItSaysSo()
    {
        var (window, vm) = Open();
        var sling = vm.Game.Player.Inventory.Bow!;
        vm.Game.Player.Inventory.TakeOff(sling);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
        Assert.Equal("You have nothing to fire with.", vm.LastMessage);
    }

    [AvaloniaFact]
    public void Apostrophe_TargetsTheClosest()
    {
        var (window, vm) = Open();
        var near = Jackal(vm, 3);
        window.KeyPressQwerty(PhysicalKey.Quote, RawInputModifiers.None);
        Assert.Same(near, vm.Game.TargetMonster);
        Assert.Contains(vm.Messages, m => m.EndsWith("is targeted."));
    }
}
