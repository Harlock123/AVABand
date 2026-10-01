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
}
