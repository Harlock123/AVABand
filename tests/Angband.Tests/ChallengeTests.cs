using Angband.Core.Game;
using Angband.Core.Persistence;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>AVABand's difficulty presets and challenge runs (Game/Challenges.cs).</summary>
public class ChallengeTests
{
    private static GameSession NewGame(params string[] options) =>
        GameSession.NewGame(TestData.Game, 6, CharacterSpec.Default("human", "warrior") with { Options = options.ToDictionary(o => o, _ => true) });

    private static List<string> Messages(GameSession game)
    {
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        return said;
    }

    // --- Presets ---------------------------------------------------------------------------------

    [Fact]
    public void Classic_is_the_games_defaults_and_the_presets_go_from_easy_to_hard()
    {
        var defaults = new GameOptions();
        Assert.Equal("classic", Challenges.PresetFor(id => defaults[id])?.Id);
        Assert.Equal(["relaxed", "classic", "hard"], Challenges.Presets.Select(p => p.Id));
        Assert.All(Challenges.Presets.SelectMany(p => p.Options.Keys), id => Assert.Equal(OptionKind.Birth, OptionCatalog.Find(id)!.Kind));

        var relaxed = Challenges.Presets[0].Options;
        Assert.True(relaxed[OptionIds.KnowRunes] && relaxed[OptionIds.KnowFlavors] && !relaxed[OptionIds.AiLearn]);
        var hard = Challenges.Presets[2].Options;
        Assert.False(hard[OptionIds.ConnectStairs] || hard[OptionIds.AvaEasyCasting] || hard[OptionIds.Hirelings]);

        // Any other mix is custom.
        var custom = new GameOptions { [OptionIds.ConnectStairs] = false };
        Assert.Null(Challenges.PresetFor(id => custom[id]));
    }

    // --- Which challenges ------------------------------------------------------------------------

    [Fact]
    public void Ironman_is_forced_descent_without_recall_and_says_so_once()
    {
        Assert.Equal(["Ironman"], NewGame(OptionIds.ForceDescend, OptionIds.NoRecall).ChallengeNames);
        Assert.Equal(["Forced descent"], NewGame(OptionIds.ForceDescend).ChallengeNames);
        Assert.Equal(["Ironman", "No shopping", "Speed run"],
            NewGame(OptionIds.ForceDescend, OptionIds.NoRecall, OptionIds.AvaNoShopping, OptionIds.AvaSpeedRun).ChallengeNames);
        Assert.Empty(NewGame().ChallengeNames);
        Assert.False(OptionCatalog.Find(OptionIds.AvaNoShopping)!.Default || OptionCatalog.Find(OptionIds.AvaSpeedRun)!.Default);
    }

    [Fact]
    public void Challenges_tag_the_high_scores_and_the_headstone_and_are_saved()
    {
        var game = NewGame(OptionIds.ForceDescend, OptionIds.NoRecall, OptionIds.AvaSpeedRun);
        var entry = ScoreEntry.For(game);
        Assert.Equal(["Ironman", "Speed run"], entry.Challenges);
        Assert.Equal(" [Ironman, Speed run]", Challenges.Tag(FallenRecord.From(entry).Challenges));
        Assert.Null(ScoreEntry.For(NewGame()).Challenges);
        Assert.Equal("", Challenges.Tag(null));

        game.Player.SpeedRunResult = 1234;
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal(1234, loaded.Player.SpeedRunResult);
        Assert.Equal(["Ironman", "Speed run"], loaded.ChallengeNames);
        Assert.Contains("Challenges: Ironman, Speed run", CharacterDump.Build(loaded));
    }

    // --- No shopping -----------------------------------------------------------------------------

    [Fact]
    public void No_shopping_keeps_the_shops_from_selling_but_not_the_home_from_giving_back()
    {
        var game = NewGame(OptionIds.AvaNoShopping);
        var said = Messages(game);
        game.Player.Gold = 100_000;
        game.Player.Position = game.Level.AllLocs().First(p => game.Level.FeatureAt(p).Shop == "general");
        game.Execute(new EnterStoreCommand());
        Assert.Contains(said, s => s.Contains("you can sell here, but not buy"));
        var item = game.StoreHere!.Stock[0];
        var pack = game.Player.Inventory.All.Sum(i => i.Number);
        game.Execute(new BuyCommand(item));
        Assert.Equal(100_000, game.Player.Gold);
        Assert.Equal(pack, game.Player.Inventory.All.Sum(i => i.Number));
        Assert.Contains(said, s => s.Contains("keep only what you find"));
        Assert.False(game.StrongroomOn);
        Assert.True(NewGame().StrongroomOn);
    }

    // --- Speed run -------------------------------------------------------------------------------

    [Fact]
    public void A_speed_run_is_made_by_reaching_1000_ft_in_time()
    {
        var game = NewGame(OptionIds.AvaSpeedRun);
        game.MarkDebugUsed();
        var said = Messages(game);
        Assert.Equal("speed run: 15,000 left", game.SpeedRunStatus);
        game.Execute(new DebugJumpCommand(Challenges.SpeedRunDepth));
        for (var i = 0; i < 3; i++) game.Execute(new HoldCommand());
        Assert.True(game.SpeedRunMade);
        Assert.Contains(said, s => s.StartsWith("Speed run made: 1000 ft in ", StringComparison.Ordinal));
        Assert.Contains(game.History, h => h.Text.StartsWith("Made the speed run", StringComparison.Ordinal));
        Assert.StartsWith("speed run made in ", game.SpeedRunStatus);
        Assert.Null(NewGame().SpeedRunStatus);
    }

    [Fact]
    public void A_speed_run_runs_out_of_time_once_and_reaching_the_depth_after_doesnt_count()
    {
        var game = NewGame(OptionIds.AvaSpeedRun);
        game.MarkDebugUsed();
        var said = Messages(game);
        game.Scheduler.SetGameTurn((Challenges.SpeedRunTurns + 1) * 10);
        for (var i = 0; i < 3; i++) game.Execute(new HoldCommand());
        Assert.Equal(-1, game.Player.SpeedRunResult);
        Assert.Equal("speed run: out of time", game.SpeedRunStatus);
        Assert.Single(said, s => s.StartsWith("Your speed run is over", StringComparison.Ordinal));
        game.Execute(new DebugJumpCommand(Challenges.SpeedRunDepth));
        game.Execute(new HoldCommand());
        Assert.False(game.SpeedRunMade);
    }

    // --- Feats -----------------------------------------------------------------------------------

    [Fact]
    public void Each_challenge_has_feats_earned_only_on_that_challenge()
    {
        var feats = Feats.All(TestData.Game);
        bool Earned(GameSession g, string id) => feats.First(f => f.Id == id).Earned(g);

        var ironman = NewGame(OptionIds.ForceDescend, OptionIds.NoRecall);
        ironman.Player.MaxDepth = 30;
        Assert.True(Earned(ironman, "challenge_ironman"));
        Assert.True(Earned(ironman, "challenge_descent")); // (an Ironman descends by force too)
        Assert.False(Earned(ironman, "challenge_ironman_win"));
        ironman.Player.IsWinner = true;
        Assert.True(Earned(ironman, "challenge_ironman_win"));

        var plain = NewGame();
        plain.Player.MaxDepth = 30;
        Assert.All(feats.Where(f => f.Id.StartsWith("challenge_", StringComparison.Ordinal)), f => Assert.False(f.Earned(plain), f.Id));

        var noShop = NewGame(OptionIds.AvaNoShopping);
        noShop.Player.MaxDepth = 20;
        Assert.True(Earned(noShop, "challenge_noshop"));

        var speed = NewGame(OptionIds.AvaSpeedRun);
        Assert.False(Earned(speed, "challenge_speed"));
        speed.Player.SpeedRunResult = 9000;
        Assert.True(Earned(speed, "challenge_speed"));
    }
}
