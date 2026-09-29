using Angband.Core.Definitions;
using Angband.Core.Generation;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Randomness;
using Angband.Core.World;

namespace Angband.Tests;

/// <summary>
/// Monster groups and room monsters as in Angband 4.2: monster.txt friends / friends-base escorts
/// (mon-make.c place_new_monster, place_friends), vault letters (gen-monster.c get_vault_monsters)
/// and how often interesting rooms appear (dungeon_profile.txt).
/// </summary>
public class MonsterGroupTests
{
    private static readonly GameData Data = TestData.Game;

    /// <summary>A big open room at <paramref name="depth"/>.</summary>
    private static Level Room(int depth)
    {
        var rows = new List<string> { new('#', 61) };
        for (var y = 0; y < 29; y++) rows.Add("#" + new string(',', 59) + "#");
        rows.Add(new string('#', 61));
        return TestLevels.FromAscii(depth, out _, [.. rows]);
    }

    private static readonly Loc Centre = new(30, 15);

    private static List<Monster> Place(string race, int depth, ulong seed, HashSet<string>? uniques = null) =>
        new MonsterSpawner(Data).PlaceWithFriends(Room(depth), new GameRandom(seed), Data.Monster(race)!, Centre, uniques ?? []);

    [Fact]
    public void Escorts_AreImportedFromMonsterTxt()
    {
        var jackal = Data.Monster("jackal")!; // Angband 4.2's "wild dog"
        var pack = Assert.Single(jackal.Friends);
        Assert.True(pack.IsSame);
        Assert.Equal((100, "2d7"), (pack.Chance, pack.Number.ToString()));

        var soldier = Data.Monster("soldier")!;
        Assert.Equal([20, 40, 80], soldier.Friends.Where(f => f.Base == "person").Select(f => f.Chance));
        Assert.All(soldier.Friends.Where(f => f.Base is not null), f => Assert.Equal('p', f.Glyph));
        Assert.Contains(soldier.Friends, f => f.IsSame && f.Chance == 50);

        // "spider" is looked up as Angband does: the first race whose name contains it.
        Assert.Contains(Data.Monster("ancient_spider")!.Friends, f => f.Race == "cave_spider");
        Assert.DoesNotContain(Data.Monsters, r => r.Flags.Contains("FRIENDS"));
    }

    [Fact]
    public void Packs_ShrinkNearTheirNativeDepth()
    {
        // Wild dogs: 2d7. At depth 1 (five levels from "too deep") the pack is halved; at depth 20 it isn't.
        double Average(int depth) => Enumerable.Range(1, 200).Average(i => Place("jackal", depth, (ulong)i).Count);
        var shallow = Average(1);
        var deep = Average(20);
        Assert.InRange(shallow, 3.5, 6.5);   // 1 + about half of 2d7 (8)
        Assert.InRange(deep, 7.5, 10.5);     // 1 + 2d7
    }

    [Fact]
    public void AnOutOfDepthLeader_ComesWithoutItsPack()
    {
        // Hill orcs are native to level 15: at level 1 their warband is far out of depth.
        for (ulong seed = 1; seed <= 20; seed++)
            Assert.Single(Place("hill_orc", 1, seed));
        Assert.Contains(Enumerable.Range(1, 20), i => Place("hill_orc", 20, (ulong)i).Count > 1);
    }

    [Fact]
    public void BaseEscorts_AreDrawnFromThatBase()
    {
        var withPersons = Enumerable.Range(1, 60).Select(i => Place("soldier", 10, (ulong)i)).First(g => g.Count > 1);
        Assert.All(withPersons, m => Assert.Equal('p', m.Race.Glyph));
    }

    [Fact]
    public void GroupsNeverExceedTheGroupMaximum()
    {
        foreach (var race in Data.Monsters.Where(r => r.Friends.Any(f => f.IsSame && f.Number.Max > 25)).Take(3))
        {
            var group = Place(race.Id, 100, 3);
            Assert.True(group.Count(m => m.Race == race) <= MonsterSpawner.GroupMax + 1, race.Id);
        }
    }

    [Fact]
    public void UniqueEscorts_ComeOnce_AndNotIfAlreadyMet()
    {
        bool OtherUnique(MonsterRaceDef leader, MonsterFriendDef f) =>
            f.Race is { } id && !f.IsSame && id != leader.Id && Data.Monster(id)!.IsUnique;
        var chief = Data.Monsters.First(r => r.Friends.Any(f => OtherUnique(r, f)));
        var unique = Data.Monster(chief.Friends.First(f => OtherUnique(chief, f)).Race!)!;
        var group = Place(chief.Id, 60, 5);
        Assert.True(group.Count(m => m.Race == unique) <= 1);
        var met = new HashSet<string> { unique.Id };
        Assert.DoesNotContain(Place(chief.Id, 60, 5, met), m => m.Race == unique);
    }

    [Fact]
    public void RoomLetters_AreAtTheLevelsDepth_AwakeAndAlone()
    {
        // "Birds of a feather" deeper down: nine birds of about that level, awake, with no escorts.
        var birds = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var level = Room(10);
            for (var y = 0; y < 3; y++)
            for (var x = 0; x < 3; x++)
                level.SpawnHints.Add(new SpawnHint(new Loc(10 + x, 10 + y), SpawnKind.Monster, 0, "awake,base:B,uniques"));
            new MonsterSpawner(Data).Populate(level, new GameRandom(seed), new Loc(50, 25), new HashSet<string>());
            var placed = level.Monsters.All.ToList();
            Assert.Equal(9, placed.Count);
            Assert.All(placed, m => Assert.Equal('B', m.Race.Glyph));
            Assert.All(placed, m => Assert.False(m.IsAsleep));
            Assert.All(placed, m => Assert.True(m.Race.Depth <= 10 + 4, $"{m.Race.Id} is level {m.Race.Depth}")); // or a rare out-of-depth pick
            birds += placed.Count;
        }
        Assert.Equal(360, birds);

        // On level 1 no bird is native (the crow is level 2): the room mostly stays empty, as in Angband.
        var shallow = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var level = Room(1);
            for (var x = 0; x < 9; x++) level.SpawnHints.Add(new SpawnHint(new Loc(10 + x, 10), SpawnKind.Monster, 0, "awake,base:B,uniques"));
            new MonsterSpawner(Data).Populate(level, new GameRandom(seed), new Loc(50, 25), new HashSet<string>());
            Assert.All(level.Monsters.All, m => Assert.Equal('B', m.Race.Glyph));
            shallow += level.Monsters.All.Count();
        }
        Assert.InRange(shallow, 0, 360 / 10);
    }
}
