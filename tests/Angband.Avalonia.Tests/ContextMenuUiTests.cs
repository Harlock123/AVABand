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
        Assert.Equal(["Knowledge", "Show map", "Show messages", "Show monster list", "Show object list",
            "Toggle ignored", "Ignore an item", "Options", "Commands"], vm.MenuLabels);
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
