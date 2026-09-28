using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

/// <summary>Repeat the last command (Angband 'n'; here Ctrl+V, the roguelike key).</summary>
public class RepeatUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open(string classId)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.StartGame(42, classId);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        return (window, vm);
    }

    private static void CtrlV(MainWindow window) => window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);

    [AvaloniaFact]
    public void WithNothingDone_ThereIsNothingToRepeat()
    {
        var (window, vm) = Open("warrior");
        CtrlV(window);
        Assert.Equal("There is no command to repeat.", vm.LastMessage);
    }

    [AvaloniaFact]
    public void Quaffing_Repeats_UntilTheStackRunsOut_AndWalkingDoesntReplaceIt()
    {
        var (window, vm) = Open("warrior");
        var game = vm.Game;
        var potions = game.Player.Inventory.Pack.First(i => i.Kind.Id == "cure_light_wounds");
        Assert.Equal(2, potions.Number);

        vm.Execute(new UseCommand(potions));
        Assert.Equal(1, potions.Number);
        vm.Execute(new WalkCommand(Angband.Core.Geometry.Direction.East)); // walking isn't remembered
        vm.Execute(new WalkCommand(Angband.Core.Geometry.Direction.West));
        Assert.IsType<UseCommand>(vm.LastCommand);

        var turn = game.GameTurn;
        CtrlV(window);
        Assert.True(game.GameTurn > turn);
        Assert.DoesNotContain(game.Player.Inventory.Pack, i => i.Kind.Id == "cure_light_wounds");

        CtrlV(window);
        Assert.StartsWith("You have no more ", vm.LastMessage);
        Assert.Contains("Cure Light Wounds", vm.LastMessage);
    }

    [AvaloniaFact]
    public void AMage_CastsTheSameSpellAgain()
    {
        var (window, vm) = Open("mage");
        var game = vm.Game;
        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Shift); // study
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);  // Magic Missile
        game.Player.MaxMana = game.Player.Mana = 20;
        // Something tough to shoot at, in the open east of the player.
        var at = game.Level.AllLocs().First(p => p.Y == game.Player.Position.Y && p.X > game.Player.Position.X + 1
                                                 && p.X < game.Player.Position.X + 5 && game.Level.IsEmptyFloor(p));
        new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster("bullroarer")!, at, asleep: true);
        game.UpdateView();

        vm.Execute(new CastCommand("magic_missile"));
        var mana = game.Player.Mana;
        Assert.True(mana < 20, $"{vm.LastMessage} mana {mana}/{game.Player.MaxMana}");

        CtrlV(window);
        Assert.True(game.Player.Mana < mana, vm.LastMessage);
        Assert.Equal(new CastCommand("magic_missile"), vm.LastCommand);
    }
}
