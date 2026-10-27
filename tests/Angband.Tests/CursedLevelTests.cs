using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Monsters;
using Angband.Core.Persistence;
using Angband.Core.Records;
using Angband.Core.World;

namespace Angband.Tests;

/// <summary>AVABand's cursed levels: now and then a new level's curse, its terms told on arrival, to stay on or to leave.</summary>
public class CursedLevelTests
{
    private static GameData Data => TestData.Game;

    private static GameSession Game(ulong seed = 7, bool cursed = true, bool persist = false) =>
        GameSession.NewGame(Data, seed, CharacterSpec.Default("human", "warrior") with
        {
            Options = new Dictionary<string, bool> { [OptionIds.AvaCursedLevels] = cursed, [OptionIds.LevelsPersist] = persist },
        });

    /// <summary>A game (made strong, so nothing kills it) with prompts and messages caught.</summary>
    private static (GameSession Game, List<QuestPromptEvent> Prompts, List<LevelCursedEvent> Curses, List<string> Said) Watched(ulong seed = 7,
        bool persist = false)
    {
        var game = Game(seed, persist: persist);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        var prompts = new List<QuestPromptEvent>();
        var curses = new List<LevelCursedEvent>();
        var said = new List<string>();
        game.Events.Subscribe<QuestPromptEvent>(prompts.Add);
        game.Events.Subscribe<LevelCursedEvent>(curses.Add);
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        return (game, prompts, curses, said);
    }

    /// <summary>Jumps to a depth until the level made there is cursed; false if none was in so many tries.</summary>
    private static bool JumpUntilCursed(GameSession game, int depth, int tries = 80)
    {
        for (var i = 0; i < tries; i++)
        {
            game.Execute(new DebugJumpCommand(depth));
            if (game.Level.Curse is not null) return true;
        }
        return false;
    }

    /// <summary>The level at a depth made with this curse (or none), from the same game seed: the same level otherwise.</summary>
    private static GameSession LevelWith(LevelCurse? curse, int depth = 12, ulong seed = 31)
    {
        var game = Game(seed, cursed: curse is not null);
        game.CurseNextLevel = curse;
        game.Execute(new DebugJumpCommand(depth));
        Assert.Equal(curse?.Id, game.Level.Curse);
        return game;
    }

    private static GameSession RoundTrip(GameSession game)
    {
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        return SaveGame.Load(Data, stream);
    }

    // --- The option, the chance and the gates ---------------------------------------------------------

    [Fact]
    public void Cursed_levels_are_a_birth_option_on_by_default_but_never_in_the_daily_dungeon_the_weekly_challenge_or_the_tutorial()
    {
        var option = OptionCatalog.Find(OptionIds.AvaCursedLevels)!;
        Assert.Equal(OptionKind.Birth, option.Kind);
        Assert.True(option.Default);
        var game = GameSession.NewGame(Data, 3, "warrior");
        Assert.True(game.CursedLevelsOn);
        game.Player.DailyDate = "2026-10-26";
        Assert.False(game.CursedLevelsOn);
        game.Player.DailyDate = null;
        game.Player.WeeklyWeek = "2026-W43";
        Assert.False(game.CursedLevelsOn);
        Assert.False(Tutorial.Create(Data).CursedLevelsOn);
        Assert.False(Game(cursed: false).CursedLevelsOn);
        // Classic is still the defaults.
        Assert.Equal("classic", Challenges.PresetFor(id => OptionCatalog.Find(id)!.Default)?.Id);
    }

    [Fact]
    public void About_one_level_in_ten_is_cursed_by_its_seed_and_each_curse_comes()
    {
        var rolled = Enumerable.Range(0, 20_000).Select(i => LevelCurses.Roll((ulong)i * 7919 + 13)).ToList();
        var share = rolled.Count(c => c is not null) / 20_000.0;
        Assert.InRange(share, 0.085, 0.115);
        foreach (var curse in LevelCurses.All) Assert.Contains(curse, rolled);
        // The same seed, the same curse.
        Assert.Equal(LevelCurses.Roll(12345), LevelCurses.Roll(12345));
    }

    [Fact]
    public void None_above_250_ft_none_two_in_a_row_and_none_on_Sauron_or_Morgoths_level()
    {
        var game = Game(5);
        for (var i = 0; i < 60; i++)
        {
            game.Execute(new DebugJumpCommand(4));
            Assert.Null(game.Level.Curse);
        }
        var cursedInARow = 0;
        var any = false;
        for (var i = 0; i < 150; i++)
        {
            game.Execute(new DebugJumpCommand(5 + i % 20));
            if (game.Level.Curse is null) cursedInARow = 0;
            else
            {
                any = true;
                Assert.Equal(1, ++cursedInARow);
            }
        }
        Assert.True(any);

        game.CurseNextLevel = LevelCurses.Teeming;
        game.Execute(new DebugJumpCommand(99));
        Assert.Null(game.Level.Curse);
        game.Execute(new DebugJumpCommand(100));
        Assert.Null(game.Level.Curse);
        game.Execute(new DebugJumpCommand(30));
        Assert.Equal("teeming", game.Level.Curse); // (it waited for a level that may be cursed)
    }

    [Fact]
    public void A_level_made_without_the_option_or_in_a_seeded_run_is_never_cursed_and_the_dice_roll_as_before()
    {
        var on = Game(11);
        var off = Game(11, cursed: false);
        var daily = Game(11);
        daily.Player.DailyDate = "2026-10-26";
        var cursed = false;
        for (var i = 0; i < 40; i++)
        {
            foreach (var g in new[] { on, off, daily }) g.Execute(new DebugJumpCommand(5 + i % 10));
            Assert.Null(off.Level.Curse);
            Assert.Null(daily.Level.Curse);
            Assert.Equal(off.Level.Seed, daily.Level.Seed);
            // The curse comes from the level's own seed: up to the first cursed level (whose monsters then
            // act), the game's dice are where they were, and the levels the same.
            if (cursed) continue;
            Assert.Equal(off.Level.Seed, on.Level.Seed);
            if (on.Level.Curse is null) Assert.Equal(off.Level.Monsters.All.Count(), on.Level.Monsters.All.Count());
            cursed = on.Level.Curse is not null;
        }
        Assert.True(cursed);
    }

    // --- Each curse, made with the level --------------------------------------------------------------

    [Fact]
    public void Teeming_has_half_as_many_monsters_again_and_more_good_objects()
    {
        var plain = LevelWith(null);
        var teeming = LevelWith(LevelCurses.Teeming);
        Assert.Equal(plain.Level.Seed, teeming.Level.Seed);
        var before = plain.Level.Monsters.All.Count();
        var after = teeming.Level.Monsters.All.Count();
        Assert.True(after >= before * 5 / 4, $"{before} monsters, then {after}");
        var extraObjects = (plain.Level.Population.RoomObjects + plain.Level.Population.AnywhereObjects + 1) / 2;
        Assert.True(teeming.Level.Objects.All.Count() >= plain.Level.Objects.All.Count() + extraObjects - 1,
            $"{plain.Level.Objects.All.Count()} objects, then {teeming.Level.Objects.All.Count()}");
        // The new monsters act.
        Assert.All(teeming.Level.Monsters.All, m => Assert.Contains(m, teeming.Scheduler.Actors));
    }

    [Fact]
    public void The_restless_dead_bring_undead_worth_twice_the_experience()
    {
        static int Undead(GameSession g) => g.Level.Monsters.All.Count(m => m.Race.Has(MonsterFlags.Undead));
        var plain = LevelWith(null, depth: 20);
        var dead = LevelWith(LevelCurses.RestlessDead, depth: 20);
        Assert.True(Undead(dead) >= Undead(plain) + 3, $"{Undead(plain)} undead, then {Undead(dead)}");
        Assert.Equal(plain.Level.Monsters.All.Count(m => !m.Race.Has(MonsterFlags.Undead)),
            dead.Level.Monsters.All.Count(m => !m.Race.Has(MonsterFlags.Undead)));

        var zombie = Data.Monsters.First(r => r.Has(MonsterFlags.Undead));
        var orc = Data.Monsters.First(r => !r.Has(MonsterFlags.Undead) && r.Depth > 0);
        Assert.Equal((21, 0x8000), dead.CurseExperience(zombie, 10, 0xC000));
        Assert.Equal((10, 0xC000), dead.CurseExperience(orc, 10, 0xC000));
        Assert.Equal((10, 0xC000), plain.CurseExperience(zombie, 10, 0xC000));
    }

    [Fact]
    public void A_blood_moon_hastes_the_monsters_and_makes_its_treasure_deeper()
    {
        var plain = LevelWith(null);
        var moon = LevelWith(LevelCurses.BloodMoon);
        var monster = moon.Level.Monsters.All.First();
        Assert.Equal(LevelCurses.BloodMoonSpeed, moon.Scheduler.SpeedBonus!(monster));
        Assert.Equal(0, moon.Scheduler.SpeedBonus!(moon.Player));
        Assert.Equal(0, plain.Scheduler.SpeedBonus!(plain.Level.Monsters.All.First()));
        Assert.Equal(LevelCurses.BloodMoonDepth, moon.CurseObjectDepth);
        Assert.Equal(0, plain.CurseObjectDepth);
        // The same level, its treasure made deeper.
        Assert.NotEqual(plain.Level.Objects.All.Select(o => o.Item.Kind.Id), moon.Level.Objects.All.Select(o => o.Item.Kind.Id));
        Assert.True(moon.Level.Objects.All.Average(o => o.Item.Kind.Level) > plain.Level.Objects.All.Average(o => o.Item.Kind.Level) - 1);
    }

    [Fact]
    public void Darkness_puts_out_the_rooms_and_shortens_your_light_for_double_gold_and_something_excellent()
    {
        var plain = LevelWith(null, depth: 6);
        var dark = LevelWith(LevelCurses.Darkness, depth: 6);
        Assert.Contains(plain.Level.AllLocs(), p => plain.Level[p].Has(SquareFlags.Glow) && plain.Level[p].Has(SquareFlags.Room));
        Assert.DoesNotContain(dark.Level.AllLocs(), p => dark.Level[p].Has(SquareFlags.Glow) && !dark.Level.FeatureAt(p).Has(TerrainFlags.Bright));
        Assert.Equal(plain.Level.Objects.All.Count() + 1, dark.Level.Objects.All.Count());
        Assert.Contains(dark.Level.Objects.All, o => o.Item.Ego is not null || o.Item.IsArtifact);
        Assert.Equal(200, dark.CurseGold(100));
        Assert.Equal(100, plain.CurseGold(100));

        Assert.True(plain.Player.LightRadius >= 2, $"light {plain.Player.LightRadius}");
        Assert.Equal(plain.Player.LightRadius - 1, dark.Player.LightRadius);
        // Gone with the curse.
        dark.Execute(new DebugJumpCommand(3));
        Assert.Equal(plain.Player.LightRadius, dark.Player.LightRadius);
    }

    // --- Arriving: the terms, and the choice ----------------------------------------------------------

    [Fact]
    public void Arriving_tells_the_curse_and_asks_whether_to_stay_on_a_staircase_made_under_you()
    {
        var (game, prompts, curses, said) = Watched();
        game.CurseNextLevel = LevelCurses.Teeming;
        game.Execute(new DebugJumpCommand(8));
        var told = Assert.Single(curses);
        Assert.Equal("teeming", told.Id);
        Assert.True(told.CanLeave);
        Assert.Contains("A curse lies on this level: Teeming.", said);
        var prompt = Assert.Single(prompts);
        Assert.Equal("A cursed level: Teeming", prompt.Title);
        Assert.Contains(LevelCurses.Teeming.Risk, prompt.Text);
        Assert.Contains(LevelCurses.Teeming.Reward, prompt.Text);
        Assert.Equal(["cursed:stay", "cursed:leave"], prompt.Choices.Select(c => c.Id));
        Assert.Equal("Take the stairs up and leave it", prompt.Choices[1].Label);
        // Come by no stairs: an up staircase under you.
        Assert.True(game.Level.Has(game.Player.Position, TerrainFlags.UpStair));
        Assert.Equal("Cursed: Teeming", game.CurseStatus);
    }

    [Fact]
    public void Leaving_takes_the_stairs_back_for_nothing_but_the_level()
    {
        var (game, prompts, _, said) = Watched();
        game.CurseNextLevel = LevelCurses.BloodMoon;
        game.Execute(new DebugJumpCommand(8));
        var turn = game.GameTurn;
        Assert.True(game.Execute(new QuestChoiceCommand("cursed:leave")));
        Assert.Equal(7, game.Player.Depth);
        Assert.Null(game.Level.Curse); // (never two in a row)
        Assert.Null(game.CurseStatus);
        Assert.Contains("You turn from the curse, and take the stairs.", said);
        Assert.True(game.GameTurn > turn); // (a turn, as the stairs take)
    }

    [Fact]
    public void Staying_keeps_you_there_and_once_you_have_acted_the_way_back_is_gone()
    {
        var (game, _, _, said) = Watched();
        game.CurseNextLevel = LevelCurses.Darkness;
        game.Execute(new DebugJumpCommand(8));
        Assert.False(game.Execute(new QuestChoiceCommand("cursed:stay")));
        Assert.Equal(8, game.Player.Depth);
        Assert.Contains("You stay, and take the terms of the curse (Darkness).", said);
        Assert.Equal("Cursed: Darkness", game.CurseStatus);

        game.CurseNextLevel = LevelCurses.Darkness;
        game.Execute(new DebugJumpCommand(3));
        game.Execute(new DebugJumpCommand(9));
        Assert.Equal("darkness", game.Level.Curse);
        game.Execute(new HoldCommand());
        game.Execute(new QuestChoiceCommand("cursed:leave")); // (too late: you've acted)
        Assert.Equal(9, game.Player.Depth);
    }

    [Fact]
    public void Arriving_on_a_staircase_leaves_by_it()
    {
        var (game, prompts, _, _) = Watched();
        game.Execute(new DebugJumpCommand(9));
        var down = game.Level.AllLocs().First(p => game.Level.Has(p, TerrainFlags.DownStair));
        game.UseLevel(game.Level, down);
        game.CurseNextLevel = LevelCurses.Teeming;
        prompts.Clear();
        Assert.True(game.Execute(new TakeStairsCommand(true)));
        Assert.Equal(10, game.Player.Depth);
        Assert.Equal("teeming", game.Level.Curse);
        // Connected stairs: you stand on the way back up.
        Assert.True(game.Level.Has(game.Player.Position, TerrainFlags.UpStair));
        Assert.Equal("Take the stairs up and leave it", Assert.Single(prompts).Choices[1].Label);
        game.Execute(new QuestChoiceCommand("cursed:leave"));
        Assert.Equal(9, game.Player.Depth);
    }

    [Fact]
    public void A_kept_level_keeps_its_curse_and_asks_nothing_when_you_come_back()
    {
        var (game, prompts, _, _) = Watched(persist: true);
        game.CurseNextLevel = LevelCurses.RestlessDead;
        game.Execute(new DebugJumpCommand(10));
        game.Execute(new QuestChoiceCommand("cursed:stay"));
        game.Execute(new DebugJumpCommand(9));
        Assert.Null(game.CurseStatus);
        prompts.Clear();
        game.Execute(new DebugJumpCommand(10));
        Assert.Equal("restless_dead", game.Level.Curse);
        Assert.Equal("Cursed: Restless dead", game.CurseStatus);
        Assert.Empty(prompts);
    }

    // --- Turns, kills and the feats -------------------------------------------------------------------

    [Fact]
    public void Turns_and_kills_on_a_cursed_level_count_toward_its_feats()
    {
        var (game, _, _, said) = Watched();
        game.CurseNextLevel = LevelCurses.Teeming;
        game.Execute(new DebugJumpCommand(12));
        for (var i = 0; i < 20; i++) game.Execute(new HoldCommand());
        Assert.InRange(game.Player.CursedTurns, 15, 40);
        var feats = Feats.All(Data);
        var kept = feats.Single(f => f.Id == "cursed_kept");
        var defied = feats.Single(f => f.Id == "curse_defied");
        Assert.False(kept.Earned(game));
        game.Player.CursedTurns = LevelCurses.KeptTurns;
        Assert.True(kept.Earned(game));

        foreach (var monster in game.Level.Monsters.All.Take(3).ToList()) game.ProjectileHitsMonster(monster, "test", null, 1_000_000);
        Assert.Equal(3, game.Level.CursedKills);
        game.Level.CursedKills = LevelCurses.DefiedKills;
        Assert.False(defied.Earned(game));
        game.Execute(new DebugJumpCommand(13));
        Assert.Equal(1, game.Player.CursesDefied);
        Assert.True(defied.Earned(game));
        Assert.Contains("You leave the cursed level, its curse defied.", said);
        Assert.Contains(game.History, h => h.Text.StartsWith("Defied the curse (Teeming) at 600 ft"));
    }

    [Fact]
    public void Too_shallow_or_too_few_slain_and_the_curse_is_not_defied()
    {
        var game = Game();
        game.CurseNextLevel = LevelCurses.Teeming;
        game.Execute(new DebugJumpCommand(9));
        game.Level.CursedKills = 100;
        game.Execute(new DebugJumpCommand(3));
        game.CurseNextLevel = LevelCurses.Teeming;
        game.Execute(new DebugJumpCommand(15));
        game.Level.CursedKills = LevelCurses.DefiedKills - 1;
        game.Execute(new DebugJumpCommand(3));
        Assert.Equal(0, game.Player.CursesDefied);
    }

    // --- Saves and replays ----------------------------------------------------------------------------

    [Fact]
    public void A_save_keeps_the_curse_and_the_counts_and_an_older_save_has_none()
    {
        var game = Game();
        game.CurseNextLevel = LevelCurses.BloodMoon;
        game.Execute(new DebugJumpCommand(10));
        game.Level.CursedKills = 4;
        game.Player.CursedTurns = 77;
        game.Player.CursesDefied = 2;
        var loaded = RoundTrip(game);
        Assert.Equal("blood_moon", loaded.Level.Curse);
        Assert.Equal(4, loaded.Level.CursedKills);
        Assert.Equal("Cursed: Blood moon", loaded.CurseStatus);
        Assert.True(loaded.Player.LastLevelCursed);
        Assert.Equal(77, loaded.Player.CursedTurns);
        Assert.Equal(2, loaded.Player.CursesDefied);
        Assert.Equal(LevelCurses.BloodMoonSpeed, loaded.Scheduler.SpeedBonus!(loaded.Level.Monsters.All.First()));

        // A save from before cursed levels has none of it: a level without a curse, nothing counted.
        Assert.Null(new LevelSave().Curse);
        Assert.Equal(0, new LevelSave().CursedKills);
        Assert.False(new PlayerSave().LastLevelCursed);
        Assert.Equal(0, new PlayerSave().CursedTurns);
        Assert.Equal(0, new PlayerSave().CursesDefied);
    }

    [Fact]
    public void A_replay_with_a_cursed_level_plays_back_and_an_older_one_has_none()
    {
        var game = Game(23);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        game.Recorder = new ReplayRecorder(game);
        Assert.True(JumpUntilCursed(game, 12));
        var curse = game.Level.Curse;
        game.Execute(new QuestChoiceCommand("cursed:stay"));
        for (var i = 0; i < 10; i++) game.Execute(new HoldCommand());
        Assert.True(JumpUntilCursed(game, 14));
        game.Execute(new QuestChoiceCommand("cursed:leave"));
        Assert.Equal(13, game.Player.Depth);
        for (var i = 0; i < 5; i++) game.Execute(new HoldCommand());
        var file = game.Recorder.ToFile(game);
        Assert.Equal(4, file.Version);

        var player = new ReplayPlayer(Data, file);
        string? seen = null;
        player.Game.Events.Subscribe<LevelCursedEvent>(e => seen ??= e.Id);
        while (player.Step()) { }
        Assert.True(player.Matches);
        Assert.Equal(curse, seen);
        Assert.Equal(13, player.Game.Player.Depth);

        file.Version = 3;
        var older = new ReplayPlayer(Data, file);
        var cursed = false;
        older.Game.Events.Subscribe<LevelCursedEvent>(_ => cursed = true);
        while (older.Step()) { }
        Assert.False(cursed);
        Assert.False(older.Game.CursedLevelsOn);
    }
}
