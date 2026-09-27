using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Sight;
using Angband.Core.World;

namespace Angband.Tests;

public class VisionTests
{
    private static (VisionSystem Vision, KnownMap Known) See(Level level, Loc eye, int lightRadius, bool blind = false)
    {
        var vision = new VisionSystem();
        var known = new KnownMap(level.Width, level.Height);
        var lights = lightRadius > 0 ? new[] { new LightSource(eye, lightRadius) } : [];
        vision.Update(level, known, eye, 20, lights, blind);
        return (vision, known);
    }

    [Fact]
    public void DarkRoom_OnlyTorchRadiusIsSeen()
    {
        var level = TestLevels.FromAscii(out var eye,
            "###########",
            "#.........#",
            "#....@....#",
            "#.........#",
            "###########");
        See(level, eye, lightRadius: 2);

        foreach (var p in level.AllLocs())
        {
            Assert.True(level[p].Has(SquareFlags.View));
            Assert.Equal(eye.DistanceTo(p) <= 2, level[p].Has(SquareFlags.Seen));
        }
    }

    [Fact]
    public void LitRoom_IsSeenAndRememberedInFull()
    {
        var level = TestLevels.FromAscii(out var eye,
            "ggggggggggg",
            "g,,,,,,,,,g",
            "g,,,,@,,,,g",
            "g,,,,,,,,,g",
            "ggggggggggg");
        level[eye].Flags |= SquareFlags.Glow;
        var (_, known) = See(level, eye, lightRadius: 1);

        Assert.All(level.AllLocs(), p =>
        {
            Assert.True(level[p].Has(SquareFlags.Seen), $"{p}");
            Assert.True(known.IsKnown(p));
        });
    }

    [Fact]
    public void LitRoomWalls_AreOnlySeenFromTheirLitSide()
    {
        string[] map =
        [
            "...........",
            "...........",
            "...........",
            "...........",
            "ggggggggggg",
            "g,,,,,,,,,g",
            "ggggggggggg",
        ];
        var wall = new Loc(5, 4);

        // Outside, in the dark: the wall's lit face points away from us.
        var outside = TestLevels.FromAscii(out _, map);
        See(outside, new Loc(5, 1), lightRadius: 1);
        Assert.True(outside[wall].Has(SquareFlags.View));
        Assert.False(outside[wall].Has(SquareFlags.Seen));

        // Inside the room: seen.
        var inside = TestLevels.FromAscii(out _, map);
        See(inside, new Loc(5, 5), lightRadius: 0);
        Assert.True(inside[wall].Has(SquareFlags.Seen));

        // Outside but close enough for the torch to reach it: seen.
        var close = TestLevels.FromAscii(out _, map);
        See(close, new Loc(5, 3), lightRadius: 1);
        Assert.True(close[wall].Has(SquareFlags.Seen));
    }

    [Fact]
    public void Blind_SeesNothing_ButStillHasView()
    {
        var level = TestLevels.FromAscii(out var eye, ",,,,,", ",,@,,", ",,,,,");
        var (_, known) = See(level, eye, lightRadius: 2, blind: true);
        Assert.All(level.AllLocs(), p =>
        {
            Assert.False(level[p].Has(SquareFlags.Seen));
            Assert.False(known.IsKnown(p));
        });
    }

    [Fact]
    public void Memory_PersistsAfterLeavingView_AndFlagsAreCleared()
    {
        var level = TestLevels.FromAscii(out var eye,
            "#############",
            "#@....+.....#",
            "#############");
        var vision = new VisionSystem();
        var known = new KnownMap(level.Width, level.Height);
        vision.Update(level, known, eye, 20, [new LightSource(eye, 2)]);
        Assert.True(known.IsKnown(new Loc(2, 1)));

        // Move far behind the door: the start area is out of view but still remembered.
        var far = new Loc(10, 1);
        vision.Update(level, known, far, 20, [new LightSource(far, 2)]);
        Assert.False(level[new Loc(2, 1)].HasAny(SquareFlags.View | SquareFlags.Seen | SquareFlags.Lit));
        Assert.True(known.IsKnown(new Loc(2, 1)));
    }

    [Fact]
    public void Memory_CanBeStale()
    {
        var level = TestLevels.FromAscii(out var eye, "#######", "#@.'..#", "#######");
        var vision = new VisionSystem();
        var known = new KnownMap(level.Width, level.Height);
        vision.Update(level, known, eye, 20, [new LightSource(eye, 5)]);

        var door = new Loc(3, 1);
        level[door].Feature = TestData.Game.Terrain.Ids.ClosedDoor; // closed while... still in view
        vision.Update(level, known, eye, 20, []); // no light now: nothing is seen, memory not refreshed
        Assert.Equal(TestData.Game.Terrain.Ids.OpenDoor, known.Feature(door));
    }

    [Fact]
    public void Traps_BecomeVisibleWhenSeen()
    {
        var level = TestLevels.FromAscii(out var eye, "#######", "#@....#", "#######");
        level[new Loc(3, 1)].Trap = 1;
        level[new Loc(5, 1)].Trap = 1;
        See(level, eye, lightRadius: 2);
        Assert.True(level[new Loc(3, 1)].Has(SquareFlags.TrapVisible));
        Assert.False(level[new Loc(5, 1)].Has(SquareFlags.TrapVisible));
    }

    [Fact]
    public void GameSession_PlayerSeesSurroundingsOnArrival()
    {
        var game = GameSession.NewGame(TestData.Game, 9);
        game.Player.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();
        game.Execute(new TakeStairsCommand(true));

        var pos = game.Player.Position;
        Assert.True(game.Level[pos].Has(SquareFlags.Seen));
        Assert.True(game.Known.IsKnown(pos));
        // Something beyond max sight is unknown in a fresh classic level.
        Assert.Contains(game.Level.AllLocs(), p => !game.Known.IsKnown(p));
    }

    [Fact]
    public void Town_GoesDarkAtNight_AndLightReturnsAtDawn()
    {
        var game = GameSession.NewGame(TestData.Game, 4);
        TestGames.ClearMonsters(game); // a 1000-turn wait next to the town dogs would be fatal
        var messages = new List<string>();
        using var _ = game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));
        Assert.True(game.IsDaytime);

        var half = TestData.Game.Constants.DayLength / 2;
        TestGames.HoldUntil(game, half);
        Assert.False(game.IsDaytime);
        Assert.Contains("The sun has fallen.", messages);

        var level = game.Level;
        Assert.DoesNotContain(level.AllLocs(), p => level.IsFloor(p) && level[p].Has(SquareFlags.Glow));
        Assert.All(level.FindFeature(TerrainFlags.Shop), p => Assert.True(level[p].Has(SquareFlags.Glow)));

        TestGames.HoldUntil(game, 2 * half);
        Assert.True(game.IsDaytime);
        Assert.Contains("The sun has risen.", messages);
        Assert.All(level.AllLocs(), p => Assert.True(level[p].Has(SquareFlags.Glow)));
    }
}
