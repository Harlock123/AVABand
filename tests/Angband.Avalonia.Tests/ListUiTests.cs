using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

public class ListUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open(AppSettings? settings = null)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings ?? new(), save: null);
        vm.StartGame(42);
        TestKit.Give(vm);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    private static void Populate(MainWindowViewModel vm)
    {
        var game = vm.Game;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        var p = game.Player.Position;
        var spots = game.Level.AllLocs().Where(l => l.DistanceTo(p) is > 1 and < 6 && game.Level.IsEmptyFloor(l)
                                                    && game.Level[l].Has(Angband.Core.World.SquareFlags.Seen)).ToList();
        var spawner = new Angband.Core.Monsters.MonsterSpawner(game.Data);
        spawner.Place(game.Level, game.Rng, game.Data.Monster("jackal")!, spots[0], asleep: true);
        spawner.Place(game.Level, game.Rng, game.Data.Monster("jackal")!, spots[1], asleep: false);
        spawner.Place(game.Level, game.Rng, game.Data.Monster("grip")!, spots[2], asleep: false);
        game.Level.Objects.Add(spots[3], game.Objects.Create("dagger"));
        game.Level.Objects.Add(spots[4], game.Objects.Create("cure_light_wounds", 2));
        game.UpdateView();
    }

    [AvaloniaFact]
    public void Bracket_ShowsTheMonsterList_XResorts_AnyKeyCloses()
    {
        var (window, vm) = Open();
        Populate(vm);
        window.KeyPressQwerty(PhysicalKey.BracketLeft, RawInputModifiers.None);
        Assert.True(vm.IsShowingList);
        Assert.StartsWith("You can see 3 monsters", vm.ListRows[0].Text);
        Assert.Contains(vm.ListRows, r => r.Text.Contains("2 jackals (1 asleep)"));
        Assert.Contains("turn ON 'sort by exp'", vm.ListFooter);
        TileRenderingTests.Save(window, "monster-list");

        window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.None);
        Assert.True(vm.IsShowingList);
        Assert.Contains("turn OFF", vm.ListFooter);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(vm.IsShowingList);
    }

    [AvaloniaFact]
    public void TheObjectList_ShowsWhatIsAround()
    {
        var (window, vm) = Open();
        Populate(vm);
        window.KeyPressQwerty(PhysicalKey.BracketRight, RawInputModifiers.None);
        Assert.True(vm.IsShowingList);
        Assert.Contains(vm.ListRows, r => r.Text.Contains("a Dagger") && r.Location.Length > 0);
        Assert.Contains(vm.ListRows, r => r.Text.Contains("2 Potions"));
        TileRenderingTests.Save(window, "object-list");
        window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.None); // any key closes (and does nothing else)
        Assert.False(vm.IsShowingList);
        Assert.False(vm.IsPrompting);
    }

    [AvaloniaFact]
    public void TheKeysAreBracketsByDefault()
    {
        var bindings = InputBindings.Defaults();
        Assert.Contains("Char:[", bindings.KeysFor(InputAction.MonsterList));
        Assert.Contains("Char:]", bindings.KeysFor(InputAction.ObjectList));
    }

    [AvaloniaFact]
    public void TheSidebarLists_FollowTheGame_AndAreRemembered()
    {
        var settings = new AppSettings();
        var (window, vm) = Open(settings);
        Populate(vm);
        Assert.Empty(vm.MonsterPanelRows);
        vm.ToggleMonsterPanelCommand.Execute(null);
        vm.ToggleObjectPanelCommand.Execute(null);
        Assert.True(settings.ShowMonsterPanel && settings.ShowObjectPanel);
        Assert.Contains(vm.MonsterPanelRows, r => r.Text.Contains("Grip"));
        Assert.Contains(vm.ObjectPanelRows, r => r.Text.Contains("Dagger"));
        TileRenderingTests.Save(window, "list-panels");

        // Kept current as the game moves on.
        var game = vm.Game;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        vm.Execute(new Angband.Core.Game.HoldCommand());
        Assert.Equal("You can see no monsters.", vm.MonsterPanelRows.Single().Text);

        var (_, again) = Open(settings);
        Assert.True(again.ShowMonsterPanel);
    }
}
