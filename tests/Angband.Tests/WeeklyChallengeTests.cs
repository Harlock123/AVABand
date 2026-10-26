using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Persistence;
using Angband.Core.Records;
using Angband.Core.World;

namespace Angband.Tests;

/// <summary>The weekly challenge: one seed, one character and one twist a week, the same for everyone; a table of tries.</summary>
public class WeeklyChallengeTests
{
    private const string Week = "2026-W44";

    private static GameSession NewGame(WeeklyTwist? twist, ulong seed = 6)
    {
        var game = GameSession.NewGame(TestData.Game, seed, CharacterSpec.Default("human", "warrior"));
        if (twist is not null)
        {
            game.Player.WeeklyWeek = Week;
            game.Player.WeeklyTwist = twist.Id;
        }
        return game;
    }

    private static GameSession Arena(WeeklyTwist? twist)
    {
        var game = Tests.Arena.Create();
        if (twist is not null)
        {
            game.Player.WeeklyWeek = Week;
            game.Player.WeeklyTwist = twist.Id;
        }
        return game;
    }

    private static List<string> Messages(GameSession game)
    {
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        return said;
    }

    // --- Weeks, seeds and characters -------------------------------------------------------------

    [Fact]
    public void Weeks_are_ISO_weeks_in_UTC_across_a_years_end_too()
    {
        Assert.Equal("2026-W44", WeeklyChallenge.Stamp(new DateOnly(2026, 10, 26))); // a Monday
        Assert.Equal("2026-W44", WeeklyChallenge.Stamp(new DateOnly(2026, 11, 1))); // its Sunday
        Assert.Equal("2026-W43", WeeklyChallenge.Stamp(new DateOnly(2026, 10, 25)));
        // 2026 has 53 weeks: its last runs into 2027.
        Assert.Equal("2026-W53", WeeklyChallenge.Stamp(new DateOnly(2026, 12, 31)));
        Assert.Equal("2026-W53", WeeklyChallenge.Stamp(new DateOnly(2027, 1, 3)));
        Assert.Equal("2027-W01", WeeklyChallenge.Stamp(new DateOnly(2027, 1, 4)));
        // And a year's first week can begin in December.
        Assert.Equal("2025-W01", WeeklyChallenge.Stamp(new DateOnly(2024, 12, 30)));
        Assert.Equal("2026-W44", WeeklyChallenge.ThisWeek(new DateTime(2026, 11, 1, 23, 59, 0, DateTimeKind.Utc)));
        Assert.Equal("2026-W45", WeeklyChallenge.ThisWeek(new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(new DateOnly(2026, 10, 26), WeeklyChallenge.MondayOf("2026-W44"));
        Assert.Equal(new DateOnly(2026, 12, 28), WeeklyChallenge.MondayOf("2026-W53"));
        Assert.Equal(new DateOnly(2024, 12, 30), WeeklyChallenge.MondayOf("2025-W01"));
        Assert.Null(WeeklyChallenge.MondayOf("2025-W53")); // (2025 has 52)
        Assert.Null(WeeklyChallenge.MondayOf("2026-10-26"));
        Assert.Null(WeeklyChallenge.MondayOf("2026-W00"));
        Assert.Null(WeeklyChallenge.MondayOf(""));
    }

    [Fact]
    public void Each_week_has_its_own_seed_the_same_everywhere_and_not_a_days()
    {
        Assert.Equal(WeeklyChallenge.SeedFor(Week), WeeklyChallenge.SeedFor("2026-W44"));
        Assert.NotEqual(WeeklyChallenge.SeedFor(Week), WeeklyChallenge.SeedFor("2026-W45"));
        Assert.NotEqual(WeeklyChallenge.SeedFor(Week), DailyDungeon.SeedFor(new DateOnly(2026, 10, 26)));
    }

    [Fact]
    public void The_weeks_character_is_the_same_for_everyone_but_their_name()
    {
        var mine = WeeklyChallenge.SpecFor(TestData.Game, Week, "Mine");
        var theirs = WeeklyChallenge.SpecFor(TestData.Game, Week, "Theirs");
        Assert.Equal((mine.RaceId, mine.ClassId), (theirs.RaceId, theirs.ClassId));
        Assert.Equal(mine.BaseStats, theirs.BaseStats);
        Assert.Equal("Mine", mine.Name);
        Assert.Equal("Adventurer", WeeklyChallenge.SpecFor(TestData.Game, Week, " ").Name);
        Assert.NotNull(TestData.Game.Race(mine.RaceId));
        Assert.NotNull(TestData.Game.Class(mine.ClassId));

        var monday = WeeklyChallenge.MondayOf(Week)!.Value;
        var kinds = Enumerable.Range(0, 30).Select(i => WeeklyChallenge.SpecFor(TestData.Game, WeeklyChallenge.Stamp(monday.AddDays(7 * i)), "x"))
            .Select(s => s.RaceId + s.ClassId).Distinct().Count();
        Assert.True(kinds > 10);
    }

    [Fact]
    public void The_twists_take_turns_never_the_same_two_weeks_running_and_every_one_in_seven()
    {
        var monday = new DateOnly(2026, 9, 7);
        var twists = Enumerable.Range(0, 60).Select(i => WeeklyChallenge.TwistFor(WeeklyChallenge.Stamp(monday.AddDays(7 * i)))).ToList();
        for (var i = 1; i < twists.Count; i++) Assert.NotSame(twists[i - 1], twists[i]);
        for (var i = 0; i + 7 <= twists.Count; i++) Assert.Equal(7, twists.Skip(i).Take(7).Distinct().Count());
        Assert.NotSame(WeeklyChallenge.TwistFor("2026-W53"), WeeklyChallenge.TwistFor("2027-W01")); // over the year's end too
        Assert.Same(WeeklyChallenge.TwistFor(Week), WeeklyChallenge.TwistFor("2026-W44"));
        Assert.Throws<ArgumentException>(() => WeeklyChallenge.TwistFor("nonsense"));
        Assert.Equal(WeeklyChallenge.Twists.Count, WeeklyChallenge.Twists.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public void A_weekly_character_keeps_its_week_and_twist_in_the_save_and_its_replay()
    {
        var game = GameSession.NewGame(TestData.Game, WeeklyChallenge.SeedFor(Week), WeeklyChallenge.SpecFor(TestData.Game, Week, "x"));
        WeeklyChallenge.Prepare(game, Week);
        Assert.Equal(WeeklyChallenge.TwistFor(Week).Id, game.Player.WeeklyTwist);
        Assert.Same(WeeklyChallenge.TwistFor(Week), game.Twist);
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal(Week, loaded.Player.WeeklyWeek);
        Assert.Same(WeeklyChallenge.TwistFor(Week), loaded.Twist);

        var recorder = new ReplayRecorder(game);
        var replay = new ReplayPlayer(TestData.Game, recorder.ToFile(game));
        Assert.Same(WeeklyChallenge.TwistFor(Week), replay.Game.Twist);
    }

    [Fact]
    public void A_weekly_character_has_no_hirelings_ghosts_strongroom_or_challenges()
    {
        var game = GameSession.NewGame(TestData.Game, 6, CharacterSpec.Default("human", "warrior") with
        {
            Options = new Dictionary<string, bool> { [OptionIds.AvaSpeedRun] = true },
        });
        Assert.True(game.HirelingsOn);
        Assert.True(game.StrongroomOn);
        Assert.NotEmpty(game.ChallengeNames);
        Assert.False(game.Player.IsSeededRun);
        WeeklyChallenge.Prepare(game, Week);
        Assert.True(game.Player.IsSeededRun);
        Assert.False(game.HirelingsOn);
        Assert.False(game.GhostsOn);
        Assert.False(game.StrongroomOn);
        Assert.Empty(game.ChallengeNames);
        Assert.False(game.SpeedRunOn);
    }

    [Fact]
    public void An_ordinary_game_or_a_daily_one_has_no_twist()
    {
        Assert.Null(NewGame(null).Twist);
        var daily = NewGame(null);
        daily.Player.DailyDate = "2026-10-26";
        Assert.Null(daily.Twist);
        Assert.True(daily.Player.IsSeededRun);
    }

    [Fact]
    public void A_weekly_game_under_each_twist_plays_back_from_its_replay_exactly()
    {
        foreach (var twist in WeeklyChallenge.Twists)
        {
            var game = NewGame(twist);
            game.Recorder = new ReplayRecorder(game);
            game.Execute(new DebugJumpCommand(3));
            for (var i = 0; i < 300 && !game.Player.IsDead; i++) game.Execute(new HoldCommand());
            var file = game.Recorder.ToFile(game);
            var replay = new ReplayPlayer(TestData.Game, file);
            while (replay.Step()) { }
            Assert.True(replay.Matches, twist.Name);
            Assert.Equal(game.Level.Monsters.All.Count(), replay.Game.Level.Monsters.All.Count());
        }
    }

    // --- The twists -----------------------------------------------------------------------------

    /// <summary>Squares on the level that glow though their terrain doesn't (a lit room's).</summary>
    private static int LitSquares(GameSession game) =>
        game.Level.AllLocs().Count(p => game.Level[p].Has(SquareFlags.Glow) && !game.Level.FeatureAt(p).Has(TerrainFlags.Bright));

    [Fact]
    public void Darkness_puts_out_every_rooms_light_in_the_dungeon_but_not_the_town()
    {
        var dark = NewGame(WeeklyChallenge.Darkness);
        var usual = NewGame(null);
        var lit = 0;
        for (var depth = 1; depth <= 6; depth++)
        {
            dark.Execute(new DebugJumpCommand(depth));
            usual.Execute(new DebugJumpCommand(depth));
            Assert.Equal(0, LitSquares(dark));
            Assert.False(dark.Level.IsLit);
            lit += LitSquares(usual);
        }
        Assert.True(lit > 0); // (shallow rooms are mostly lit, as a rule)
        dark.Execute(new DebugJumpCommand(0));
        usual.Execute(new DebugJumpCommand(0));
        Assert.Equal(LitSquares(usual), LitSquares(dark));
    }

    [Fact]
    public void Glass_cannon_makes_your_blows_and_theirs_hurt_half_as_much_again()
    {
        foreach (var (twist, factor) in new[] { (WeeklyChallenge.GlassCannon, 15), (null, 10) })
        {
            var game = Arena(twist);
            game.Player.MaxHp = game.Player.Hp = 500;
            game.TakeHit(10, "a test");
            Assert.Equal(500 - factor, game.Player.Hp);

            var monster = Tests.Arena.AddMonster(game, "jackal", Start.Step(Direction.East));
            monster.MaxHp = monster.Hp = 100;
            game.DamageMonster(monster, 10);
            Assert.Equal(100 - factor, monster.Hp);
        }
    }

    [Fact]
    public void Famine_makes_food_go_twice_as_fast()
    {
        var used = new List<int>();
        foreach (var twist in new[] { WeeklyChallenge.Famine, null })
        {
            var game = Arena(twist);
            game.Player.Food = game.Data.Constants.FoodFull - 1;
            var before = game.Player.Food;
            TestGames.HoldUntil(game, game.GameTurn + 20_000);
            used.Add(before - game.Player.Food);
        }
        Assert.True(used[1] > 0);
        Assert.InRange(used[0], used[1] * 2 - 20, used[1] * 2 + 20);
    }

    [Fact]
    public void Swarm_gives_each_dungeon_level_half_as_many_monsters_again()
    {
        var (more, ordinary) = (0, 0);
        for (var depth = 1; depth <= 5; depth++)
        {
            var swarm = NewGame(WeeklyChallenge.Swarm);
            var usual = NewGame(null);
            swarm.Execute(new DebugJumpCommand(depth));
            usual.Execute(new DebugJumpCommand(depth));
            // The same level, peopled the same way, and then some.
            Assert.Equal(usual.Level.AllLocs().Select(p => usual.Level[p].Feature), swarm.Level.AllLocs().Select(p => swarm.Level[p].Feature));
            Assert.True(swarm.Level.Monsters.All.Count() >= usual.Level.Monsters.All.Count());
            more += swarm.Level.Monsters.All.Count();
            ordinary += usual.Level.Monsters.All.Count();
            Assert.Equal(swarm.Level.Monsters.All.Count(), swarm.Scheduler.Actors.Count(a => a is Angband.Core.Monsters.Monster));
        }
        Assert.True(more > ordinary * 5 / 4, $"{more} v {ordinary}");

        // And the town is as ever.
        var town = NewGame(WeeklyChallenge.Swarm);
        var usualTown = NewGame(null);
        town.Execute(new DebugJumpCommand(0));
        usualTown.Execute(new DebugJumpCommand(0));
        Assert.Equal(usualTown.Level.Monsters.All.Count(), town.Level.Monsters.All.Count());
    }

    [Fact]
    public void Restless_foes_are_two_faster_and_you_are_not()
    {
        var game = Arena(WeeklyChallenge.Restless);
        var monster = Tests.Arena.AddMonster(game, "jackal", Start.Step(Direction.East));
        Assert.Equal(2, game.Scheduler.SpeedBonus!(monster));
        Assert.Equal(0, game.Scheduler.SpeedBonus!(game.Player));

        var usual = Arena(null);
        var theirs = Tests.Arena.AddMonster(usual, "jackal", Start.Step(Direction.East));
        Assert.Equal(0, usual.Scheduler.SpeedBonus!(theirs));
    }

    [Fact]
    public void Gold_rush_doubles_the_gold_you_find()
    {
        foreach (var (twist, factor) in new[] { (WeeklyChallenge.GoldRush, 2), (null, 1) })
        {
            var game = Arena(twist);
            var gold = game.Objects.MakeGold(game.Rng, 5);
            game.Level.Objects.Add(Start.Step(Direction.East), gold);
            var before = game.Player.Gold;
            game.Execute(new WalkCommand(Direction.East));
            Assert.Equal(before + gold.GoldValue * factor, game.Player.Gold);
        }
    }

    [Fact]
    public void Lean_times_leave_only_the_General_Store_selling()
    {
        var game = NewGame(WeeklyChallenge.LeanTimes);
        var said = Messages(game);
        game.Player.Gold = 100_000;
        game.Player.Position = game.Level.AllLocs().First(p => game.Level.FeatureAt(p).Shop == "armoury");
        game.Execute(new EnterStoreCommand());
        Assert.Contains(said, s => s.Contains("Lean times"));
        game.Execute(new BuyCommand(game.StoreHere!.Stock[0]));
        Assert.Equal(100_000, game.Player.Gold);
        Assert.Contains(WeeklyChallenge.LeanTimes.Name.Split(' ')[0], said.Last());

        game.Execute(new LeaveStoreCommand());
        game.Player.Position = game.Level.AllLocs().First(p => game.Level.FeatureAt(p).Shop == "general");
        game.Execute(new EnterStoreCommand());
        game.Execute(new BuyCommand(game.StoreHere!.Stock[0]));
        Assert.True(game.Player.Gold < 100_000);

        // And in an ordinary game, the Armoury sells.
        var usual = NewGame(null);
        usual.Player.Gold = 100_000;
        usual.Player.Position = usual.Level.AllLocs().First(p => usual.Level.FeatureAt(p).Shop == "armoury");
        usual.Execute(new EnterStoreCommand());
        usual.Execute(new BuyCommand(usual.StoreHere!.Stock[0]));
        Assert.True(usual.Player.Gold < 100_000);
    }

    // --- The board and its feats ----------------------------------------------------------------

    [Fact]
    public void The_weekly_table_numbers_tries_puts_the_best_first_and_has_a_line_to_share()
    {
        var board = new WeeklyBoard();
        board.Add(new WeeklyEntry { Day = Week, MaxDepth = 5, Points = 100 });
        board.Add(new WeeklyEntry { Day = Week, MaxDepth = 9, Points = 300 });
        board.Add(new WeeklyEntry { Day = "2026-W43", MaxDepth = 20, Points = 900 });
        Assert.Equal([1, 2], board.For(Week).Select(e => e.Attempt).Order());
        Assert.Equal(9, board.For(Week)[0].MaxDepth);
        Assert.Equal([Week, Week, "2026-W43"], board.Ordered().Select(e => e.Day));

        var path = Path.Combine(Path.GetTempPath(), "avaband-weekly-" + Guid.NewGuid() + ".json");
        try
        {
            board.Save(path);
            var loaded = WeeklyBoard.Load(path);
            Assert.Equal(3, loaded.Entries.Count);
            Assert.IsType<WeeklyEntry>(loaded.Entries[0]);
        }
        finally
        {
            File.Delete(path);
        }

        var entry = new WeeklyEntry
        {
            Day = Week, Twist = "Darkness", Attempt = 2, Name = "Bob", Race = "Human", Class = "Warrior", Level = 18, MaxDepth = 25, Points = 9876,
            Fate = "killed by a cave troll at 1250 ft",
        };
        Assert.Equal("AVABand weekly 2026-W44 (Darkness): Bob the Human Warrior reached 1250 ft (level 18), 9,876 points — "
                     + "killed by a cave troll at 1250 ft; 3 tries.", entry.ShareLine(3));
        Assert.EndsWith("; 1 try.", entry.ShareLine(1));
        Assert.Equal("AVABand-weekly-2026-W44-try2.avareplay", entry.ReplayFileName(".avareplay"));
    }

    [Fact]
    public void Four_weeks_running_counts_weeks_in_a_row_over_a_years_end()
    {
        var board = new WeeklyBoard();
        foreach (var week in new[] { "2026-W50", "2026-W51", "2026-W52", "2026-W53", "2027-W01", "2027-W01", "2027-W05" })
            board.Add(new WeeklyEntry { Day = week, DateUtc = new DateTime(2027, 1, 5, 0, 0, 0, DateTimeKind.Utc), Name = "Bob" });
        Assert.Equal(5, Feats.WeeklyStreak(board));
        Assert.Equal(0, Feats.WeeklyStreak(new WeeklyBoard()));

        var gappy = new WeeklyBoard();
        foreach (var week in new[] { "2026-W40", "2026-W41", "2026-W42", "2026-W44" }) gappy.Add(new WeeklyEntry { Day = week });
        Assert.Equal(3, Feats.WeeklyStreak(gappy));
    }

    [Fact]
    public void The_weekly_feats_come_from_the_weekly_board_and_never_the_daily_ones()
    {
        var feats = Feats.All(TestData.Game);
        var book = new FeatBook();
        Assert.Empty(book.CheckWeeklyBoard(new WeeklyBoard(), feats));

        var board = new WeeklyBoard();
        board.Add(new WeeklyEntry { Day = Week, Name = "Bob", Race = "Human", Class = "Warrior", DateUtc = DateTime.UtcNow });
        Assert.Equal(["weekly"], book.CheckWeeklyBoard(board, feats).Select(f => f.Id));
        Assert.Equal("Bob the Human Warrior", book.Earned["weekly"].Character);
        foreach (var week in new[] { "2026-W41", "2026-W42", "2026-W43" }) board.Add(new WeeklyEntry { Day = week, Name = "Bob" });
        Assert.Equal(["weekly_month"], book.CheckWeeklyBoard(board, feats).Select(f => f.Id));
        Assert.DoesNotContain("daily", book.Earned.Keys);

        // A daily try is no weekly feat.
        var daily = new DailyBoard();
        daily.Add(new DailyEntry { Day = "2026-10-26", Name = "Dain" });
        Assert.DoesNotContain(new FeatBook().CheckBoard(daily, feats), f => f.Id.StartsWith("weekly"));

        // Week's Deepest: 1000 ft in a weekly challenge, and only there.
        var deep = feats.Single(f => f.Id == "weekly_deep");
        var game = NewGame(WeeklyChallenge.Swarm);
        game.Player.MaxDepth = 20;
        Assert.True(deep.Earned(game));
        var usual = NewGame(null);
        usual.Player.MaxDepth = 20;
        Assert.False(deep.Earned(usual));
    }

    private static readonly Loc Start = new(5, 2);
}
