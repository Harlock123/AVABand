using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Persistence;
using Angband.Core.Quests;
using Angband.Core.Randomness;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>Raids on the town (AVABand's own).</summary>
public partial class AvaQuestTests
{
    /// <summary>A character in a town grown to <paramref name="stage"/> (by jobs done), with no raid on it yet.</summary>
    private static Quester InGrownTown(int stage, int level = 20, ulong seed = 3)
    {
        var q = Start(level, seed);
        q.Game.AvaQuests.JobsDone = GameSession.TownStageRenown[stage];
        Return(q, 3);
        Assert.Equal(stage, q.Game.TownStage);
        Assert.Null(q.Game.Raid); // (never on the first return to a new stage)
        return q;
    }

    /// <summary>Down and back up to town, with no raid of its own on the way (they're started by hand here).</summary>
    private static void Return(Quester q, int depth = 3)
    {
        q.Jump(depth);
        q.Game.AvaQuests.LastReturnRaided = true;
        q.Jump(0);
        foreach (var m in q.Game.Level.Monsters.All.Where(m => m.Race.Depth == 0).ToList()) q.Game.Level.Monsters.Remove(m);
    }

    private static TownRaid Raid(Quester q, ulong dice = 7) => Assert.IsType<TownRaid>(q.Game.StartRaid(new GameRandom(dice)));

    private static bool AtEdge(Angband.Core.World.Level level, Loc p, int within) =>
        p.X <= within || p.Y <= within || p.X >= level.Width - 1 - within || p.Y >= level.Height - 1 - within;

    [Fact]
    public void Town_raids_are_a_birth_option_on_by_default_and_never_in_seeded_runs_the_tutorial_or_older_replays()
    {
        var option = OptionCatalog.Find(OptionIds.AvaRaids)!;
        Assert.Equal("birth_ava_raids", option.Id);
        Assert.Equal(OptionKind.Birth, option.Kind);
        Assert.True(option.Default);

        var game = Start().Game;
        Assert.True(game.RaidsOn);
        game.Player.DailyDate = "2026-10-26";
        Assert.False(game.RaidsOn);
        game.Player.DailyDate = null;
        game.Player.WeeklyWeek = "2026-W43";
        Assert.False(game.RaidsOn);
        Assert.False(Tutorial.Create(TestData.Game).RaidsOn);

        var without = GameSession.NewGame(TestData.Game, 3, CharacterSpec.Default("human", "warrior") with
        {
            Options = new Dictionary<string, bool> { [OptionIds.AvaRaids] = false },
        });
        Assert.False(without.RaidsOn);
        Assert.True(without.TownGrowthOn); // (the town still grows)

        // A replay recorded before there were raids plays back without them.
        var recorded = Start().Game;
        var file = new ReplayRecorder(recorded).ToFile(recorded);
        Assert.Equal(4, ReplayFile.CurrentVersion);
        file.Version = 3;
        var old = new ReplayPlayer(TestData.Game, file).Game;
        Assert.True(old.TownGrowthOn);
        Assert.False(old.RaidsOn);
        file.Version = ReplayFile.CurrentVersion;
        Assert.True(new ReplayPlayer(TestData.Game, file).Game.RaidsOn);
    }

    [Fact]
    public void Raids_come_about_one_return_in_eight_never_two_running_and_not_before_the_second_stage()
    {
        var q = InGrownTown(2);
        var game = q.Game;
        var raided = new List<bool>();
        for (var i = 0; i < 96; i++)
        {
            q.Jump(3); // (leaving a raid to it sets the town back; that doesn't stop raids)
            q.Jump(0);
            raided.Add(game.Raid is not null);
            if (game.Raid is { } raid)
            {
                Assert.True(game.AvaQuests.LastReturnRaided);
                Assert.Contains(q.Said, s => s.StartsWith("Raiders! A band of", StringComparison.Ordinal));
                Assert.Equal(raid.RaiderIds.Count, game.Raiders(raid).Count());
            }
        }
        for (var i = 1; i < raided.Count; i++) Assert.False(raided[i] && raided[i - 1], $"two raids running at {i}");
        Assert.InRange(raided.Count(r => r), 4, 24);

        // Never at the first stage, before level 5, or with the option off.
        var first = InGrownTown(1);
        var low = InGrownTown(2, level: 4);
        var off = GameSession.NewGame(TestData.Game, 3, CharacterSpec.Default("human", "warrior") with
        {
            Options = new Dictionary<string, bool> { [OptionIds.AvaRaids] = false },
        });
        off.MarkDebugUsed();
        off.GainExperience(off.ExperienceForLevel(19));
        off.AvaQuests.JobsDone = 3;
        var never = new Quester { Game = off };
        for (var i = 0; i < 40; i++)
            foreach (var other in new[] { first, low, never })
            {
                other.Jump(3);
                other.Jump(0);
                Assert.Null(other.Game.Raid);
            }
        Assert.Equal(2, off.TownStage);
    }

    [Fact]
    public void A_newly_grown_town_is_never_raided_on_that_return()
    {
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var q = Start(level: 20, seed: seed);
            q.Game.AvaQuests.JobsDone = 3;
            q.Jump(3);
            q.Jump(0);
            Assert.Equal(2, q.Game.TownStage);
            Assert.Null(q.Game.Raid);
            Assert.False(q.Game.AvaQuests.LastReturnRaided);
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(45)]
    public void Raiders_come_over_the_wall_at_the_towns_edge_a_band_suited_to_your_level(int level)
    {
        var q = InGrownTown(3, level);
        var game = q.Game;
        for (ulong dice = 1; dice <= 6; dice++)
        {
            var raid = Raid(q, dice);
            var raiders = game.Raiders(raid).ToList();
            Assert.Equal(1 + GameSession.RaidFollowers(level), raiders.Count);
            var leader = raiders[0];
            Assert.Equal(raid.LeaderId, leader.Id);
            Assert.Equal(leader.Race.Name, raid.Leader);
            Assert.Contains(raid.From, new[] { "north", "south", "east", "west" });
            Assert.True(AtEdge(game.Level, leader.Position, 2), $"leader at {leader.Position}");
            Assert.True(leader.Position.DistanceTo(game.Player.Position) > 12);
            var depth = GameSession.RaidDepth(level);
            Assert.InRange(leader.Race.Depth, 1, depth + 2);
            foreach (var m in raiders)
            {
                Assert.True(AtEdge(game.Level, m.Position, 6), $"{m} near the edge");
                Assert.False(m.IsAsleep);
                Assert.False(m.Race.IsUnique);
                Assert.Equal(leader.Race.Base, m.Race.Base); // (one kind)
                Assert.Equal(leader.Id, m.GroupId);
                Assert.True(m.Race.Depth <= leader.Race.Depth);
                if (m != leader) Assert.InRange(m.Race.Depth, 1, depth);
            }
            foreach (var m in raiders) game.Level.Monsters.Remove(m);
            game.AvaQuests.Raid = null;
        }
        // A level-5 character faces kobolds or jackals; a level-45 one, something far worse.
        Assert.True(GameSession.RaidDepth(5) <= 3);
        Assert.True(GameSession.RaidDepth(45) >= 20);
    }

    [Fact]
    public void The_townsfolk_run_indoors_and_the_raid_is_announced()
    {
        var q = Start(level: 20);
        var game = q.Game;
        game.AvaQuests.JobsDone = 3;
        q.Jump(3);
        q.Jump(0);
        Assert.Contains(game.Level.Monsters.All, m => m.Race.Depth == 0);
        var events = new List<TownRaidEvent>();
        game.Events.Subscribe<TownRaidEvent>(events.Add);
        var raid = Raid(q);
        Assert.DoesNotContain(game.Level.Monsters.All, m => m.Race.Depth == 0 && !m.IsVisible);
        Assert.All(game.Level.Monsters.All.Where(m => m.Race.Depth == 0), m => Assert.True(m.IsAfraid));
        var e = Assert.Single(events);
        Assert.Equal(raid.LeaderId, e.Leader.Id);
        Assert.Equal("Raiders in the town!", e.Caption);
        Assert.Contains($"from the {raid.From}", e.Subtitle);
        Assert.Contains(q.Said, s => s.Contains($"led by {(("aeiou".Contains(raid.Leader[0])) ? "an" : "a")} {raid.Leader}", StringComparison.Ordinal));
    }

    [Fact]
    public void Killing_the_leader_drives_the_raid_off_for_renown_gold_history_and_a_feat()
    {
        var q = InGrownTown(2);
        var game = q.Game;
        var renown = game.TownRenown;
        var gold = game.Player.Gold;
        var reward = game.RaidReward;
        var raid = Raid(q);
        var raiders = game.Raiders(raid).ToList();
        var feat = Feats.All(TestData.Game).First(f => f.Id == "raid");
        Assert.Equal("Defender of Bree", feat.Name);
        Assert.False(feat.Earned(game));

        Assert.True(game.DamageMonster(raiders[0], 1_000_000));
        Assert.Null(game.Raid);
        Assert.All(raiders, m => Assert.True(m.IsRemoved)); // (the rest fled over the wall)
        Assert.Equal(1, game.AvaQuests.RaidsRepelled);
        Assert.Equal(renown + 1, game.TownRenown);
        Assert.Equal(gold + reward, game.Player.Gold);
        Assert.Contains(q.Said, s => s.Contains("break and flee", StringComparison.Ordinal));
        Assert.Contains(game.History, h => h.Text == $"Drove raiders led by {(("aeiou".Contains(raid.Leader[0])) ? "an" : "a")} {raid.Leader} out of the town");
        Assert.True(feat.Earned(game));
        var bulwark = Feats.All(TestData.Game).First(f => f.Id == "raid_5");
        Assert.Equal("Bulwark", bulwark.Name);
        Assert.False(bulwark.Earned(game));
        game.AvaQuests.RaidsRepelled = 5;
        Assert.True(bulwark.Earned(game));
    }

    [Fact]
    public void Killing_most_of_the_band_drives_it_off_too()
    {
        var q = InGrownTown(2, level: 30);
        var game = q.Game;
        var raid = Raid(q);
        var followers = game.Raiders(raid).Skip(1).ToList();
        var needed = raid.RaiderIds.Count / 2 + 1;
        for (var i = 0; i < needed; i++)
        {
            Assert.NotNull(game.Raid);
            game.DamageMonster(followers[i], 1_000_000);
        }
        Assert.Null(game.Raid);
        Assert.Equal(1, game.AvaQuests.RaidsRepelled);
    }

    [Fact]
    public void Raiders_left_alone_march_on_the_square_and_sack_it()
    {
        var q = InGrownTown(4);
        var game = q.Game;
        var raid = Raid(q);
        var middle = new Loc(game.Level.Width / 2, game.Level.Height / 2);
        // You, far off in a corner of town.
        game.Player.Position = game.Level.AllLocs().Where(p => game.Level.IsEmptyFloor(p))
            .OrderByDescending(p => p.DistanceTo(middle)).First();
        var start = game.Raiders(raid).Min(m => m.Position.DistanceTo(middle));
        for (var i = 0; i < 10; i++) game.RaidUpkeep();
        Assert.True(game.Raiders(raid).Min(m => m.Position.DistanceTo(middle)) < start);

        for (var i = 0; i < 400 && game.Raid is not null; i++) game.RaidUpkeep();
        Assert.Null(game.Raid);
        Assert.Contains(q.Said, s => s.StartsWith("The raiders have reached the square!", StringComparison.Ordinal));
        Assert.Contains(q.Said, s => s.StartsWith("The raiders have the square", StringComparison.Ordinal));
        Assert.Equal(1, game.AvaQuests.RaidsLost);
        Assert.True(game.TownSetBack);
        Assert.Equal(3, game.TownStage);
        Assert.Equal(4, game.TownStageReached);
        Assert.Contains(q.Said, s => s.Contains("the fountain is broken", StringComparison.Ordinal));
    }

    [Fact]
    public void Standing_in_the_square_you_hold_it()
    {
        var q = InGrownTown(2);
        var game = q.Game;
        var raid = Raid(q);
        var middle = new Loc(game.Level.Width / 2, game.Level.Height / 2);
        game.Player.Position = game.Level.AllLocs().Where(p => game.Level.IsEmptyFloor(p) && p.DistanceTo(middle) <= 5).First();
        for (var i = 0; i < 400; i++) game.RaidUpkeep();
        Assert.Same(raid, game.Raid);
        Assert.Equal(0, raid.Held);
    }

    [Fact]
    public void Leaving_town_during_a_raid_sets_the_town_back_until_your_next_renown_and_never_below_the_first_stage()
    {
        var q = InGrownTown(5);
        var game = q.Game;
        var home = game.Stores["home"].Stock.Count;
        Raid(q);
        q.Jump(3);
        Assert.Null(game.Raid);
        Assert.Equal(1, game.AvaQuests.RaidsLost);
        Assert.Contains(q.Said, s => s.StartsWith("Behind you, the raiders sack the town", StringComparison.Ordinal));
        Assert.Contains(q.Said, s => s.Contains("your statue is pulled down", StringComparison.Ordinal));
        Assert.Contains(game.History, h => h.Text.EndsWith("sacked the town", StringComparison.Ordinal));
        Assert.Equal(game.TownRenown + 1, game.AvaQuests.SetbackUntil);
        Assert.Equal(4, game.TownStage);
        Assert.Equal(5, game.TownStageReached);

        q.Said.Clear();
        game.AvaQuests.LastReturnRaided = true;
        q.Jump(0);
        Assert.Equal(0, CountFeature(game.Level, "town_statue"));
        Assert.Equal(1, CountFeature(game.Level, "town_fountain"));
        Assert.Contains("The town is still mending from the raid.", q.Said);
        Assert.Equal(home, game.Stores["home"].Stock.Count); // (nothing of yours is touched)

        // The next renown mends it.
        game.AvaQuests.JobsDone++;
        q.Said.Clear();
        q.Jump(3);
        game.AvaQuests.LastReturnRaided = true;
        q.Jump(0);
        Assert.False(game.TownSetBack);
        Assert.Equal(5, game.TownStage);
        Assert.Equal(1, CountFeature(game.Level, "town_statue"));
        Assert.Contains(q.Said, s => s.StartsWith("The town has mended from the raid", StringComparison.Ordinal));

        // A town at the second stage falls back to the first, and no further.
        var small = InGrownTown(2);
        Raid(small);
        small.Jump(3);
        Assert.Equal(1, small.Game.TownStage);
        Return(small);
        Raid(small);
        small.Jump(4);
        Assert.Equal(1, small.Game.TownStage);
        Assert.Equal(2, small.Game.AvaQuests.RaidsLost);
    }

    [Fact]
    public void A_raid_under_way_is_kept_in_the_save()
    {
        var q = InGrownTown(3);
        var game = q.Game;
        var raid = Raid(q);
        var races = game.Raiders(raid).Select(m => m.Race.Id).ToList();
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        var kept = Assert.IsType<TownRaid>(loaded.Raid);
        Assert.Equal(raid.RaiderIds, kept.RaiderIds);
        Assert.Equal(raid.Leader, kept.Leader);
        Assert.Equal(races, loaded.Raiders(kept).Select(m => m.Race.Id));
        Assert.True(loaded.DamageMonster(loaded.Raiders(kept).First(), 1_000_000));
        Assert.Null(loaded.Raid);
        Assert.Equal(1, loaded.AvaQuests.RaidsRepelled);

        // An older save has none of it.
        var old = System.Text.Json.JsonSerializer.Deserialize<AvaQuestLog>("""{"JobsDone": 12, "TownStage": 4}""")!;
        Assert.Null(old.Raid);
        Assert.Equal(0, old.SetbackUntil);
        Assert.Equal(0, old.RaidsRepelled + old.RaidsLost);
    }

    [Fact]
    public void Shops_bar_their_doors_while_raiders_are_at_them_but_the_home_never()
    {
        var q = InGrownTown(2);
        var game = q.Game;
        var raid = Raid(q);
        var entered = new List<ShopEnteredEvent>();
        game.Events.Subscribe<ShopEnteredEvent>(entered.Add);
        var leader = game.Raiders(raid).First();
        foreach (var shop in new[] { "armoury", "home" })
        {
            var door = game.Level.AllLocs().First(l => game.Level.FeatureAt(l).Shop == shop);
            var near = game.Level.AllLocs().First(l => l.ChebyshevTo(door) == 1 && game.Level.IsEmptyFloor(l));
            game.Level.Monsters.Move(leader, near);
            game.Player.Position = door;
            game.Execute(new EnterStoreCommand());
        }
        Assert.Single(entered);
        Assert.Contains(q.Said, s => s.StartsWith("The door is barred.", StringComparison.Ordinal));
    }
}
