using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Data;

namespace Angband.Tests;

/// <summary>
/// Angband 4.2's separate innate and spell frequencies (monster.txt innate-freq and spell-freq;
/// mon-attack.c monster_can_cast and make_ranged_attack).
/// </summary>
public class MonsterFrequencyTests
{
    [Fact]
    public void Frequencies_AreImportedFromMonsterTxt()
    {
        var data = TestData.Game;
        Assert.Equal((2, 0), (data.Monster("kobold_archer")!.InnateFrequency, data.Monster("kobold_archer")!.SpellFrequency));
        Assert.Equal((3, 10), (data.Monster("scout")!.InnateFrequency, data.Monster("scout")!.SpellFrequency));
        Assert.Equal(12, data.Monster("baby_red_dragon")!.InnateFrequency);
        Assert.Equal(0, data.Monster("baby_red_dragon")!.SpellFrequency);
    }

    [Fact]
    public void EveryCaster_HasTheFrequenciesItsSpellsNeed()
    {
        var data = TestData.Game;
        foreach (var race in data.Monsters.Where(r => r.Spells.Count > 0))
        {
            var spells = race.Spells.Select(data.MonsterSpell).OfType<MonsterSpellDef>().ToList();
            Assert.Equal(spells.Any(s => s.Innate), race.InnateFrequency > 0);
            Assert.Equal(spells.Any(s => !s.Innate), race.SpellFrequency > 0);
        }
    }

    [Fact]
    public void TheLoader_RejectsACaster_WithoutItsFrequency()
    {
        var dir = Directory.CreateTempSubdirectory("avaband-freq-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, DataLoader.MonstersFile), """
                [{ "id": "test_archer", "name": "test archer", "glyph": "p", "color": "White", "depth": 1, "hitPoints": 5,
                   "spellFrequency": 3, "spells": ["ARROW"] }]
                """);
            var ex = Assert.ThrowsAny<Exception>(() => DataLoader.Load(DataLoader.DefaultDataDirectory, dir));
            Assert.Contains("innateFrequency", ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// Counts which kind of ranged attack a race makes over many turns, the monster kept seven squares
    /// away — off its preferred range, where the chance would double (see <see cref="AtItsPreferredRange_AMonsterCastsTwiceAsOften"/>).
    /// </summary>
    private static (int Innate, int Spells, int Turns) Watch(string raceId, bool taunt = false, int turns = 3000, Loc? at = null)
    {
        var game = Arena.Create(7,
            "###############",
            "#,,,,,,,,,,,,,#",
            "#,,@,,,,,,,,,,#",
            "#,,,,,,,,,,,,,#",
            "###############");
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 1_000_000;
        var home = at ?? new Loc(10, 2);
        var monster = Arena.AddMonster(game, raceId, home);
        monster.Hp = monster.MaxHp = 1_000_000;
        int innate = 0, spells = 0;
        game.Events.Subscribe<MonsterSpellEvent>(e =>
        {
            if (game.Data.MonsterSpell(e.SpellId)!.Innate) innate++;
            else spells++;
        });
        for (var i = 0; i < turns; i++)
        {
            if (taunt) game.Player.Timed.Set(game.Data.Timed("taunt")!, 100);
            game.Player.Hp = game.Player.MaxHp;
            if (monster.Position != home) game.Level.Monsters.Move(monster, home); // hold it where it was put
            game.RunMonsterTurn(monster);
        }
        return (innate, spells, turns);
    }

    [Fact]
    public void AnArcher_Shoots_AboutHalfItsTurns()
    {
        var (innate, spells, turns) = Watch("kobold_archer"); // innate-freq 2: 50%
        Assert.Equal(0, spells);
        Assert.InRange(innate, turns * 40 / 100, turns * 60 / 100);
    }

    [Fact]
    public void EachKind_HasItsOwnChance()
    {
        // The scout: spells (haste) 10%, arrows 33% of the remaining turns.
        var (innate, spells, turns) = Watch("scout");
        Assert.InRange(spells, 1, turns * 16 / 100);
        Assert.InRange(innate, turns * 22 / 100, turns * 40 / 100);
    }

    [Fact]
    public void Taunting_HalvesTheChance()
    {
        var (innate, _, turns) = Watch("kobold_archer", taunt: true); // 50% → 25%
        Assert.InRange(innate, turns * 18 / 100, turns * 32 / 100);
    }

    /// <summary>Angband mon-attack.c: a monster exactly at its preferred range casts twice as often.</summary>
    [Fact]
    public void AtItsPreferredRange_AMonsterCastsTwiceAsOften()
    {
        // A kobold archer's preferred range is 1: right beside the player its 50% becomes certain.
        var (innate, _, turns) = Watch("kobold_archer", at: new Loc(4, 2));
        Assert.Equal(turns, innate);
    }
}
