using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Persistence;
using Angband.Core.Randomness;

namespace Angband.Tests;

/// <summary>Merchant caravans in the town square (AVABand's own).</summary>
public partial class AvaQuestTests
{
    /// <summary>Down and back up until a caravan has come (it comes one return in four, from 250 ft).</summary>
    private static Quester WithACaravan()
    {
        var q = Start(level: 15);
        for (var i = 0; i < 60 && q.Game.AvaQuests.Caravan is null; i++)
        {
            q.Jump(5);
            q.Jump(0);
        }
        Assert.NotNull(q.Game.AvaQuests.Caravan);
        return q;
    }

    [Fact]
    public void A_caravan_comes_to_town_with_rare_wares()
    {
        var q = WithACaravan();
        var game = q.Game;
        var caravan = game.AvaQuests.Caravan!;
        var at = new Loc(caravan.X, caravan.Y);
        Assert.Equal("caravan", game.Level.FeatureAt(at).Shop);
        Assert.InRange(caravan.Stock.Count, 1, 5);
        Assert.Contains(q.Said, s => s.Contains("A merchant caravan has come to town"));

        q.EnterShop("caravan");
        Assert.Equal("A merchant caravan", q.Last.Title);
        Assert.Equal(caravan.Stock.Count, q.Last.Choices.Count(c => c.Id.StartsWith("caravan:buy:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Buying_from_the_caravan_needs_its_price_and_the_thing_comes_known()
    {
        var q = WithACaravan();
        var game = q.Game;
        var caravan = game.AvaQuests.Caravan!;
        var price = caravan.Prices[0];
        var count = caravan.Stock.Count;
        game.Player.Gold = price - 1;
        q.EnterShop("caravan");
        q.Choose("caravan:buy:0");
        Assert.Contains(q.Said, s => s.Contains("I don't haggle"));
        Assert.Equal(count, caravan.Stock.Count);

        game.Player.Gold = price;
        q.EnterShop("caravan");
        var pack = game.Player.Inventory.All.Count();
        q.Choose("caravan:buy:0");
        Assert.Equal(0, game.Player.Gold);
        Assert.Equal(count - 1, caravan.Stock.Count);
        Assert.Contains(q.Said, s => s.StartsWith("You buy ", StringComparison.Ordinal));
        var bought = game.Player.Inventory.All.Last(i => game.Knowledge.IsFullyKnown(i));
        Assert.True(game.Player.Inventory.All.Count() >= pack);
        Assert.NotNull(bought);
    }

    [Fact]
    public void The_caravan_moves_on_after_three_returns()
    {
        var q = WithACaravan();
        for (var i = 0; i < GameSession.CaravanStays; i++)
        {
            q.Jump(5);
            q.Jump(0);
        }
        Assert.Null(q.Game.AvaQuests.Caravan);
        Assert.Contains(q.Said, s => s == "The merchant caravan has moved on.");
        Assert.DoesNotContain(q.Game.Level.AllLocs(), p => q.Game.Level.FeatureAt(p).Shop == "caravan");
    }

    [Fact]
    public void The_caravans_wares_are_deep_egos_and_fine_trinkets_never_cursed_or_artifacts_at_three_times_their_worth()
    {
        var q = Start(level: 30);
        var game = q.Game;
        var seen = new HashSet<string>();
        foreach (var depth in new[] { 5, 12, 30, 60 })
        {
            game.Player.MaxDepth = depth;
            Assert.Equal(Math.Max(25, depth + 15), game.CaravanLevel); // never shallower than 1250 ft
            for (ulong seed = 1; seed <= 25; seed++)
            {
                var caravan = game.MakeCaravan(new GameRandom(seed * 7919 + (ulong)depth))!;
                Assert.Equal(GameSession.CaravanWares, caravan.Stock.Count); // always a full wagon
                for (var i = 0; i < caravan.Stock.Count; i++)
                {
                    var item = SaveGame.ItemFromJson(game, caravan.Stock[i])!;
                    Assert.Null(item.Artifact);
                    Assert.False(item.IsCursed, $"{item.Kind.Id} is cursed");
                    Assert.Equal(game.CaravanLevel, item.OriginDepth);
                    Assert.Equal(Math.Max(10, ItemValue.Of(item, TestData.Game) * item.Number * 3), caravan.Prices[i]);
                    var trinket = item.Base.Slot is EquipSlot.Ring or EquipSlot.Amulet || item.Base.Id is "wand" or "staff" or "rod";
                    if (trinket) Assert.True(ItemValue.Of(item, TestData.Game) >= GameSession.CaravanTrinketValue, $"{item.Kind.Id} is cheap");
                    else
                    {
                        Assert.True(item.IsWearable || item.IsAmmo, $"{item.Kind.Id} isn't gear");
                        Assert.NotNull(item.Ego);
                    }
                    seen.Add(trinket ? item.Base.Id : "gear");
                }
            }
        }
        // Rings, amulets and devices come too, beside the gear.
        Assert.Contains("gear", seen);
        Assert.Contains(seen, k => k is "ring" or "amulet");
        Assert.Contains(seen, k => k is "wand" or "staff" or "rod");
    }

    [Fact]
    public void Making_a_caravan_moves_nothing_else_in_the_game()
    {
        var q = Start(level: 30);
        var game = q.Game;
        game.Player.MaxDepth = 40;
        var serial = game.Objects.NextSerial;
        var artifacts = game.Objects.CreatedArtifacts.Count;
        for (ulong seed = 1; seed <= 20; seed++) game.MakeCaravan(new GameRandom(seed));
        Assert.Equal(serial, game.Objects.NextSerial);
        Assert.Equal(artifacts, game.Objects.CreatedArtifacts.Count);
    }
}
