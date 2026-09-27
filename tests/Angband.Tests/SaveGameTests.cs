using System.IO.Compression;
using System.Text;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Persistence;

namespace Angband.Tests;

public class SaveGameTests
{
    private static byte[] SaveBytes(GameSession game)
    {
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        return stream.ToArray();
    }

    private static GameSession RoundTrip(GameSession game)
    {
        using var stream = new MemoryStream(SaveBytes(game));
        return SaveGame.Load(TestData.Game, stream);
    }

    /// <summary>The decompressed save with the timestamp removed: equal fingerprints mean equal game state.</summary>
    private static string Fingerprint(GameSession game)
    {
        using var gzip = new GZipStream(new MemoryStream(SaveBytes(game)), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var json = reader.ReadToEnd();
        return System.Text.RegularExpressions.Regex.Replace(json, "\"SavedAtUtc\":\"[^\"]*\"", "");
    }

    /// <summary>A seeded stream of wandering, stair-taking and fighting, identical for identical games.</summary>
    private static void Play(GameSession game, ulong seed, int commands)
    {
        var rng = new Angband.Core.Randomness.GameRandom(seed);
        for (var i = 0; i < commands && !game.Player.IsDead; i++)
        {
            GameCommand command = rng.RandInt0(20) switch
            {
                0 => new TakeStairsCommand(Down: true),
                1 => new RestCommand(),
                2 => new PickupCommand(),
                _ => new WalkCommand(DirectionExtensions.Compass[rng.RandInt0(8)]),
            };
            game.Execute(command);
        }
    }

    [Fact]
    public void New_game_round_trips_exactly()
    {
        var game = GameSession.NewGame(TestData.Game, 7);
        Assert.Equal(Fingerprint(game), Fingerprint(RoundTrip(game)));
    }

    [Fact]
    public void Game_in_the_dungeon_round_trips_exactly()
    {
        var game = GameSession.NewGame(TestData.Game, 11);
        game.Execute(new DebugJumpCommand(5));
        Play(game, 3, 200);
        Assert.NotEmpty(game.Level.Monsters.All);
        var loaded = RoundTrip(game);
        Assert.Equal(Fingerprint(game), Fingerprint(loaded));
        Assert.Equal(game.Player.Position, loaded.Player.Position);
        Assert.Equal(game.Player.Hp, loaded.Player.Hp);
        Assert.Equal(game.Player.Inventory.Pack.Count, loaded.Player.Inventory.Pack.Count);
        Assert.Equal(game.Level.Monsters.All.Count(), loaded.Level.Monsters.All.Count());
    }

    [Theory]
    [InlineData(1ul)]
    [InlineData(42ul)]
    [InlineData(1234ul)]
    public void Loaded_game_continues_identically(ulong seed)
    {
        var game = GameSession.NewGame(TestData.Game, seed);
        game.Execute(new DebugJumpCommand(3));
        Play(game, seed, 100);
        var loaded = RoundTrip(game);

        var a = new List<string>();
        var b = new List<string>();
        using (game.Events.Subscribe<MessageEvent>(m => a.Add(m.Text)))
        using (loaded.Events.Subscribe<MessageEvent>(m => b.Add(m.Text)))
        {
            Play(game, seed + 1, 300);
            Play(loaded, seed + 1, 300);
        }
        Assert.Equal(Fingerprint(game), Fingerprint(loaded));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Monsters_keep_their_state()
    {
        var game = Arena.Create(5);
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(3, 0));
        orc.Hp = 3;
        orc.Fear = 4;
        var loaded = RoundTrip(game);
        var copy = Assert.Single(loaded.Level.Monsters.All);
        Assert.Equal(orc.Id, copy.Id);
        Assert.Equal(3, copy.Hp);
        Assert.Equal(4, copy.Fear);
        Assert.Same(copy, loaded.Level[copy.Position].Monster is { } id ? loaded.Level.Monsters[id] : null);
    }

    [Fact]
    public void Knowledge_and_equipment_survive()
    {
        var game = GameSession.NewGame(TestData.Game, 9, "mage");
        var loaded = RoundTrip(game);
        Assert.Equal(game.Player.Inventory.Equipped.Select(i => i.Kind.Id), loaded.Player.Inventory.Equipped.Select(i => i.Kind.Id));
        Assert.Equal(game.Player.LearnedSpells, loaded.Player.LearnedSpells);
        foreach (var item in game.Player.Inventory.Pack)
        {
            var copy = loaded.Player.Inventory.Pack.Single(i => i.Serial == item.Serial);
            Assert.Equal(Angband.Core.Items.ItemNaming.Describe(item, game.Knowledge), Angband.Core.Items.ItemNaming.Describe(copy, loaded.Knowledge));
        }
        Assert.Equal(game.Player.Class!.Id, loaded.Player.Class!.Id);
    }

    [Fact]
    public void Summary_describes_the_character()
    {
        var game = GameSession.NewGame(TestData.Game, 4);
        game.Player.Name = "Frodo";
        using var stream = new MemoryStream(SaveBytes(game));
        var summary = SaveGame.ReadSummary(stream);
        Assert.Equal("Frodo", summary.Name);
        Assert.Equal(game.Player.Race!.Name, summary.Race);
        Assert.Equal(game.Player.Level, summary.Level);
        Assert.False(summary.IsDead);
    }

    [Fact]
    public void Corrupt_file_is_rejected()
    {
        using var stream = new MemoryStream([1, 2, 3, 4, 5]);
        Assert.Throws<SaveGameException>(() => SaveGame.Load(TestData.Game, stream));
    }

    [Fact]
    public void Newer_version_is_rejected()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"Version\":99}");
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true)) gzip.Write(bytes);
        stream.Position = 0;
        var ex = Assert.Throws<SaveGameException>(() => SaveGame.Load(TestData.Game, stream));
        Assert.Contains("newer", ex.Message);
    }

    [Fact]
    public void Unknown_data_is_reported()
    {
        var game = Arena.Create(5);
        Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(2, 0));
        var json = Encoding.UTF8.GetString(Decompress(SaveBytes(game))).Replace("\"cave_orc\"", "\"no_such_beast\"");
        using var stream = new MemoryStream(Compress(Encoding.UTF8.GetBytes(json)));
        var ex = Assert.Throws<SaveGameException>(() => SaveGame.Load(TestData.Game, stream));
        Assert.Contains("no_such_beast", ex.Message);
    }

    [Fact]
    public void File_save_is_atomic_and_loadable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "avaband-test-" + Guid.NewGuid());
        try
        {
            var path = Path.Combine(dir, "hero" + SaveGame.Extension);
            var game = GameSession.NewGame(TestData.Game, 8);
            SaveGame.SaveToFile(game, path);
            SaveGame.SaveToFile(game, path);
            Assert.False(File.Exists(path + ".tmp"));
            Assert.Equal(Fingerprint(game), Fingerprint(SaveGame.LoadFromFile(TestData.Game, path)));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    private static byte[] Decompress(byte[] data)
    {
        using var gzip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true)) gzip.Write(data);
        return output.ToArray();
    }
}
