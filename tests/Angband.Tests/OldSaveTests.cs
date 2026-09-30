using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>
/// Saves written by earlier versions of AVABand (Saves/, named by the commit that wrote them) must
/// keep loading: a player's character should never be lost to an update. Each was made the same
/// way — a seed-7 mage, jumped to 3 (150 ft) and walked about for up to 60 turns (stopping at half hit points), then saved.
/// To add one: build an old commit, run such a script with it, and drop the file in Saves/.
/// </summary>
public class OldSaveTests
{
    public static TheoryData<string> Saves()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Saves"), "*" + SaveGame.Extension).Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(path));
        return data;
    }

    [Fact]
    public void ThereAreOldSavesToTest() => Assert.True(Saves().Count >= 10);

    [Theory]
    [MemberData(nameof(Saves))]
    public void AnOldSave_Loads_Plays_AndSavesAgain(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Saves", name);
        using (var stream = File.OpenRead(path))
        {
            var summary = SaveGame.ReadSummary(stream);
            Assert.Equal(3, summary.Depth);
        }

        var game = SaveGame.LoadFromFile(TestData.Game, path);
        Assert.Equal(3, game.Player.Depth);
        Assert.Equal(1, game.Player.Level);
        Assert.Equal("mage", game.Player.Class?.Id);
        Assert.False(game.Player.IsDead);
        Assert.True(game.GameTurn > 0);
        Assert.NotEmpty(game.Player.Inventory.All);
        Assert.True(game.Level.InBounds(game.Player.Position));
        Assert.All(game.Hotbar, Assert.Null); // older than the hotbar, or saved with it empty

        // It plays on: time passes, monsters move, and it goes down a level.
        var turn = game.GameTurn;
        game.Player.Hp = game.Player.MaxHp; // (the saves were made at up to half hit points)
        TestGames.ClearMonsters(game);      // (a 10 hp mage: nothing here should decide whether it lives)
        for (var i = 0; i < 30 && game.Player.Hp > game.Player.MaxHp / 2; i++) game.Execute(new HoldCommand()); // (a 10 hp mage)
        Assert.True(game.GameTurn > turn);
        game.Execute(new DebugJumpCommand(4));
        Assert.Equal(4, game.Player.Depth);

        // A few hundred turns more of walking, resting and holding, with a cure now and then (debug).
        var dirs = new[] { Direction.East, Direction.South, Direction.West, Direction.North, Direction.NorthEast, Direction.SouthWest };
        for (var i = 0; i < 300 && !game.IsGameOver; i++)
        {
            if (game.Player.Hp < game.Player.MaxHp / 2) game.Execute(new DebugCureAllCommand());
            game.Execute(i % 7 == 0 ? new HoldCommand() : new WalkCommand(dirs[i / 5 % dirs.Length]));
        }

        // What later versions added reads as nothing from older saves, and the sheet still builds.
        var sheet = Angband.Core.Records.CharacterDump.Build(game);
        if (game.Player.Age == 0) Assert.DoesNotContain(" Age ", sheet);
        else Assert.StartsWith("You are", game.Player.Background);
        Assert.Contains("[Abilities]", sheet);

        // And saves in today's format, loading back the same.
        using var again = new MemoryStream();
        SaveGame.Save(game, again);
        again.Position = 0;
        var reloaded = SaveGame.Load(TestData.Game, again);
        Assert.Equal(game.GameTurn, reloaded.GameTurn);
        Assert.Equal(game.Player.Position, reloaded.Player.Position);
        Assert.Equal(game.History, reloaded.History);
    }
}
