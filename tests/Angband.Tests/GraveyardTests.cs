using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>The graveyard: every end kept, with a steady epitaph, and the high scores' dead brought in once.</summary>
public class GraveyardTests
{
    private static ScoreEntry Dead(string name, int depth, string killer, bool won = false, int day = 1) => new()
    {
        Name = name, Race = "Human", Class = "Warrior", Level = 20, MaxLevel = 22, MaxDepth = depth, Depth = depth,
        KilledBy = won ? null : killer, Won = won, Seed = 7, DateUtc = new DateTime(2026, 9, day, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void AnEnd_IsRecorded_WithAnEpitaphToSuitIt()
    {
        var deep = FallenRecord.From(Dead("Bob", 30, "a cave troll"), "/dumps/bob.txt");
        Assert.Equal("a cave troll", deep.KilledBy);
        Assert.Equal(22, deep.Level);
        Assert.Equal("/dumps/bob.txt", deep.DumpPath);
        Assert.False(string.IsNullOrWhiteSpace(deep.Epitaph));
        Assert.DoesNotContain("{", deep.Epitaph);
        Assert.Equal(deep.Epitaph, FallenRecord.From(Dead("Bob", 30, "a cave troll")).Epitaph);   // the same death, the same line

        var won = FallenRecord.From(Dead("Ann", 100, "", won: true));
        Assert.Equal("retired victorious", won.KilledBy);
        Assert.Contains(won.Epitaph, new[] { "Cast down Morgoth, and lived to lay down the sword.", "The Iron Crown lies broken. Rest well.",
            "They went into the dark and brought back the dawn." });
        var town = FallenRecord.From(Dead("Tim", 0, "Grip")).Epitaph;
        Assert.True(town.Contains("town", StringComparison.OrdinalIgnoreCase) || town.Contains("stairs") || town.Contains("Prancing Pony"), town);
    }

    [Fact]
    public void TheHighScoresDead_AreBroughtIn_Once_NewestFirst()
    {
        var yard = new Graveyard();
        var alive = new ScoreEntry { Name = "Living", KilledBy = null, DateUtc = new DateTime(2026, 9, 5) };
        yard.Backfill([Dead("Old", 5, "a jackal", day: 1), Dead("Newer", 12, "Wormtongue", day: 9), alive]);
        Assert.True(yard.Backfilled);
        Assert.Equal(["Newer", "Old"], yard.Fallen.Select(f => f.Name));
        yard.Backfill([Dead("Old", 5, "a jackal", day: 1)]);
        Assert.Equal(2, yard.Fallen.Count);                                  // not twice

        // A death laid in afterwards, with its dump, takes the place of its line from the scores.
        yard.Add(FallenRecord.From(Dead("Old", 5, "a jackal", day: 1), "/dumps/old.txt"));
        Assert.Equal(2, yard.Fallen.Count);
        Assert.Equal("/dumps/old.txt", yard.Fallen.Single(f => f.Name == "Old").DumpPath);
    }

    [Fact]
    public void TheGraveyard_IsSaved_AndReadBack()
    {
        var path = Path.Combine(Path.GetTempPath(), $"avaband-graves-{Guid.NewGuid():N}.json");
        try
        {
            var yard = new Graveyard();
            yard.Add(FallenRecord.From(Dead("Bob", 30, "a cave troll")));
            yard.Backfilled = true;
            yard.Save(path);
            var back = Graveyard.Load(path);
            Assert.True(back.Backfilled);
            Assert.Equal("Bob", Assert.Single(back.Fallen).Name);
            File.WriteAllText(path, "{ broken");
            Assert.Empty(Graveyard.Load(path).Fallen);
        }
        finally { File.Delete(path); }
    }
}
