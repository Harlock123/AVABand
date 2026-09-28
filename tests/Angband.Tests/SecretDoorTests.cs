using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>
/// Angband 4.2 has no search command: secret doors beside the player are found by themselves, after
/// each step, when holding still and on arriving (player-util.c search()).
/// </summary>
public class SecretDoorTests
{
    // A lit corridor with a secret door in its south wall at (5, 2).
    private static readonly Loc Door = new(5, 2);

    private static GameSession Corridor()
    {
        var game = Arena.Create(3,
            "############",
            "#@,,,,,,,,,#",
            "############",
            "############");
        game.Level[Door].Feature = game.Data.Terrain.Ids.SecretDoor;
        game.Known.RememberAll(game.Level);
        game.UpdateView();
        return game;
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    private static bool IsSecret(GameSession game) => game.Level.FeatureAt(Door).Has(TerrainFlags.Secret);

    [Fact]
    public void WalkingPast_FindsTheSecretDoor()
    {
        var game = Corridor();
        var messages = Messages(game);
        for (var i = 0; i < 2; i++) game.Execute(new WalkCommand(Direction.East)); // to (3, 1): not yet beside it
        Assert.True(IsSecret(game));

        game.Execute(new WalkCommand(Direction.East)); // (4, 1): the door is diagonally beside
        Assert.False(IsSecret(game));
        Assert.True(game.Level.FeatureAt(Door).Has(TerrainFlags.DoorClosed));
        Assert.Contains("You have found a secret door.", messages);
        Assert.True(game.Data.Terrain[game.Known.Feature(Door)].Has(TerrainFlags.DoorClosed)); // the map shows it
    }

    [Fact]
    public void Running_StopsAtTheDoorItFinds()
    {
        var game = Corridor();
        game.Execute(new RunCommand(Direction.East));
        Assert.False(IsSecret(game));
        Assert.Equal(new Loc(4, 1), game.Player.Position); // stopped on finding it, not at the far wall
    }

    [Fact]
    public void HoldingStill_Searches()
    {
        var game = Corridor();
        game.Player.Position = new Loc(5, 1);
        Assert.True(IsSecret(game)); // put there, not walked there
        game.Execute(new HoldCommand());
        Assert.False(IsSecret(game));
    }

    [Fact]
    public void BlindOrConfused_FindNothing()
    {
        foreach (var timed in new[] { TimedIds.Blind, TimedIds.Confused })
        {
            var game = Corridor();
            game.Player.Timed.Set(game.Data.Timed(timed)!, 100);
            game.Player.Position = new Loc(5, 1);
            game.Execute(new HoldCommand());
            Assert.True(IsSecret(game), timed);
        }
    }

    [Fact]
    public void InTheDark_WithoutALight_NothingIsFound()
    {
        var game = Arena.Create(3,
            "############",
            "#@.........#",
            "############",
            "############");
        Arena.StripGear(game); // no torch
        game.Level[Door].Feature = game.Data.Terrain.Ids.SecretDoor;
        game.UpdateView();
        Assert.Equal(0, game.Player.LightRadius);
        game.Player.Position = new Loc(5, 1);
        game.Execute(new HoldCommand());
        Assert.True(IsSecret(game));
    }

    [Fact]
    public void FoundDoors_AreSometimesLocked_AsInAngband()
    {
        var locked = 0;
        const int doors = 400;
        for (var seed = 0UL; seed < doors; seed++)
        {
            var game = Arena.Create(seed, "#####", "#,@,#", "##g##", "#####");
            var door = new Loc(2, 2);
            game.Level[door].Feature = game.Data.Terrain.Ids.SecretDoor;
            game.Execute(new HoldCommand());
            Assert.True(game.Level.FeatureAt(door).Has(TerrainFlags.DoorClosed));
            if (game.Level[door].LockPower > 0)
            {
                locked++;
                Assert.InRange(game.Level[door].LockPower, 1, 7);
            }
        }
        Assert.InRange(locked, doors / 4 - 40, doors / 4 + 40); // one in four
    }

    [Fact]
    public void ArrivingOnALevel_FindsADoorAlreadyBesideYou()
    {
        // Across many new levels, the player never starts beside an unfound secret door they could see.
        var game = GameSession.NewGame(TestData.Game, 11, CharacterSpec.Default("human", "warrior"));
        var found = 0;
        game.Events.Subscribe<MessageEvent>(m => found += m.Text == "You have found a secret door." ? 1 : 0);
        for (var i = 0; i < 200; i++)
        {
            game.Player.MaxHp = game.Player.Hp = 100_000; // arrivals, not fights
            Assert.False(game.IsGameOver);
            game.Execute(new DebugJumpCommand(1 + i % 30));
            if (game.Player.LightRadius == 0) continue;
            Assert.DoesNotContain(game.Level.Neighbors(game.Player.Position), p => game.Level.FeatureAt(p).Has(TerrainFlags.Secret));
        }
        Assert.True(found > 0, "no arrival was beside a secret door: the test would prove nothing");
    }
}
