using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Data;

namespace Angband.Tests;

public class ShopTests
{
    private static GameSession InTown(ulong seed = 1, GameData? data = null)
    {
        var game = GameSession.NewGame(data ?? TestData.Game, seed, CharacterSpec.Default("human", "warrior"));
        Arena.GiveTestKit(game);
        return game;
    }

    /// <summary>Puts the player on a store's entrance.</summary>
    private static Store At(GameSession game, string id)
    {
        game.Player.Position = game.Level.AllLocs().First(p => game.Level.FeatureAt(p).Shop == id);
        return game.StoreHere!;
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    [Fact]
    public void Town_HasEveryStore_WithSensibleStock()
    {
        var game = InTown();
        Assert.Equal(8, game.Stores.Count);
        foreach (var store in game.Stores.Values)
        {
            if (store.IsHome)
            {
                Assert.Empty(store.Stock);
                continue;
            }
            // Angband store_maint: the staples, plus min..max other piles.
            var always = store.Def.Always.Count;
            if (store.Def.Turnover > 0) Assert.InRange(store.Stock.Count, store.Def.MinItems + always, store.Def.MaxItems + always);
            else Assert.Equal(always, store.Stock.Count);
            Assert.All(store.Def.Always, id => Assert.Contains(store.Stock, i => i.Kind.Id == id && i.Number == i.Base.MaxStack));
            Assert.DoesNotContain(store.Stock, i => i.IsCursed || ItemValue.Of(i, TestData.Game) <= 0 || i.Artifact is not null);
            Assert.All(store.Stock, i => Assert.InRange(i.Number, 1, i.Base.MaxStack));
            Assert.All(store.Stock.Where(i => i.IsAmmo && !store.IsAlways(i) && ItemValue.Of(i, TestData.Game) <= 500), i => Assert.Equal(0, i.Number % 5));
            Assert.Contains(store.Owner!, store.Def.Owners);
        }
    }

    [Fact]
    public void WalkingOntoAnEntrance_OpensTheStore()
    {
        var game = InTown(2);
        var entrance = game.Level.AllLocs().First(p => game.Level.FeatureAt(p).Shop == "alchemist");
        var beside = game.Level.Neighbors(entrance).First(game.Level.IsPassable);
        game.Player.Position = beside;
        ShopEnteredEvent? entered = null;
        game.Events.Subscribe<ShopEnteredEvent>(e => entered = e);

        game.Execute(new WalkCommand(Angband.Core.Geometry.DirectionExtensions.FromOffset(entrance.X - beside.X, entrance.Y - beside.Y)));

        Assert.Equal("alchemist", entered?.StoreId);
    }

    [Fact]
    public void Buying_ChargesGold_AndTeachesTheItem()
    {
        var game = InTown(3);
        var store = At(game, "alchemist");
        game.Player.Gold = 10_000;
        var potion = store.Stock.First(i => i.Kind.Id == "cure_light_wounds");
        var price = game.BuyPrice(store, potion);

        game.Execute(new BuyCommand(potion, 3));

        Assert.Equal(10_000 - 3 * price, game.Player.Gold);
        Assert.Contains(store.Stock, i => i.Kind.Id == "cure_light_wounds"); // always stocked
        Assert.Equal(2 + 3, game.Player.Inventory.Pack.Single(i => i.Kind.Id == "cure_light_wounds").Number); // the test kit's 2
        Assert.True(game.Knowledge.IsAware(potion.Kind));
    }

    [Fact]
    public void Buying_NeedsGold_AndTheStore()
    {
        var game = InTown(4);
        var store = At(game, "weaponsmith");
        var messages = Messages(game);
        game.Player.Gold = 0;
        var item = store.Stock.First(i => !store.IsAlways(i));
        game.Execute(new BuyCommand(item));
        Assert.Contains("You do not have enough gold for this item.", messages);
        Assert.Contains(item, store.Stock);

        game.Player.Position = game.Level.AllLocs().First(game.Level.IsEmptyFloor);
        game.Player.Gold = 100_000;
        game.Execute(new BuyCommand(item));
        Assert.Contains("That is not for sale here.", messages);
    }

    [Fact]
    public void BoughtEquipment_IsFullyKnown()
    {
        var game = InTown(5);
        var store = At(game, "armoury");
        game.Player.Gold = 1_000_000;
        var item = store.Stock.First();
        game.Execute(new BuyCommand(item));
        var mine = game.Player.Inventory.Pack.First(i => i.Kind == item.Kind);
        Assert.True(game.Knowledge.IsFullyKnown(mine));
    }

    [Fact]
    public void Selling_GivesNothingByDefault_AndStoresOnlyTakeTheirGoods()
    {
        var game = InTown(6);
        var store = At(game, "alchemist");
        var messages = Messages(game);
        var gold = game.Player.Gold;

        var flask = game.Player.Inventory.Pack.First(i => i.Kind.Id == "flask_of_oil");
        game.Execute(new SellCommand(flask));
        Assert.Contains("I don't deal in that kind of item.", messages);

        var scroll = game.Player.Inventory.Pack.First(i => i.Kind.Id == "phase_door");
        game.Execute(new SellCommand(scroll, 2));
        Assert.Equal(gold, game.Player.Gold);
        Assert.DoesNotContain(game.Player.Inventory.Pack, i => i.Kind.Id == "phase_door");
        Assert.Contains(messages, m => m.StartsWith("You give"));
    }

    [Fact]
    public void ClassicSelling_PaysTwoThirdsOfTheValue()
    {
        var dir = Directory.CreateTempSubdirectory("avaband-shop-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, DataLoader.ConstantsFile), """{ "noSelling": false }""");
            var game = InTown(7, DataLoader.Load(DataLoader.DefaultDataDirectory, dir));
            var store = At(game, "weaponsmith");
            var dagger = game.Player.Inventory.Weapon!;
            game.Player.Inventory.TakeOff(dagger);
            var gold = game.Player.Gold;

            game.Execute(new SellCommand(dagger));

            Assert.Equal(gold + ItemValue.Of(dagger, TestData.Game) * 2 / 3, game.Player.Gold); // Angband 4.2: two thirds
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Home_StoresItemsForFree_UpToItsCapacity()
    {
        var game = InTown(8);
        var home = At(game, "home");
        var gold = game.Player.Gold;
        var torches = game.Player.Inventory.Add(game.Objects.Create("wooden_torch", 2))!;

        game.Execute(new SellCommand(torches, 2));
        Assert.Contains(home.Stock, i => i.Kind.Id == "wooden_torch" && i.Number == 2);
        game.Execute(new BuyCommand(home.Stock.First(), 1));
        Assert.Contains(home.Stock, i => i.Kind.Id == "wooden_torch" && i.Number == 1);
        Assert.Equal(gold, game.Player.Gold);

        for (var i = 0; i < 30; i++) home.Stock.Add(game.Objects.Create("whip"));
        var messages = Messages(game);
        game.Execute(new SellCommand(game.Player.Inventory.Pack.First(i => i.Kind.Id == "ration_of_food")));
        Assert.Contains("Your home is full.", messages);
    }

    [Fact]
    public void BlackMarket_ChargesTriple()
    {
        var game = InTown(9);
        var bm = At(game, "black_market");
        Assert.NotEmpty(bm.Stock);
        Assert.All(bm.Stock, i => Assert.Equal(ItemValue.Of(i, TestData.Game) * 3, game.BuyPrice(bm, i)));
    }

    [Fact]
    public void Stores_RestockWhileYouAreAway()
    {
        var game = InTown(10);
        string Stock() => string.Join(",", game.Stores["weaponsmith"].Stock.Select(i => i.Serial));
        var before = Stock();

        game.Player.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();
        game.Execute(new TakeStairsCommand(true));
        game.Player.Hp = game.Player.MaxHp = 100_000;
        TestGames.HoldUntil(game, game.GameTurn + 3 * 10 * TestData.Game.Constants.StoreTurns);
        Assert.InRange(game.StoreDays, 2, 3); // a store day is 10 × store_turns game turns
        Assert.Equal(before, Stock());       // nothing changes until you're back
        game.Player.Position = game.Level.FindFeature(TerrainFlags.UpStair).First();
        game.Execute(new TakeStairsCommand(false));

        Assert.Equal(0, game.Player.Depth);
        Assert.Equal(0, game.StoreDays);
        Assert.NotEqual(before, Stock());
    }

    [Fact]
    public void NoSelling_MultipliesDungeonGold()
    {
        long Gold(bool noSelling)
        {
            var dir = Directory.CreateTempSubdirectory("avaband-gold-").FullName;
            File.WriteAllText(Path.Combine(dir, DataLoader.ConstantsFile), $$"""{ "noSelling": {{noSelling.ToString().ToLowerInvariant()}} }""");
            var game = GameSession.NewGame(DataLoader.Load(DataLoader.DefaultDataDirectory, dir), 11);
            Directory.Delete(dir, true);
            long total = 0;
            for (var depth = 5; depth <= 9; depth++)
            {
                game.Execute(new DebugJumpCommand(depth));
                total += game.Level.Objects.All.Where(o => o.Item.IsGold).Sum(o => (long)o.Item.GoldValue);
            }
            return total;
        }
        Assert.True(Gold(true) > Gold(false) * 3, $"{Gold(true)} vs {Gold(false)}");
    }

    [Fact]
    public void Stores_AreDeterministic()
    {
        static string Stock(ulong seed) => string.Join("|", InTown(seed).Stores.Values
            .SelectMany(s => s.Stock.Select(i => $"{s.Id}:{i.Kind.Id}:{i.Number}:{i.ToHit}:{i.Ego?.Id}")));
        Assert.Equal(Stock(12), Stock(12));
        Assert.NotEqual(Stock(12), Stock(13));
    }

    /// <summary>
    /// The Game menu's switch for birth_no_selling is kept in the save, says what it did — and is
    /// not a cheat: the character is still scored.
    /// </summary>
    [Fact]
    public void ShopsPay_IsSaved_Announced_AndStillScored()
    {
        var game = GameSession.NewGame(TestData.Game, 5);
        var messages = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));
        Assert.True(game.NoSelling);

        game.SetShopsPay(true);
        Assert.False(game.NoSelling);
        Assert.False(game.IsCheater);
        Assert.Contains(messages, m => m.StartsWith("Shops now pay gold"));
        game.SetShopsPay(true); // already so: nothing more said
        Assert.Single(messages, m => m.StartsWith("Shops now pay gold"));

        using var stream = new MemoryStream();
        Angband.Core.Persistence.SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = Angband.Core.Persistence.SaveGame.Load(TestData.Game, stream);
        Assert.False(loaded.NoSelling);
        Assert.False(loaded.IsCheater);
    }

    /// <summary>A burnt-out torch is worth nothing, and no store buys it; a lit one still sells.</summary>
    [Fact]
    public void ASpentTorch_IsWorthless_AndNoStoreBuysIt()
    {
        var game = InTown(6);
        game.SetShopsPay(true);
        var store = At(game, "general");
        var messages = Messages(game);

        var spent = game.Objects.Create("wooden_torch");
        spent.Fuel = 0;
        var lit = game.Objects.Create("wooden_torch");
        Assert.True(lit.Fuel > 0);
        Assert.True(ItemValue.IsSpent(spent));
        Assert.False(ItemValue.IsSpent(lit));
        Assert.Equal(0, ItemValue.Of(spent, TestData.Game));
        Assert.Equal(0, game.SellPrice(store, spent));
        Assert.True(game.SellPrice(store, lit) > 0);

        game.Player.Inventory.Add(spent);
        var gold = game.Player.Gold;
        game.Execute(new SellCommand(spent));
        Assert.Contains("I have no interest in that.", messages);
        Assert.Equal(gold, game.Player.Gold);
        Assert.Contains(spent, game.Player.Inventory.Pack);

        // An empty lantern can be refilled, so it keeps its worth.
        var lantern = game.Objects.Create("lantern");
        lantern.Fuel = 0;
        Assert.False(ItemValue.IsSpent(lantern));
        Assert.True(ItemValue.Of(lantern, TestData.Game) > 0);
    }
}
