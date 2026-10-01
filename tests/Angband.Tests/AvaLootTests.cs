using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>
/// AVABand's added loot (ava_objects.json, ava_egos.json, ava_artifacts.json, ava_curses.json):
/// weapon oils, egos for bracers and bags, its own artifacts, curses for the new slots, gem sets,
/// monster trophies the Armoury works, richer lairs and the new lights and helms.
/// </summary>
public class AvaLootTests
{
    private static GameSession NewGame(string cls = "warrior", ulong seed = 7) => GameSession.NewGame(TestData.Game, seed, cls);

    private static Item Carry(GameSession game, string kind, int number = 1)
    {
        var item = game.Objects.Create(kind, number);
        game.Knowledge.LearnKind(item.Kind);
        return game.Player.Inventory.Add(item)!;
    }

    // --- Weapon oils ---------------------------------------------------------------------------

    [Theory]
    [InlineData("oil_of_venom", "att_pois")]
    [InlineData("oil_of_burning", "att_fire")]
    [InlineData("oil_of_frost", "att_cold")]
    [InlineData("oil_of_storms", "att_elec")]
    [InlineData("oil_of_corrosion", "att_acid")]
    [InlineData("holy_water", "att_evil")]
    public void A_weapon_oil_brands_your_blows_for_a_while(string kind, string timed)
    {
        var game = NewGame();
        var oil = Carry(game, kind, 2);
        Assert.Equal("oil", oil.Base.Id);
        Assert.True(game.Execute(new UseCommand(oil)));
        Assert.True(game.Player.Timed.Has(timed));
        Assert.Equal(1, oil.Number);
        Assert.InRange(game.Player.Timed[timed], 20, 40);
        Assert.Contains("When applied, it makes your blows", ObjectInfo.DescribeItem(game, oil));
    }

    [Fact]
    public void Venom_oil_uses_angbands_own_poison_brand()
    {
        var game = NewGame();
        game.Execute(new UseCommand(Carry(game, "oil_of_venom")));
        var brands = game.Data.Timed("att_pois")!.Brand!;
        Assert.Equal("pois", brands.Element);
        Assert.Equal(3, brands.Multiplier);
    }

    [Fact]
    public void The_alchemist_stocks_and_buys_oils()
    {
        var alchemist = TestData.Game.Stores.Single(s => s.Id == "alchemist");
        Assert.Contains("oil_of_venom", alchemist.Stocked);
        Assert.Contains("holy_water", alchemist.Stocked);
        Assert.Contains("oil", alchemist.AvabandBuys);
    }

    // --- Egos for bracers and bags ---------------------------------------------------------------

    private static Item WithEgo(GameSession game, string kind, string ego)
    {
        var item = game.Objects.Create(kind);
        ObjectFactory.ApplyEgo(game.Rng, item, game.Data.Egos.Single(e => e.Id == ego), 30);
        game.Knowledge.LearnKind(item.Kind);
        foreach (var rune in item.Runes()) game.Knowledge.LearnRune(rune);
        return game.Player.Inventory.Add(item)!;
    }

    [Theory]
    [InlineData("archer", "bracers")]
    [InlineData("warding", "bracers")]
    [InlineData("duelist", "bracers")]
    [InlineData("celebrimbor", "bracers")]
    [InlineData("fireproof", "bag")]
    [InlineData("insulated", "bag")]
    public void AVABands_egos_fit_their_bases(string ego, string baseId)
    {
        var def = TestData.Game.Egos.Single(e => e.Id == ego);
        Assert.Equal([baseId], def.Bases);
        var kind = TestData.Game.Objects.First(k => k.Base == baseId && k.Id != "bag_of_devouring");
        Assert.True(def.Fits(kind));
    }

    [Fact]
    public void Celebrimbors_bracers_have_one_more_socket()
    {
        var game = NewGame();
        var plain = game.Objects.Create("iron_bracers");
        var bracers = WithEgo(game, "iron_bracers", "celebrimbor");
        Assert.Equal(plain.Sockets + 1, bracers.Sockets);
        foreach (var gem in new[] { "ruby", "sapphire", "topaz" })
            Assert.True(game.Execute(new SetGemCommand(bracers, Carry(game, gem))));
        Assert.Equal(3, bracers.Gems.Count);
        Assert.Contains("of Celebrimbor's", game.Describe(bracers).Replace("(Celebrimbor's)", "of Celebrimbor's"));
    }

    [Fact]
    public void Bracers_of_the_Duelist_ease_the_off_hand()
    {
        var game = NewGame();
        Assert.Equal(GameSession.OffHandToHitPenalty, game.OffHandPenalty);
        var bracers = WithEgo(game, "leather_bracers", "duelist");
        game.Execute(new WieldCommand(bracers));
        Assert.Equal(GameSession.OffHandToHitPenalty - 10, game.OffHandPenalty);
        Assert.Contains("off hand's blow 10 easier to land", ObjectInfo.DescribeItem(game, bracers));
    }

    [Fact]
    public void Bracers_of_the_Archer_and_of_Warding_roll_their_bonuses()
    {
        var game = NewGame();
        var archer = WithEgo(game, "leather_bracers", "archer");
        Assert.InRange(archer.Modifier(ItemModifiers.Dexterity), 1, 2);
        Assert.True(archer.ToHit >= 3);
        var warding = WithEgo(game, "leather_bracers", "warding");
        Assert.True(warding.ToAc >= 4);
        Assert.Single(warding.Resists, r => r is "acid" or "elec" or "fire" or "cold");
    }

    [Theory]
    [InlineData("fireproof", "fire", "phase_door")]
    [InlineData("insulated", "cold", "cure_light_wounds")]
    public void A_guarding_bag_keeps_the_pack_safe_from_its_element(string ego, string element, string kind)
    {
        // Without the bag, a sure hit destroys them all; with it, none.
        var bare = NewGame();
        var lost = Carry(bare, kind, 5);
        Assert.True(bare.InventoryDamage(element, 10_000) >= 5);
        Assert.DoesNotContain(lost, bare.Player.Inventory.Pack);

        var game = NewGame();
        var stack = Carry(game, kind, 5);
        var bag = WithEgo(game, "sack_of_holding", ego);
        Assert.Equal(0, game.InventoryDamage(element, 10_000));
        Assert.Equal(5, stack.Number);
        Assert.Contains("keeps what's in your pack safe from", ObjectInfo.DescribeItem(game, bag));
    }
}
