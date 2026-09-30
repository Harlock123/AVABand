using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Data;

namespace Angband.Tests;

public class MonsterAiTests
{
    private static void Hold(GameSession game, int turns)
    {
        for (var i = 0; i < turns && !game.IsGameOver; i++) game.Execute(new HoldCommand());
    }

    private static List<T> Collect<T>(GameSession game) where T : IGameEvent
    {
        var list = new List<T>();
        game.Events.Subscribe<T>(list.Add);
        return list;
    }

    // --- Senses --------------------------------------------------------------------------------

    [Fact]
    public void Stealth_ReducesHearing()
    {
        var game = Arena.Create();
        var kobold = Arena.AddMonster(game, "kobold", new Loc(9, 3)); // hearing 20, ~4 away
        game.Player.Stealth = 0;
        game.Execute(new HoldCommand()); // the noise map is laid once a game turn (process_world)
        Assert.True(game.CanHear(kobold));
        game.Player.Stealth = 60; // hearing 20 - 60/3 = 0: deaf to the player
        Assert.False(game.CanHear(kobold));
    }

    /// <summary>An L-shaped dark corridor: the far end can't be seen from the start.</summary>
    private static readonly string[] Corridor =
    [
        "################",
        "#@............##",
        "#############.##",
        "#############.##",
        "#############.##",
        "#############.##",
        "################",
    ];

    private static GameSession WalkTheCorridor(ulong seed)
    {
        var game = Arena.Create(seed, Corridor);
        game.Player.Stealth = 60; // silent: only scent gives the player away
        game.Player.Hp = game.Player.MaxHp = 1000;
        for (var i = 0; i < 12; i++) game.Execute(new WalkCommand(Direction.East));
        for (var i = 0; i < 4; i++) game.Execute(new WalkCommand(Direction.South));
        Assert.Equal(new Loc(13, 5), game.Player.Position);
        return game;
    }

    [Fact]
    public void Smell_LetsMonstersTrackAPlayerTheyCannotSenseOtherwise()
    {
        var game = WalkTheCorridor(1);
        var fang = Arena.AddMonster(game, "fang", new Loc(1, 1)); // smell 30, fast
        Assert.False(game.CanSee(fang));
        Assert.False(game.CanHear(fang));
        Assert.True(game.CanSmell(fang));

        for (var i = 0; i < 15 && fang.Position.ChebyshevTo(game.Player.Position) > 1; i++) Hold(game, 1);

        Assert.Equal(1, fang.Position.ChebyshevTo(game.Player.Position));
    }

    [Fact]
    public void WithoutSmell_TheSameMonsterJustWanders()
    {
        var game = WalkTheCorridor(1);
        var lizard = Arena.AddMonster(game, "green_naga", new Loc(1, 1)); // no smell
        Assert.False(game.CanSmell(lizard));
        Hold(game, 15);
        Assert.True(lizard.Position.ChebyshevTo(game.Player.Position) > 1);
    }

    [Fact]
    public void Monsters_that_cannot_sense_the_player_do_nothing()
    {
        var game = Arena.Create(3,
            "##########################",
            "#,,,,,,,,,,#.............#",
            "#,,,,@,,,,,#.............#",
            "#,,,,,,,,,,#.............#",
            "##########################");
        game.Player.Stealth = 60;
        // 4.2.5's monster_check_active: out of view, unheard, unsmelt and unhurt, it takes no turn.
        var lizard = Arena.AddMonster(game, "rock_lizard", new Loc(18, 2));
        var from = lizard.Position;

        Hold(game, 40);

        Assert.Equal(from, lizard.Position);
        Assert.False(lizard.Active);

        lizard.Hp--; // a hurt monster is always active
        for (var i = 0; i < 200 && lizard.Position == from; i++) Hold(game, 1);
        Assert.NotEqual(from, lizard.Position);
    }

    // --- Fear -----------------------------------------------------------------------------------

    [Fact]
    public void Cornered_frightened_monsters_freeze()
    {
        var game = Arena.Create(1,
            "#######",
            "#,,@k##",
            "#######");
        game.Player.Hp = game.Player.MaxHp = 1000;
        var kobold = Arena.AddMonster(game, "kobold", new Loc(4, 1));
        kobold.Fear = 50;
        var messages = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));

        Hold(game, 2);

        // 4.2.5's monster_turn: with nowhere to run, its fear turns to paralysis.
        Assert.Equal(0, kobold.Fear);
        Assert.True(kobold.Held > 0);
        Assert.Contains("The kobold is held.", messages);
    }

    // --- Pack tactics --------------------------------------------------------------------------

    [Fact]
    public void Packs_WaitOutOfSightWhileThePlayerIsInACorridor_ThenAttackInTheOpen()
    {
        var game = Arena.Create(4,
            "###############",
            "#,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,#",
            "#######.#######",
            "#######.#######",
            "#######@#######",
            "###############");
        game.Player.Hp = game.Player.MaxHp = 1000;
        var jackals = new[] { new Loc(2, 1), new Loc(12, 1), new Loc(3, 2) }
            .Select(p => Arena.AddMonster(game, "jackal", p)).ToList();
        var bites = Collect<MonsterAttackEvent>(game);

        Hold(game, 15);
        Assert.Empty(bites);
        Assert.All(jackals, j => Assert.False(game.Level[j.Position].Has(Angband.Core.World.SquareFlags.View),
            $"jackal at {j.Position} is in plain view"));

        game.Player.Position = new Loc(7, 2); // step out into the room
        game.UpdateView();
        Hold(game, 15);
        Assert.NotEmpty(bites);
    }

    // --- Breeding --------------------------------------------------------------------------------

    private static readonly string[] BigRoom =
    [
        "##############################",
        "#,,,,,,,,,,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,,,,,,,,,,#",
        "#@,,,,,,,,,,,,,,,,,,,,,,,,,,,#",
        "##############################",
    ];

    [Fact]
    public void Breeders_Multiply()
    {
        var game = Arena.Create(5, BigRoom);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        game.Player.Stealth = 60;
        Arena.AddMonster(game, "white_worm_mass", new Loc(10, 3)); // in view: silent players go unheard

        Hold(game, 150);

        Assert.True(game.Level.Monsters.Count > 3, $"{game.Level.Monsters.Count} worms");
    }

    [Fact]
    public void Breeding_StopsAtTheLevelCap()
    {
        using var mod = new TempDir();
        File.WriteAllText(Path.Combine(mod.Path, DataLoader.ConstantsFile), """{ "maxBreeders": 6 }""");
        var data = DataLoader.Load(DataLoader.DefaultDataDirectory, mod.Path);
        var game = Arena.CreateWith(data, 6, BigRoom);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        game.Player.Stealth = 60;
        Arena.AddMonster(game, "white_worm_mass", new Loc(10, 3)); // in view: silent players go unheard

        Hold(game, 300);

        Assert.Equal(6, game.Level.Monsters.All.Count(m => m.Race.Id == "white_worm_mass")); // (a wanderer may join them)
    }

    // --- Movement rules -------------------------------------------------------------------------

    [Fact]
    public void BigMonsters_PushPastWeakerOnes()
    {
        var game = Arena.Create(7,
            "#############",
            "#@.......,,,#",
            "#############");
        game.Player.Hp = game.Player.MaxHp = 100_000;
        var giant = Arena.AddMonster(game, "mughash_the_kobold_lord", new Loc(10, 1)); // MOVE_BODY
        var mold = Arena.AddMonster(game, "rock_lizard", new Loc(8, 1));
        mold.Sleep = 10_000;

        for (var i = 0; i < 20 && giant.Position.X > 7; i++) Hold(game, 1);

        Assert.True(giant.Position.X < 8, $"giant stuck at {giant.Position}");
    }

    [Fact]
    public void BashersBreakLockedDoors()
    {
        var game = Arena.Create(8,
            "#########",
            "#@,,+,,,#",
            "#########");
        game.Player.Hp = game.Player.MaxHp = 100_000;
        var door = new Loc(4, 1);
        game.Level[door].LockPower = 1;
        Arena.AddMonster(game, "green_naga", new Loc(6, 1)); // BASH_DOOR only, and strong enough to break a lock
        var messages = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));

        Hold(game, 80);

        Assert.False(game.Level.Has(door, TerrainFlags.DoorClosed));
        Assert.Contains(messages, m => m.Contains("burst open"));
    }

    // --- Spells --------------------------------------------------------------------------------

    private static readonly string[] Hall =
    [
        "#################",
        "#,,,,,,,,,,,,,,,#",
        "#,,@,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,#",
        "#################",
    ];

    [Fact]
    public void Archers_ShootFromRange()
    {
        var game = Arena.Create(9, Hall);
        game.Player.Hp = game.Player.MaxHp = 1000;
        var archer = Arena.AddMonster(game, "kobold_archer", new Loc(14, 2));
        archer.Hp = archer.MaxHp = 10_000;
        var spells = Collect<MonsterSpellEvent>(game);
        var hurt = Collect<PlayerHurtEvent>(game);

        Hold(game, 10);

        Assert.Contains(spells, s => s.SpellId == "ARROW");
        Assert.NotEmpty(hurt); // (regeneration at 1000 max HP hides small hits in the HP total)
    }

    [Fact]
    public void Bolts_AreNotCastThroughOtherMonsters()
    {
        var game = Arena.Create(10,
            "#################",
            "#@,,,,,,,,,,,,,,#",
            "#################");
        game.Player.Hp = game.Player.MaxHp = 1000;
        Arena.AddMonster(game, "grey_mold", new Loc(5, 1)).Hp = 100_000;
        var archer = Arena.AddMonster(game, "kobold_archer", new Loc(12, 1));
        archer.Sleep = 0;
        var spells = Collect<MonsterSpellEvent>(game);

        Hold(game, 20);

        Assert.DoesNotContain(spells, s => s.SpellId == "ARROW");
    }

    [Fact]
    public void Casters_HealTheirWounds_AndPullThePlayerClose_AndSummon()
    {
        var game = Arena.Create(11, BigRoom);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        game.Player.SkillSave = 1000;
        var orfax = Arena.AddMonster(game, "orfax", new Loc(15, 2));
        var spells = Collect<MonsterSpellEvent>(game);
        var moves = Collect<PlayerMovedEvent>(game);

        bool SeenAll() => new[] { "HEAL", "TELE_TO", "S_MONSTER" }.All(id => spells.Any(s => s.SpellId == id));
        for (var i = 0; i < 600 && !SeenAll(); i++)
        {
            orfax.Hp = Math.Min(orfax.Hp, orfax.MaxHp / 2); // keep him hurt so healing is worthwhile
            Hold(game, 1);
        }

        Assert.Contains(spells, s => s.SpellId == "HEAL");
        Assert.Contains(spells, s => s.SpellId == "TELE_TO");
        Assert.Contains(spells, s => s.SpellId == "S_MONSTER");
        Assert.NotEmpty(moves);
        Assert.True(game.Level.Monsters.Count > 1);
    }

    [Fact]
    public void SavingThrows_AndProtections_StopStatusSpells()
    {
        var game = Arena.Create(12, Hall);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        game.Player.SkillSave = 1000; // always saves
        var priest = Arena.AddMonster(game, "apprentice", new Loc(12, 2)); // BLIND, CONF (and no darkness, which blinds unsaved)
        priest.Hp = priest.MaxHp = 100_000;
        var spells = Collect<MonsterSpellEvent>(game);

        Hold(game, 160);

        Assert.Contains(spells, s => s.SpellId is "BLIND" or "CONF" or "SCARE");
        Assert.False(game.Player.Timed.Has(TimedIds.Blind));
        Assert.False(game.Player.Timed.Has(TimedIds.Confused));
        Assert.False(game.Player.Timed.Has(TimedIds.Afraid));
    }

    [Fact]
    public void Shattering_blows_shake_the_ground_when_they_hit_hard()
    {
        // Angband 4.2.5 SHATTER: a blow doing more than 23 sets off an earthquake around the monster.
        var game = Arena.Create(21, Hall);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        var elemental = Arena.AddMonster(game, "great_earth_elemental", new Loc(4, 2));
        elemental.Hp = elemental.MaxHp = 100_000;
        var shook = false;
        var hits = new List<(int Damage, bool Shook)>();
        game.Events.Subscribe<MessageEvent>(e => { if (e.Text == "The ground shakes!") shook = true; });
        game.Events.Subscribe<MonsterAttackEvent>(e =>
        {
            if (e.Hit) hits.Add((e.Damage, shook));
            shook = false;
        });
        for (var i = 0; i < 300 && hits.Count < 12; i++) Hold(game, 1);

        Assert.NotEmpty(hits);
        // Its three 6d6 blows (36 at most) never shake anything; its 10d10 shattering blow does above 23.
        Assert.All(hits.Where(h => h.Damage <= 23), h => Assert.False(h.Shook));
        Assert.All(hits.Where(h => h.Damage > 36), h => Assert.True(h.Shook));
        Assert.Contains(hits, h => h.Shook);
        Assert.Equal(60, game.Data.BlowEffect("shatter")!.Power);
    }

    [Fact]
    public void Breath_ScalesWithTheCastersHealth_AndIsResisted()
    {
        int BreathDamage(int resist, int hp)
        {
            var game = Arena.Create(13, Hall);
            game.Player.Hp = game.Player.MaxHp = 100_000;
            if (resist != 0) game.Player.IntrinsicResists["elec"] = resist;
            game.RecalculateBonuses();
            var dragon = Arena.AddMonster(game, "baby_blue_dragon", new Loc(12, 2));
            dragon.Hp = dragon.MaxHp = hp;
            var hurt = 0;
            var breathing = false;
            game.Events.Subscribe<MonsterSpellEvent>(_ => breathing = true);
            game.Events.Subscribe<PlayerHurtEvent>(e => { if (breathing) hurt = e.Damage; breathing = false; });
            for (var i = 0; i < 400 && hurt == 0; i++) Hold(game, 1);
            return hurt;
        }

        Assert.Equal(600 / 3, BreathDamage(0, 600)); // 4.2.5: hit points / 3 (projection.txt)
        Assert.Equal(60 / 3, BreathDamage(0, 60));
        Assert.Equal(600 / 3 / 3, BreathDamage(1, 600));
    }

    [Fact]
    public void A_caster_at_full_health_never_heals()
    {
        // Angband remove_bad_spells: no HEAL at full hit points (for any monster that isn't stupid).
        var game = Arena.Create(14, BigRoom);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        var orfax = Arena.AddMonster(game, "orfax", new Loc(15, 2));
        var spells = Collect<MonsterSpellEvent>(game);
        for (var i = 0; i < 150; i++)
        {
            orfax.Hp = orfax.MaxHp;
            Hold(game, 1);
        }
        Assert.NotEmpty(spells);
        Assert.DoesNotContain(spells, s => s.SpellId == "HEAL");
    }

    [Fact]
    public void Afraid_casters_fail_more_often()
    {
        // Angband monster_spell_failrate: 24% (4.2.5's MIN(spell power, 1)), 20 more while afraid.
        double FailShare(bool afraid)
        {
            var game = Arena.Create(15, Hall);
            game.Player.Hp = game.Player.MaxHp = 100_000;
            var shaman = Arena.AddMonster(game, "kobold_shaman", new Loc(12, 2));
            shaman.Hp = shaman.MaxHp = 100_000;
            int fails = 0, casts = 0;
            game.Events.Subscribe<MessageEvent>(m => fails += m.Text.EndsWith("tries to cast a spell, but fails.") ? 1 : 0);
            game.Events.Subscribe<MonsterSpellEvent>(_ => casts++);
            for (var i = 0; i < 1500; i++)
            {
                if (afraid) shaman.Fear = 50;
                Hold(game, 1);
            }
            return fails / (double)Math.Max(1, fails + casts);
        }
        var calm = FailShare(false);
        var scared = FailShare(true);
        Assert.InRange(calm, 0.15, 0.33);
        Assert.InRange(scared, 0.35, 0.55);
    }

    [Fact]
    public void LowLevelCasters_SometimesFail()
    {
        var game = Arena.Create(15, Hall);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        var shaman = Arena.AddMonster(game, "kobold_shaman", new Loc(12, 2));
        shaman.Hp = shaman.MaxHp = 100_000;
        var messages = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));

        Hold(game, 300);

        Assert.Contains(messages, m => m.EndsWith("tries to cast a spell, but fails."));
    }

    [Fact]
    public void PitsAndNests_AreFilledWithOneThemesMonsters()
    {
        // Angband build_pit / build_nest: every monster fits one pit.txt theme.
        var generator = new Angband.Core.Generation.DungeonGenerator(TestData.Game);
        var checkedRooms = 0;
        for (ulong seed = 0; seed < 200 && checkedRooms < 3; seed++)
        {
            var level = generator.Generate(new Angband.Core.Generation.LevelRequest(40, seed, ProfileId: "classic")).Level;
            var races = level.SpawnHints.Where(h => h.Kind == Angband.Core.World.SpawnKind.Race)
                .Select(h => TestData.Game.Monster(h.Tag!.Split('|')[0])!).ToList();
            if (races.Count == 0) continue;
            Assert.Contains(TestData.Game.Pits, p => races.Distinct().All(r => Angband.Core.Generation.Cave.PitHook(p, r))
                || races.Count > 70); // two pits on one level may have two themes
            checkedRooms++;
        }
        Assert.True(checkedRooms > 0, "no pits or nests were generated");
    }

    [Fact]
    public void MonsterAi_IsDeterministic()
    {
        static string Run()
        {
            var game = GameSession.NewGame(TestData.Game, 31);
            game.Execute(new DebugJumpCommand(12));
            game.Player.Hp = game.Player.MaxHp = 100_000;
            for (var i = 0; i < 200; i++) game.Execute(new HoldCommand());
            return string.Join(";", game.Level.Monsters.All.Select(m => $"{m.Race.Id}@{m.Position}:{m.Hp}")) + $"|{game.Player.Hp}";
        }
        Assert.Equal(Run(), Run());
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("avaband-ai-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
