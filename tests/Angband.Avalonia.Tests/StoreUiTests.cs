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
        TestKit.Give(vm);
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
        if (vm.IsEnteringNumber) window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); // "Quantity (1-N)?": one
        Assert.Equal(gold, game.Player.Gold);
        Assert.Contains("shops pay nothing", vm.LastMessage);
    }

    /// <summary>Debug → "Shops pay gold when you sell": switched on mid-game, even inside a shop, selling pays at once.</summary>
    [AvaloniaFact]
    public void TheDebugToggle_MakesShopsPay_AndBackAgain()
    {
        var (window, vm, game) = OpenAt("general");
        game.MarkDebugUsed(); // already agreed to use debug commands
        Assert.False(vm.ShopsPayGold);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None); // the give/sell pane

        vm.ToggleShopsPayGoldCommand.Execute(null);
        Assert.True(vm.ShopsPayGold);
        Assert.False(game.NoSelling);
        Assert.Equal("Sell (letter to sell, Tab to buy)", vm.StoreModeText);
        Assert.DoesNotContain("Shops pay nothing", vm.StoreSubtitle);
        var flask = vm.StoreRows.First(r => r.Item.Kind.Id == "flask_of_oil");
        Assert.EndsWith(" gold", flask.Price);

        var gold = game.Player.Gold;
        window.KeyPressQwerty(Enum.Parse<PhysicalKey>(flask.Letter.ToUpperInvariant()), RawInputModifiers.None);
        if (vm.IsEnteringNumber) window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); // "Quantity (1-N)?": one
        Assert.True(game.Player.Gold > gold, vm.LastMessage);
        Assert.StartsWith("You sold", vm.LastMessage);

        vm.ToggleShopsPayGoldCommand.Execute(null);
        Assert.False(vm.ShopsPayGold);
        Assert.StartsWith("Give", vm.StoreModeText);
    }

    /// <summary>
    /// What you have on is marked in the sell list, and a hasty key press doesn't sell it: it asks,
    /// and the answer goes to the question, not to the store.
    /// </summary>
    [AvaloniaFact]
    public void WieldedAndWornItems_AreMarked_AndAskedAboutBeforeSelling()
    {
        var (window, vm, game) = OpenAt("weaponsmith");
        game.MarkDebugUsed();
        vm.ToggleShopsPayGoldCommand.Execute(null); // selling for gold, the case that matters most
        var spare = game.Objects.Create("dagger");
        spare.ToHit = spare.ToDam = 0;
        game.Player.Inventory.Add(spare);
        var wielded = game.Player.Inventory.Weapon!;
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);

        var weaponRow = vm.StoreRows.Single(r => r.Item == wielded);
        var spareRow = vm.StoreRows.Single(r => r.Item == spare);
        Assert.Equal("wielded", weaponRow.Equipped);
        Assert.False(spareRow.IsEquipped);
        Assert.Equal("wielded", vm.StoreRows.Single(r => r.Item.Base.Id == "sling").Equipped); // launchers are wielded too

        // The hasty key: a question, nothing sold, and "n" keeps it.
        window.KeyPressQwerty(Enum.Parse<PhysicalKey>(weaponRow.Letter.ToUpperInvariant()), RawInputModifiers.None);
        Assert.True(vm.IsConfirming);
        Assert.Contains("which you are wielding?", vm.LastMessage);
        Assert.Same(wielded, game.Player.Inventory.Weapon);
        window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);
        Assert.False(vm.IsConfirming);
        Assert.Same(wielded, game.Player.Inventory.Weapon);

        // Enter answers the question too (it doesn't buy or sell the selected row).
        window.KeyPressQwerty(Enum.Parse<PhysicalKey>(weaponRow.Letter.ToUpperInvariant()), RawInputModifiers.None);
        var gold = game.Player.Gold;
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Null(game.Player.Inventory.Weapon);
        Assert.True(game.Player.Gold > gold);

        // The spare in the pack still sells at once.
        var row = vm.StoreRows.Single(r => r.Item == spare);
        window.KeyPressQwerty(Enum.Parse<PhysicalKey>(row.Letter.ToUpperInvariant()), RawInputModifiers.None);
        Assert.False(vm.IsConfirming);
        Assert.DoesNotContain(spare, game.Player.Inventory.Pack);
    }

    [AvaloniaFact]
    public void AtTheArmoury_YourArmourIsMarkedWorn()
    {
        var (window, vm, game) = OpenAt("armoury");
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        var body = game.Player.Inventory.Equipped.Single(i => i.Base.Slot == Angband.Core.Definitions.EquipSlot.Body);
        Assert.Equal("worn", vm.StoreRows.Single(r => r.Item == body).Equipped);
        TileRenderingTests.Save(window, "store-sell-equipped");
    }

    [AvaloniaFact]
    public void Letters_Buy_Tab_Switches_AndEscapeLeaves()
    {
        var (window, vm, game) = OpenAt("general");
        var gold = game.Player.Gold;
        var first = vm.StoreRows[0];

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        if (vm.IsEnteringNumber) window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); // "Quantity (1-N)?": one
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

    [AvaloniaFact]
    public void ALetter_AsksHowMany_AsAngbandDoes()
    {
        var (window, vm, game) = OpenAt("general");
        game.Player.Gold = 100_000;
        var flasks = vm.StoreRows.First(r => r.Item.Kind.Id == "flask_of_oil");
        var had = game.Player.Inventory.All.Where(i => i.Kind.Id == "flask_of_oil").Sum(i => i.Number);

        window.KeyPressQwerty(Enum.Parse<PhysicalKey>(flasks.Letter.ToUpperInvariant()), RawInputModifiers.None);
        Assert.True(vm.IsEnteringNumber);
        Assert.StartsWith("Quantity (1-", vm.LastMessage);
        Assert.EndsWith(")? 1", vm.LastMessage);                       // one, unless you say otherwise
        window.KeyPressQwerty(PhysicalKey.Digit5, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal(had + 5, game.Player.Inventory.All.Where(i => i.Kind.Id == "flask_of_oil").Sum(i => i.Number));

        // The controller: up and down change it, A confirms.
        flasks = vm.StoreRows.First(r => r.Item.Kind.Id == "flask_of_oil");
        window.KeyPressQwerty(Enum.Parse<PhysicalKey>(flasks.Letter.ToUpperInvariant()), RawInputModifiers.None);
        vm.HandleAction(InputAction.MoveNorth);
        vm.HandleAction(InputAction.MoveNorth);
        Assert.EndsWith(")? 3", vm.LastMessage);
        vm.HandleAction(InputAction.Confirm);
        Assert.Equal(had + 8, game.Player.Inventory.All.Where(i => i.Kind.Id == "flask_of_oil").Sum(i => i.Number));

        // Escape (or 0) buys nothing.
        flasks = vm.StoreRows.First(r => r.Item.Kind.Id == "flask_of_oil");
        window.KeyPressQwerty(Enum.Parse<PhysicalKey>(flasks.Letter.ToUpperInvariant()), RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(vm.IsEnteringNumber);
        Assert.True(vm.IsInStore);
        Assert.Equal(had + 8, game.Player.Inventory.All.Where(i => i.Kind.Id == "flask_of_oil").Sum(i => i.Number));
    }
}
