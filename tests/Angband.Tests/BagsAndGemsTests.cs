using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>AVABand's bags of holding, and socketed bracers with their gems.</summary>
public class BagsAndGemsTests
{
    private static GameSession Game(string cls = "warrior") => GameSession.NewGame(TestData.Game, 11, cls);

    private static Item Give(GameSession game, string kind, int number = 1)
    {
        var item = game.Objects.Create(kind, number);
        game.Knowledge.LearnKind(item.Kind);
        return game.Player.Inventory.Add(item)!;
    }

    private static Item Wear(GameSession game, string kind)
    {
        var item = Give(game, kind);
        Assert.True(game.Execute(new WieldCommand(item)));
        return game.Player.Inventory.Equipped.First(i => i.Kind.Id == kind);
    }

    // --- Bags ------------------------------------------------------------------------------------------

    [Fact]
    public void The_best_bag_carried_counts_for_weight_and_room()
    {
        var game = Game();
        var inv = game.Player.Inventory;
        var limit = game.Player.WeightLimit;
        Assert.Equal(23, inv.PackSize);

        Give(game, "sack_of_holding");
        game.Execute(new HoldCommand());
        Assert.Equal(limit * 125 / 100, game.Player.WeightLimit);
        Assert.Equal(23, inv.PackSize); // a sack gives no room

        Give(game, "bag_of_holding");
        game.Execute(new HoldCommand());
        Assert.Equal(limit * 150 / 100, game.Player.WeightLimit); // only the best counts, not both
        Assert.Equal(25, inv.PackSize);
        Assert.Equal("bag_of_holding", inv.BestBag!.Kind.Id);
    }

    [Fact]
    public void A_lost_bag_spills_what_no_longer_fits()
    {
        var game = Game();
        var inv = game.Player.Inventory;
        var bag = Give(game, "greater_bag_of_holding");
        game.Execute(new HoldCommand());
        Assert.Equal(27, inv.PackSize);
        foreach (var kind in game.Data.Objects.Where(k => k.Base is "potion" or "scroll" && k.Commonness > 0).Select(k => k.Id))
            if (inv.SlotsUsed < inv.PackSize) Give(game, kind);
        Assert.Equal(27, inv.SlotsUsed);
        var messages = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));

        game.Execute(new DropCommand(bag, 1));
        Assert.Equal(23, inv.PackSize);
        Assert.True(inv.SlotsUsed <= 23);
        Assert.Contains(messages, m => m.StartsWith("Your pack overflows! You drop "));
        Assert.True(game.Level.Objects.At(game.Player.Position).Count() >= 4 || game.Level.Objects.All.Count() >= 4);
    }

    [Fact]
    public void A_Bag_of_Devouring_passes_for_a_Bag_of_Holding_until_it_eats()
    {
        var game = Game();
        var bag = Give(game, "bag_of_devouring");
        Assert.Equal("a Bag of Holding", game.Describe(bag)); // the curse unknown
        Assert.Equal(25, game.Player.Inventory.PackSize);      // and it does hold more
        Give(game, "ration_of_food", 5);
        var before = game.Player.Inventory.Pack.Sum(i => i.Number);
        var messages = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));
        for (var i = 0; i < 20 * GameSession.DevouringChance && !messages.Any(m => m.Contains("satisfied noise")); i++)
        {
            game.Player.Hp = game.Player.MaxHp;
            game.Player.Food = 10000;
            game.Execute(new HoldCommand());
        }
        Assert.Contains(messages, m => m.Contains("makes a small, satisfied noise") && m.EndsWith("is gone!"));
        Assert.Equal(before - 1, game.Player.Inventory.Pack.Sum(i => i.Number));
        Assert.Equal("a Bag of Holding {cursed}", game.Describe(bag));
    }

    // --- Bracers and gems -----------------------------------------------------------------------------------

    [Fact]
    public void Bracers_have_a_slot_of_their_own_and_gems_set_in_them_count()
    {
        var game = Game();
        Wear(game, "set_of_leather_gloves" is var g && game.Data.Object(g) is null ? "leather_gloves" : g);
        var bracers = Wear(game, "iron_bracers");
        Assert.NotNull(game.Player.Inventory.InSlot(EquipSlot.Hands)); // beside the gloves, not instead
        Assert.Equal("a Pair of Iron Bracers (-1,+0) [5,+0] (2 empty sockets)", game.Describe(bracers));

        var ruby = Give(game, "ruby");
        var toDam = game.Player.ToDam;
        Assert.True(game.Execute(new SetGemCommand(bracers, ruby)));
        Assert.Contains("fire", game.Player.Resists.Keys);
        Assert.Equal(toDam + 3, game.Player.ToDam);
        Assert.Equal("a Pair of Iron Bracers (-1,+3) [5,+0] (Ruby, 1 empty socket)", game.Describe(bracers));
        Assert.DoesNotContain(game.Player.Inventory.Pack, i => i.Kind.Id == "ruby");

        Give(game, "sapphire");
        Assert.True(game.Execute(new SetGemCommand(bracers, game.Player.Inventory.Pack.First(i => i.Kind.Id == "sapphire"))));
        Assert.False(game.Execute(new SetGemCommand(bracers, Give(game, "chipped_ruby")))); // no socket left
        Assert.Equal(2, bracers.Gems.Count);
    }

    [Fact]
    public void The_armourer_takes_a_gem_out_for_gold_and_its_virtue_goes_with_it()
    {
        var removed = 0;
        var cracked = 0;
        foreach (var seed in Enumerable.Range(1, 12).Select(s => (ulong)s))
        {
            var game = GameSession.NewGame(TestData.Game, seed, "warrior");
            var prompts = new List<QuestPromptEvent>();
            game.Events.Subscribe<QuestPromptEvent>(prompts.Add);
            var messages = new List<string>();
            game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));
            var bracers = Wear(game, "leather_bracers");
            game.Execute(new SetGemCommand(bracers, Give(game, "ruby")));
            game.Player.Gold = 10_000;

            game.Player.Position = game.Level.AllLocs().First(l => game.Level.FeatureAt(l).Shop == "armoury");
            game.Execute(new EnterStoreCommand());
            var offer = prompts[^1];
            Assert.Equal("The Armoury", offer.Title);
            Assert.Equal("Just shop", offer.Choices[0].Label);
            var take = offer.Choices.Single(c => c.Id.StartsWith("gem:out:"));
            Assert.Contains($"({GameSession.GemRemovalCost(bracers.Gems[0])} gold)", take.Label);
            game.Execute(new QuestChoiceCommand(take.Id));

            Assert.Empty(bracers.Gems);
            Assert.DoesNotContain("fire", game.Player.Resists.Keys);
            Assert.True(game.Player.Gold < 10_000);
            if (game.Player.Inventory.Pack.Any(i => i.Kind.Id == "ruby")) removed++;
            else
            {
                Assert.Contains(messages, m => m.Contains("cracks as it comes free"));
                cracked++;
            }
        }
        Assert.True(removed > 0);
        Assert.True(removed > cracked); // (one in eight cracks)
    }

    [Fact]
    public void A_cursed_gem_gives_much_and_stays_until_its_curse_is_broken()
    {
        var game = Game();
        var prompts = new List<QuestPromptEvent>();
        game.Events.Subscribe<QuestPromptEvent>(prompts.Add);
        var messages = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));
        var bracers = Wear(game, "leather_bracers");
        var str = game.Player.Stats["str"];
        game.Execute(new SetGemCommand(bracers, Give(game, "bloodstone")));
        Assert.True(game.Player.Stats["str"] > str);
        Assert.Contains("impair_hitpoint_recovery", bracers.Curses);
        Assert.Contains("cursed", game.Describe(bracers)); // a passive curse shows at once on something worn

        game.Player.Gold = 10_000;
        game.Player.Position = game.Level.AllLocs().First(l => game.Level.FeatureAt(l).Shop == "armoury");
        game.Execute(new EnterStoreCommand());
        game.Execute(new QuestChoiceCommand(prompts[^1].Choices.Single(c => c.Id.StartsWith("gem:out:")).Id));
        Assert.Contains(messages, m => m.Contains("Break its curse first"));
        Assert.Single(bracers.Gems);
        Assert.Equal(10_000, game.Player.Gold);

        bracers.Curses.Remove("impair_hitpoint_recovery"); // as a Remove Curse that broke it
        game.Execute(new LeaveStoreCommand());
        game.Execute(new EnterStoreCommand());
        game.Execute(new QuestChoiceCommand(prompts[^1].Choices.Single(c => c.Id.StartsWith("gem:out:")).Id));
        Assert.Empty(bracers.Gems);
        Assert.Equal(str, game.Player.Stats["str"]);
    }

    [Fact]
    public void Gems_and_bags_are_saved()
    {
        var game = Game();
        var bracers = Wear(game, "mithril_bracers");
        game.Execute(new SetGemCommand(bracers, Give(game, "emerald")));
        game.Execute(new SetGemCommand(bracers, Give(game, "star_sapphire")));
        Give(game, "bag_of_holding");
        game.Execute(new HoldCommand());
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        var again = loaded.Player.Inventory.InSlot(EquipSlot.Arms)!;
        Assert.Equal(["emerald", "star_sapphire"], again.Gems.Select(g => g.Kind.Id));
        Assert.Equal(bracers.Gems[0].AddedResists, again.Gems[0].AddedResists);
        Assert.Equal(game.Describe(bracers), loaded.Describe(again));
        Assert.Equal(game.Player.Speed, loaded.Player.Speed);
        Assert.Equal(25, loaded.Player.Inventory.PackSize);
        Assert.Equal(game.Player.WeightLimit, loaded.Player.WeightLimit);
    }

    [Fact]
    public void Gems_and_bags_have_shop_notes_and_proper_plurals()
    {
        var game = Game();
        Assert.Equal("you have no bracers to set it in", game.AdviceFor(game.Objects.Create("ruby"))!.Text);
        Wear(game, "leather_bracers");
        Assert.StartsWith("Better — set in your Pair of Leather Bracers: +3 damage, resist fire", game.AdviceFor(game.Objects.Create("ruby"))!.Text);
        Assert.StartsWith("Better — you carry no bag: +50% carrying, +2 pack slots", game.AdviceFor(game.Objects.Create("bag_of_holding"))!.Text);
        Give(game, "greater_bag_of_holding");
        Assert.Equal(-1, game.AdviceFor(game.Objects.Create("sack_of_holding"))!.Tone);
        Assert.StartsWith("2 Rubies (+0,+3)", game.Describe(game.Objects.Create("ruby", 2)));
        Assert.StartsWith("3 Chipped Rubies (+0,+1)", game.Describe(game.Objects.Create("chipped_ruby", 3)));
    }
}
