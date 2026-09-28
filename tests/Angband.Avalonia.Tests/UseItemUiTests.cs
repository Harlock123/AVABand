using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

/// <summary>Angband's use-an-item ('U', here X) and walk-into-a-trap ('W' / '-').</summary>
public class UseItemUiTests
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
        return (window, vm);
    }

    [AvaloniaFact]
    public void X_OffersEverythingUsable_AndUsesEachItsOwnWay()
    {
        var (window, vm) = Open();
        var game = vm.Game;
        var wand = game.Objects.Create("wand_of_stinking_cloud");
        wand.Charges = 3;
        game.Player.Inventory.Add(wand);
        window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Shift);
        Assert.True(vm.IsPrompting);
        Assert.Equal("Use which item?", vm.PromptTitle);
        var bases = vm.PromptRows.Select(r => r.Item.Base.Id).ToHashSet();
        Assert.Contains("potion", bases);
        Assert.Contains("scroll", bases);
        Assert.Contains("food", bases);
        Assert.Contains("wand", bases);
        Assert.DoesNotContain("sword", bases);

        // A potion is quaffed.
        var potion = vm.PromptRows.First(r => r.Item.Base.Id == "potion");
        var before = potion.Item.Number;
        window.KeyPressQwerty(PhysicalKey.A + (potion.Letter[0] - 'a'), RawInputModifiers.None);
        Assert.Equal(before - 1, potion.Item.Number);
    }

    [AvaloniaFact]
    public void Minus_WalksIntoATrap_OnPurpose()
    {
        var (window, vm) = Open();
        var game = vm.Game;
        var at = game.Level.AllLocs().First(l => l.ChebyshevTo(game.Player.Position) == 1 && game.Level.IsEmptyFloor(l)
            && game.Level.FeatureAt(l).Shop is null);
        game.Level[at].Trap = game.Data.Traps.Single(t => t.Id == "pit").Index;
        game.Level[at].Flags |= Angband.Core.World.SquareFlags.TrapVisible;
        vm.HandleAction(InputAction.WalkIntoTrap);
        Assert.True(vm.IsAwaitingDirection);
        var dir = Angband.Core.Geometry.DirectionExtensions.FromOffset(at.X - game.Player.Position.X, at.Y - game.Player.Position.Y);
        vm.HandleAction(InputActions.FromDirection(dir));
        Assert.Equal(at, game.Player.Position);
    }
}
