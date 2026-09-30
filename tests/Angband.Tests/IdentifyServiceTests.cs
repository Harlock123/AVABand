using Angband.Core.Game;
using Angband.Core.Items;

namespace Angband.Tests;

/// <summary>AVABand's identify service at the Alchemist: all about an item, for gold.</summary>
public class IdentifyServiceTests
{
    private sealed record Shop(GameSession Game, List<QuestPromptEvent> Prompts, List<string> Said)
    {
        public QuestPromptEvent Last => Prompts[^1];

        public void Enter()
        {
            Game.Player.Position = Game.Level.AllLocs().First(l => Game.Level.FeatureAt(l).Shop == "alchemist");
            Game.Execute(new EnterStoreCommand());
        }

        public void Choose(string id) => Game.Execute(new QuestChoiceCommand(id));
    }

    private static Shop Open(ulong seed = 5)
    {
        var game = GameSession.NewGame(TestData.Game, seed, "warrior");
        var shop = new Shop(game, [], []);
        game.Events.Subscribe<QuestPromptEvent>(shop.Prompts.Add);
        game.Events.Subscribe<MessageEvent>(m => shop.Said.Add(m.Text));
        return shop;
    }

    /// <summary>A ring whose kind (flavour) and runes aren't known.</summary>
    private static Item UnknownRing(GameSession game)
    {
        var ring = game.Objects.Create("ring_of_resist_fire_and_cold");
        return game.Player.Inventory.Add(ring)!;
    }

    [Fact]
    public void Nothing_unknown_nothing_asked()
    {
        var shop = Open();
        foreach (var item in shop.Game.Player.Inventory.All.ToList())
        {
            shop.Game.Knowledge.LearnKind(item.Kind);
            foreach (var rune in item.Runes()) shop.Game.Knowledge.LearnRune(rune);
        }
        shop.Enter();
        Assert.DoesNotContain(shop.Prompts, p => p.Choices.Any(c => c.Id == "ident:list"));
    }

    [Fact]
    public void The_alchemist_tells_all_about_an_item_for_gold()
    {
        var shop = Open();
        var game = shop.Game;
        var ring = UnknownRing(game);
        Assert.False(game.Knowledge.IsFullyKnown(ring));
        var cost = game.IdentifyCost(ring);
        Assert.Equal(50 + 50 * game.Knowledge.UnknownRunes(ring).Count() + 50, cost); // runes, and the kind
        game.Player.Gold = 1000;

        shop.Enter();
        Assert.Equal(["Just shop", "Have something identified"], shop.Last.Choices.Select(c => c.Label));
        shop.Choose("ident:list");
        var offer = shop.Last.Choices.Single(c => c.Id == $"ident:item:{ring.Serial}");
        Assert.EndsWith($"({cost} gold)", offer.Label);
        var asked = shop.Prompts.Count;
        shop.Choose(offer.Id);

        Assert.True(game.Knowledge.IsFullyKnown(ring));
        Assert.Equal(1000 - cost, game.Player.Gold);
        Assert.Contains(shop.Said, s => s.StartsWith("The alchemist turns") && s.Contains("Resist Fire and Cold"));
        // Asked again only if something else is unknown — and never about the ring.
        Assert.All(shop.Prompts.Skip(asked), p => Assert.DoesNotContain(p.Choices, c => c.Id == $"ident:item:{ring.Serial}"));
    }

    [Fact]
    public void Everything_at_once_and_not_without_the_gold()
    {
        var shop = Open();
        var game = shop.Game;
        UnknownRing(game);
        game.Player.Inventory.Add(game.Objects.Create("speed"));
        game.Player.Gold = 0;
        shop.Enter();
        shop.Choose("ident:list");
        var everything = shop.Last.Choices.Single(c => c.Id == "ident:all");
        shop.Choose(everything.Id);
        Assert.Contains(shop.Said, s => s.Contains("you haven't got it"));
        Assert.NotEmpty(game.Unidentified());

        game.Player.Gold = 100_000;
        shop.Choose("ident:all");
        Assert.Empty(game.Unidentified());
        Assert.True(game.Player.Gold < 100_000);
    }

    [Fact]
    public void It_shows_a_Bag_of_Devouring_for_what_it_is()
    {
        var shop = Open();
        var game = shop.Game;
        var bag = game.Player.Inventory.Add(game.Objects.Create("bag_of_devouring"))!;
        game.Knowledge.LearnKind(bag.Kind);
        game.Player.Gold = 1000;
        shop.Enter();
        shop.Choose("ident:list");
        shop.Choose($"ident:item:{bag.Serial}");
        Assert.Equal("a Bag of Holding {cursed}", game.Describe(bag));
    }
}
