using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

public class StoreUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, GameSession Game) OpenAt(string storeId)
    {
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var game = (GameSession)typeof(MainWindowViewModel)
            .GetField("_game", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(vm)!;
        game.Player.Position = game.Level.AllLocs().First(p => game.Level.FeatureAt(p).Shop == storeId);
        game.Player.Gold = 5000;
        vm.HandleAction(InputAction.EnterStore);
        return (window, vm, game);
    }

    [AvaloniaFact]
    public void Store_OpensWithItsStock()
    {
        var (window, vm, game) = OpenAt("alchemist");
        Assert.True(vm.IsInStore);
        Assert.Equal("Alchemy shop", vm.StoreTitle);
        Assert.Equal(game.StoreHere!.Stock.Count, vm.StoreRows.Count);
        Assert.Contains(vm.StoreRows, r => r.Name.Contains("Cure Light Wounds") && r.Price.EndsWith("gold"));
        Assert.StartsWith(game.StoreHere!.Owner!.Name, vm.StoreSubtitle); // one of store.txt's four keepers
        TileRenderingTests.Save(window, "store-alchemist");
    }

    /// <summary>
    /// Angband 4.2's default birth_no_selling: shops pay nothing, so the store says "Give" (as 4.2's
    /// "Give which item?"), explains why, and shows no price, rather than looking like a broken sale.
    /// </summary>
    [AvaloniaFact]
    public void WithNoSelling_TheStoreSaysGive_AndWhy()
    {
        var (window, vm, game) = OpenAt("general");
        Assert.True(game.NoSelling); // the default, as in 4.2
        Assert.Contains("Shops pay nothing (birth option \"no selling\")", vm.StoreSubtitle);
        Assert.Equal("For sale (letter to buy, Tab to give items)", vm.StoreModeText);

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Assert.StartsWith("Give (letter to give; the shop identifies it", vm.StoreModeText);
        var food = vm.StoreRows.First(r => r.Item.Kind.Id == "ration_of_food");
        Assert.Equal("", food.Price);

        var gold = game.Player.Gold;
        window.KeyPressQwerty(Enum.Parse<PhysicalKey>(food.Letter.ToUpperInvariant()), RawInputModifiers.None);
        Assert.Equal(gold, game.Player.Gold);
        Assert.Contains("shops pay nothing", vm.LastMessage);
    }

    [AvaloniaFact]
    public void Letters_Buy_Tab_Switches_AndEscapeLeaves()
    {
        var (window, vm, game) = OpenAt("general");
        var gold = game.Player.Gold;
        var first = vm.StoreRows[0];

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        Assert.True(game.Player.Gold < gold);
        Assert.Contains(game.Player.Inventory.All, i => i.Kind == first.Item.Kind);

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Assert.True(vm.StoreSellMode);
        var toSell = vm.StoreRows.First(r => r.Item.Kind.Id == "ration_of_food");
        window.KeyPressQwerty(Enum.Parse<PhysicalKey>(toSell.Letter.ToUpperInvariant()), RawInputModifiers.Shift);
        Assert.DoesNotContain(game.Player.Inventory.Pack, i => i.Kind.Id == "ration_of_food");

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(vm.IsInStore);
    }

    [AvaloniaFact]
    public void Gamepad_SelectsAndBuys()
    {
        var (_, vm, game) = OpenAt("weaponsmith");
        vm.HandleAction(InputAction.MoveSouth);
        vm.HandleAction(InputAction.MoveSouth);
        var chosen = vm.StoreRows[vm.StoreSelectedIndex].Item.Kind;
        vm.HandleAction(InputAction.Confirm);
        Assert.Contains(game.Player.Inventory.All, i => i.Kind == chosen);
        vm.HandleAction(InputAction.Cancel);
        Assert.False(vm.IsInStore);
    }

    [AvaloniaFact]
    public void Home_KeepsYourThings()
    {
        var (_, vm, game) = OpenAt("home");
        Assert.Empty(vm.StoreRows);
        vm.HandleAction(InputAction.SwitchPane);
        var torch = vm.StoreRows.First(r => r.Item.Kind.Id == "wooden_torch");
        vm.StoreTransact(torch.Letter[0], all: true);
        vm.HandleAction(InputAction.SwitchPane);
        Assert.Contains(vm.StoreRows, r => r.Item.Kind.Id == "wooden_torch");
        Assert.Equal(5000, game.Player.Gold);
    }
}
