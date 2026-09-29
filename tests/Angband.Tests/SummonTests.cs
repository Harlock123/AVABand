using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>
/// Summoning as Angband 4.2.5 does it (summon.txt, effect_handler_SUMMON, summon_specific): kinds
/// by monster base and flag, a budget of depth × the caster's level, summons around the caster.
/// </summary>
public class SummonTests
{
    private static readonly string[] Room =
    [
        "#####################",
        "#,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,@#",
        "#####################",
    ];

    private static GameSession Game(int depth, ulong seed = 4)
    {
        var game = Arena.Create(seed, Room);
        var level = TestLevels.FromAscii(depth, out var eye, Room);
        game.UseLevel(level, eye);
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 1_000_000;
        return game;
    }

    private static Monster Caster(GameSession game, string race, Loc at)
    {
        var m = Arena.AddMonster(game, race, at);
        m.Held = 1000;
        game.UpdateView();
        return m;
    }

    [Fact]
    public void Summon_kinds_are_summon_txts()
    {
        var data = TestData.Game;
        Assert.Equal(17, data.Summons.Count);
        var hound = data.Summon("HOUND")!;
        Assert.True(GameSession.SummonOkay(hound, data.Monster("light_hound")!, null));
        Assert.True(GameSession.SummonOkay(hound, data.Monster("jackal")!, null)); // canines answer too
        Assert.False(GameSession.SummonOkay(hound, data.Monster("forest_wight")!, null));

        var kin = data.Summon("KIN")!;
        Assert.True(GameSession.SummonOkay(kin, data.Monster("wolf")!, "canine"));
        Assert.False(GameSession.SummonOkay(kin, data.Monster("grip")!, "canine")); // never a unique
        Assert.False(GameSession.SummonOkay(kin, data.Monster("forest_wight")!, "canine"));

        var wraith = data.Summon("WRAITH")!;
        Assert.True(GameSession.SummonOkay(wraith, data.Monster("the_witch_king_of_angmar")!, null));
        Assert.False(GameSession.SummonOkay(wraith, data.Monster("black_wraith")!, null)); // only the Nine
        Assert.Equal("HI_UNDEAD", wraith.Fallback);
        Assert.False(GameSession.SummonOkay(data.Summon("MONSTER")!, data.Monster("grip")!, null));
        Assert.True(GameSession.SummonOkay(data.Summon("ANY")!, data.Monster("grip")!, null));
    }

    [Fact]
    public void Summons_come_around_the_caster_until_the_depth_times_level_budget_is_met()
    {
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var game = Game(depth: 20, seed);
            var caster = Caster(game, "wolf", new Loc(4, 3)); // level 10: a budget of 200
            var before = game.Level.Monsters.All.Select(m => m.Id).ToHashSet();
            game.CastSpellForTest(caster, "S_MONSTERS");
            var came = game.Level.Monsters.All.Where(m => !before.Contains(m.Id)).OrderBy(m => m.Id).ToList();
            Assert.NotEmpty(came);
            Assert.True(came.Count <= 8); // S_MONSTERS: at most eight tries
            Assert.All(came, m => Assert.True(m.Position.DistanceTo(caster.Position) <= 4));
            // Every one but the last came while the levels (squared) were still under budget.
            var levels = came.Select(m => m.Race.Depth * m.Race.Depth).ToList();
            Assert.True(levels.Take(levels.Count - 1).Sum() < 20 * 10);
            Assert.All(came, m => Assert.False(m.Race.IsUnique)); // MONSTERS: no uniques
        }
    }

    [Fact]
    public void Kin_share_the_casters_base()
    {
        var game = Game(depth: 15);
        var caster = Caster(game, "wolf", new Loc(10, 5));
        var before = game.Level.Monsters.All.Select(m => m.Id).ToHashSet();
        game.CastSpellForTest(caster, "S_KIN");
        var came = game.Level.Monsters.All.Where(m => !before.Contains(m.Id)).ToList();
        Assert.NotEmpty(came);
        Assert.All(came, m => Assert.Equal("canine", m.Race.Base));
    }

    [Fact]
    public void When_nothing_can_answer_nothing_comes()
    {
        var game = Game(depth: 30);
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        var count = game.Level.Monsters.Count;
        // Morgoth is the only one of his kind, and kin are never unique.
        game.CastSpellForTest(Caster(game, "morgoth_lord_of_darkness", new Loc(10, 5)), "S_KIN");
        Assert.Equal(count + 1, game.Level.Monsters.Count);
        Assert.Contains("But nothing comes.", said);
    }

    [Fact]
    public void Traps_and_scrolls_summon_around_the_player_and_the_newcomers_wait()
    {
        var game = Game(depth: 25);
        Assert.True(game.SummonNearPlayer(6, "UNDEAD"));
        var came = game.Level.Monsters.All.ToList();
        Assert.NotEmpty(came);
        Assert.All(came, m =>
        {
            Assert.True(m.Position.DistanceTo(game.Player.Position) <= 4);
            Assert.True(m.Race.Has("UNDEAD"));
            Assert.Equal(0, m.Energy);
            if (m.Speed > game.Player.Speed) Assert.True(m.Held > 0); // held so the player acts first
        });
    }

    [Fact]
    public void Recall_names_the_kind()
    {
        var data = TestData.Game;
        var text = MonsterRecall.Describe(data, data.Monster("the_witch_king_of_angmar")!, new RaceLore { Probed = true }, 50);
        Assert.Contains("summon ringwraiths", text);
    }
}
