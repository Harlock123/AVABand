using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
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

/// <summary>The map's right-click menus (Angband ui-context.c).</summary>
public class ContextMenuUiTests
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

    private static Angband.Core.Monsters.Monster Jackal(MainWindowViewModel vm, int distance, Loc? not = null)
    {
        var game = vm.Game;
        var at = game.Level.AllLocs().First(l => l.DistanceTo(game.Player.Position) == distance && game.Level.IsEmptyFloor(l)
            && l != not && Angband.Core.Combat.ProjectionPath.Projectable(game.Level, game.Player.Position, l, 20));
        var m = new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster("jackal")!, at, asleep: true)!;
        m.Hp = m.MaxHp = 10_000;
        game.UpdateView();
        return m;
    }

    private static void Pick(MainWindowViewModel vm, string label)
    {
        var index = vm.MenuLabels.ToList().IndexOf(label);
        Assert.True(index >= 0, $"no '{label}' in [{string.Join(", ", vm.MenuLabels)}]");
        vm.PromptKey((char)('a' + index));
    }

    [AvaloniaFact]
    public void RightClickingYourself_OffersThePlayerMenu_AndOther()
    {
        var (_, vm) = Open();
        vm.ClickCell(vm.Game.Player.Position, secondary: true);
        Assert.True(vm.IsPrompting);
        Assert.Equal(["Use", "Look", "Rest", "Character", "Other"], vm.MenuLabels);

        Pick(vm, "Other");
        Assert.True(vm.IsPrompting);
        Assert.Equal(["Knowledge", "Your journey", "Quest log", "Explore", "Show map", "Show messages", "Show monster list", "Show object list",
            "Toggle ignored", "Ignore an item", "Clear out junk", "Tidy pack", "Options", "Commands"], vm.MenuLabels);
        vm.CancelPrompt();
        Assert.False(vm.IsPrompting);
        Assert.Empty(vm.MenuLabels);
    }

    [AvaloniaFact]
    public void RightClickingAMonsterNextToYou_OffersAttack_AndAttackAttacks()
    {
        var (window, vm) = Open();
        var jackal = Jackal(vm, 1);
        vm.ClickCell(jackal.Position, secondary: true);
        TileRenderingTests.Save(window, "context-menu");
        Assert.StartsWith("The jackal", vm.LastMessage); // what is there, as looking says it
        Assert.Equal(["Look at", "Recall info", "Use item on", "Attack", "Walk towards", "Fire on", "Throw to"], vm.MenuLabels);

        Pick(vm, "Attack");
        Assert.False(vm.IsPrompting);
        Assert.True(jackal.Hp < jackal.MaxHp || vm.LastMessage.Contains("miss", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void FireOn_ShootsTheMonsterChosen_NotTheNearest_EvenWithoutUseOldTarget()
    {
        var (_, vm) = Open();
        vm.Game.SetOption(OptionIds.UseOldTarget, false);
        var near = Jackal(vm, 2);
        var far = Jackal(vm, 5);
        var shots = new List<MissileFiredEvent>();
        vm.Game.Events.Subscribe<MissileFiredEvent>(shots.Add);

        vm.ClickCell(far.Position, secondary: true);
        Assert.Contains("Pathfind to", vm.MenuLabels);
        Pick(vm, "Fire on");
        if (vm.IsPrompting) vm.PromptKey('a'); // which ammunition, if asked
        var shot = Assert.Single(shots);
        Assert.Contains(far.Position, shot.Path.Concat([shot.Path[^1]]));
        Assert.Same(far, vm.Game.TargetMonster);
        Assert.False(vm.Game.AimAtTargetNext); // just once
        Assert.NotSame(near, vm.Game.TargetMonster);
    }

    [AvaloniaFact]
    public void PathfindTo_Travels_AndAClickOnALinePicksIt()
    {
        var (window, vm) = Open();
        var game = vm.Game;
        var start = game.Player.Position;
        var goal = game.Level.AllLocs().First(l => l.DistanceTo(start) == 4 && game.Level.IsEmptyFloor(l) && game.Known.IsKnown(l)
            && Angband.Core.Combat.ProjectionPath.Projectable(game.Level, start, l, 20));

        vm.ClickCell(goal, secondary: true);
        window.CaptureRenderedFrame();
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "ChoiceList");
        var row = list.GetVisualDescendants().OfType<ListBoxItem>()
            .Single(i => i.DataContext is ChoiceRow { Text: "Pathfind to" });
        var centre = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);

        Assert.False(vm.IsPrompting);
        Assert.True(game.Player.Position.DistanceTo(goal) < start.DistanceTo(goal));
    }
}

/// <summary>The right-click menu opens beside the pointer, as Angband's context menus do.</summary>
public class ContextMenuPlacementUiTests
{
    [AvaloniaFact]
    public void ARightClick_OpensTheMenuBesideThePointer_AndItGoesBackToTheTopAfterwards()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        TestKit.Give(vm);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        vm.Game.MarkDebugUsed();
        vm.HandleAction(InputAction.JumpNextLevel); // a level bigger than the view, so you are in the middle
        window.CaptureRenderedFrame();

        var map = window.GetVisualDescendants().OfType<Angband.Avalonia.Controls.MapView>().First(m => m.Name == "Map");
        var cell = map.Renderer.CellSize;
        var me = vm.Game.Player.Position - map.ViewOffset; // your square, wherever the view has put it
        var you = map.TranslatePoint(new Point((me.X + 0.5) * cell.Width, (me.Y + 0.5) * cell.Height), window)!.Value;
        window.MouseDown(you, MouseButton.Right);
        window.MouseUp(you, MouseButton.Right);
        Assert.Contains("Rest", vm.MenuLabels); // your own menu
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "context-menu-at-pointer");

        var box = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PromptBox");
        var topLeft = box.TranslatePoint(new Point(0, 0), window)!.Value;
        var right = topLeft.X + box.Bounds.Width;
        Assert.True(topLeft.X - you.X is >= 0 and <= 40 || you.X - right is >= 0 and <= 40,
            $"box {topLeft.X:0}..{right:0}, pointer {you.X:0}"); // beside the square (on the left near the right edge)
        // Level with it, or (near the bottom of the window) moved up just enough to fit, still beside it.
        var bottom = topLeft.Y + box.Bounds.Height;
        Assert.True(Math.Abs(topLeft.Y - you.Y) <= 40 || (bottom <= window.Bounds.Height && topLeft.Y <= you.Y && you.Y <= bottom),
            $"box {topLeft.Y:0}..{bottom:0}, pointer {you.Y:0}, window {window.Bounds.Height:0}");

        vm.CancelPrompt();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(global::Avalonia.Layout.HorizontalAlignment.Center, box.HorizontalAlignment);
        Assert.Equal(420, box.MinWidth);
    }
}

/// <summary>The sidebar's monster recall (Angband's monster recall subwindow).</summary>
public class RecallPanelUiTests
{
    [AvaloniaFact]
    public void LookingAtAMonster_ShowsItsRecall_InTheSidebar_UntilSwitchedOff()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings();
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        TestKit.Give(vm);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var game = vm.Game;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        vm.Execute(new HoldCommand());
        TestKit.OpenGround(vm);
        Assert.False(vm.HasRecallPanel); // nothing tracked yet

        var at = game.Level.AllLocs().First(l => l.DistanceTo(game.Player.Position) == 3 && game.Level.IsEmptyFloor(l)
            && Angband.Core.Combat.ProjectionPath.Projectable(game.Level, game.Player.Position, l, 20));
        var jackal = new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster("jackal")!, at, asleep: true)!;
        game.UpdateView();

        vm.HandleAction(InputAction.Look);
        Assert.True(vm.HasRecallPanel);
        Assert.Equal(jackal.Race.Name, vm.RecallPanelTitle);
        Assert.Equal(game.Recall(jackal.Race), vm.RecallPanelText);
        var panel = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "RecallPanel");
        window.CaptureRenderedFrame();
        Assert.True(panel.IsEffectivelyVisible);
        TileRenderingTests.Save(window, "recall-panel");

        vm.HandleAction(InputAction.Cancel); // stops looking; the jackal stays tracked
        Assert.True(vm.HasRecallPanel);

        vm.ToggleRecallPanelCommand.Execute(null);
        Assert.False(vm.HasRecallPanel);
        Assert.False(settings.ShowRecallPanel);
        vm.ToggleRecallPanelCommand.Execute(null);
        Assert.True(vm.HasRecallPanel);

        game.IncreaseTimed(Angband.Core.Effects.TimedIds.Image, 50); // no recall of visions
        vm.Execute(new HoldCommand());
        Assert.False(vm.HasRecallPanel);
    }
}
