using Angband.Avalonia.Controls;
using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Data;
using Angband.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

public class InputUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open(ulong seed = 42)
    {
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(seed, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    [AvaloniaFact]
    public void ArrowsAndEnter_NavigateMenus()
    {
        var (window, vm) = Open();
        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None); // drop menu: several items
        Assert.True(vm.PromptRows.Count > 2);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.Equal(2, vm.PromptSelectedIndex);
        var third = vm.PromptRows[2].Item;

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.False(vm.IsPrompting);
        Assert.True(vm.FloorRows.Any(r => r.Item == third),
            $"last message: {vm.LastMessage}; floor: {string.Join(",", vm.FloorRows.Select(r => r.Name))}; third: {third}");
    }

    [AvaloniaFact]
    public void GamepadActions_DriveMenusAndTheContextAction()
    {
        var (_, vm) = Open(7);
        vm.HandleAction(InputAction.Quaff);
        Assert.True(vm.IsPrompting);
        vm.HandleAction(InputAction.Cancel);
        Assert.False(vm.IsPrompting);

        // Stand on the stairs and press Confirm (the A button): down we go.
        var game = typeof(MainWindowViewModel).GetField("_game", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(vm) as Angband.Core.Game.GameSession;
        game!.Player.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();
        vm.HandleAction(InputAction.Confirm);
        Assert.Contains("250 ft", vm.StatusText.Replace("50 ft (L1)", "250 ft")); // at depth 1 (50 ft)
        Assert.Equal(1, game.Player.Depth);
    }

    [AvaloniaFact]
    public void Keys_CanBeRebound_InTheControlsTab()
    {
        var (window, vm) = Open();
        var settings = window.OpenSettings();
        settings.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 2;
        TileRenderingTests.Save(settings, "settings-controls");
        var row = vm.BindingRows.Single(r => r.Action == InputAction.Rest);

        vm.RebindKeyCommand.Execute(row);
        Assert.True(vm.IsCapturingKey);
        settings.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);

        Assert.False(vm.IsCapturingKey);
        Assert.Equal(InputAction.Rest, vm.Bindings.ForKey("F2"));
        Assert.Contains("F2", vm.BindingRows.Single(r => r.Action == InputAction.Rest).Keys);
    }

    [AvaloniaFact]
    public void ClickingTheMap_TravelsThere()
    {
        var (window, vm) = Open(3);
        var map = window.GetVisualDescendants().OfType<MapView>().First();
        window.CaptureRenderedFrame(); // lay out and render once so the camera is known

        var game = typeof(MainWindowViewModel).GetField("_game", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(vm) as Angband.Core.Game.GameSession;
        foreach (var m in game!.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m); // town folk would interrupt travel
        var start = game.Player.Position;
        var target = game.Level.AllLocs()
            .Where(p => game.Level.IsPassable(p) && game.Known.IsKnown(p) && p.DistanceTo(start) is > 2 and < 6)
            .OrderBy(p => p.Y).ThenBy(p => p.X).First();

        // Translate the target square into a point in the window.
        var cell = map.Renderer.CellSize;
        var fx = game.Player.Position; // the camera centres on the player when the map is larger than the view
        var cols = (int)(map.Bounds.Width / cell.Width);
        var rows = (int)(map.Bounds.Height / cell.Height);
        double Origin(int size, int view, double px, double c) => size <= view ? Math.Floor((px - size * c) / 2) : 0;
        int Offset(int size, int view, int focus) => size <= view ? 0 : Math.Clamp(focus - view / 2, 0, size - view);
        var x = Origin(game.Level.Width, cols, map.Bounds.Width, cell.Width) + (target.X - Offset(game.Level.Width, cols, fx.X) + 0.5) * cell.Width;
        var y = Origin(game.Level.Height, rows, map.Bounds.Height, cell.Height) + (target.Y - Offset(game.Level.Height, rows, fx.Y) + 0.5) * cell.Height;
        var point = map.TranslatePoint(new Point(x, y), window)!.Value;

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        Assert.Equal(target, game.Player.Position);
    }

    /// <summary>Shift+arrow runs: one key press takes the player the length of a corridor.</summary>
    [AvaloniaFact]
    public void ShiftArrow_RunsDownACorridor()
    {
        var (window, vm) = Open();
        var game = vm.Game;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);

        // Carve a known corridor running east from the player, ten squares long (somewhere it fits).
        game.Player.Position = new Loc(Math.Clamp(game.Player.Position.X, 2, game.Level.Width - 14),
            Math.Clamp(game.Player.Position.Y, 2, game.Level.Height - 3));
        var start = game.Player.Position;
        var terrain = game.Data.Terrain.Ids;
        for (var dx = -1; dx <= 11; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            var p = new Loc(start.X + dx, start.Y + dy);
            if (!game.Level.InBounds(p)) continue;
            game.Level[p].Feature = dy == 0 && dx is >= 0 and <= 10 ? terrain.Floor : terrain.Granite;
            game.Level[p].Trap = 0;
            foreach (var item in game.Level.Objects.At(p).ToList()) game.Level.Objects.Remove(p, item);
        }
        game.Known.RememberAll(game.Level);
        game.UpdateView();

        // Drawn a step at a time: the first step at once, the rest one after another (held here between steps).
        vm.RunStepDelay = TimeSpan.FromHours(1);
        window.KeyPress(Key.Right, RawInputModifiers.Shift, PhysicalKey.ArrowRight, null);
        Assert.Equal(start + new Loc(1, 0), game.Player.Position);
        Assert.True(vm.IsRunningSteps);
        vm.RunStepNow();
        vm.RunStepNow();
        Assert.Equal(start + new Loc(3, 0), game.Player.Position);
        // The key that started it, repeating while held, leaves it be.
        window.KeyPress(Key.Right, RawInputModifiers.Shift, PhysicalKey.ArrowRight, null);
        Assert.True(vm.IsRunningSteps);
        Assert.Equal(start + new Loc(3, 0), game.Player.Position);
        window.KeyRelease(Key.Right, RawInputModifiers.Shift, PhysicalKey.ArrowRight, null);

        // Any other key stops it where it is, and does nothing more.
        var turn = game.GameTurn;
        window.KeyPressQwerty(PhysicalKey.Digit5, RawInputModifiers.None);
        Assert.False(vm.IsRunningSteps);
        Assert.Equal(turn, game.GameTurn);
        vm.RunStepNow();
        Assert.Equal(start + new Loc(3, 0), game.Player.Position);

        // On again, to the end.
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        vm.FinishRun();
        Assert.False(vm.IsRunningSteps);
        Assert.Equal(start + new Loc(10, 0), game.Player.Position);

        // With the option off, the whole run at once.
        vm.SetOption(DisplayOptions.RunStepByStep, false);
        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Shift);
        Assert.False(vm.IsRunningSteps);
        Assert.Equal(start, game.Player.Position);
    }

    /// <summary>In a menu, Shift+arrow moves the selection as the arrow does, rather than closing the menu.</summary>
    [AvaloniaFact]
    public void ShiftArrow_InAMenu_MovesTheSelection()
    {
        var (window, vm) = Open();
        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None);
        Assert.True(vm.PromptRows.Count > 2);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Assert.True(vm.IsPrompting);
        Assert.Equal(1, vm.PromptSelectedIndex);
    }

    [AvaloniaFact]
    public void Up_and_Right_together_step_north_east()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42, "warrior");
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        // (The pair's timing held still, so a slow machine can't let the first arrow go alone.)
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760, ArrowClock = () => TimeSpan.Zero };
        window.Show();
        TestKit.OpenGround(vm);
        var from = vm.Game.Player.Position;

        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Assert.Equal(from + new Angband.Core.Geometry.Loc(1, -1), vm.Game.Player.Position);

        // A single tap still steps straight.
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.Equal(from + new Angband.Core.Geometry.Loc(1, 0), vm.Game.Player.Position);
    }

    /// <summary>
    /// Two arrows together answer "Direction?" diagonally too — disarming, opening, closing, tunnelling,
    /// aiming — not only when walking (a keyboard with no keypad has no other diagonal arrows).
    /// </summary>
    [AvaloniaFact]
    public void Up_and_Right_together_answer_a_direction_prompt_diagonally()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42, "warrior");
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760, ArrowClock = () => TimeSpan.Zero };
        window.Show();
        TestKit.OpenGround(vm);
        var game = vm.Game;
        var ne = game.Player.Position + new Angband.Core.Geometry.Loc(1, -1);
        var messages = new List<string>();
        game.Events.Subscribe<Angband.Core.Game.MessageEvent>(m => messages.Add(m.Text));

        // A trap to the north-east: D, then Up and Right together.
        game.Level[ne].Trap = game.Data.Traps.First(t => !t.Warding && t.Id != "rune").Index;
        game.Level[ne].Flags |= Angband.Core.World.SquareFlags.TrapVisible;
        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Shift);
        Assert.True(vm.IsAwaitingDirection);
        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        Assert.True(vm.IsAwaitingDirection);                                    // (the first arrow waits for a partner)
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Assert.False(vm.IsAwaitingDirection);
        Assert.DoesNotContain(messages, m => m.Contains("nothing there to disarm", StringComparison.OrdinalIgnoreCase));
        Assert.True(game.Level[ne].Trap == 0 || messages.Count > 0, string.Join(" | ", messages)); // (the north-east trap was what it went for)
        Assert.True(messages.Any(m => m.StartsWith("You have disarmed", StringComparison.Ordinal) || m.StartsWith("You failed", StringComparison.Ordinal)
                                      || m.StartsWith("You set off", StringComparison.Ordinal) || m.StartsWith("You clear", StringComparison.Ordinal)), string.Join(" | ", messages));

        // And a single arrow still answers straight.
        var north = game.Player.Position + new Angband.Core.Geometry.Loc(0, -1);
        game.Level[north].Trap = game.Data.Traps.First(t => !t.Warding && t.Id != "rune").Index;
        game.Level[north].Flags |= Angband.Core.World.SquareFlags.TrapVisible;
        messages.Clear();
        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Shift);
        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        Assert.False(vm.IsAwaitingDirection);
        Assert.DoesNotContain(messages, m => m.Contains("nothing there to disarm", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>S searches carefully (AVABand's own), and says so when there's nothing to find.</summary>
    [AvaloniaFact]
    public void S_searches_carefully()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42, "warrior");
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var turn = vm.Game.GameTurn;
        window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Shift);
        Assert.Equal("You find nothing.", vm.LastMessage);
        Assert.True(vm.Game.GameTurn > turn);
    }

    /// <summary>'>' away from the stairs walks to the nearest known down staircase; '>' again takes it.</summary>
    [AvaloniaFact]
    public void GreaterThan_AwayFromTheStairs_WalksToThem_AndAgainTakesThem()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings();
        settings.Options[DisplayOptions.Scenes] = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null);
        vm.StartGame(42, "warrior");
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var game = vm.Game;
        var stairs = game.Level.FindFeature(Angband.Core.Definitions.TerrainFlags.DownStair).First();
        game.Player.Position = game.Level.AllLocs().First(p => game.Level.IsEmptyFloor(p) && p.DistanceTo(stairs) > 8 && game.FindPath(p, stairs) is not null);
        // (the > key; again if something interrupts the walk — a townsperson wandering into view)
        for (var i = 0; i < 10 && game.Player.Position != stairs; i++)
        {
            foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
            vm.HandleAction(Angband.Input.InputAction.StairsDown);
        }
        Assert.True(stairs == game.Player.Position, string.Join(" | ", vm.Messages.TakeLast(4)));
        Assert.Equal(0, game.Player.Depth);
        Assert.Contains(vm.Messages, m => m.StartsWith("You are on the down staircase", StringComparison.Ordinal));
        vm.HandleAction(Angband.Input.InputAction.StairsDown);
        Assert.Equal(1, game.Player.Depth);
    }
}
