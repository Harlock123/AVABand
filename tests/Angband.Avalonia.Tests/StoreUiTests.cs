using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia;
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
    private static (MainWindow Window, MainWindowViewModel Vm, GameSession Game) OpenAt(string storeId, string cls = "warrior")
    {
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42, cls);
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

    /// <summary>A newcomer can tell a holy book from a magic one, and which are theirs, buying or selling.</summary>
    [AvaloniaFact]
    public void The_Bookseller_says_which_books_are_yours()
    {
        var (window, vm, game) = OpenAt("bookseller", "priest");
        var books = vm.StoreRows.Where(r => r.HasBookNote).ToList();
        Assert.Contains(books, r => System.Text.RegularExpressions.Regex.IsMatch(r.Name, @"Holy Books? of \[") && r.IsBookForYou);
        Assert.Contains(books, r => System.Text.RegularExpressions.Regex.IsMatch(r.Name, @"Magic Books? of \[") && r.BookNote == "not for a Priest" && r.IsBookNotForYou);
        Assert.All(books, r => Assert.Equal(r.Item.Base.Id == "prayer_book", r.IsBookForYou));
        TileRenderingTests.Save(window, "store-bookseller");

        // Selling too: the priest's own book is marked as theirs.
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Assert.Contains(vm.StoreRows, r => r.Item.Base.Id == "prayer_book" && r.IsBookForYou);
    }

    /// <summary>"Will this suit me?": armour and weapons say how they compare with what you have, missiles whether they fit.</summary>
    [AvaloniaFact]
    public void The_shops_say_whether_things_suit_you()
    {
        var (window, vm, game) = OpenAt("armoury");
        var armour = vm.StoreRows.Where(r => r.Item.IsWearable).ToList();
        Assert.NotEmpty(armour);
        Assert.All(armour, r => Assert.True(r.HasAdvice, r.Name));
        Assert.Contains(armour, r => r.Advice.Contains("vs your Soft Leather Armour") || r.Advice.Contains("for your empty"));
        Assert.All(armour, r => Assert.Matches("^(Better|Worse|Mixed|Much the same) — ", r.Advice));
        TileRenderingTests.Save(window, "store-armoury-advice");

        // The colour-blind option reaches the notes too (their verdict is in words as well).
        static uint Colour(StoreRow r) => ((global::Avalonia.Media.Immutable.ImmutableSolidColorBrush)r.AdviceBrush).Color.ToUInt32();
        var better = vm.StoreRows.First(r => r.Advice.StartsWith("Better"));
        Assert.NotEqual(MapCellBuilder.ColorBlindPalette["LightGreen"], Colour(better));
        vm.SetOption(DisplayOptions.ColorBlind, true);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None); // (refreshed: selling, then buying again)
        better = vm.StoreRows.First(r => r.Advice.StartsWith("Better"));
        Assert.Equal(MapCellBuilder.ColorBlindPalette["LightGreen"], Colour(better));

        (window, vm, game) = OpenAt("weaponsmith");
        Assert.Contains(vm.StoreRows, r => r.Item.Base.Slot == Angband.Core.Definitions.EquipSlot.Weapon && r.Advice.Contains("damage a turn"));
        Assert.Contains(vm.StoreRows, r => r.Item.IsAmmo && (r.Advice.StartsWith("fits your") || r.Advice.StartsWith("not for your")));
        TileRenderingTests.Save(window, "store-weaponsmith-advice");
    }

    /// <summary>The Alchemist's identify service: asked on the way in when you carry something unknown.</summary>
    [AvaloniaFact]
    public void The_Alchemist_offers_to_identify_what_you_dont_know()
    {
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var game = vm.Game;
        var ring = game.Player.Inventory.Add(game.Objects.Create("ring_of_resist_fire_and_cold"))!;
        game.Player.Gold = 5000;
        game.Player.Position = game.Level.AllLocs().First(p => game.Level.FeatureAt(p).Shop == "alchemist");
        vm.HandleAction(InputAction.EnterStore);
        Assert.True(vm.IsInStore); // straight in: nothing asked at the door
        Assert.Equal("Identify something", vm.StoreServiceLabel);
        window.CaptureRenderedFrame();
        Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "StoreServiceButton").IsEffectivelyVisible);
        TileRenderingTests.Save(window, "alchemist-service-button");
        window.KeyPress(Key.D1, RawInputModifiers.Shift, PhysicalKey.Digit1, "!"); // as a keyboard reports Shift+1
        Assert.Equal("The Alchemy shop", vm.PromptTitle);
        Assert.Contains(vm.ChoiceRows, r => r.Text.EndsWith("gold)") && r.Text.Contains("Ring"));
        TileRenderingTests.Save(window, "alchemist-identify");
        vm.PromptKey(vm.ChoiceRows.First(r => r.Text.Contains("Ring")).Letter[0]);
        Assert.True(game.Knowledge.IsFullyKnown(ring));
        Assert.True(vm.IsInStore); // nothing else unknown: on into the shop
    }

    /// <summary>The mouse wheel scrolls a shop's list when it's too long to see at once.</summary>
    [AvaloniaFact]
    public void The_mouse_wheel_scrolls_a_long_shop_list()
    {
        var (window, vm, game) = OpenAt("general");
        window.Height = 420; // a small window: the list can't all show
        window.CaptureRenderedFrame();
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "StoreList");
        var scroller = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height, $"{vm.StoreRows.Count} rows fit: nothing to scroll");
        Assert.Equal(0, scroller.Offset.Y);

        var middle = list.TranslatePoint(new Point(list.Bounds.Width / 2, list.Bounds.Height / 2), window)!.Value;
        window.MouseWheel(middle, new Vector(0, -3), RawInputModifiers.None);
        window.CaptureRenderedFrame();
        Assert.True(scroller.Offset.Y > 0);
        Assert.True(vm.IsInStore); // still shopping
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

    /// <summary>
    /// Game → "Shops pay gold when you sell": switched on mid-game, even inside a shop, selling pays
    /// at once — without asking about debug commands or losing the character's place in the scores.
    /// </summary>
    [AvaloniaFact]
    public void TheShopsPayToggle_MakesShopsPay_AndBackAgain_AndKeepsTheScore()
    {
        var (window, vm, game) = OpenAt("general");
        Assert.False(vm.ShopsPayGold);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None); // the give/sell pane

        vm.ToggleShopsPayGoldCommand.Execute(null);
        Assert.True(vm.ShopsPayGold);
        Assert.False(game.NoSelling);
        Assert.False(game.IsCheater);
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
        if (vm.IsEnteringNumber) vm.HandleAction(InputAction.Confirm); // (a stack: "Quantity?", one)
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

    /// <summary>
    /// AVABand's own: a slip of the finger undone. What's sold on this visit is listed after the selling
    /// pane (Tab), to buy back by its letter, and Ctrl+Z takes back the last thing sold.
    /// </summary>
    [AvaloniaFact]
    public void ASale_CanBeBoughtBack_FromItsOwnList_OrWithCtrlZ()
    {
        var (window, vm, game) = OpenAt("alchemist");
        int Scrolls() => game.Player.Inventory.Pack.Where(i => i.Kind.Id == "phase_door").Sum(i => i.Number);
        var had = Scrolls();
        vm.HandleAction(InputAction.SwitchPane);                                    // selling
        Assert.False(vm.CanBuyBack);
        var row = vm.StoreRows.First(r => r.Item.Kind.Id == "phase_door");
        vm.StoreTransact(row.Letter[0], all: false);                                // (one of them: no question)
        if (vm.IsEnteringNumber) window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal(had - 1, Scrolls());
        Assert.True(vm.CanBuyBack);
        Assert.Contains("to buy back", vm.StoreModeText);

        vm.HandleAction(InputAction.SwitchPane);                                    // selling → buying back
        Assert.True(vm.StoreBuybackMode);
        var back = Assert.Single(vm.StoreRows);
        Assert.Equal("free", back.Price);
        TileRenderingTests.Save(window, "store-buyback");
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        Assert.Equal(had, Scrolls());
        Assert.False(vm.StoreBuybackMode);                                          // all bought back: selling again
        Assert.True(vm.StoreSellMode);

        // Ctrl+Z: the last sale, at once.
        row = vm.StoreRows.First(r => r.Item.Kind.Id == "phase_door");
        vm.StoreTransact(row.Letter[0], all: false);
        if (vm.IsEnteringNumber) window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal(had - 1, Scrolls());
        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal(had, Scrolls());
        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal("You haven't sold anything here to buy back.", vm.LastMessage);

        // Tab with nothing to buy back: selling → buying, as before.
        vm.HandleAction(InputAction.SwitchPane);
        Assert.False(vm.StoreSellMode);
        Assert.False(vm.StoreBuybackMode);
    }

    /// <summary>Every other row of a shop's list a shade lighter, so a name is easy to follow across to its price.</summary>
    [AvaloniaFact]
    public void TheShopsRows_AreStriped()
    {
        var (window, vm, _) = OpenAt("general");
        window.CaptureRenderedFrame();
        var items = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "StoreList")
            .GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.True(items.Count >= 4);
        var shade = global::Avalonia.Media.Color.Parse("#1C2130");
        Assert.All(items.Where((_, i) => i % 2 == 1), i => Assert.Equal(shade, (i.Background as global::Avalonia.Media.ISolidColorBrush)?.Color));
        Assert.All(items.Where((_, i) => i % 2 == 0), i => Assert.NotEqual(shade, (i.Background as global::Avalonia.Media.ISolidColorBrush)?.Color));
        TileRenderingTests.Save(window, "store-striped");
    }

    /// <summary>AVABand's own: at "Quantity (1-N, a for all)?", A sells (or buys) the lot.</summary>
    [AvaloniaFact]
    public void At_the_quantity_A_trades_them_all()
    {
        var (window, vm, game) = OpenAt("general");
        int Flasks() => game.Player.Inventory.All.Where(i => i.Kind.Id == "flask_of_oil").Sum(i => i.Number);
        var stack = game.Objects.Create("flask_of_oil");
        stack.Number = 12;
        game.Knowledge.LearnKind(stack.Kind);
        game.Player.Inventory.Add(stack);
        var had = Flasks();
        Assert.True(had >= 12);

        vm.HandleAction(InputAction.SwitchPane);                         // selling
        var row = vm.StoreRows.First(r => r.Item.Kind.Id == "flask_of_oil");
        window.KeyPressQwerty(Enum.Parse<PhysicalKey>(row.Letter.ToUpperInvariant()), RawInputModifiers.None);
        Assert.True(vm.IsEnteringNumber);
        Assert.Equal($"Quantity (1-{had}, a for all)? 1", vm.LastMessage);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        Assert.False(vm.IsEnteringNumber);
        Assert.Equal(0, Flasks());                                        // the whole stack sold
        Assert.True(vm.IsInStore);

        // Buying: A takes as many as there are (and you can pay for). (Tab goes by the buy-back list first, after a sale.)
        vm.HandleAction(InputAction.SwitchPane);
        Assert.True(vm.StoreBuybackMode);
        vm.HandleAction(InputAction.SwitchPane);
        game.Player.Gold = 100_000;
        row = vm.StoreRows.First(r => r.Item.Kind.Id == "flask_of_oil");
        window.KeyPressQwerty(Enum.Parse<PhysicalKey>(row.Letter.ToUpperInvariant()), RawInputModifiers.None);
        var most = int.Parse(vm.LastMessage!.Split('-')[1].Split(',')[0]);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Shift);  // (a capital A as well)
        Assert.Equal(most, Flasks());
    }

    /// <summary>Debug → "Character marked by debug": unticking it clears the mark without asking; ticking puts it back.</summary>
    [AvaloniaFact]
    public void TheDebugMarkTick_ClearsAndRestoresTheMark()
    {
        var (_, vm, game) = OpenAt("general");
        game.MarkDebugUsed();
        vm.Refresh();
        Assert.True(vm.DebugMarked);

        vm.ToggleDebugMarkCommand.Execute(null);
        Assert.False(vm.DebugMarked);
        Assert.False(game.IsCheater);

        vm.ToggleDebugMarkCommand.Execute(null);
        Assert.True(vm.DebugMarked);
    }

    /// <summary>The Arcane Artificer ('0'): walking in, SlatriBartSlow offers a socket for each piece that could take one.</summary>
    [AvaloniaFact]
    public void The_Arcane_Artificer_offers_sockets()
    {
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var game = vm.Game;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        var door = game.Level.AllLocs().Single(p => game.Level.FeatureAt(p).Shop == "artificer");
        game.Player.Position = game.Level.AllLocs().First(p => p.ChebyshevTo(door) == 1 && game.Level.IsPassable(p)
                                                              && game.Level.FeatureAt(p).Shop is null);
        vm.Execute(new WalkCommand(Angband.Core.Geometry.DirectionExtensions.FromOffset(door.X - game.Player.Position.X, door.Y - game.Player.Position.Y)));
        Assert.True(vm.IsPrompting);
        Assert.Equal("The Arcane Artificer", vm.PromptTitle);
        Assert.Contains(vm.ChoiceRows, r => r.Text.StartsWith("A socket in your Soft Leather Armour"));
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "artificer");
    }

    /// <summary>The town in tiles: each shop's door its own (a picture for the README and the eye).</summary>
    [AvaloniaFact]
    public void The_town_in_tiles_shows_every_shops_own_door()
    {
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory),
            Angband.Data.Tiles.TilesetCatalog.Discover([Angband.Data.Tiles.TilesetCatalog.DefaultDirectory]),
            new AppSettings { UseTiles = true, TilesetId = "dcss", TileScale = 1 }, save: null);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        vm.Game.Known.RememberAll(vm.Game.Level);
        vm.Refresh();
        Assert.True(vm.UseTiles);
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "town-doors");
    }
}
