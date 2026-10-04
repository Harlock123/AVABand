using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Persistence;
using Angband.Data;

namespace Angband.Tests;

/// <summary>Buying back what you sold on this visit to a shop (AVABand's own).</summary>
public class BuybackTests
{
    private static GameSession InTown(ulong seed = 6, Angband.Core.Definitions.GameData? data = null)
    {
        var game = GameSession.NewGame(data ?? TestData.Game, seed, CharacterSpec.Default("human", "warrior"));
        Arena.GiveTestKit(game);
        return game;
    }

    private static Store Enter(GameSession game, string id)
    {
        game.Player.Position = game.Level.AllLocs().First(p => game.Level.FeatureAt(p).Shop == id);
        game.Execute(new EnterStoreCommand());
        return game.StoreHere!;
    }

    private static int Carried(GameSession game, string kind) => game.Player.Inventory.Pack.Where(i => i.Kind.Id == kind).Sum(i => i.Number);

    [Fact]
    public void A_sale_can_be_bought_back_while_in_the_shop()
    {
        var game = InTown();
        var store = Enter(game, "alchemist");
        var had = Carried(game, "phase_door");
        var inStock = store.Stock.Where(i => i.Kind.Id == "phase_door").Sum(i => i.Number);
        game.Execute(new SellCommand(game.Player.Inventory.Pack.First(i => i.Kind.Id == "phase_door"), 2));
        Assert.Equal(had - 2, Carried(game, "phase_door"));

        var entry = Assert.Single(game.Buyback);
        Assert.Equal(2, entry.Item.Number);
        Assert.Equal(0, entry.Price); // (no selling: given, so taken back for nothing)
        game.Execute(new BuybackCommand(0));
        Assert.Equal(had, Carried(game, "phase_door"));
        Assert.Equal(inStock, store.Stock.Where(i => i.Kind.Id == "phase_door").Sum(i => i.Number)); // out of the stock again
        Assert.Empty(game.Buyback);
    }

    [Fact]
    public void Buying_back_costs_what_the_shop_paid_and_needs_the_gold()
    {
        var dir = Directory.CreateTempSubdirectory("avaband-buyback-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, DataLoader.ConstantsFile), """{ "noSelling": false }""");
            var game = InTown(7, DataLoader.Load(DataLoader.DefaultDataDirectory, dir));
            var store = Enter(game, "weaponsmith");
            var dagger = game.Player.Inventory.Weapon!;
            game.Player.Inventory.TakeOff(dagger);
            var before = game.Player.Gold;
            game.Execute(new SellCommand(dagger));
            var paid = game.Player.Gold - before;
            Assert.True(paid > 0);
            Assert.Equal(paid, game.Buyback[0].Price);

            var messages = new List<string>();
            game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));
            game.Player.Gold = paid - 1;
            game.Execute(new BuybackCommand(0));
            Assert.Contains(messages, m => m.StartsWith($"You need {paid} gold to buy back"));
            Assert.Single(game.Buyback);

            game.Player.Gold = paid;
            game.Execute(new BuybackCommand(0));
            Assert.Equal(0, game.Player.Gold);
            Assert.Contains(game.Player.Inventory.Pack, i => i.Kind.Id == dagger.Kind.Id);
            Assert.Contains(messages, m => m.StartsWith("You buy back") && m.EndsWith($"for {paid} gold."));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>The shop tops a wand up when it takes it; bought back, it's as it was (no free recharge).</summary>
    [Fact]
    public void A_wand_comes_back_as_it_was_sold()
    {
        var game = InTown();
        var store = Enter(game, "magic");
        var wand = game.Player.Inventory.Add(game.Objects.Create("wand_of_magic_missile"))!;
        wand.Charges = 0;
        game.Knowledge.LearnKind(wand.Kind);
        game.Execute(new SellCommand(wand));
        var pile = game.Buyback[0].Pile;
        var stockCharges = pile.Charges;
        game.Execute(new BuybackCommand(0));
        var back = game.Player.Inventory.Pack.Single(i => i.Kind.Id == "wand_of_magic_missile");
        Assert.Equal(0, back.Charges);
        Assert.True(!store.Stock.Contains(pile) || pile.Charges <= stockCharges);
    }

    [Fact]
    public void The_list_is_this_visits_only_and_the_Home_has_none()
    {
        var game = InTown();
        Enter(game, "alchemist");
        game.Execute(new SellCommand(game.Player.Inventory.Pack.First(i => i.Kind.Id == "phase_door")));
        Assert.Single(game.Buyback);
        game.Execute(new LeaveStoreCommand());
        Enter(game, "alchemist");
        Assert.Empty(game.Buyback);                                             // a new visit

        Enter(game, "home");
        game.Execute(new SellCommand(game.Player.Inventory.Pack.First(i => i.Kind.Id == "flask_of_oil")));
        Assert.Empty(game.Buyback);                                             // (just take it back)
    }

    [Fact]
    public void A_buy_back_is_recorded_and_replays()
    {
        var node = ReplayCodec.Encode(new BuybackCommand(3));
        var game = InTown();
        Assert.Equal(new BuybackCommand(3), ReplayCodec.DecodeCommand(game, node));
    }
}
