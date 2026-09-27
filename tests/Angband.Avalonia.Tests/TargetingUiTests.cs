using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

public class TargetingUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        return (window, vm);
    }

    private static Angband.Core.Monsters.Monster Place(MainWindowViewModel vm, string race, int distance)
    {
        var game = vm.Game;
        var p = game.Player.Position;
        var spot = game.Level.AllLocs().First(l => l.DistanceTo(p) == distance && game.Level.IsEmptyFloor(l)
                                                  && game.Level[l].Has(Angband.Core.World.SquareFlags.Seen)
                                                  && Angband.Core.Combat.ProjectionPath.Projectable(game.Level, p, l, 20));
        var m = new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster(race)!, spot, asleep: true);
        game.Scheduler.Add(m);
        game.UpdateView();
        return m;
    }

    [AvaloniaFact]
    public void Star_CyclesMonsters_AndT_Targets()
    {
        var (window, vm) = Open();
        var near = Place(vm, "jackal", 2);
        var far = Place(vm, "jackal", 4);

        window.KeyPressQwerty(PhysicalKey.NumPadMultiply, RawInputModifiers.None); // '*'
        Assert.True(vm.IsTargeting);
        Assert.Equal(near.Position, vm.Cursor);
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Assert.Equal(far.Position, vm.Cursor);
        TileRenderingTests.Save(window, "targeting-cursor");

        window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.None);
        Assert.False(vm.IsTargeting);
        Assert.Same(far, vm.Game.TargetMonster);
        Assert.Equal(far.Position, vm.Target);
        Assert.Contains("Target: the jackal", vm.StatusText);
    }

    [AvaloniaFact]
    public void P_FreesTheCursor_WhichDescribesSquares()
    {
        var (window, vm) = Open();
        vm.HandleAction(Angband.Input.InputAction.Target); // '*' with no monsters: a free cursor
        Assert.Equal(vm.Game.Player.Position, vm.Cursor);
        window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.None); // south
        Assert.Equal(vm.Game.Player.Position + new Loc(0, 1), vm.Cursor);
        Assert.Equal(vm.Cursor, vm.Focus);
        Assert.StartsWith("You see", vm.LastMessage);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal(vm.Game.Player.Position + new Loc(0, 1), vm.Game.TargetLocation);
        Assert.Equal(vm.Game.Player.Position, vm.Focus);
    }

    [AvaloniaFact]
    public void Look_CanTargetToo_AndClicksPickTheSquare()
    {
        var (window, vm) = Open();
        var dog = Place(vm, "jackal", 3);
        window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.None);
        Assert.Equal(CursorMode.Look, vm.CursorMode);
        vm.ClickCell(dog.Position, secondary: false);
        Assert.Same(dog, vm.Game.TargetMonster);
        Assert.False(vm.IsLooking);
    }

    [AvaloniaFact]
    public void T_InADirectionPrompt_AimsAtTheTarget()
    {
        var (window, vm) = Open();
        vm.Game.Player.SkillDevice = 500;
        var east = vm.Game.Player.Position + new Loc(3, 0);
        vm.Game.SetTarget(east);
        var wand = vm.Game.Objects.Create("wand_of_stone_to_mud");
        wand.Charges = 5;
        vm.Game.Knowledge.LearnKind(wand.Kind);
        vm.Game.Player.Inventory.Add(wand);

        for (var i = 0; i < 3 && wand.Charges == 5; i++)
        {
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
            vm.PromptKey(vm.PromptRows.Single(r => r.Item == wand).Letter[0]);
            Assert.True(vm.IsAwaitingDirection);
            window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.None);
        }
        Assert.Equal(4, wand.Charges);
        Assert.False(vm.IsAwaitingDirection);
    }
}
