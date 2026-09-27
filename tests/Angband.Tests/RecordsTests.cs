using Angband.Core.Game;
using Angband.Core.Records;

namespace Angband.Tests;

public class RecordsTests
{
    private static GameSession Mage()
    {
        var spec = CharacterSpec.Default("high_elf", "mage") with { Name = "Galadwen" };
        return GameSession.NewGame(TestData.Game, 12, spec);
    }

    [Fact]
    public void Dump_describes_the_character()
    {
        var game = Mage();
        game.Execute(new StudyCommand());
        var dump = CharacterDump.Build(game, ["You feel something in the air."], new DateTime(2026, 9, 26, 12, 0, 0));
        var path = Environment.GetEnvironmentVariable("AVABAND_DUMP");
        if (path is not null) File.WriteAllText(path, dump);

        Assert.Contains("Name   Galadwen", dump);
        Assert.Contains("Race   High-Elf", dump);
        Assert.Contains("Class  Mage", dump);
        Assert.Contains("Still alive (fed).", dump);
        Assert.Contains("[Stats]", dump);
        Assert.Contains("[Equipment]", dump);
        Assert.Contains("[Inventory]", dump);
        Assert.Contains("[Spells]", dump);
        Assert.Contains("You feel something in the air.", dump);
        foreach (var item in game.Player.Inventory.Pack) Assert.Contains(game.Describe(item), dump);
        Assert.Contains($"{Scoring.Points(game.Player)} points", dump);
    }

    [Fact]
    public void Dump_hides_unknown_runes()
    {
        var game = Arena.Create(3);
        var weapon = game.Player.Inventory.Weapon!;
        weapon.Resists.Add("fire");
        game.RecalculateBonuses();
        var fireRow = CharacterDump.Build(game).Split('\n').First(l => l.TrimStart().StartsWith("Fire"));
        Assert.Contains("?", fireRow);
        Assert.DoesNotContain("Resist", fireRow);

        game.LearnRune(Angband.Core.Definitions.RuneIds.Resist("fire"));
        fireRow = CharacterDump.Build(game).Split('\n').First(l => l.TrimStart().StartsWith("Fire"));
        Assert.Contains("+", fireRow);
        Assert.Contains("Resist", fireRow);
    }

    [Fact]
    public void Dump_reports_the_killer()
    {
        var game = Arena.Create(3);
        game.TakeHit(10_000, "a Cave orc");
        Assert.Contains("Killed by a Cave orc", CharacterDump.Build(game));
    }

    [Fact]
    public void Points_follow_angband()
    {
        var game = Arena.Create(3);
        game.Player.Experience = 1234;
        game.Player.MaxDepth = 7;
        Assert.Equal(1934, Scoring.Points(game.Player));
    }

    private static ScoreEntry Entry(long points, long turns = 100, int day = 1) =>
        new() { Name = $"p{points}-{turns}", Points = points, Turns = turns, DateUtc = new DateTime(2026, 1, day), KilledBy = "x" };

    [Fact]
    public void Board_ranks_best_first_with_ties_by_turns()
    {
        var board = new ScoreBoard();
        Assert.Equal(1, board.Add(Entry(100)));
        Assert.Equal(1, board.Add(Entry(500)));
        Assert.Equal(3, board.Add(Entry(50)));
        Assert.Equal(2, board.Add(Entry(100, turns: 50)));
        Assert.Equal(4, board.Add(Entry(100, turns: 100, day: 2))); // same score and turns: the older one stays ahead
        Assert.Equal(["p500-100", "p100-50", "p100-100", "p100-100", "p50-100"], board.Entries.Select(e => e.Name));
        Assert.Equal(new DateTime(2026, 1, 1), board.Entries[2].DateUtc);
        Assert.Equal(2, board.RankOf(Entry(200)));
        Assert.Equal(5, board.Entries.Count);
    }

    [Fact]
    public void Board_is_capped()
    {
        var board = new ScoreBoard();
        for (var i = 0; i < ScoreBoard.MaxEntries + 10; i++) board.Add(Entry(1000 + i));
        Assert.Equal(ScoreBoard.MaxEntries, board.Entries.Count);
        Assert.Equal(0, board.Add(Entry(1)));
        Assert.Equal(1000 + ScoreBoard.MaxEntries + 9, board.Entries[0].Points);
    }

    [Fact]
    public void Board_round_trips_and_survives_damage()
    {
        var dir = Path.Combine(Path.GetTempPath(), "avaband-scores-" + Guid.NewGuid());
        try
        {
            var path = Path.Combine(dir, "scores.json");
            var board = new ScoreBoard();
            var game = Arena.Create(3);
            game.TakeHit(10_000, "a trap");
            board.Add(ScoreEntry.For(game));
            board.Add(Entry(5));
            board.Save(path);

            var loaded = ScoreBoard.Load(path);
            Assert.Equal(board.Entries.Select(e => (e.Name, e.Points, e.KilledBy)), loaded.Entries.Select(e => (e.Name, e.Points, e.KilledBy)));
            Assert.Equal("a trap", loaded.Entries.First(e => e.Name == game.Player.Name).KilledBy);

            File.WriteAllText(path, "{ nonsense");
            Assert.Empty(ScoreBoard.Load(path).Entries);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
