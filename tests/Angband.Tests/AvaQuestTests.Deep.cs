using Angband.Core.Game;

namespace Angband.Tests;

/// <summary>The deep quests: the Heart of the Mountain, the Last Watch, the Seeing Stone.</summary>
public partial class AvaQuestTests
{
    private static void PickUp(Quester q, string kind)
    {
        var game = q.Game;
        var found = game.Level.Objects.All.First(o => o.Item.Kind.Id == kind);
        game.Player.Position = found.Loc;
        game.Execute(new PickupCommand(found.Item));
    }

    [Fact]
    public void The_deep_quests_wait_for_a_seasoned_character()
    {
        var q = Start(level: 29);
        q.EnterShop("inn");
        q.ChooseLabel("Ask who has work");
        Assert.DoesNotContain(q.Last.Choices, c => c.Id is "offer:heart" or "offer:watch" or "offer:stone");

        q = Start(level: 40);
        q.EnterShop("inn");
        q.ChooseLabel("Ask who has work");
        Assert.Contains(q.Last.Choices, c => c.Id == "offer:heart");
        Assert.Contains(q.Last.Choices, c => c.Id == "offer:watch");
        Assert.Contains(q.Last.Choices, c => c.Id == "offer:stone");
    }

    [Fact]
    public void The_Heart_of_the_Mountain_lies_under_Skorvath_and_goes_home_to_the_dwarves()
    {
        var q = Start(level: 35);
        var game = q.Game;
        q.TakeQuest("heart");
        var state = game.AvaQuests.Get("heart")!;
        Assert.Equal("hunt", state.Stage);
        Assert.InRange(state.N("depth"), 38, 50);
        q.Jump(state.N("depth"));
        Assert.NotNull(q.Find("skorvath_the_cold_drake"));
        PickUp(q, "heart_of_the_mountain");
        Assert.Equal("choose", state.Stage);

        // Worn, it lends strength (and calls dragons: its curse).
        var heart = q.Carried("heart_of_the_mountain")!;
        Assert.Contains("dragon_summon", heart.Curses);
        var str = game.Player.Stats["str"];
        game.Execute(new WieldCommand(heart));
        Assert.True(game.Player.Stats["str"] > str);

        var gold = game.Player.Gold;
        q.EnterShop("inn");
        Assert.Contains(q.Last.Choices, c => c.Id == "heart:keep");
        q.Choose("heart:return");
        Assert.Equal("returned", state.Stage);
        Assert.True(state.IsDone);
        Assert.Null(q.Carried("heart_of_the_mountain"));
        Assert.True(game.Player.Gold >= gold + 5000);
        Assert.Equal(-20, game.AvaQuests.PriceAdjust["armoury"]);
        Assert.Equal(str, game.Player.Stats["str"]);
    }

    [Fact]
    public void The_Heart_of_the_Mountain_can_be_kept()
    {
        var q = Start(level: 35);
        var game = q.Game;
        q.TakeQuest("heart");
        var state = game.AvaQuests.Get("heart")!;
        q.Jump(state.N("depth"));
        PickUp(q, "heart_of_the_mountain");
        q.EnterShop("inn");
        q.Choose("heart:keep");
        Assert.Equal("kept", state.Stage);
        Assert.True(state.IsDone);
        Assert.NotNull(q.Carried("heart_of_the_mountain"));
    }

    [Fact]
    public void The_Last_Watch_held_long_enough_brings_the_rangers_and_war_bands_come_meanwhile()
    {
        var q = Start(level: 35);
        var game = q.Game;
        q.TakeQuest("watch");
        var state = game.AvaQuests.Get("watch")!;
        Assert.Equal("hold", state.Stage);
        q.Jump(state.N("depth"));
        Assert.NotNull(q.Find("grishnag_the_warchief"));
        game.Level.Monsters.Remove(q.Find("grishnag_the_warchief")!); // to see it held, not won

        for (var i = 0; i < 3000 && state.Stage == "hold"; i++)
        {
            game.Player.Hp = game.Player.MaxHp;
            game.Execute(new HoldCommand());
        }
        Assert.Contains(q.Said, s => s.Contains("another war-band comes"));
        Assert.Equal("relieved", state.Stage);
        Assert.Equal(GameSession.WatchTurns, state.N("held"));
        Assert.Contains(q.Said, s => s.Contains("the host breaks and flees"));
    }

    [Fact]
    public void The_Last_Watch_ends_when_Grishnag_falls()
    {
        var q = Start(level: 35);
        var game = q.Game;
        q.TakeQuest("watch");
        var state = game.AvaQuests.Get("watch")!;
        q.Jump(state.N("depth"));
        Assert.True(game.DamageMonster(q.Find("grishnag_the_warchief")!, 1_000_000));
        Assert.Equal("relieved", state.Stage);
        Assert.Contains(q.Said, s => s.Contains("Grishnag falls"));
    }

    [Fact]
    public void The_Seeing_Stone_shows_the_level_and_goes_to_the_White_Council()
    {
        var q = Start(level: 40);
        var game = q.Game;
        q.TakeQuest("stone");
        var state = game.AvaQuests.Get("stone")!;
        Assert.Equal("seek", state.Stage);
        q.Jump(state.N("depth"));
        Assert.NotNull(q.Find("the_keeper_of_the_stone"));
        PickUp(q, "palantir");
        Assert.Equal("carry", state.Stage);

        var before = game.MappedFraction();
        game.Execute(new UseCommand(q.Carried("palantir")!));
        Assert.Contains(q.Said, s => s.Contains("You look into the stone"));
        Assert.True(game.MappedFraction() > before);

        q.EnterShop("bookseller");
        Assert.Equal("The Bookseller", q.Last.Title);
        q.Choose("stone:give");
        Assert.Equal("given", state.Stage);
        Assert.True(state.IsDone);
        Assert.Null(q.Carried("palantir"));
    }

    [Fact]
    public void The_Eye_sometimes_looks_back_from_the_palantir()
    {
        var q = Start(level: 40);
        var game = q.Game;
        q.TakeQuest("stone");
        var state = game.AvaQuests.Get("stone")!;
        q.Jump(state.N("depth"));
        PickUp(q, "palantir");
        for (var i = 0; i < 30 && !q.Said.Any(s => s.Contains("lidless Eye")); i++)
            game.Execute(new UseCommand(q.Carried("palantir")!));
        Assert.Contains(q.Said, s => s.Contains("lidless Eye"));

        q.EnterShop("bookseller");
        q.Choose("stone:keep");
        Assert.Equal("keep", state.Stage);
        Assert.NotNull(q.Carried("palantir"));
    }
}
