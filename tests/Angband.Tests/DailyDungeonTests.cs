using Angband.Core.Game;
using Angband.Core.Persistence;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>The daily dungeon: one seed and one character a day, the same for everyone; a table of tries.</summary>
public class DailyDungeonTests
{
    private static readonly DateOnly Day = new(2026, 10, 1);

    [Fact]
    public void EachDay_HasItsOwnSeed_TheSameEverywhere()
    {
        Assert.Equal(DailyDungeon.SeedFor(Day), DailyDungeon.SeedFor(new DateOnly(2026, 10, 1)));
        Assert.NotEqual(DailyDungeon.SeedFor(Day), DailyDungeon.SeedFor(Day.AddDays(1)));
        Assert.Equal(new DateOnly(2026, 10, 1), DailyDungeon.Today(new DateTime(2026, 10, 1, 23, 59, 0, DateTimeKind.Utc)));
        Assert.Equal("2026-10-01", DailyDungeon.Stamp(Day));
    }

    [Fact]
    public void TheDaysCharacter_IsTheSameForEveryone_ButTheirName()
    {
        var mine = DailyDungeon.SpecFor(TestData.Game, Day, "Mine");
        var theirs = DailyDungeon.SpecFor(TestData.Game, Day, "Theirs");
        Assert.Equal((mine.RaceId, mine.ClassId), (theirs.RaceId, theirs.ClassId));
        Assert.Equal(mine.BaseStats, theirs.BaseStats);
        Assert.Equal("Mine", mine.Name);
        Assert.NotNull(TestData.Game.Race(mine.RaceId));
        Assert.NotNull(TestData.Game.Class(mine.ClassId));

        // Over a month, the days bring different characters.
        var kinds = Enumerable.Range(0, 30).Select(i => DailyDungeon.SpecFor(TestData.Game, Day.AddDays(i), "x")).Select(s => s.RaceId + s.ClassId).Distinct().Count();
        Assert.True(kinds > 10);

        // And the same dungeon: two players' first levels down are the same.
        var a = GameSession.NewGame(TestData.Game, DailyDungeon.SeedFor(Day), mine);
        var b = GameSession.NewGame(TestData.Game, DailyDungeon.SeedFor(Day), theirs);
        a.Player.Position = a.Level.FindFeature(Angband.Core.Definitions.TerrainFlags.DownStair).First();
        b.Player.Position = b.Level.FindFeature(Angband.Core.Definitions.TerrainFlags.DownStair).First();
        a.Execute(new TakeStairsCommand(Down: true));
        b.Execute(new TakeStairsCommand(Down: true));
        Assert.Equal(a.Level.AllLocs().Select(p => a.Level[p].Feature), b.Level.AllLocs().Select(p => b.Level[p].Feature));
    }

    [Fact]
    public void ADailyCharacter_KeepsItsDay_InTheSave()
    {
        var game = GameSession.NewGame(TestData.Game, DailyDungeon.SeedFor(Day), DailyDungeon.SpecFor(TestData.Game, Day, "x"));
        game.Player.DailyDate = "2026-10-01";
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        Assert.Equal("2026-10-01", SaveGame.Load(TestData.Game, stream).Player.DailyDate);
    }

    [Fact]
    public void TheDailyTable_NumbersTries_AndPutsTheBestFirst()
    {
        var board = new DailyBoard();
        board.Add(new DailyEntry { Day = "2026-10-01", MaxDepth = 5, Points = 100 });
        board.Add(new DailyEntry { Day = "2026-10-01", MaxDepth = 9, Points = 300 });
        board.Add(new DailyEntry { Day = "2026-09-30", MaxDepth = 20, Points = 900 });
        Assert.Equal([1, 2], board.For("2026-10-01").Select(e => e.Attempt).Order());
        Assert.Equal(9, board.For("2026-10-01")[0].MaxDepth);
        Assert.Equal(["2026-10-01", "2026-10-01", "2026-09-30"], board.Ordered().Select(e => e.Day));
    }

    [Fact]
    public void A_try_has_a_line_to_share_and_a_name_for_its_replay()
    {
        var entry = new DailyEntry
        {
            Day = "2026-10-01", Attempt = 2, Name = "Dain", Race = "Dwarf", Class = "Priest", Level = 18, MaxDepth = 29, Points = 12345,
            Fate = "killed by Grip, Farmer Maggot's Dog at 1450 ft",
        };
        Assert.Equal("AVABand daily 2026-10-01 (try 2): Dain the Dwarf Priest reached 1450 ft (level 18), 12,345 points — "
                     + "killed by Grip, Farmer Maggot's Dog at 1450 ft.", entry.ShareLine());
        Assert.Equal("AVABand-daily-2026-10-01-try2.avareplay", entry.ReplayFileName(".avareplay"));
        entry.Attempt = 1;
        Assert.StartsWith("AVABand daily 2026-10-01: ", entry.ShareLine());
    }
}
