using System.IO.Compression;
using System.Text.Json.Nodes;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Monsters;
using Angband.Core.Persistence;
using Angband.Core.Quests;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>The notice board's bounties on uniques (AVABand's own).</summary>
public partial class AvaQuestTests
{
    /// <summary>To the inn and the board, where the poster goes up if there isn't one.</summary>
    private static UniqueBounty ReadBoard(Quester q)
    {
        q.EnterShop("inn");
        q.Choose("inn:board");
        return Assert.IsType<UniqueBounty>(q.Game.Bounty);
    }

    private static void Slay(Quester q, MonsterRaceDef race)
    {
        var game = q.Game;
        q.Jump(Math.Max(1, race.Depth));
        var spot = game.Level.AllLocs().First(l => game.Level.IsEmptyFloor(l) && l.DistanceTo(game.Player.Position) > 3);
        Assert.True(game.DamageMonster(new MonsterSpawner(game.Data).Place(game.Level, game.Rng, race, spot)!, 1_000_000));
    }

    [Fact]
    public void A_bounty_is_on_a_living_unique_from_your_deepest_to_five_levels_past_it()
    {
        var game = Start(level: 20).Game;
        game.Player.MaxDepth = 10;
        var candidates = game.BountyCandidates();
        Assert.NotEmpty(candidates);
        Assert.All(candidates, r =>
        {
            Assert.True(r.IsUnique);
            Assert.False(r.Has(MonsterFlags.Questor));
            Assert.InRange(r.Depth, 10, 15);
        });
        // One already slain is never posted.
        game.KilledUniques.Add(candidates[0].Id);
        Assert.DoesNotContain(candidates[0], game.BountyCandidates());
        // Nor Sauron or Morgoth (the game's own quests), however deep you've been.
        game.Player.MaxDepth = 97;
        Assert.Empty(game.BountyCandidates().Where(r => r.Id.Contains("sauron", StringComparison.Ordinal) || r.Id.StartsWith("morgoth", StringComparison.Ordinal) || r.Has(MonsterFlags.Questor)).Select(r => r.Id));
        Assert.DoesNotContain(game.BountyCandidates(), r => game.Data.Quests.Any(quest => quest.Race == r.Id));
    }

    [Fact]
    public void The_board_shows_the_poster_with_its_hint_and_pay_and_a_picture()
    {
        var q = Start(level: 15);
        q.Game.Player.MaxDepth = 8;
        var bounty = ReadBoard(q);
        var race = q.Game.Data.Monster(bounty.Race)!;
        Assert.InRange(race.Depth, 8, 13);
        Assert.InRange(race.Depth, bounty.HintFrom, bounty.HintFrom + 3);
        Assert.Equal(GameSession.BountyReward(race), bounty.Reward);
        Assert.Contains($"WANTED — {race.Name}, said to haunt the levels around {bounty.HintFrom * 50}–{(bounty.HintFrom + 3) * 50} ft", q.Last.Text);
        var take = Assert.Single(q.Last.Choices, c => c.Id == "board:bounty:take");
        Assert.Equal($"monster:{race.Id}", take.Picture);
        Assert.StartsWith($"Take: Bounty: {race.Name} ({bounty.Reward} gold and a trophy)", take.Label);
        // The board's other monster postings have their pictures too.
        Assert.All(q.Last.Choices.Where(c => c.Label.StartsWith("Take: Hunt", StringComparison.Ordinal)), c => Assert.StartsWith("monster:", c.Picture));
    }

    [Fact]
    public void A_bounty_taken_and_its_unique_slain_pays_gold_and_a_trophy_at_the_inn()
    {
        var q = Start(level: 25);
        var game = q.Game;
        game.Player.MaxDepth = 5;
        var bounty = ReadBoard(q);
        var race = game.Data.Monster(bounty.Race)!;
        q.Choose("board:bounty:take");
        Assert.True(bounty.Taken);
        Assert.Contains(q.Last.Choices, c => c.Id == "board:bounty:drop");
        var card = Assert.Single(game.QuestLog(), e => e.Name == $"Bounty: {race.Name}");
        Assert.Equal("0 of 1 slain", card.Status);
        Assert.Equal($"{bounty.Reward} gold and a trophy", card.Reward);

        Slay(q, race);
        Assert.True(bounty.Slain);
        Assert.Contains(q.Said, s => s == $"The bounty is yours: Bounty: {race.Name}. Collect at the Prancing Pony.");
        Assert.StartsWith("Ready", game.QuestLog().Single(e => e.Name == $"Bounty: {race.Name}").Status);

        var gold = game.Player.Gold;
        q.EnterShop("inn");
        Assert.Same(bounty, game.Bounty); // (a return to town doesn't lose it)
        q.Choose("board:bounty:collect");
        Assert.Equal(gold + bounty.Reward, game.Player.Gold);
        Assert.Equal([race.Id], game.AvaQuests.BountiesClaimed);
        Assert.Contains(game.History, h => h.Text == $"Claimed the bounty on {race.Name}");
        var trophy = Assert.Single(game.Player.Inventory.Pack, i => GameSession.IsTrophy(i) && i.Note == race.Name);
        var own = GameSession.TrophiesOf(race).Kinds;
        Assert.Equal(own.Count > 0 ? own[0] : GameSession.HuntersTrophy, trophy.Kind.Id);
        // A new poster goes up, on another unique; the claimed one is gone from the log.
        Assert.NotNull(game.Bounty);
        Assert.NotEqual(race.Id, game.Bounty!.Race);
        Assert.DoesNotContain(game.QuestLog(), e => e.Name == $"Bounty: {race.Name}");
    }

    [Fact]
    public void A_unique_with_a_trophy_of_its_own_pays_that_trophy()
    {
        var q = Start(level: 40);
        var game = q.Game;
        var race = game.Data.Monsters.First(r => r.IsUnique && !r.Has(MonsterFlags.Questor) && GameSession.TrophiesOf(r).Kinds.Count > 0);
        game.AvaQuests.Bounty = new UniqueBounty { Race = race.Id, Reward = 500, HintFrom = race.Depth, Taken = true, Slain = true, VisitsLeft = 3 };
        q.EnterShop("inn");
        q.Choose("board:bounty:collect");
        Assert.Contains(game.Player.Inventory.Pack, i => i.Kind.Id == GameSession.TrophiesOf(race).Kinds[0] && i.Note == race.Name);
    }

    [Fact]
    public void A_bounty_lapses_if_its_unique_dies_some_other_way()
    {
        var q = Start(level: 20);
        var game = q.Game;
        game.Player.MaxDepth = 6;
        var bounty = ReadBoard(q);
        q.Choose("board:bounty:take");
        game.KilledUniques.Add(bounty.Race); // (say, at the hands of a monster you commanded)
        q.Jump(1);
        q.Jump(0);
        Assert.Contains(q.Said, s => s.EndsWith("is dead — but not by your hand. The bounty lapses.", StringComparison.Ordinal));
        Assert.NotEqual(bounty.Race, game.Bounty?.Race);
        Assert.Empty(game.AvaQuests.BountiesClaimed);
    }

    [Fact]
    public void A_poster_nobody_takes_comes_down_after_a_few_returns()
    {
        var q = Start(level: 20);
        var game = q.Game;
        game.Player.MaxDepth = 6;
        var bounty = ReadBoard(q);
        for (var i = 0; i < GameSession.BountyVisits - 1; i++)
        {
            q.Jump(1);
            q.Jump(0);
            Assert.Same(bounty, game.Bounty);
        }
        q.Jump(1);
        q.Jump(0);
        Assert.NotSame(bounty, game.Bounty);

        // One taken stays up as long as it takes.
        var taken = ReadBoard(q);
        q.Choose("board:bounty:take");
        for (var i = 0; i < GameSession.BountyVisits + 2; i++)
        {
            q.Jump(1);
            q.Jump(0);
        }
        Assert.Same(taken, game.Bounty);
        // Given up, it's back on the board for anyone.
        q.EnterShop("inn");
        q.Choose("inn:board");
        q.Choose("board:bounty:drop");
        Assert.False(taken.Taken);
    }

    [Fact]
    public void Only_the_poster_has_bounties_now_not_the_boards_other_postings()
    {
        var q = Start(level: 12);
        var game = q.Game;
        for (var i = 0; i < 25; i++)
        {
            q.Jump(8);
            q.EnterShop("inn");
            Assert.DoesNotContain(game.AvaQuests.Board, j => j.Kind == "bounty");
        }
    }

    [Fact]
    public void A_bounty_is_saved_and_an_older_save_without_one_loads()
    {
        var q = Start(level: 20);
        var game = q.Game;
        game.Player.MaxDepth = 6;
        var bounty = ReadBoard(q);
        q.Choose("board:bounty:take");
        game.AvaQuests.BountiesClaimed.Add("grip_farmer_maggot_s_dog");
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal(bounty.Race, loaded.AvaQuests.Bounty!.Race);
        Assert.True(loaded.AvaQuests.Bounty.Taken);
        Assert.Equal(bounty.Reward, loaded.AvaQuests.Bounty.Reward);
        Assert.Equal(bounty.HintFrom, loaded.AvaQuests.Bounty.HintFrom);
        Assert.Equal(["grip_farmer_maggot_s_dog"], loaded.AvaQuests.BountiesClaimed);

        // An older save (from before bounties): neither is there.
        stream.Position = 0;
        JsonNode file;
        using (var unzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true)) file = JsonNode.Parse(unzip)!;
        var log = file["AvaQuests"]!.AsObject();
        log.Remove("Bounty");
        log.Remove("BountiesClaimed");
        var old = new MemoryStream();
        using (var zip = new GZipStream(old, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new System.Text.Json.Utf8JsonWriter(zip)) file.WriteTo(writer);
        old.Position = 0;
        var older = SaveGame.Load(TestData.Game, old);
        Assert.Null(older.AvaQuests.Bounty);
        Assert.Empty(older.AvaQuests.BountiesClaimed);
    }

    [Fact]
    public void No_bounties_in_the_daily_dungeon_the_weekly_challenge_the_tutorial_or_an_older_replay()
    {
        var daily = Start(level: 20);
        daily.Game.Player.DailyDate = "2026-10-26";
        var weekly = Start(level: 20);
        weekly.Game.Player.WeeklyWeek = "2026-W43";
        var tutorial = Start(level: 20);
        tutorial.Game.IsTutorial = true;
        var replayed = Start(level: 20);
        replayed.Game.BeforeBounties = true;
        foreach (var q in new[] { daily, weekly, tutorial, replayed })
        {
            Assert.False(q.Game.BountiesOn);
            q.EnterShop("inn");
            q.Choose("inn:board");
            Assert.Null(q.Game.AvaQuests.Bounty);
            Assert.DoesNotContain(q.Last.Choices, c => c.Id.StartsWith("board:bounty", StringComparison.Ordinal));
            Assert.DoesNotContain("WANTED", q.Last.Text);
        }
    }

    [Fact]
    public void A_replay_from_before_bounties_plays_without_them_and_a_new_one_with()
    {
        var game = GameSession.NewGame(TestData.Game, 3, "warrior");
        game.Recorder = new ReplayRecorder(game);
        var file = game.Recorder.ToFile(game);
        Assert.Equal(ReplayFile.CurrentVersion, file.Version);
        Assert.True(new ReplayPlayer(TestData.Game, file).Game.BountiesOn);
        file.Version = 2;
        Assert.False(new ReplayPlayer(TestData.Game, file).Game.BountiesOn);
    }

    [Fact]
    public void Claiming_a_bounty_earns_its_feats_and_marks_the_museum()
    {
        var game = GameSession.NewGame(TestData.Game, 3, "warrior"); // (no debug: the museum and feats count it)
        var race = game.Data.Monsters.First(r => r.IsUnique && !r.Has(MonsterFlags.Questor) && r.Depth > 0);
        var other = game.Data.Monsters.First(r => r.IsUnique && !r.Has(MonsterFlags.Questor) && r.Depth > 0 && r != race);
        game.KilledUniques.Add(race.Id);
        game.KilledUniques.Add(other.Id);
        var museum = new Museum();
        museum.Check(game);
        Assert.False(museum.Uniques[race.Id].Bounty);

        game.AvaQuests.BountiesClaimed.Add(race.Id);
        museum.Check(game);
        Assert.True(museum.Uniques[race.Id].Bounty);
        Assert.False(museum.Uniques[other.Id].Bounty);
        Assert.Equal(1, museum.CountBounties());

        var feats = Feats.All(TestData.Game);
        bool Earned(string id) => feats.Single(f => f.Id == id).Earned(game);
        Assert.True(Earned("bounty"));
        Assert.False(Earned("bounty_5"));
        for (var i = 0; i < 4; i++) game.AvaQuests.BountiesClaimed.Add($"other_{i}");
        Assert.True(Earned("bounty_5"));
    }
}
