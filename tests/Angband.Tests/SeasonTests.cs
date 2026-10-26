using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Persistence;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>AVABand's seasonal events: the calendar, the event fixed at birth and kept, its monsters, keepsakes and feat, and what stays as it was.</summary>
public class SeasonTests
{
    private static GameData Data => TestData.Game;

    private static GameSession Game(string? season, ulong seed = 5) =>
        GameSession.NewGame(Data, seed, CharacterSpec.Default("human", "warrior") with { Season = season });

    private static bool IsSeasonal(Monster m) => Data.SeasonalMonsters.Any(r => r.Id == m.Race.Id);

    /// <summary>The seasonal races met on so many new levels at a depth.</summary>
    private static HashSet<string> SeasonalMet(GameSession game, int depth, int levels)
    {
        var met = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < levels; i++)
        {
            game.Execute(new DebugJumpCommand(depth));
            foreach (var m in game.Level.Monsters.All.Where(IsSeasonal)) met.Add(m.Race.Id);
        }
        return met;
    }

    private static Monster Place(GameSession game, string raceId)
    {
        var at = game.Level.AllLocs().First(p => game.Level.IsEmptyFloor(p) && p.DistanceTo(game.Player.Position) > 2);
        var monster = new MonsterSpawner(Data).Place(game.Level, game.Rng, Data.Monster(raceId)!, at);
        game.Scheduler.Add(monster);
        return monster;
    }

    [Theory]
    [InlineData("2026-10-23", null)]
    [InlineData("2026-10-24", "halloween")]
    [InlineData("2026-10-26", "halloween")]
    [InlineData("2026-10-31", "halloween")]
    [InlineData("2026-11-07", "halloween")]
    [InlineData("2026-11-08", null)]
    [InlineData("2026-12-17", null)]
    [InlineData("2026-12-18", "midwinter")]
    [InlineData("2026-12-31", "midwinter")]
    [InlineData("2027-01-01", "midwinter")]
    [InlineData("2027-01-06", "midwinter")]
    [InlineData("2027-01-07", null)]
    [InlineData("2028-02-29", null)]
    [InlineData("2026-06-15", null)]
    public void TheCalendar_GivesTheEventRunningOnADay_EndsIncluded_AndMidwinterWrapsTheYear(string day, string? season) =>
        Assert.Equal(season, Seasons.At(DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture))?.Id);

    [Fact]
    public void EachEvent_IsWhole_ItsMonstersKeptApart_ItsKeepsakesAndFeatReal()
    {
        Assert.Equal(["halloween", "midwinter"], Seasons.All.Select(s => s.Id));
        Assert.Equal("24 October to 7 November", Seasons.AllHallowsEve.When);
        Assert.Equal("18 December to 6 January", Seasons.Midwinter.When);
        var feats = Feats.All(Data);
        foreach (var season in Seasons.All)
        {
            Assert.Same(season, Seasons.Find(season.Id));
            Assert.Contains(season.Unique, season.Monsters);
            foreach (var id in season.Monsters)
            {
                var race = Data.Monster(id);
                Assert.NotNull(race);
                Assert.Contains(race, Data.SeasonalMonsters);
                Assert.DoesNotContain(race, Data.Monsters);                   // (so the dungeon never draws on it by itself)
                Assert.Contains(race, Data.AllMonsters);
                Assert.Same(season, Seasons.Of(id));
                Assert.Equal(id == season.Unique, race.IsUnique);
                Assert.NotNull(Data.Monster(race.TileAs!));                    // (drawn as one of the game's own)
                Assert.True(race.Depth > 0);
            }
            Assert.NotEmpty(season.Cosmetics);
            foreach (var c in season.Cosmetics)
            {
                Assert.NotNull(Data.Object(c.Kind));
                Assert.Same(c, Seasons.Cosmetic(c.Id));
            }
            Assert.Contains(feats, f => f.Id == season.FeatId && f.Name == season.FeatName);
        }
        Assert.All(Data.SeasonalMonsters, r => Assert.NotNull(Seasons.Of(r.Id)));  // (none belongs to no event)
        Assert.Null(Seasons.Find("easter"));
        Assert.Null(Seasons.Of("jackal"));
    }

    [Fact]
    public void TheEvent_IsFixedAtBirth_KeptInTheSave_AndAnOlderSaveHasNone()
    {
        var game = Game("halloween");
        Assert.Equal("halloween", game.Player.Season);
        Assert.Same(Seasons.AllHallowsEve, game.Season);
        Assert.Contains("Born at All Hallows' Eve", CharacterDump.Build(game));
        Assert.Null(Game(null).Season);
        Assert.DoesNotContain("Born at", CharacterDump.Build(Game(null)));
        Assert.Null(Game("easter").Player.Season);                            // (an event the game doesn't have: none)

        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(Data, stream);
        Assert.Equal("halloween", loaded.Player.Season);
        Assert.Same(Seasons.AllHallowsEve, loaded.Season);

        // A save from before seasonal events: no Season in it at all.
        using var plain = new MemoryStream();
        SaveGame.Save(Game(null), plain);
        var json = System.Text.Encoding.UTF8.GetString(plain.ToArray());
        Assert.DoesNotContain("halloween", json);
        plain.Position = 0;
        Assert.Null(SaveGame.Load(Data, plain).Season);
    }

    [Fact]
    public void TheEventsMonsters_JoinTheDungeon_OnlyForACharacterBornInIt()
    {
        var halloween = SeasonalMet(Game("halloween", seed: 21), 6, 40);
        Assert.NotEmpty(halloween);
        Assert.All(halloween, id => Assert.Same(Seasons.AllHallowsEve, Seasons.Of(id)));

        var midwinter = SeasonalMet(Game("midwinter", seed: 22), 16, 40);
        Assert.NotEmpty(midwinter);
        Assert.All(midwinter, id => Assert.Same(Seasons.Midwinter, Seasons.Of(id)));

        Assert.Empty(SeasonalMet(Game(null, seed: 21), 6, 40));
        Assert.Empty(SeasonalMet(Game(null, seed: 22), 16, 40));
    }

    [Fact]
    public void NeverInTheDailyDungeon_TheWeeklyChallenge_OrTheTutorial()
    {
        var daily = Game("halloween", seed: 23);
        daily.Player.DailyDate = "2026-10-26";
        Assert.Null(daily.Season);
        Assert.Empty(SeasonalMet(daily, 6, 30));

        var weekly = Game("halloween", seed: 24);
        WeeklyChallenge.Prepare(weekly, "2026-W44");
        Assert.Null(weekly.Season);
        Assert.Empty(SeasonalMet(weekly, 6, 30));

        Assert.Null(Tutorial.Create(Data).Season);
        Assert.Null(Tutorial.Create(Data).Player.Season);
    }

    [Fact]
    public void TheTown_SaysWhatSeasonItIs_OnArrival()
    {
        var game = Game("midwinter", seed: 25);
        var said = new List<string>();
        using var _ = game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        game.Execute(new DebugJumpCommand(2));
        Assert.DoesNotContain(said, t => t == Seasons.Midwinter.TownByDay || t == Seasons.Midwinter.TownByNight);
        game.Execute(new DebugJumpCommand(0));
        Assert.Contains(said, t => t == (game.IsDaytime ? Seasons.Midwinter.TownByDay : Seasons.Midwinter.TownByNight));

        var plain = Game(null, seed: 25);
        var quiet = new List<string>();
        using var __ = plain.Events.Subscribe<MessageEvent>(m => quiet.Add(m.Text));
        plain.Execute(new DebugJumpCommand(0));
        Assert.DoesNotContain(quiet, t => Seasons.All.Any(s => t == s.TownByDay || t == s.TownByNight));
    }

    [Fact]
    public void TheEventsUnique_Slain_EarnsItsFeat_AndLetsFallAKeepsake()
    {
        var game = Game("halloween", seed: 26);
        game.Execute(new DebugJumpCommand(20));
        var feat = Feats.All(Data).Single(f => f.Id == Seasons.AllHallowsEve.FeatId);
        Assert.False(feat.Earned(game));
        var horseman = Place(game, "the_headless_horseman");
        Assert.Equal("The Headless Horseman", horseman.Race.Name);
        game.ProjectileHitsMonster(horseman, "test", null, 1_000_000);
        Assert.False(horseman.IsActive);

        Assert.True(feat.Earned(game));
        Assert.Equal("Trick or Treat", feat.Name);
        Assert.False(Feats.All(Data).Single(f => f.Id == Seasons.Midwinter.FeatId).Earned(game));
        var keepsake = Assert.Single(game.Level.Objects.All.Select(o => o.Item), i => i.Cosmetic is not null);
        var cosmetic = Seasons.Cosmetic(keepsake.Cosmetic)!;
        Assert.Contains(cosmetic, Seasons.AllHallowsEve.Cosmetics);
        Assert.Equal(cosmetic.Kind, keepsake.Kind.Id);
    }

    [Fact]
    public void AKeepsake_IsItsKind_UnderAThemedName_KeptInTheSave_AndApartFromThePlainOnes()
    {
        var game = Game("halloween");
        var lantern = game.MakeCosmetic(Seasons.Cosmetic("pumpkin_lantern")!, 5)!;
        var plain = game.Objects.Create("lantern");
        Assert.Equal("lantern", lantern.Kind.Id);
        Assert.Equal((plain.Fuel, plain.Base.Id), (lantern.Fuel, lantern.Base.Id));   // (a lantern in all but name)
        Assert.StartsWith("a Carved Pumpkin Lantern", game.Describe(lantern));
        Assert.Contains("grinning face", ObjectInfo.DescribeItem(game, lantern));
        Assert.False(lantern.CanStackWith(plain));

        var skulls = game.MakeCosmetic(Seasons.Cosmetic("sugar_skull")!, 5)!;
        skulls.Number = 2;
        Assert.Equal("ration_of_food", skulls.Kind.Id);
        Assert.StartsWith("2 Sugar Skulls", game.Describe(skulls));

        var back = SaveGame.ItemFromJson(game, SaveGame.ItemToJson(lantern))!;
        Assert.Equal("pumpkin_lantern", back.Cosmetic);
        Assert.Equal("pumpkin_lantern", back.Clone(999, 1).Cosmetic);
    }

    [Fact]
    public void TheEventsOtherMonsters_LetFallKeepsakes_NowAndThen()
    {
        var game = Game("midwinter", seed: 27);
        game.Execute(new DebugJumpCommand(10));
        var dropped = 0;
        for (var i = 0; i < 60; i++)
        {
            var sprite = Place(game, "frost_sprite");
            var before = game.Level.Objects.All.Count(o => o.Item.Cosmetic is not null);
            game.ProjectileHitsMonster(sprite, "test", null, 1_000_000);
            dropped += game.Level.Objects.All.Count(o => o.Item.Cosmetic is not null) - before;
        }
        Assert.InRange(dropped, 3, 25);                                       // (about one in six)
        Assert.All(game.Level.Objects.All.Where(o => o.Item.Cosmetic is not null),
            o => Assert.Contains(Seasons.Cosmetic(o.Item.Cosmetic), Seasons.Midwinter.Cosmetics));
    }

    [Fact]
    public void AReplay_PlaysTheEventBackFromTheSave_WhateverTheDate()
    {
        var game = Game("halloween", seed: 28);
        game.Recorder = new ReplayRecorder(game);
        for (var i = 0; i < 12; i++) game.Execute(new DebugJumpCommand(6));
        for (var i = 0; i < 40; i++) game.Execute(new HoldCommand());
        var file = game.Recorder.ToFile(game);

        var player = new ReplayPlayer(Data, file);
        Assert.Same(Seasons.AllHallowsEve, player.Game.Season);
        while (player.Step()) { }
        Assert.True(player.Matches);
        Assert.Equal(game.Level.Monsters.All.Select(m => (m.Race.Id, m.Position)), player.Game.Level.Monsters.All.Select(m => (m.Race.Id, m.Position)));
    }
}
