using System.Security.Cryptography;
using System.Text;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Generation;
using Angband.Core.Geometry;
using Angband.Core.Persistence;
using Angband.Core.Quests;
using Angband.Core.World;

namespace Angband.Tests;

/// <summary>The town grows with your renown (AVABand's own).</summary>
public partial class AvaQuestTests
{
    private static readonly string[] TownDecorations = ["town_lamp", "town_well", "market_stall", "town_fountain", "town_statue"];

    private static Level Town(ulong seed, int stage) =>
        new DungeonGenerator(TestData.Game).Generate(new LevelRequest(0, seed, TownStage: stage)).Level;

    private static int CountFeature(Level level, string id) => level.AllLocs().Count(p => level.FeatureAt(p).Id == id);

    /// <summary>The town's squares, by terrain id, as one string to compare.</summary>
    private static string TownMap(Level level) =>
        string.Join("\n", Enumerable.Range(0, level.Height).Select(y => string.Join(",", Enumerable.Range(0, level.Width).Select(x => level.FeatureAt(new Loc(x, y)).Id))));

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    [Fact]
    public void Renown_counts_quests_finished_and_jobs_done_and_the_stages_come_at_1_3_6_10_and_15()
    {
        var game = Start().Game;
        Assert.Equal(0, game.TownRenown);
        game.AvaQuests.JobsDone = 2;
        game.AvaQuests.Quests["cellar"] = new AvaQuestState { Id = "cellar", Stage = "done" };
        game.AvaQuests.Quests["letter"] = new AvaQuestState { Id = "letter", Stage = "burned" };
        game.AvaQuests.Quests["apprentice"] = new AvaQuestState { Id = "apprentice", Stage = "lost" }; // (failed: no renown)
        game.AvaQuests.Quests["thief"] = new AvaQuestState { Id = "thief", Stage = "hunt" };          // (under way)
        Assert.Equal(4, game.TownRenown);

        int[] expected = [0, 1, 1, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 4, 5, 5];
        for (var renown = 0; renown < expected.Length; renown++) Assert.Equal(expected[renown], GameSession.TownStageFor(renown));
        Assert.Equal(5, GameSession.TownStageFor(100));
    }

    [Fact]
    public void Coming_back_up_the_town_reaches_its_stage_and_says_so_once()
    {
        var q = Start(level: 10);
        var game = q.Game;
        game.AvaQuests.JobsDone = 3;
        Assert.Equal(0, game.TownStage); // (earned, but not yet seen)
        q.Jump(3);
        q.Jump(0);
        Assert.Equal(2, game.TownStage);
        Assert.Single(q.Said, s => s.StartsWith("The town has grown with your renown.", StringComparison.Ordinal));
        Assert.Contains(q.Said, s => s.Contains("Lamps now light the square") && s.Contains("a well is dug"));
        Assert.Equal(4, CountFeature(game.Level, "town_lamp"));
        Assert.Equal(1, CountFeature(game.Level, "town_well"));
        Assert.Equal(0, CountFeature(game.Level, "passable_rubble"));

        q.Said.Clear();
        q.Jump(3);
        q.Jump(0);
        Assert.DoesNotContain(q.Said, s => s.StartsWith("The town has grown", StringComparison.Ordinal));
        Assert.Contains(q.Said, s => s.Contains("town", StringComparison.Ordinal)); // (a word on how it's doing)

        // Butterbur notices.
        q.EnterShop("inn");
        Assert.Contains("lamps in the square", q.Last.Text);
    }

    [Fact]
    public void The_towns_stage_is_kept_in_the_save_and_older_saves_start_at_the_first()
    {
        var q = Start(level: 10);
        var game = q.Game;
        game.AvaQuests.JobsDone = 10;
        q.Jump(3);
        q.Jump(0);
        Assert.Equal(4, game.TownStage);

        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal(4, loaded.TownStage);
        Assert.Equal(1, CountFeature(loaded.Level, "town_fountain"));

        // A save from before the town could grow has no stage: the town as it was, until the next return.
        Assert.Equal(0, System.Text.Json.JsonSerializer.Deserialize<AvaQuestLog>("""{"JobsDone": 12}""")!.TownStage);
    }

    [Fact]
    public void Stage_0_is_the_town_as_it_always_was()
    {
        // The town maps of these seeds as the generator made them before the town could grow.
        foreach (var (seed, hash) in new[] { (1UL, Stage0Towns[0]), (2UL, Stage0Towns[1]), (77UL, Stage0Towns[2]) })
        {
            var town = Town(seed, 0);
            Assert.Equal(hash, Hash(TownMap(town)));
            Assert.Equal(TownMap(new DungeonGenerator(TestData.Game).Generate(new LevelRequest(0, seed)).Level), TownMap(town));
            Assert.All(TownDecorations, id => Assert.Equal(0, CountFeature(town, id)));
        }
    }

    private static readonly string[] Stage0Towns = ["457DC5B7CF0392B9", "7F304FD97D95141C", "C37D0E0454696CB6"];

    [Fact]
    public void Each_stage_adds_to_the_last_and_keeps_every_street_walkable()
    {
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var before = Town(seed, 0);
            var previous = before;
            for (var stage = 1; stage <= 5; stage++)
            {
                var town = Town(seed, stage);
                foreach (var p in town.AllLocs())
                {
                    var was = previous.FeatureAt(p).Id;
                    var now = town.FeatureAt(p).Id;
                    // Only open street changes: to a decoration (or rubble cleared to street).
                    if (was != now) Assert.True(was == "floor" && TownDecorations.Contains(now) || was == "passable_rubble" && now == "floor",
                        $"seed {seed} stage {stage}: {p} {was} -> {now}");
                }
                // The buildings, doors and stairs stand where they always did, and every one can be reached.
                foreach (var p in before.AllLocs().Where(p => before.FeatureAt(p).Id is not ("floor" or "passable_rubble")))
                    Assert.Equal(before.FeatureAt(p).Id, town.FeatureAt(p).Id);
                var start = town.AllLocs().First(p => town.FeatureAt(p).Has(TerrainFlags.DownStair));
                Assert.Empty(LevelValidator.Validate(town, start, TestData.Game.Constants));
                previous = town;
            }
            Assert.True(CountFeature(previous, "town_lamp") >= 4, $"seed {seed}: lamps");
            Assert.Equal(1, CountFeature(previous, "town_statue"));
            Assert.Equal(1, CountFeature(previous, "town_fountain"));
            Assert.Equal(3, CountFeature(previous, "market_stall"));
        }
    }

    [Fact]
    public void At_night_the_lamps_light_the_square()
    {
        var q = Start(level: 10);
        var game = q.Game;
        game.Scheduler.SetGameTurn(game.Data.Constants.DayLength / 2 + 10);
        q.Jump(3);
        q.Jump(0);
        Assert.False(game.IsDaytime);
        var glowing = game.Level.AllLocs().Count(p => game.Level[p].Has(SquareFlags.Glow));
        Assert.Equal(game.Level.AllLocs().Count(p => game.Level.FeatureAt(p).Has(TerrainFlags.Shop)), glowing); // (stage 0: the doors alone)

        game.AvaQuests.JobsDone = 1;
        q.Jump(3);
        q.Jump(0);
        Assert.False(game.IsDaytime);
        var lamp = q.FeatureLoc("town_lamp");
        Assert.True(game.Level[lamp + new Loc(2, 0)].Has(SquareFlags.Glow));
        Assert.True(game.Level.AllLocs().Count(p => game.Level[p].Has(SquareFlags.Glow)) > glowing + 30);
    }

    [Fact]
    public void The_shops_stock_more_and_better_as_the_town_grows()
    {
        Assert.Equal(0, GameSession.TownShopSlots(1));
        Assert.Equal(4, GameSession.TownShopSlots(5));
        Assert.Equal(8, GameSession.TownShopMagic(5));

        int Stocked(int stage)
        {
            var game = Start(seed: 9).Game;
            game.AvaQuests.TownStage = stage;
            var armoury = game.Stores["armoury"];
            var fewest = int.MaxValue;
            for (var i = 0; i < 30; i++)
            {
                game.Maintain(armoury);
                var count = armoury.Stock.Count(it => !armoury.IsAlways(it));
                Assert.InRange(count, armoury.Def.MinItems + GameSession.TownShopSlots(stage), armoury.Def.MaxItems + GameSession.TownShopSlots(stage));
                fewest = Math.Min(fewest, count);
            }
            return fewest;
        }
        Assert.True(Stocked(5) > Stocked(0));
    }

    [Fact]
    public void The_daily_dungeon_the_weekly_challenge_the_tutorial_and_older_replays_keep_the_town_as_it_was()
    {
        var q = Start(level: 10);
        var game = q.Game;
        game.Player.DailyDate = "2026-10-01";
        game.AvaQuests.JobsDone = 20;
        q.Jump(3);
        q.Jump(0);
        Assert.False(game.TownGrowthOn);
        Assert.Equal(0, game.TownStage);
        Assert.Equal(0, game.AvaQuests.TownStage);
        Assert.All(TownDecorations, id => Assert.Equal(0, CountFeature(game.Level, id)));

        var tutorial = Start().Game;
        tutorial.IsTutorial = true;
        Assert.False(tutorial.TownGrowthOn);

        // A replay recorded before the town could grow plays back without it.
        var recorded = Start(level: 10).Game;
        var recorder = new ReplayRecorder(recorded);
        var file = recorder.ToFile(recorded);
        file.Version = 2;
        Assert.False(new ReplayPlayer(TestData.Game, file).Game.TownGrowthOn);
        file.Version = ReplayFile.CurrentVersion;
        Assert.True(new ReplayPlayer(TestData.Game, file).Game.TownGrowthOn);
    }

    [Fact]
    public void Walking_into_the_statue_you_see_yourself()
    {
        var q = Start(level: 10);
        var game = q.Game;
        game.AvaQuests.JobsDone = 15;
        q.Jump(3);
        q.Jump(0);
        Assert.Equal(5, game.TownStage);
        q.WalkInto(q.FeatureLoc("town_statue"));
        Assert.Contains(q.Said, s => s.StartsWith($"A statue of you, {game.Player.Name}", StringComparison.Ordinal));
    }
}
