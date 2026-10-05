using Angband.Core.Game;
using Angband.Core.Geometry;

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
}
