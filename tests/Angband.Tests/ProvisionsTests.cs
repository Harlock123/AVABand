using Angband.Core.Game;
using Angband.Core.Items;

namespace Angband.Tests;

/// <summary>AVABand's hunger safeguards, the engine's part: what counts as food, and the best of it to eat.</summary>
public class ProvisionsTests
{
    private static GameSession Start()
    {
        var game = GameSession.NewGame(TestData.Game, 5, "warrior");
        foreach (var food in game.Player.Inventory.Pack.Where(i => i.Base.Id is "food" or "mushroom" or "scroll").ToList())
            game.Player.Inventory.Remove(food, food.Number, () => game.Objects.NextSerial++);
        return game;
    }

    private static Item Give(GameSession game, string kind, int n = 1)
    {
        var item = game.Objects.Create(kind, n);
        game.Knowledge.LearnKind(item.Kind);
        return game.Player.Inventory.Add(item)!;
    }

    [Fact]
    public void Only_what_does_nothing_but_feed_counts_as_food()
    {
        var game = Start();
        Assert.Equal(0, game.Provisions);
        Assert.Null(game.BestFood());
        Assert.Equal(6000, game.PlainNourishment(game.Data.Object("ration_of_food")!));
        Assert.Equal(0, game.PlainNourishment(game.Data.Object("flask_of_whisky")!));          // confuses
        Assert.Equal(0, game.PlainNourishment(game.Data.Object("mushroom_of_terror")!));       // frightens
        Give(game, "flask_of_whisky", 3);
        Assert.Equal(0, game.Provisions);
        Give(game, "ration_of_food", 2);
        Give(game, "scroll_of_remove_hunger");
        Assert.Equal(3, game.Provisions);

        // A mushroom you don't know yet isn't counted.
        var unknown = game.Objects.Create("mushroom_of_terror");
        game.Player.Inventory.Add(unknown);
        Assert.Equal(3, game.Provisions);
    }

    [Fact]
    public void The_best_food_is_the_most_filling_that_wont_gorge_you_and_scrolls_come_last()
    {
        var game = Start();
        var c = game.Data.Constants;
        game.SetFood(c.FoodWeak - 1);
        var scroll = Give(game, "scroll_of_remove_hunger");
        Assert.Same(scroll, game.BestFood());                                   // (nothing else)
        Give(game, "hard_biscuit");
        var ration = Give(game, "ration_of_food");
        Assert.Same(ration, game.BestFood());                                   // a ration before a biscuit, food before scrolls

        // Nearly full: a ration would gorge you, a biscuit wouldn't.
        game.SetFood(c.FoodMax - 1 - 2500);
        Assert.Equal("hard_biscuit", game.BestFood()!.Kind.Id);
        game.SetFood(c.FoodMax - 1);
        Assert.Equal("scroll_of_remove_hunger", game.BestFood()?.Kind.Id ?? "scroll_of_remove_hunger"); // (satisfy never gorges)
        game.Player.Inventory.Remove(scroll, 1, () => game.Objects.NextSerial++);
        Assert.Null(game.BestFood());                                           // too full for anything
    }
}
