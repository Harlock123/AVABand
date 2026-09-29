using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>Replays: a recorded game plays back to exactly the same end.</summary>
public class ReplayTests
{
    /// <summary>Plays a busy, varied game: walking, fighting, firing, resting, stairs, targets, options.</summary>
    private static void Play(GameSession game, int steps, int seed)
    {
        var rng = new Random(seed); // (the script's choices, not the game's)
        var dirs = new[] { Direction.North, Direction.South, Direction.East, Direction.West, Direction.NorthEast, Direction.SouthWest };
        game.MarkDebugUsed();
        game.Execute(new DebugJumpCommand(2));
        for (var i = 0; i < steps && !game.Player.IsDead; i++)
        {
            switch (rng.Next(12))
            {
                case 0: game.TargetClosest(quiet: true); game.Execute(new FireCommand()); break;
                case 1: game.Execute(new HoldCommand()); break;
                case 2 when game.Level.Objects.Any(game.Player.Position): game.Execute(new PickupCommand()); break;
                case 3: game.SetOption(OptionIds.UseOldTarget, rng.Next(2) == 0); break;
                case 4 when i % 50 == 0: game.Execute(new DebugJumpCommand(game.Player.Depth + 1)); break;
                case 5: game.Execute(new CountedCommand(new WalkCommand(dirs[rng.Next(dirs.Length)]), 3)); break;
                case 6: game.SetTarget(game.Player.Position + new Loc(rng.Next(-3, 4), rng.Next(-3, 4))); break;
                default: game.Execute(new WalkCommand(dirs[rng.Next(dirs.Length)])); break;
            }
            if (game.Player.Hp < game.Player.MaxHp / 3 && game.Player.Inventory.Pack.FirstOrDefault(p => p.Kind.Id == "cure_light_wounds") is { } potion)
                game.Execute(new UseCommand(potion));
            else if (game.Player.Hp < game.Player.MaxHp / 3) game.Execute(new DebugCureAllCommand()); // (recorded too)
        }
    }

    [Fact]
    public void ARecordedGame_PlaysBack_ToExactlyTheSameEnd()
    {
        var game = GameSession.NewGame(TestData.Game, 77, "warrior");
        game.Recorder = new ReplayRecorder(game);
        Play(game, 600, seed: 11);
        var file = game.Recorder.ToFile(game);
        Assert.True(file.Steps.Count > 150, $"{file.Steps.Count} steps"); // (the warrior may die first: a real game)

        var path = Path.Combine(Path.GetTempPath(), "avaband-replay-" + Guid.NewGuid() + ReplayFile.Extension);
        try
        {
            file.Write(path);
            var player = new ReplayPlayer(TestData.Game, ReplayFile.Read(path));
            Assert.True(player.Game.IsReplay);
            Assert.Null(player.Matches);
            while (player.Step()) { }
            Assert.True(player.Matches);
            Assert.Equal(game.GameTurn, player.Game.GameTurn);
            Assert.Equal(game.Player.Position, player.Game.Player.Position);
            Assert.Equal(game.Player.Hp, player.Game.Player.Hp);
            Assert.Equal(game.History.Select(h => h.Text), player.Game.History.Select(h => h.Text));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RecordingFromALoadedSave_StartsThere()
    {
        var game = GameSession.NewGame(TestData.Game, 3, "mage");
        Play(game, 100, seed: 1);
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        loaded.Recorder = new ReplayRecorder(loaded);
        Play(loaded, 200, seed: 2);
        var player = new ReplayPlayer(TestData.Game, loaded.Recorder.ToFile(loaded));
        while (player.Step()) { }
        Assert.True(player.Matches);
    }

    [Fact]
    public void ChoicesInsideACommand_AreNotRecorded_ButThoseFromOutsideAre()
    {
        var game = GameSession.NewGame(TestData.Game, 9, "warrior");
        game.Recorder = new ReplayRecorder(game);
        game.SetTarget(new Loc(3, 3));
        game.ApplyInterfaceOptions(new Dictionary<string, bool> { [OptionIds.UseOldTarget] = true }); // one step, not one per option
        game.Execute(new HoldCommand());
        var ops = game.Recorder.ToFile(game).Steps.Select(s => s!["op"]!.GetValue<string>()).ToList();
        Assert.Equal(["target-loc", "interface-options", "cmd"], ops);
    }
}
