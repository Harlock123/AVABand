using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Tests;

/// <summary>AVABand's careful search ('S'): a turn on the squares around you, better the longer you keep at it.</summary>
public class CarefulSearchTests
{
    private static GameSession Room()
    {
        var game = Arena.Create(5,
            "#########",
            "#,,,,,,,#",
            "#,,,@,,,#",
            "#,,,,,,,#",
            "#########");
        TestGames.ClearMonsters(game);
        return game;
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    /// <summary>A hidden trap beside the player, so many points past their search skill.</summary>
    private static Loc HiddenTrap(GameSession game, int pastSkill)
    {
        var at = game.Player.Position + new Loc(1, 0);
        ref var sq = ref game.Level[at];
        sq.Trap = game.Data.Traps.First(t => !t.Warding && t.Id != "rune").Index;
        sq.TrapPower = (byte)Math.Clamp(game.SearchSkill + pastSkill, 0, 255);
        sq.Flags &= ~SquareFlags.TrapVisible;
        return at;
    }

    private static bool Seen(GameSession game, Loc at) => game.Level[at].Has(SquareFlags.TrapVisible);

    [Fact]
    public void A_search_finds_a_trap_just_past_the_skill_that_walking_missed()
    {
        var game = Room();
        var at = HiddenTrap(game, 5);
        game.Execute(new HoldCommand());
        Assert.False(Seen(game, at));                                         // looking the ordinary way: not seen
        var messages = Messages(game);
        var turn = game.GameTurn;
        game.Execute(new SearchCommand());
        Assert.True(Seen(game, at));
        Assert.Contains("You have found a trap.", messages);
        Assert.True(game.GameTurn > turn);                                    // it takes a turn
    }

    [Fact]
    public void Searching_on_in_the_same_spot_does_better_and_says_when_theres_nothing()
    {
        var game = Room();
        var at = HiddenTrap(game, 28);                                        // needs three turns: +10, +20, +30
        var messages = Messages(game);
        game.Execute(new SearchCommand());
        game.Execute(new SearchCommand());
        Assert.False(Seen(game, at));
        Assert.Equal(2, messages.Count(m => m == "You find nothing."));
        game.Execute(new SearchCommand());
        Assert.True(Seen(game, at));

        // Anything else in between starts it afresh.
        var game2 = Room();
        var at2 = HiddenTrap(game2, 18);
        game2.Execute(new SearchCommand());
        game2.Execute(new HoldCommand());
        game2.Execute(new SearchCommand());
        Assert.False(Seen(game2, at2));
        game2.Execute(new SearchCommand());
        Assert.True(Seen(game2, at2));
    }

    [Fact]
    public void The_bonus_stops_at_fifty()
    {
        var game = Room();
        var at = HiddenTrap(game, GameSession.CarefulSearchBonusMax + 1);
        for (var i = 0; i < 10; i++) game.Execute(new SearchCommand());
        Assert.False(Seen(game, at));
        Assert.Equal(GameSession.CarefulSearchBonusMax, game.CarefulSearchBonusNow);
    }

    [Fact]
    public void A_count_searches_that_many_turns_and_stops_at_a_find()
    {
        var game = Room();
        var at = HiddenTrap(game, 25);
        var messages = Messages(game);
        game.Execute(new CountedCommand(new SearchCommand(), 9));
        Assert.True(Seen(game, at));
        Assert.Equal(2, messages.Count(m => m == "You find nothing."));        // two turns, then the find on the third
    }

    [Fact]
    public void A_secret_door_is_found_by_touch_even_in_the_dark()
    {
        var game = Room();
        var door = game.Player.Position + new Loc(0, -2);
        game.Player.Position += new Loc(0, -1);                               // beside the north wall
        game.Level[door].Feature = game.Data.Terrain.Ids.SecretDoor;
        foreach (var p in game.Level.AllLocs()) game.Level[p].Flags &= ~SquareFlags.Glow;
        game.Player.Inventory.Remove(game.Player.Inventory.Light!, 1, () => game.Objects.NextSerial++);
        game.UpdateView();
        var messages = Messages(game);
        game.Execute(new SearchCommand());
        Assert.False(game.Level.FeatureAt(door).Has(Angband.Core.Definitions.TerrainFlags.Secret));
        Assert.Contains("You have found a secret door.", messages);
    }

    [Fact]
    public void Too_confused_to_search()
    {
        var game = Room();
        var messages = Messages(game);
        game.Player.Timed.Set(game.Data.Timed(TimedIds.Confused)!, 10);
        game.Execute(new SearchCommand());
        Assert.Contains("You are too confused to search carefully.", messages);
    }
}
