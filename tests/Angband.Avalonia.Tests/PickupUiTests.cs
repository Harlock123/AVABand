using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

/// <summary>Picking up from a pile (AVABand's own): a) takes everything, the pile's things lettered from b).</summary>
public class PickupUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open(params string[] pile)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings();
        settings.Options[DisplayOptions.Hints] = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        var game = vm.Game;
        foreach (var kind in pile) game.Level.Objects.Add(game.Player.Position, game.Objects.Create(kind));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    private static int Underfoot(MainWindowViewModel vm) => vm.Game.Level.Objects.At(vm.Game.Player.Position).Count();

    [AvaloniaFact]
    public void A_PicksUpEverything_AndThePilesThingsStartAtB()
    {
        var (window, vm) = Open("dagger", "flask_of_oil", "cure_light_wounds");
        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.None);
        Assert.True(vm.IsPrompting);
        Assert.Equal("Pick up which item?", vm.PromptTitle);
        Assert.Equal(4, vm.PromptRows.Count);
        Assert.Equal(("a", "Everything here (3 things)"), (vm.PromptRows[0].Letter, vm.PromptRows[0].Name));
        Assert.True(vm.PromptRows[0].AllOfThem);
        Assert.Equal(["b", "c", "d"], vm.PromptRows.Skip(1).Select(r => r.Letter));
        TileRenderingTests.Save(window, "pickup-everything");

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        Assert.False(vm.IsPrompting);
        Assert.Equal(0, Underfoot(vm));
        Assert.Contains(vm.Game.Player.Inventory.Pack, i => i.Kind.Id == "cure_light_wounds");
    }

    [AvaloniaFact]
    public void B_PicksUpJustTheFirstThing()
    {
        var (window, vm) = Open("dagger", "flask_of_oil");
        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.None);
        var first = vm.PromptRows[1].Item;
        window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.None);
        Assert.Equal(1, Underfoot(vm));
        Assert.DoesNotContain(vm.Game.Level.Objects.At(vm.Game.Player.Position), i => ReferenceEquals(i, first));
    }

    [AvaloniaFact]
    public void ALoneThing_IsPickedUpWithoutAsking()
    {
        var (window, vm) = Open("dagger");
        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.None);
        Assert.False(vm.IsPrompting);
        Assert.Equal(0, Underfoot(vm));
    }
}
