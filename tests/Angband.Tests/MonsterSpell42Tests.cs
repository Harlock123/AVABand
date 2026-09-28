using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Monsters;

namespace Angband.Tests;

/// <summary>
/// The monster spells AVABand lacked until now (Angband 4.2 monster_spell.txt): WEAVE, STORM, the
/// power-scaled WOUND, and the greater summons — Ainur, greater demons, undead and dragons,
/// Ringwraiths and uniques.
/// </summary>
public class MonsterSpell42Tests
{
    private static readonly string[] Room =
    [
        "#################",
        "#,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,#",
        "#,,,,,,,@,,,,,,,#",
        "#,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,#",
        "#################",
    ];

    /// <summary>The room, at dungeon level <paramref name="depth"/>.</summary>
    private static GameSession Game(int depth = 1)
    {
        var game = Arena.Create(4, Room);
        if (depth > 1)
        {
            var level = TestLevels.FromAscii(depth, out var eye, Room);
            game.UseLevel(level, eye);
        }
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 1_000_000;
        return game;
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    private static Monster Near(GameSession game, string race, int dx = 3, int dy = 0)
    {
        var m = Arena.AddMonster(game, race, game.Player.Position + new Loc(dx, dy));
        m.Held = 1000; // it stays where it is put
        game.UpdateView();
        return m;
    }

    [Fact]
    public void AllOf42sSpells_AreThere_AndCastByTheirMonsters()
    {
        Assert.Equal(91, TestData.Game.MonsterSpells.Count);
        Assert.Contains("WEAVE", TestData.Game.Monster("giant_spider")!.Spells);
        Assert.Contains("S_WRAITH", TestData.Game.Monster("the_witch_king_of_angmar")!.Spells);
        Assert.Contains("S_UNIQUE", TestData.Game.Monster("morgoth_lord_of_darkness")!.Spells);
        Assert.Contains("STORM", TestData.Game.Monster("storm_giant")!.Spells);
        Assert.Equal(10, TestData.Game.Monster("giant_spider")!.InnateFrequency);
        Assert.Equal(130, TestData.Game.Monster("sauron_the_sorcerer")!.Power); // spell-power above its level
    }

    [Fact]
    public void Wound_GrowsWithTheCastersPower_AndSaysSo()
    {
        static (int Damage, List<string> Said, bool Cut) Cast(string race)
        {
            var game = Game();
            game.Player.SkillSave = 0;
            var caster = Near(game, race);
            var said = Messages(game);
            var hp = game.Player.Hp;
            game.CastSpellForTest(caster, "WOUND");
            return (hp - game.Player.Hp, said, game.Player.Timed.Has(TimedIds.Cut));
        }

        var weak = Cast("kobold_shaman");          // power 4: (4/3*2)d5, no cut
        var strong = Cast("sauron_the_sorcerer"); // power 130: (130/3*2)d5, and cuts
        Assert.InRange(weak.Damage, 2, 10);
        Assert.True(strong.Damage > 100);
        Assert.False(weak.Cut);
        Assert.True(strong.Cut);
        Assert.Contains(weak.Said, m => m.EndsWith("points at you and curses!"));
        Assert.Contains(strong.Said, m => m.EndsWith("screams the word 'DIE!'"));

        // A saving throw turns it aside, with the tier's own words.
        var game = Game();
        game.Player.SkillSave = 100;
        var said = Messages(game);
        game.CastSpellForTest(Near(game, "kobold_shaman"), "WOUND");
        Assert.Contains("Your body tingles briefly.", said);
    }

    [Fact]
    public void Storm_BringsWater_Lightning_AndIce()
    {
        var game = Game();
        var said = Messages(game);
        var hp = game.Player.Hp;
        game.CastSpellForTest(Near(game, "storm_giant"), "STORM");
        Assert.Contains(said, m => m.EndsWith("creates a little storm."));
        Assert.True(game.Player.Hp < hp - 60); // three balls, each at least 20
        Assert.True(game.Player.Timed.Has(TimedIds.Confused)); // water
        Assert.True(game.Player.Timed.Has(TimedIds.Stun));     // water and ice
        Assert.True(game.Player.Timed.Has(TimedIds.Cut));      // ice
    }

    [Fact]
    public void Weave_SpinsWebs_WhichYouClearBeforeYouCanMove()
    {
        var game = Game();
        var spider = Near(game, "giant_spider", dx: 1);
        var said = Messages(game);
        game.CastSpellForTest(spider, "WEAVE");
        Assert.Contains(said, m => m.EndsWith("weaves a web."));
        Assert.True(game.IsWebbed(game.Player.Position)); // within a square of the spider
        Assert.True(game.IsWebbed(spider.Position + new Loc(1, 0)));

        var at = game.Player.Position;
        Assert.True(game.Execute(new WalkCommand(Direction.West)));
        Assert.Equal(at, game.Player.Position);
        Assert.Contains("You clear the web.", said);
        Assert.False(game.IsWebbed(at));
        game.Execute(new WalkCommand(Direction.West));
        Assert.NotEqual(at, game.Player.Position);
    }

    [Fact]
    public void Monsters_InWebs_AreStuck_ClearThem_OrWalkThrough()
    {
        var game = Game();
        var web = game.Data.Traps.Single(t => t.Web);
        void Webbed(Monster m) { game.Level[m.Position].Trap = web.Index; }

        var jackal = Arena.AddMonster(game, "soldier_ant", new Loc(3, 2)); // no CLEAR_WEB: stuck
        var uruk = Arena.AddMonster(game, "uruk", new Loc(3, 6));     // CLEAR_WEB: a turn to clear it
        var spider = Arena.AddMonster(game, "giant_spider", new Loc(14, 6)); // PASS_WEB
        foreach (var m in new[] { jackal, uruk, spider }) Webbed(m);
        game.UpdateView();

        var jackalAt = jackal.Position;
        for (var i = 0; i < 3; i++) game.RunMonsterTurn(jackal);
        Assert.Equal(jackalAt, jackal.Position);

        var urukAt = uruk.Position;
        game.RunMonsterTurn(uruk);
        Assert.Equal(urukAt, uruk.Position);
        Assert.False(game.IsWebbed(urukAt));

        var spiderAt = spider.Position;
        game.RunMonsterTurn(spider);
        Assert.NotEqual(spiderAt, spider.Position);
    }

    [Fact]
    public void Running_StopsAtAWeb()
    {
        var game = Arena.Create(4, "############", "#@,,,,,,,,,#", "############");
        game.Known.RememberAll(game.Level);
        game.Level[new Loc(6, 1)].Trap = game.Data.Traps.Single(t => t.Web).Index;
        game.Level[new Loc(6, 1)].Flags |= Angband.Core.World.SquareFlags.TrapVisible;
        game.UpdateView();
        game.Execute(new RunCommand(Direction.East));
        Assert.True(game.Player.Position.X < 6);
    }

    [Fact]
    public void GreaterSummons_BringTheirKinds()
    {
        static List<Monster> Summoned(string caster, string spell, Action<GameSession>? setup = null)
        {
            var game = Game(depth: 60); // where such summoners are met
            setup?.Invoke(game);
            var before = game.Level.Monsters.All.Select(m => m.Id).ToHashSet();
            game.CastSpellForTest(Near(game, caster), spell);
            return [.. game.Level.Monsters.All.Where(m => !before.Contains(m.Id) && m.Race.Id != caster)];
        }

        var dragons = Summoned("saruman_of_many_colours", "S_HI_DRAGON");
        Assert.NotEmpty(dragons);
        Assert.All(dragons, m => Assert.Equal('D', m.Race.Glyph));

        var ainur = Summoned("maia_of_yavanna", "S_AINU");
        Assert.NotEmpty(ainur);
        Assert.All(ainur, m => { Assert.Equal('A', m.Race.Glyph); Assert.False(m.Race.IsUnique); });

        var wraiths = Summoned("the_witch_king_of_angmar", "S_WRAITH");
        Assert.NotEmpty(wraiths);
        // Ringwraiths first; once none are left to come at this depth, greater undead fill in (the fallback).
        Assert.Contains(wraiths, m => m.Race.IsUnique && m.Race.Glyph == 'W');
        Assert.All(wraiths, m => Assert.True((m.Race.IsUnique && m.Race.Glyph == 'W') || "VWL".Contains(m.Race.Glyph)));

        // With every Ringwraith dead, greater undead come instead (summon.txt's fallback).
        var fallback = Summoned("the_witch_king_of_angmar", "S_WRAITH", g =>
        {
            foreach (var r in g.Data.Monsters.Where(r => r.IsUnique && r.Glyph == 'W')) g.KilledUniques.Add(r.Id);
        });
        Assert.NotEmpty(fallback);
        Assert.All(fallback, m => Assert.Contains(m.Race.Glyph, "VWL"));
    }

    [Fact]
    public void SummonUniques_SaysWhose()
    {
        var game = Game();
        var said = Messages(game);
        game.CastSpellForTest(Near(game, "morgoth_lord_of_darkness"), "S_UNIQUE");
        Assert.Contains(said, m => m.EndsWith("summons his servants."));
    }
}
