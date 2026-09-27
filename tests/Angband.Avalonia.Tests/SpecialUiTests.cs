using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

public class SpecialUiTests
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

    [AvaloniaFact]
    public void Banishment_AsksForAMonsterLetter()
    {
        var (window, vm) = Open();
        var game = vm.Game;
        var spawner = new Angband.Core.Monsters.MonsterSpawner(game.Data);
        var spot = game.Level.AllLocs().First(l => l.DistanceTo(game.Player.Position) is > 3 and < 8 && game.Level.IsEmptyFloor(l));
        game.Scheduler.Add(spawner.Place(game.Level, game.Rng, game.Data.Monster("jackal")!, spot));
        var scroll = game.Objects.Create("scroll_of_banishment");
        game.Knowledge.LearnKind(scroll.Kind);
        scroll = game.Player.Inventory.Add(scroll)!;

        window.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.None);
        vm.PromptKey(vm.PromptRows.Single(r => r.Item == scroll).Letter[0]);
        Assert.True(vm.IsChoosingGlyph);
        Assert.Equal("Choose a monster race (by symbol) to banish:", vm.LastMessage);

        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Shift); // 'C': dogs
        Assert.False(vm.IsChoosingGlyph);
        Assert.DoesNotContain(game.Level.Monsters.All, m => m.Race.Id == "jackal");
        Assert.False(game.Player.Inventory.Contains(scroll));
    }

    [AvaloniaFact]
    public void InAShape_ItemCommandsOfferToChangeBack()
    {
        var (window, vm) = Open();
        vm.Game.Shapechange("bat");
        vm.Execute(new HoldCommand());
        Assert.Contains("Form: bat", vm.StatusText);

        window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.None); // quaff
        Assert.True(vm.IsConfirming);
        Assert.StartsWith("You cannot do this while in bat form.", vm.LastMessage);
        window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
        Assert.Null(vm.Game.Player.Shape);
        Assert.DoesNotContain("Form:", vm.StatusText);
    }
}
