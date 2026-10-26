using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Persistence;
using Angband.Core.Randomness;

namespace Angband.Tests;

/// <summary>AVABand's rival adventurers: a band made at birth, heard of in town and now and then met below.</summary>
public class RivalTests
{
    /// <summary>A wide lit room, the player at '@' (8,3).</summary>
    private static readonly string[] Room =
    [
        "########################",
        "#,,,,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,@,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,,,,#",
        "########################",
    ];

    /// <summary>A strong character in the room, with a band of the temper given; prompts and messages caught.</summary>
    private static (GameSession Game, RivalBand Band, List<QuestPromptEvent> Prompts, List<string> Said) InTheRoom(RivalTemper temper = RivalTemper.Friendly)
    {
        var game = Arena.Create(5, Room);
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        game.GainExperience(game.ExperienceForLevel(29));
        game.Player.Hp = game.Player.MaxHp = 10_000;
        var band = game.Rivals!;
        band.Temper = temper;
        band.Depth = 10;
        var prompts = new List<QuestPromptEvent>();
        var said = new List<string>();
        game.Events.Subscribe<QuestPromptEvent>(prompts.Add);
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        return (game, band, prompts, said);
    }

    /// <summary>One of the band beside you, to the east.</summary>
    private static Monster Beside(GameSession game, int index = 0)
    {
        var body = game.MeetRival(index, new Loc(10, 3), new GameRandom(77))!;
        game.UpdateView();
        return body;
    }

    private static GameSession RoundTrip(GameSession game)
    {
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        return SaveGame.Load(TestData.Game, stream);
    }

    // --- The option and the band --------------------------------------------------------------------

    [Fact]
    public void Rivals_are_a_birth_option_on_by_default_but_never_in_the_daily_dungeon_the_weekly_challenge_or_the_tutorial()
    {
        var option = OptionCatalog.Find(OptionIds.AvaRivals)!;
        Assert.Equal("birth_ava_rivals", option.Id);
        Assert.Equal(OptionKind.Birth, option.Kind);
        Assert.True(option.Default);
        var game = GameSession.NewGame(TestData.Game, 3, "warrior");
        Assert.True(game.RivalsOn);
        game.Player.DailyDate = "2026-10-26";
        Assert.False(game.RivalsOn);
        game.Player.DailyDate = null;
        game.Player.WeeklyWeek = "2026-W43";
        Assert.False(game.RivalsOn);
        Assert.False(Tutorial.Create(TestData.Game).RivalsOn);

        var without = GameSession.NewGame(TestData.Game, 3, CharacterSpec.Default("human", "warrior") with
        {
            Options = new Dictionary<string, bool> { [OptionIds.AvaRivals] = false },
        });
        Assert.Null(without.Rivals);
        Assert.False(without.RivalsOn);
    }

    [Fact]
    public void A_band_is_a_named_leader_and_two_companions_of_the_games_races_and_classes()
    {
        var band = GameSession.NewGame(TestData.Game, 11, "warrior").Rivals!;
        Assert.Equal(3, band.Members.Count);
        Assert.Equal($"{band.Members[0].Name}'s band", band.Name);
        Assert.Equal(3, band.Members.Select(m => m.Name).Distinct().Count());
        foreach (var m in band.Members)
        {
            Assert.Equal(TestData.Game.Race(m.RaceId)!.Name, m.Race);
            Assert.Equal(TestData.Game.Class(m.ClassId)!.Name, m.Class);
            Assert.Equal($"{m.Name} the {m.Race} {m.Class}", m.Title);
            Assert.False(m.Dead);
        }
        Assert.Equal(1, band.Depth);
        Assert.Empty(band.Deeds);
    }

    [Fact]
    public void The_band_comes_from_dice_of_its_own_so_nothing_else_in_a_new_game_shifts()
    {
        var a = GameSession.NewGame(TestData.Game, 21, "warrior");
        var b = GameSession.NewGame(TestData.Game, 21, "warrior");
        Assert.Equal(a.Rivals!.Members.Select(m => m.Title), b.Rivals!.Members.Select(m => m.Title));
        Assert.Equal(a.Rivals.Temper, b.Rivals.Temper);

        var without = GameSession.NewGame(TestData.Game, 21, CharacterSpec.Default("human", "warrior") with
        {
            Options = new Dictionary<string, bool> { [OptionIds.AvaRivals] = false },
        });
        Assert.Equal(a.Rng.State, without.Rng.State);
        Assert.Equal(a.Objects.NextSerial, without.Objects.NextSerial);

        // Different characters, different rivals (over a few seeds, not all the same).
        var bands = Enumerable.Range(1, 8).Select(s => string.Join(",", GameSession.NewGame(TestData.Game, (ulong)s, "warrior").Rivals!.Members.Select(m => m.Title)));
        Assert.True(bands.Distinct().Count() > 1);
    }

    [Fact]
    public void A_rival_is_a_unique_person_of_their_class_and_level()
    {
        var (game, band, _, _) = InTheRoom();
        band.Members[0] = new RivalMember { Name = "Brannor", RaceId = "dwarf", Race = "Dwarf", ClassId = "warrior", Class = "Warrior" };
        band.Members[1] = new RivalMember { Name = "Elwen", RaceId = "half_elf", Race = "Half-Elf", ClassId = "mage", Class = "Mage" };
        var brannor = Beside(game);
        Assert.Equal("Brannor", brannor.Race.Name);
        Assert.Equal('p', brannor.Race.Glyph);
        Assert.True(brannor.Race.IsUnique);
        Assert.Equal(RivalBands.LevelAt(band, 1), brannor.Race.Depth);
        Assert.Equal(3, brannor.Race.Blows.Count);
        Assert.Equal("warrior", brannor.Race.TileAs);
        Assert.Contains("Brannor the Dwarf Warrior, who leads Brannor's band", brannor.Race.Description);
        var elwen = RivalBands.BuildRace(TestData.Game, band, 1);
        Assert.NotEmpty(elwen.Spells);
        Assert.Equal("Brannor", game.MonsterRace("rival:0")!.Name);
        Assert.Null(game.MonsterRace("rival:9"));
        Assert.Null(game.MonsterRace("rival:x"));
    }

    // --- Saving ------------------------------------------------------------------------------------

    [Fact]
    public void The_band_and_a_rival_on_the_level_survive_a_save()
    {
        var (game, band, _, _) = InTheRoom(RivalTemper.Wary);
        band.Deeds.Add(new RivalDeed { Turn = 5, Depth = 2, Text = "Brannor's band slew Grip, Farmer Maggot's Dog at 100 ft", Told = true });
        band.Members[2].Dead = true;
        band.Members[2].Fate = "slain by Bramble at 500 ft";
        band.Meetings = 2;
        var body = Beside(game, 1);
        body.Hp -= 3;

        var loaded = RoundTrip(game);
        var lb = loaded.Rivals!;
        Assert.Equal(band.Members.Select(m => (m.Title, m.Dead, m.Fate, m.Carries, m.Price, m.MetLevel)),
            lb.Members.Select(m => (m.Title, m.Dead, m.Fate, m.Carries, m.Price, m.MetLevel)));
        Assert.Equal((band.Temper, band.Depth, band.Meetings, band.LevelsSinceMeeting), (lb.Temper, lb.Depth, lb.Meetings, lb.LevelsSinceMeeting));
        Assert.Equal("Brannor's band slew Grip, Farmer Maggot's Dog at 100 ft", Assert.Single(lb.Deeds).Text);
        var lbody = loaded.Level.Monsters.At(body.Position)!;
        Assert.Equal((body.Race.Name, body.Race.Depth, body.Race.HitPoints, body.Hp), (lbody.Race.Name, lbody.Race.Depth, lbody.Race.HitPoints, lbody.Hp));
        Assert.True(loaded.RivalAtPeace(lbody));
    }

    [Fact]
    public void A_save_from_before_rivals_loads_without_a_band()
    {
        var game = Arena.Create(5, Room);
        game.UseRivals(null);
        var loaded = RoundTrip(game);
        Assert.Null(loaded.Rivals);
        Assert.False(loaded.RivalsOn);
        loaded.MarkDebugUsed();
        loaded.Execute(new DebugJumpCommand(3));
        loaded.Execute(new DebugJumpCommand(0)); // (nothing to tell, nothing to place)
        Assert.Null(loaded.Rivals);
    }

    [Fact]
    public void The_recorded_games_have_no_band_so_they_play_as_before()
    {
        foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Replays"), "*" + ReplayFile.Extension))
        {
            var player = new ReplayPlayer(TestData.Game, ReplayFile.Read(path));
            Assert.Null(player.Game.Rivals);
        }
    }

    // --- The news ----------------------------------------------------------------------------------

    [Fact]
    public void Coming_back_to_town_the_band_has_gone_deeper_and_the_news_is_told()
    {
        var (game, band, _, said) = InTheRoom();
        band.Depth = 1;
        game.Player.MaxDepth = 20;
        game.RivalsOnReturn();
        Assert.InRange(band.Depth, 2, 6); // (at most five levels a return)
        for (var i = 0; i < 10; i++)
        {
            game.Scheduler.SetGameTurn(game.GameTurn + 1000);
            game.RivalsOnReturn();
        }
        Assert.InRange(band.Depth, 17, 22); // (near your deepest: a little behind, or a little ahead)
        Assert.Contains(band.Deeds, d => d.Text == $"{band.Name} has gone down past 500 ft");
        Assert.All(band.Deeds, d => Assert.True(d.Told));
        Assert.Contains($"They say {band.Name} has gone down past 500 ft.", said);
    }

    [Fact]
    public void The_band_slays_only_uniques_you_have_outgrown_and_never_a_quests_own()
    {
        var (game, band, _, _) = InTheRoom();
        game.Player.MaxDepth = 40;
        band.Depth = 40;
        var slain = new List<string>();
        for (var i = 0; i < 300; i++)
        {
            game.Scheduler.SetGameTurn(game.GameTurn + 977);
            var before = game.KilledUniques.ToHashSet();
            game.RivalsOnReturn();
            slain.AddRange(game.KilledUniques.Except(before));
        }
        Assert.NotEmpty(slain);
        foreach (var id in slain)
        {
            var race = TestData.Game.Monster(id)!;
            Assert.True(race.IsUnique);
            Assert.InRange(race.Depth, 1, 40 - RivalBands.OutgrownBy);
            Assert.False(race.Has(MonsterFlags.Questor), id);
            Assert.Contains(band.Deeds, d => d.Text == $"{band.Name} slew {race.Name} at {race.Depth * 50} ft");
        }
        Assert.DoesNotContain("sauron_the_sorcerer", game.KilledUniques);
        Assert.DoesNotContain("morgoth_lord_of_darkness", game.KilledUniques);
        Assert.DoesNotContain("farmer_maggot", game.KilledUniques);
    }

    [Fact]
    public void The_band_leaves_the_unique_on_the_WANTED_poster_alone()
    {
        var (game, band, _, _) = InTheRoom();
        game.Player.MaxDepth = 40;
        band.Depth = 40;
        var wanted = TestData.Game.Monsters.First(r => r.IsUnique && r.Depth is >= 10 and <= 30 && !r.Has(MonsterFlags.Questor));
        game.AvaQuests.Bounty = new Angband.Core.Quests.UniqueBounty { Race = wanted.Id, Reward = 100, HintFrom = wanted.Depth, Taken = true };
        for (var i = 0; i < 300; i++)
        {
            game.Scheduler.SetGameTurn(game.GameTurn + 977);
            game.RivalsOnReturn();
            if (game.AvaQuests.Bounty is null) break; // (the poster came down: nothing more to show)
            Assert.DoesNotContain(wanted.Id, game.KilledUniques);
        }
    }

    [Fact]
    public void A_shallow_character_has_no_unique_outgrown_so_none_is_slain()
    {
        var (game, band, _, _) = InTheRoom();
        game.Player.MaxDepth = RivalBands.OutgrownBy; // (nothing at 50 ft or deeper is outgrown yet)
        band.Depth = 30;
        for (var i = 0; i < 100; i++)
        {
            game.Scheduler.SetGameTurn(game.GameTurn + 977);
            game.RivalsOnReturn();
        }
        Assert.DoesNotContain(game.KilledUniques, id => TestData.Game.Monster(id) is { IsUnique: true });
    }

    [Fact]
    public void Now_and_then_the_band_takes_a_modest_artifact_first()
    {
        var (game, band, _, _) = InTheRoom();
        game.Player.MaxDepth = 40;
        band.Depth = 40;
        var made = game.Objects.CreatedArtifacts.ToHashSet();
        for (var i = 0; i < 200; i++)
        {
            game.Scheduler.SetGameTurn(game.GameTurn + 977);
            game.RivalsOnReturn();
        }
        var taken = game.Objects.CreatedArtifacts.Except(made).ToList();
        Assert.NotEmpty(taken);
        Assert.InRange(taken.Count, 1, 60); // (rarely: about one return in twelve)
        foreach (var id in taken)
        {
            var art = TestData.Game.Artifacts.First(a => a.Id == id);
            Assert.InRange(art.Level, 1, RivalBands.ModestArtifactLevel);
            Assert.Contains(band.Deeds, d => d.Text.StartsWith($"{band.Name} found the ", StringComparison.Ordinal) && d.Text.EndsWith(art.Name, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void In_a_seeded_run_the_band_does_nothing()
    {
        var (game, band, _, said) = InTheRoom();
        game.Player.DailyDate = "2026-10-26";
        game.Player.MaxDepth = 40;
        band.Depth = 40;
        var uniques = game.KilledUniques.Count;
        for (var i = 0; i < 50; i++)
        {
            game.Scheduler.SetGameTurn(game.GameTurn + 977);
            game.RivalsOnReturn();
        }
        Assert.Equal(40, band.Depth);
        Assert.Empty(band.Deeds);
        Assert.Equal(uniques, game.KilledUniques.Count);
        Assert.DoesNotContain(said, s => s.StartsWith("They say", StringComparison.Ordinal));
        band.LevelsSinceMeeting = 100;
        game.PlaceRivals();
        Assert.Equal(100, band.LevelsSinceMeeting); // (not even counted)
    }

    [Fact]
    public void At_the_Prancing_Pony_Butterbur_has_the_latest_word()
    {
        var game = GameSession.NewGame(TestData.Game, 3, "warrior");
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        game.Rivals!.Deeds.Add(new RivalDeed { Text = $"{game.Rivals.Name} slew Fang, Farmer Maggot's Dog at 100 ft" });
        game.Player.Position = game.Level.AllLocs().First(l => game.Level.FeatureAt(l).Shop == "inn");
        game.Execute(new EnterStoreCommand());
        Assert.Contains($"Butterbur leans over the bar. \"They say {game.Rivals.Name} slew Fang, Farmer Maggot's Dog at 100 ft.\"", said);
        Assert.True(game.Rivals.Deeds[0].Told);
    }

    // --- Meeting them ------------------------------------------------------------------------------

    [Fact]
    public void Now_and_then_the_band_is_met_near_its_depth_out_of_sight()
    {
        var (game, band, _, _) = InTheRoom();
        game.Player.MaxDepth = 10;
        game.Player.Position = new Loc(1, 1);
        var met = false;
        for (ulong seed = 1; seed < 400 && !met; seed++)
        {
            band.LevelsSinceMeeting = RivalBands.MeetingGap;
            game.Level.Seed = seed;
            game.PlaceRivals();
            met = band.Meetings > 0;
        }
        // (The room is all in sight of you, so there's nowhere for them to start: a bigger level is needed.)
        Assert.False(met);

        game.MarkDebugUsed();
        band.Depth = 10;
        for (var i = 0; i < 300 && band.Meetings == 0; i++)
        {
            band.LevelsSinceMeeting = RivalBands.MeetingGap;
            game.Execute(new DebugJumpCommand(9 + i % 3));
        }
        Assert.True(band.Meetings > 0);
        Assert.Equal(0, band.LevelsSinceMeeting);
        var bodies = game.Level.Monsters.All.Where(m => game.RivalOf(m) is not null).ToList();
        Assert.InRange(bodies.Count, 1, 3);
        Assert.Contains(bodies, b => b.Position.DistanceTo(game.Player.Position) > 10);
        Assert.All(bodies, b => Assert.True(b.Position.DistanceTo(game.Player.Position) > 6));
    }

    [Fact]
    public void Not_until_a_while_after_the_last_meeting_nor_far_from_their_depth()
    {
        var (game, band, _, _) = InTheRoom();
        game.MarkDebugUsed();
        game.Player.MaxDepth = 30;
        band.Depth = 30;
        band.LevelsSinceMeeting = 0;
        for (var i = 0; i < RivalBands.MeetingGap - 1; i++) game.Execute(new DebugJumpCommand(30));
        Assert.Equal(0, band.Meetings);
        band.LevelsSinceMeeting = 100;
        for (var i = 0; i < 60; i++) game.Execute(new DebugJumpCommand(5));
        Assert.Equal(0, band.Meetings);
    }

    [Fact]
    public void A_friendly_rival_greets_you_and_sells_what_they_carry()
    {
        var (game, band, prompts, said) = InTheRoom();
        var body = Beside(game);
        var member = band.Members[0];
        Assert.NotNull(member.Carries);
        Assert.True(member.Price >= 50);
        Assert.Contains(said, s => s.StartsWith($"{member.Name} of {band.Name} calls out: \"Well met", StringComparison.Ordinal));

        game.Player.Gold = member.Price + 7;
        var turn = game.GameTurn;
        game.Execute(new WalkCommand(Direction.East));
        Assert.Equal(turn, game.GameTurn); // (a word takes no time)
        Assert.Equal(body.MaxHp, body.Hp);
        var prompt = prompts[^1];
        Assert.Equal(band.Name, prompt.Title);
        Assert.Equal(["rival:buy:0", "rival:no", "rival:attack:0"], prompt.Choices.Select(c => c.Id));

        game.Execute(new QuestChoiceCommand("rival:buy:0"));
        Assert.Equal(7, game.Player.Gold);
        Assert.Null(member.Carries);
        Assert.Contains(said, s => s.StartsWith("You buy ", StringComparison.Ordinal));
        Assert.True(game.RivalAtPeace(body));

        // Nothing more to sell.
        game.Execute(new WalkCommand(Direction.East));
        Assert.Equal(["rival:no", "rival:attack:0"], prompts[^1].Choices.Select(c => c.Id));
    }

    [Fact]
    public void Too_poor_you_buy_nothing()
    {
        var (game, band, _, said) = InTheRoom();
        Beside(game);
        game.Player.Gold = 0;
        game.Execute(new WalkCommand(Direction.East));
        game.Execute(new QuestChoiceCommand("rival:buy:0"));
        Assert.NotNull(band.Members[0].Carries);
        Assert.Contains(said, s => s.Contains("Not a copper less"));
    }

    [Fact]
    public void A_wary_rival_passes_by_and_never_attacks()
    {
        var (game, band, prompts, said) = InTheRoom(RivalTemper.Wary);
        var body = Beside(game);
        Assert.Contains(said, s => s.Contains("Keep your distance"));
        for (var i = 0; i < 30 && body.IsActive; i++) game.Execute(new HoldCommand());
        Assert.DoesNotContain(said, s => s.StartsWith(body.Race.Name + " hits", StringComparison.Ordinal) || s.StartsWith(body.Race.Name + " misses", StringComparison.Ordinal));
        game.Execute(new WalkCommand(Direction.East));
        if (game.Level.Monsters.At(new Loc(10, 3)) == body) Assert.DoesNotContain(prompts[^1].Choices, c => c.Id.StartsWith("rival:buy", StringComparison.Ordinal));
    }

    [Fact]
    public void Attacking_a_friendly_band_makes_them_hostile_for_good()
    {
        var (game, band, _, said) = InTheRoom();
        var body = Beside(game);
        game.Execute(new WalkCommand(Direction.East));
        game.Execute(new QuestChoiceCommand("rival:attack:0"));
        Assert.True(band.Provoked);
        Assert.True(band.IsHostile);
        Assert.False(game.RivalAtPeace(body));
        Assert.True(body.Aware);
        Assert.Contains(said, s => s.Contains("won't forget this"));
        Assert.Contains(band.Deeds, d => d.Text.Contains("swore vengeance"));

        // Now a step into them is a blow.
        var hp = body.Hp;
        for (var i = 0; i < 10 && body.Hp == hp && body.IsActive; i++) game.Execute(new WalkCommand(Direction.East));
        Assert.True(body.Hp < hp || !body.IsActive);
        Assert.True(RoundTrip(game).Rivals!.IsHostile);
    }

    [Fact]
    public void Hurting_one_at_peace_with_you_any_other_way_provokes_them_too()
    {
        var (game, band, _, _) = InTheRoom(RivalTemper.Wary);
        var body = Beside(game);
        game.DamageMonster(body, 1);
        Assert.True(band.IsHostile);
    }

    [Fact]
    public void A_hostile_band_attacks()
    {
        var (game, band, _, said) = InTheRoom(RivalTemper.Hostile);
        var body = Beside(game);
        Assert.False(game.RivalAtPeace(body));
        Assert.Contains(said, s => s.Contains("You should have stayed in town"));
        for (var i = 0; i < 20; i++) game.Execute(new HoldCommand());
        Assert.Contains($"{body.Race.Name} hits you.", said);
    }

    [Fact]
    public void A_rival_killed_drops_what_they_carried_the_news_tells_it_and_with_the_last_the_band_is_gone()
    {
        var (game, band, _, said) = InTheRoom(RivalTemper.Hostile);
        game.Player.Name = "Bramble";
        for (var i = 0; i < 3; i++)
        {
            var body = game.MeetRival(i, new Loc(12 + i * 2, 1), new GameRandom((ulong)i + 3))!;
            var carried = band.Members[i].Carries;
            Assert.NotNull(carried);
            var at = body.Position;
            game.DamageMonster(body, body.Hp + 1);
            Assert.True(band.Members[i].Dead);
            Assert.Equal($"slain by Bramble at {game.Player.Depth * 50} ft", band.Members[i].Fate);
            Assert.Null(band.Members[i].Carries);
            Assert.True(game.Level.Objects.All.Count(o => o.Loc.DistanceTo(at) <= 3) >= 2); // (what they carried, and gold)
            Assert.Contains(band.Deeds, d => d.Text == $"{band.Members[i].Title} of {band.Name} fell to Bramble at {game.Player.Depth * 50} ft" && !d.Told);
            Assert.Null(game.MeetRival(i, new Loc(12, 4), new GameRandom(1))); // (the dead stay dead)
        }
        Assert.True(band.IsGone);
        Assert.Contains($"{band.Name} is no more.", said);

        // Gone, they are never met again, and the news is told once.
        game.Player.MaxDepth = 30;
        game.RivalsOnReturn();
        Assert.All(band.Deeds, d => Assert.True(d.Told));
        Assert.Contains(said, s => s == $"They say {band.Name} is no more.");
    }

    [Fact]
    public void A_game_with_a_trade_and_a_fight_plays_back_from_its_replay()
    {
        var (game, band, _, _) = InTheRoom();
        game.Player.Gold = 1_000_000;
        Beside(game);
        game.Recorder = new ReplayRecorder(game);
        game.Execute(new WalkCommand(Direction.East));
        game.Execute(new QuestChoiceCommand("rival:buy:0"));
        game.Execute(new WalkCommand(Direction.East));
        game.Execute(new QuestChoiceCommand("rival:attack:0"));
        for (var i = 0; i < 30 && !game.IsGameOver; i++) game.Execute(new WalkCommand(Direction.East));
        var file = game.Recorder.ToFile(game);

        var player = new ReplayPlayer(TestData.Game, file);
        while (player.Step()) { }
        Assert.True(player.Matches);
        Assert.Equal(band.Members[0].Dead, player.Game.Rivals!.Members[0].Dead);
        Assert.Equal(game.Player.Gold, player.Game.Player.Gold);
    }

    [Fact]
    public void Odd_data_never_breaks_it()
    {
        var (game, band, _, _) = InTheRoom();
        band.Members.Clear();
        Assert.Equal("A rival band", band.Name);
        Assert.True(band.IsGone);
        game.RivalsOnReturn();
        Assert.Null(game.MeetRival(0, new Loc(10, 3), new GameRandom(1)));
        game.Execute(new QuestChoiceCommand("rival:buy:5"));

        var odd = new RivalBand { Members = [new RivalMember { Name = "", RaceId = "nobody", ClassId = "nothing", Carries = "{not json" }] };
        game.UseRivals(odd);
        var race = RivalBands.BuildRace(TestData.Game, odd, 0);
        Assert.Equal('p', race.Glyph);
        Assert.True(race.HitPoints > 0);
        Assert.Equal("A rival band", odd.Name);
    }
}
