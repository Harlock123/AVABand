using Angband.Core.Game;
using Angband.Core.Monsters;
using Angband.Core.Persistence;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>The museum (AVABand's own): artifacts, uniques and monsters across every character.</summary>
public class MuseumTests
{
    private static GameSession NewGame(string name = "Bob", ulong seed = 6) =>
        GameSession.NewGame(TestData.Game, seed, CharacterSpec.Default("elf", "mage", name));

    private static string FirstUnique => Museum.UniqueRaces(TestData.Game).OrderBy(r => r.Depth).First().Id;

    [Fact]
    public void It_records_artifacts_found_uniques_slain_and_monsters_seen_with_who_did_it_first()
    {
        var game = NewGame();
        var museum = new Museum();
        var art = TestData.Game.Artifacts.First();
        game.Knowledge.See(game.Objects.CreateArtifact(art));
        game.KilledUniques.Add(FirstUnique);
        game.Lore = new MonsterLoreBook();
        game.Lore.For(TestData.Game.Monsters[0].Id).Sights = 1;
        game.Player.Depth = 7;

        var when = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        var news = museum.Check(game, when);

        Assert.Equal([art.Name, TestData.Game.Monster(FirstUnique)!.Name], news); // (monsters seen aren't announced)
        Assert.Equal("Bob the Elf Mage", museum.Artifacts[art.Id].Character);
        Assert.Equal(7, museum.Artifacts[art.Id].Depth);
        Assert.Equal(when, museum.Uniques[FirstUnique].WhenUtc);
        Assert.Contains(TestData.Game.Monsters[0].Id, museum.Monsters.Keys);

        // The first stays the first.
        var later = NewGame("Alice");
        later.Knowledge.See(later.Objects.CreateArtifact(art));
        Assert.Empty(museum.Check(later));
        Assert.Equal("Bob the Elf Mage", museum.Artifacts[art.Id].Character);
    }

    [Fact]
    public void Cheats_and_random_artifacts_add_nothing_and_ghosts_are_not_a_kind_of_monster()
    {
        var museum = new Museum();
        var cheat = NewGame();
        cheat.MarkDebugUsed();
        cheat.KilledUniques.Add(FirstUnique);
        Assert.Empty(museum.Check(cheat));
        Assert.Empty(museum.Uniques);

        var game = NewGame();
        game.Lore = new MonsterLoreBook();
        game.Lore.For(FallenGhosts.RacePrefix + "abc").Sights = 3;
        museum.Check(game);
        Assert.Empty(museum.Monsters);

        var randarts = GameSession.NewGame(TestData.Game, 6, CharacterSpec.Default("elf", "mage") with
        {
            Options = new Dictionary<string, bool> { [OptionIds.Randarts] = true },
        });
        var art = randarts.Artifacts.First();
        randarts.Knowledge.See(randarts.Objects.CreateArtifact(art));
        museum.Check(randarts);
        Assert.Empty(museum.Artifacts);
    }

    [Fact]
    public void The_first_time_it_fills_in_from_the_monster_memory_and_the_headstones()
    {
        var lore = new MonsterLoreBook();
        lore.For(TestData.Game.Monsters[0].Id).Sights = 2;
        lore.For(FirstUnique).TotalKills = 1;
        var art = TestData.Game.Artifacts.First();
        var item = NewGame().Objects.CreateArtifact(art);
        var yard = new Graveyard();
        yard.Fallen.Add(new FallenRecord
        {
            Name = "Old", Race = "Dwarf", Class = "Priest", MaxDepth = 12, DateUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Equipment = [SaveGame.ItemToJson(item)],
        });

        var museum = new Museum();
        museum.Backfill(TestData.Game, lore, yard);

        Assert.True(museum.Backfilled);
        Assert.Equal("", museum.Monsters[TestData.Game.Monsters[0].Id].Character);
        Assert.Contains(FirstUnique, museum.Uniques.Keys);
        Assert.Contains(FirstUnique, museum.Monsters.Keys);
        Assert.Equal("Old the Dwarf Priest", museum.Artifacts[art.Id].Character);
    }

    [Fact]
    public void It_saves_and_loads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"museum-{Guid.NewGuid():N}.json");
        try
        {
            var museum = new Museum { Backfilled = true };
            museum.Uniques["grip"] = new MuseumEntry { Character = "Bob", Depth = 3 };
            museum.Save(path);
            var loaded = Museum.Load(path);
            Assert.True(loaded.Backfilled);
            Assert.Equal("Bob", loaded.Uniques["grip"].Character);
            File.WriteAllText(path, "{ not json");
            Assert.Empty(Museum.Load(path).Uniques);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
