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

/// <summary>Showing what a click or a shot would do: the travel route, and the line of fire.</summary>
public class PathPreviewUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        vm.DismissHint();
        return (window, vm);
    }

    [AvaloniaFact]
    public void HoveringAKnownSquare_ShowsTheRouteAClickWouldTravel()
    {
        var (window, vm) = Open();
        var game = vm.Game;
        var goal = game.Level.AllLocs().First(l => l.DistanceTo(game.Player.Position) == 6 && game.Level.IsEmptyFloor(l)
            && game.Known.IsKnown(l) && game.FindPath(game.Player.Position, l) is { Count: > 0 });
        vm.HoverCell(goal);
        Assert.Empty(vm.ShownPath); // only while Shift is held

        window.KeyPressQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.Shift);
        Assert.True(vm.RouteKeyHeld);
        Assert.False(vm.PathIsAim);
        Assert.Equal(game.FindPath(game.Player.Position, goal), vm.ShownPath);
        Assert.Equal(goal, vm.ShownPath[^1]);
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "path-travel");

        window.KeyReleaseQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.None); // let go: gone
        Assert.False(vm.RouteKeyHeld);
        Assert.Empty(vm.ShownPath);
        vm.SetRouteKeyHeld(true);
        Assert.NotEmpty(vm.ShownPath);

        vm.HoverCell(null); // the mouse left the map
        Assert.Empty(vm.ShownPath);

        vm.SetOption(DisplayOptions.MouseMovement, false); // clicks don't move you: no route to show
        vm.HoverCell(goal);
        Assert.Empty(vm.ShownPath);
    }

    [AvaloniaFact]
    public void Targeting_ShowsTheLineOfFire_StoppingAtTheFirstMonsterInTheWay()
    {
        var (window, vm) = Open();
        var game = vm.Game;
        var me = game.Player.Position;
        // A straight line east with two squares of floor: a near jackal in front of a far one.
        var dir = new[] { new Loc(1, 0), new Loc(-1, 0), new Loc(0, 1), new Loc(0, -1) }
            .First(d => Enumerable.Range(1, 5).All(i => game.Level.IsEmptyFloor(me + new Loc(d.X * i, d.Y * i))));
        Angband.Core.Monsters.Monster Jackal(int i)
        {
            var m = new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster("jackal")!,
                me + new Loc(dir.X * i, dir.Y * i), asleep: true)!;
            return m;
        }
        var near = Jackal(2);
        var far = Jackal(5);
        game.UpdateView();

        vm.HandleAction(InputAction.Target); // steps through monsters: find the far one
        for (var i = 0; i < 3 && vm.Cursor != far.Position; i++) vm.HandleAction(InputAction.Target);
        Assert.Equal(far.Position, vm.Cursor);
        Assert.True(vm.PathIsAim);
        Assert.Equal(near.Position, vm.ShownPath[^1]); // the shot would hit the nearer one first
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "path-aim");

        vm.HandleAction(InputAction.Cancel);
        Assert.False(vm.IsLooking);
        Assert.False(vm.PathIsAim && vm.ShownPath.Count > 0);
    }
}
