using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Persistence;
using Angband.Data;

namespace Angband.Tests;

/// <summary>Angband 4.2's store stock (store.txt) and upkeep (store.c store_maint and friends).</summary>
public class StoreStockTests
{
    private static GameSession InTown(ulong seed = 1, GameData? data = null) =>
        GameSession.NewGame(data ?? TestData.Game, seed, CharacterSpec.Default("human", "warrior"));

    private static Store At(GameSession game, string id)
    {
        game.Player.Position = game.Level.AllLocs().First(p => game.Level.FeatureAt(p).Shop == id);
        return game.StoreHere!;
    }

    private static GameSession Classic(ulong seed)
    {
        var dir = Directory.CreateTempSubdirectory("avaband-store-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, DataLoader.ConstantsFile), """{ "noSelling": false }""");
            return InTown(seed, DataLoader.Load(DataLoader.DefaultDataDirectory, dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TheStores_FollowStoreTxt()
    {
        var data = TestData.Game;
        Assert.All(data.Stores.Where(s => !s.Home), s => Assert.Equal(4, s.Owners.Count));
        Assert.Equal("Bilbo the Friendly (Hobbit)", data.Store("general")!.Owners[0].Name);

        var general = data.Store("general")!;
        Assert.Equal(["cloak", "ration_of_food", "wooden_torch", "flask_of_oil", "shovel", "pick", "iron_shot", "arrow", "bolt"], general.Always);
        Assert.Equal((0, 4, 2), (general.MinItems, general.MaxItems, general.Turnover));

        var armoury = data.Store("armoury")!;
        Assert.Equal((6, 18, 9), (armoury.MinItems, armoury.MaxItems, armoury.Turnover));
        Assert.Equal(18, armoury.Normal.Count);

        Assert.Equal(["scroll_of_word_of_recall", "phase_door", "remove_curse", "cure_light_wounds"], data.Store("alchemist")!.Always);
        Assert.Contains("wand_of_stone_to_mud", data.Store("magic")!.Normal);
        Assert.Contains("magic_book", data.Store("magic")!.Buys);
        Assert.Equal(["sling", "bow", "crossbow"], data.Store("weaponsmith")!.Buys.Where(b => b is "sling" or "bow" or "crossbow"));
    }

    [Fact]
    public void TheBookseller_AlwaysHasEveryTownBook_AndNoDungeonBook()
    {
        var game = InTown(1);
        var books = game.Stores["bookseller"];
        Assert.Equal(10, books.Def.Always.Count);
        Assert.Contains("magical_defences", books.Def.Always);
        Assert.Contains("fear_and_torment", books.Def.Always);
        Assert.DoesNotContain("healing_and_sanctuary", books.Def.Always); // a dungeon book in 4.2
        Assert.Equal(books.Def.Always.Order(), books.Stock.Select(i => i.Kind.Id).Order());

        // It sells a book or two each day, and puts it straight back.
        for (var i = 0; i < 5; i++) game.Maintain(books);
        Assert.Equal(10, books.Stock.Count);
        Assert.All(books.Stock, b => Assert.Equal(b.Base.MaxStack, b.Number));
    }

    [Fact]
    public void Staples_NeverRunOut_ButEnchantedOnesDo()
    {
        var game = InTown(2);
        var store = At(game, "general");
        game.Player.Gold = 100_000;
        var food = store.Stock.Single(i => i.Kind.Id == "ration_of_food");
        game.Execute(new BuyCommand(food, 5));
        Assert.Equal(food.Base.MaxStack, food.Number);
        Assert.Contains(food, store.Stock);

        var cloak = game.Objects.Create("cloak");
        cloak.ToAc = cloak.Kind.ToAc + 2;
        Assert.False(store.IsAlways(cloak));
        Assert.True(store.IsAlways(game.Objects.Create("cloak")));
    }

    [Fact]
    public void Stock_IsMassProduced_InAngbandsPiles()
    {
        var game = InTown(3);
        var weapons = game.Stores["weaponsmith"];
        var general = game.Stores["general"];
        var seenPile = false;
        for (var day = 0; day < 40; day++)
        {
            game.Maintain(weapons);
            game.Maintain(general);
            Assert.All(weapons.Stock.Where(i => i.IsAmmo), i => Assert.True(i.Number % 5 == 0 || ItemValue.Of(i, TestData.Game) > 500, $"{i}")); // dear ammunition comes singly
            seenPile |= general.Stock.Any(i => !general.IsAlways(i) && i.Base.Id is "food" or "mushroom" && i.Number > 1);
            Assert.InRange(weapons.Stock.Count, weapons.Def.MinItems, weapons.Def.MaxItems);
        }
        Assert.True(seenPile, "cheap food should come in piles");
    }

    [Fact]
    public void TheBlackMarket_StocksDeepThings_NoOtherStoreSells()
    {
        var game = InTown(4);
        game.Player.MaxDepth = 30;
        var bm = game.Stores["black_market"];
        var others = game.Stores.Values.Where(s => !s.IsHome && !s.Def.BlackMarket).ToList();
        for (var day = 0; day < 20; day++)
        {
            game.Maintain(bm);
            foreach (var item in bm.Stock)
            {
                Assert.Null(item.Artifact);
                Assert.False(item.IsCursed);
                var special = item.Ego is not null || item.ToAc > 2 || item.ToHit > 1 || item.ToDam > 2;
                Assert.True(special || !others.Any(s => s.Stock.Any(o => o.Kind == item.Kind)), $"{item} is sold elsewhere");
                Assert.True(special || ItemValue.Of(item, TestData.Game) >= 10, $"{item} is too cheap");
            }
        }
        Assert.InRange(bm.Stock.Count, bm.Def.MinItems, bm.Def.MaxItems);
    }

    [Fact]
    public void Selling_Pays_TwoThirds_OrASixthAtTheBlackMarket_UpToThePurse()
    {
        var game = Classic(5);
        var sword = game.Objects.Create("long_sword");
        var value = ItemValue.Of(sword, TestData.Game);
        Assert.Equal(value * 2 / 3, game.SellPrice(game.Stores["weaponsmith"], sword));
        Assert.Equal(((value * 2 / 3) / 2 * 50 + 50) / 100, game.SellPrice(game.Stores["black_market"], sword));
        Assert.Equal(value * 3, game.BuyPrice(game.Stores["black_market"], sword));

        var blade = game.Objects.Create("blade_of_chaos");
        blade.ToHit = blade.ToDam = 250;
        var smith = game.Stores["weaponsmith"];
        Assert.Equal(smith.Owner!.Purse, game.SellPrice(smith, blade));
    }

    [Fact]
    public void SoldItems_LoseTheirInscription_AndWandsAreRecharged()
    {
        var game = Classic(6);
        var store = At(game, "magic");
        var wand = game.Objects.Create("wand_of_magic_missile");
        wand.Charges = 0;
        wand.Note = "@a1";
        game.Player.Inventory.Add(wand);
        game.Execute(new SellCommand(wand));
        var shelved = store.Stock.First(i => i.Kind.Id == "wand_of_magic_missile" && i.Note is null);
        Assert.True(shelved.Charges > 0);
    }

    [Fact]
    public void BuyingTheLastItem_BringsOutNewStock()
    {
        var game = InTown(7);
        var store = At(game, "armoury");
        var last = store.Stock[0];
        store.Stock.Clear();
        store.Stock.Add(last);
        last.Number = 1;
        game.Player.Gold = 1_000_000;
        var messages = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));

        game.Execute(new BuyCommand(last));

        Assert.Contains(messages, m => m is "The shopkeeper brings out some new stock." or "The shopkeeper retires.");
        Assert.InRange(store.Stock.Count, store.Def.MinItems, store.Def.MaxItems);
    }

    [Fact]
    public void Shopkeepers_RetireNowAndThen()
    {
        var game = InTown(8);
        var before = game.Stores.Values.Where(s => !s.IsHome).Select(s => s.Owner).ToList();
        game.StoreDays = 400; // one chance in 25 a day
        game.UpdateStores();
        Assert.Equal(0, game.StoreDays);
        Assert.NotEqual(before, game.Stores.Values.Where(s => !s.IsHome).Select(s => s.Owner));
    }

    [Fact]
    public void StoreDays_AndKeepers_AreSaved()
    {
        var game = InTown(9);
        game.StoreDays = 3;
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal(3, loaded.StoreDays);
        Assert.All(game.Stores.Values.Where(s => !s.IsHome), s => Assert.Same(s.Owner, loaded.Stores[s.Id].Owner));
    }

    [Fact]
    public void DeepCharacters_FindBetterStockInTown()
    {
        static int Bonus(GameSession g) => g.Stores["armoury"].Stock.Sum(i => i.ToAc - i.Kind.ToAc);
        var shallow = InTown(10);
        var deep = InTown(10);
        deep.Player.MaxDepth = 60; // store_magic_level + 40
        var (a, b) = (0, 0);
        for (var day = 0; day < 30; day++)
        {
            shallow.Maintain(shallow.Stores["armoury"]);
            deep.Maintain(deep.Stores["armoury"]);
            a += Bonus(shallow);
            b += Bonus(deep);
        }
        Assert.True(b > a, $"{b} vs {a}");
    }
}
