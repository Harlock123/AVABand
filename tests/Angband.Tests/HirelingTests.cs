using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Persistence;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>AVABand's hirelings: a sword for hire from the Prancing Pony.</summary>
public class HirelingTests
{
    /// <summary>A wide lit room, the player at '@' (8,3).</summary>
    private static readonly string[] Room =
    [
        "########################",
        "#,,,,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,@,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,,,,#",
        "########################",
    ];

    private static Hireling Sword(HirelingRole role = HirelingRole.Warrior, int level = 10, string name = "Bergil") => new()
    {
        Name = name, Look = "human", Role = role, Level = level, Experience = Hireling.ExperienceFor(level), Share = 15,
        Fee = Hireling.FeeFor(role, level, 15),
    };

    /// <summary>A level-20 warrior in the room, with a hireling beside them.</summary>
    private static (GameSession Game, Monster Body, List<string> Said) WithHireling(HirelingRole role = HirelingRole.Warrior, int level = 10,
        string[]? rows = null)
    {
        var game = Arena.Create(1, rows ?? Room);
        game.GainExperience(game.ExperienceForLevel(19));
        game.Player.Hp = game.Player.MaxHp = 10_000;
        game.Join(Sword(role, level));
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        return (game, game.HirelingHere!, said);
    }

    private static void Hold(GameSession game, int turns)
    {
        for (var i = 0; i < turns && !game.IsGameOver; i++) game.Execute(new HoldCommand());
    }

    /// <summary>A game at the Prancing Pony's door, at the level given, with prompts and messages caught.</summary>
    private static (GameSession Game, List<QuestPromptEvent> Prompts, List<string> Said) AtTheInn(int level = 12,
        Dictionary<string, bool>? options = null)
    {
        var game = GameSession.NewGame(TestData.Game, 3, CharacterSpec.Default("human", "warrior") with { Options = options });
        if (level > 1) game.GainExperience(game.ExperienceForLevel(level - 1));
        game.Player.Hp = game.Player.MaxHp = 10_000;
        var prompts = new List<QuestPromptEvent>();
        var said = new List<string>();
        game.Events.Subscribe<QuestPromptEvent>(prompts.Add);
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        game.Player.Position = game.Level.AllLocs().First(l => game.Level.FeatureAt(l).Shop == "inn");
        game.Execute(new EnterStoreCommand());
        return (game, prompts, said);
    }

    // --- The option ------------------------------------------------------------------------------

    [Fact]
    public void Hirelings_are_a_birth_option_on_by_default_but_never_in_the_daily_dungeon_or_the_tutorial()
    {
        var option = OptionCatalog.Find(OptionIds.Hirelings)!;
        Assert.Equal("birth_ava_hirelings", option.Id);
        Assert.Equal(OptionKind.Birth, option.Kind);
        Assert.True(option.Default);
        var game = GameSession.NewGame(TestData.Game, 3, "warrior");
        Assert.True(game.HirelingsOn);
        game.Player.DailyDate = "2026-10-26";
        Assert.False(game.HirelingsOn);
        Assert.False(Tutorial.Create(TestData.Game).HirelingsOn);
    }

    [Fact]
    public void With_the_option_off_the_inn_offers_no_swords()
    {
        var (game, prompts, _) = AtTheInn(options: new Dictionary<string, bool> { [OptionIds.Hirelings] = false });
        Assert.False(game.HirelingsOn);
        Assert.DoesNotContain(prompts[^1].Choices, c => c.Id == "inn:hire");
        game.Execute(new QuestChoiceCommand("inn:hire")); // (not offered: nothing happens)
        Assert.Null(game.Hireling);
    }

    [Fact]
    public void Without_the_quests_the_inn_still_has_swords_for_hire()
    {
        var (game, prompts, _) = AtTheInn(options: new Dictionary<string, bool> { [OptionIds.AvaQuests] = false });
        Assert.Equal("Swords for hire", prompts[^1].Title);
        Assert.Contains(prompts[^1].Choices, c => c.Id == "none" && c.Label == "Leave");
        Assert.InRange(prompts[^1].Choices.Count(c => c.Id.StartsWith("hire:take:", StringComparison.Ordinal)), 2, 3);
        Assert.True(game.HirelingsOn);
    }

    // --- Hiring ------------------------------------------------------------------------------------

    [Fact]
    public void The_Pony_offers_two_or_three_swords_a_day_near_your_level_each_with_a_fee_and_a_share()
    {
        var (game, prompts, _) = AtTheInn(level: 12);
        Assert.Contains(prompts[^1].Choices, c => c.Id == "inn:hire" && c.Label == "Hire a sword");
        game.Execute(new QuestChoiceCommand("inn:hire"));
        var offers = prompts[^1].Choices.Where(c => c.Id.StartsWith("hire:take:", StringComparison.Ordinal)).ToList();
        var candidates = game.HireCandidates();
        Assert.InRange(candidates.Count, 2, 3);
        Assert.Equal(candidates.Count, offers.Count);
        Assert.Equal(candidates.Select(GameSession.DescribeCandidate), offers.Select(o => o.Label));
        Assert.All(candidates, c =>
        {
            Assert.InRange(c.Level, 12 - GameSession.HireLevelSpread, 12); // never above you
            Assert.InRange(c.Share, GameSession.HireShareMin, GameSession.HireShareMax);
            Assert.Equal(Hireling.FeeFor(c.Role, c.Level, c.Share), c.Fee);
            Assert.Contains(c.Look, new[] { "human", "dwarf", "elf", "hobbit" });
            Assert.Equal(Hireling.ExperienceFor(c.Level), c.Experience);
        });
        Assert.Equal(candidates.Count, candidates.Select(c => c.Name).Distinct().Count());

        // The same faces all day (asking again rolls nothing), and others on other days; nothing else moves.
        var rng = game.Rng.State;
        Assert.Equal(candidates.Select(c => c.Name), game.HireCandidates().Select(c => c.Name));
        Assert.Equal(rng, game.Rng.State);
        var days = Enumerable.Range(1, 6).Select(d =>
        {
            game.Scheduler.SetGameTurn(game.GameTurn + game.Data.Constants.DayLength);
            return string.Join(",", game.HireCandidates().Select(c => c.Name + c.Level + c.Role));
        }).ToList();
        Assert.True(days.Distinct().Count() > 1);
    }

    private static string DescribeShare(int share) => GameSession.DescribeCandidate(new Hireling
    {
        Name = "Bergil", Look = "human", Role = HirelingRole.Archer, Level = 7, Share = share, Fee = 640,
    });

    [Fact]
    public void A_candidate_is_described_with_their_folk_role_level_fee_and_share()
    {
        Assert.Equal("Bergil, a human archer of level 7: 640 gold now, then 12% of the gold you find", DescribeShare(12));
        Assert.Equal("Bergil the archer", new Hireling { Name = "Bergil", Role = HirelingRole.Archer }.Title);
    }

    [Fact]
    public void Each_folk_and_role_borrows_the_picture_of_a_monster_the_tilesets_draw()
    {
        foreach (var look in new[] { "human", "dwarf", "elf", "hobbit" })
        foreach (var role in Enum.GetValues<HirelingRole>())
            Assert.NotNull(TestData.Game.Monster(GameSession.HirelingPicture(new Hireling { Look = look, Role = role })));
    }

    [Fact]
    public void Fees_grow_with_level_cost_more_for_archers_and_less_for_a_bigger_share()
    {
        Assert.Equal((30 + 4) * 120 / 100, Hireling.FeeFor(HirelingRole.Warrior, 1, 10));
        Assert.Equal((300 + 400) * 120 / 100, Hireling.FeeFor(HirelingRole.Warrior, 10, 10));
        Assert.Equal((300 + 400) * 80 / 100, Hireling.FeeFor(HirelingRole.Warrior, 10, 20));
        Assert.Equal((300 + 400) * 6 / 5 * 80 / 100, Hireling.FeeFor(HirelingRole.Archer, 10, 20));
        Assert.True(Hireling.FeeFor(HirelingRole.Warrior, 20, 15) > Hireling.FeeFor(HirelingRole.Warrior, 10, 15));
    }

    [Fact]
    public void Hiring_takes_the_fee_and_the_sword_joins_you_only_one_at_a_time()
    {
        var (game, prompts, said) = AtTheInn();
        game.Execute(new QuestChoiceCommand("inn:hire"));
        var candidate = game.HireCandidates()[0];
        game.Player.Gold = candidate.Fee - 1;
        game.Execute(new QuestChoiceCommand("hire:take:0"));
        Assert.Null(game.Hireling);
        Assert.Contains(said, s => s.Contains("not a copper less"));

        game.Player.Gold = candidate.Fee + 5;
        game.Execute(new QuestChoiceCommand("hire:take:0")); // (the offer is made again)
        Assert.Equal(5, game.Player.Gold);
        var h = game.Hireling!;
        Assert.Equal(candidate.Name, h.Name);
        var body = game.HirelingHere!;
        Assert.True(body.IsAlly);
        Assert.Equal(1, body.Position.ChebyshevTo(game.Player.Position));
        Assert.Equal(Hireling.MaxHpFor(h.Role, h.Level), body.MaxHp);
        Assert.Equal(h.Name, body.Race.Name);
        Assert.Contains(said, s => s == $"{h.Title} will follow you down, for {h.Share}% of the gold you find.");
        Assert.Contains(game.History, e => e.Text.StartsWith($"Hired {h.Title}", StringComparison.Ordinal));

        // Back at the bar: they're with you, and no one else is offered.
        game.Execute(new EnterStoreCommand());
        Assert.Contains(prompts[^1].Choices, c => c.Id == "inn:hire" && c.Label == $"Your hireling, {h.Title}");
        game.Execute(new QuestChoiceCommand("inn:hire"));
        Assert.DoesNotContain(prompts[^1].Choices, c => c.Id.StartsWith("hire:take:", StringComparison.Ordinal));
        Assert.Contains(prompts[^1].Choices, c => c.Id == "hire:dismiss");
        game.Execute(new QuestChoiceCommand("hire:take:1"));
        Assert.Same(h, game.Hireling);
    }

    [Fact]
    public void They_take_their_share_of_the_gold_you_pick_up()
    {
        var (game, body, said) = WithHireling();
        game.Hireling!.Share = 15;
        var gold = game.Objects.Create(game.Data.Objects.First(k => k.Base == "gold").Id);
        gold.GoldValue = 200;
        var spot = game.Player.Position + new Loc(1, 0);
        game.Level.Objects.Add(spot, gold);
        var before = game.Player.Gold;
        game.Execute(new WalkCommand(Direction.East));
        Assert.Equal(before + 170, game.Player.Gold);
        Assert.Equal(30, game.Hireling.Earned);
        Assert.Contains(said, s => s == "Bergil takes a share: 30 gold.");
        Assert.NotNull(body);
    }

    // --- Following -------------------------------------------------------------------------------

    [Fact]
    public void They_keep_close_and_you_change_places_walking_into_them()
    {
        var (game, body, _) = WithHireling();
        game.Level.Monsters.Move(body, new Loc(20, 5));
        Hold(game, 20);
        Assert.InRange(body.Position.ChebyshevTo(game.Player.Position), 1, GameSession.HirelingFollowDistance);

        game.Level.Monsters.Move(body, game.Player.Position + new Loc(1, 0));
        var me = game.Player.Position;
        game.Execute(new WalkCommand(Direction.East));
        Assert.Equal(me + new Loc(1, 0), game.Player.Position);
        Assert.Equal(me, body.Position);
        Assert.Equal(body.MaxHp, body.Hp); // (no blow struck)
    }

    [Fact]
    public void They_follow_you_down_the_stairs_and_arrive_beside_you()
    {
        var (game, body, _) = WithHireling();
        body.Hp -= 10;
        game.Level.SetFeature(game.Player.Position, game.Data.Terrain.Ids.DownStair);
        game.Execute(new TakeStairsCommand(true));
        Assert.Equal(2, game.Player.Depth);
        var arrived = game.HirelingHere!;
        Assert.NotSame(body, arrived);
        Assert.True(arrived.IsAlly);
        Assert.True(arrived.Position.ChebyshevTo(game.Player.Position) <= 2);
        Assert.Equal(body.MaxHp - 10, arrived.Hp); // (their wounds come with them)
        Assert.Single(game.Level.Monsters.All, m => m.IsAlly);
        Assert.Contains(game.Scheduler.Actors, a => a == arrived);
        Assert.Equal(2, game.Hireling!.DeepestTogether);
    }

    [Fact]
    public void Back_in_town_their_wounds_are_seen_to_and_the_trip_counts()
    {
        var (game, _, _) = WithHireling();
        game.MarkDebugUsed();
        game.Execute(new DebugJumpCommand(21));
        game.HirelingHere!.Hp = 3;
        game.Execute(new DebugJumpCommand(0));
        var body = game.HirelingHere!;
        Assert.Equal(body.MaxHp, body.Hp);
        Assert.Equal(21, game.Hireling!.DeepestReturned);
    }

    [Fact]
    public void A_kept_level_never_keeps_the_hireling()
    {
        var game = GameSession.NewGame(TestData.Game, 5, CharacterSpec.Default("human", "warrior")
            with { Options = new Dictionary<string, bool> { [OptionIds.LevelsPersist] = true } });
        game.MarkDebugUsed();
        game.Player.Hp = game.Player.MaxHp = 10_000;
        game.Execute(new DebugJumpCommand(1));
        game.Join(Sword(level: 1));
        for (var i = 0; i < 3; i++)
        {
            game.Execute(new DebugJumpCommand(2));
            game.Execute(new DebugJumpCommand(1));
        }
        Assert.Single(game.Level.Monsters.All, m => m.IsAlly);
        Assert.NotNull(game.HirelingHere);
    }

    // --- Fighting --------------------------------------------------------------------------------

    [Fact]
    public void A_warrior_fights_a_foe_nearby_and_the_kill_is_theirs()
    {
        var (game, body, said) = WithHireling();
        var kobold = Arena.AddMonster(game, "small_kobold", body.Position + new Loc(-1, 0));
        var race = kobold.Race;
        var exp = game.Player.Experience;
        var hirelingExp = game.Hireling!.Experience;
        Hold(game, 20);
        Assert.False(kobold.IsActive);
        Assert.Equal(1, game.Hireling.Kills);
        Assert.Contains(said, s => s == "Bergil has slain the small kobold.");
        var worth = race.Experience * race.Depth / game.Player.Level;
        Assert.InRange(game.Player.Experience - exp, worth / 2, worth / 2 + 1); // (half to you)
        Assert.Equal(hirelingExp + (long)race.Experience * race.Depth / game.Hireling.Level, game.Hireling.Experience);
        Assert.Equal(game.Player.MaxHp, game.Player.Hp);
    }

    [Fact]
    public void An_archer_shoots_what_they_can_see_from_afar()
    {
        var (game, body, said) = WithHireling(HirelingRole.Archer);
        var kobold = Arena.AddMonster(game, "kobold", game.Player.Position + new Loc(5, 0));
        kobold.Hp = kobold.MaxHp = 1000;
        kobold.Held = 1000; // (it stays put)
        Hold(game, 10);
        Assert.Contains(said, s => s.StartsWith("Bergil's arrow hits the kobold", StringComparison.Ordinal));
        Assert.True(kobold.Hp < kobold.MaxHp);
        Assert.True(kobold.HatesAlly);
        Assert.InRange(body.Position.ChebyshevTo(game.Player.Position), 1, GameSession.HirelingFollowDistance);
    }

    [Fact]
    public void Sleeping_monsters_are_left_alone_unless_you_say_otherwise()
    {
        var (game, body, said) = WithHireling();
        var kobold = Arena.AddMonster(game, "kobold", body.Position + new Loc(-1, 0), awake: false);
        kobold.Sleep = 10_000;
        game.Player.Stealth = 60;
        Hold(game, 5);
        Assert.Equal(kobold.MaxHp, kobold.Hp);
        Assert.DoesNotContain(said, s => s.StartsWith("Bergil hits", StringComparison.Ordinal));

        game.SetTarget(kobold);
        var turn = game.GameTurn;
        Assert.False(game.Execute(new HirelingOrderCommand(HirelingOrder.Attack))); // (an order takes no time)
        Assert.Equal(turn, game.GameTurn);
        Assert.Contains(said, s => s == "Bergil goes for the kobold!");
        Hold(game, 10);
        Assert.True(kobold.Hp < kobold.MaxHp || !kobold.IsActive);
    }

    [Fact]
    public void Monsters_strike_at_the_hireling_and_bear_a_grudge_against_one_that_hurt_them()
    {
        var (game, body, said) = WithHireling(level: 1);
        var kobold = Arena.AddMonster(game, "kobold", body.Position + new Loc(-1, 0));
        kobold.Hp = kobold.MaxHp = 10_000;
        body.Hp = body.MaxHp = 10_000;
        Hold(game, 15);
        Assert.Contains(said, s => s.StartsWith("The kobold hits Bergil", StringComparison.Ordinal) || s == "The kobold misses Bergil.");
        Assert.True(body.Hp < body.MaxHp);
        Assert.True(kobold.HatesAlly);
        Assert.Equal(game.Player.MaxHp, game.Player.Hp); // (it went for them, not you)
    }

    [Fact]
    public void An_archer_never_shoots_through_you()
    {
        var (game, body, said) = WithHireling(HirelingRole.Archer, rows: ["##############", "#,,,@,,,,,,,,#", "##############"]);
        Assert.Equal(game.Player.Position + new Loc(-1, 0), body.Position); // (west: the first open square beside you)
        var kobold = Arena.AddMonster(game, "kobold", game.Player.Position + new Loc(4, 0));
        kobold.Held = 1000;
        Hold(game, 5);
        Assert.Equal(kobold.MaxHp, kobold.Hp);
        Assert.DoesNotContain(said, s => s.Contains("arrow"));
        Assert.Equal(game.Player.MaxHp, game.Player.Hp);
    }

    [Fact]
    public void Badly_hurt_they_fall_back_and_heal_slowly_then_return()
    {
        var (game, body, said) = WithHireling();
        var kobold = Arena.AddMonster(game, "kobold", body.Position + new Loc(-1, 0));
        kobold.Hp = kobold.MaxHp = 10_000;
        kobold.Held = 1000;
        body.Hp = body.MaxHp / 5;
        var from = body.Position.DistanceTo(kobold.Position);
        Hold(game, 1);
        Assert.True(game.Hireling!.Fleeing);
        Assert.Contains(said, s => s == "Bergil falls back, badly hurt.");
        Assert.True(body.Position.DistanceTo(kobold.Position) > from);
        Assert.Equal("Bergil the warrior (level 10, falling back)", game.HirelingHealthBar()!.Name);
        Assert.Equal(kobold.MaxHp, kobold.Hp);

        // A fiftieth of their hit points every 100 game turns, until half: then back to it.
        var hp = body.Hp;
        TestGames.HoldUntil(game, (game.GameTurn / 100 + 1) * 100);
        Assert.Equal(hp + Math.Max(1, body.MaxHp / GameSession.HirelingRegenDivisor), body.Hp);
        kobold.Hp = 0;
        game.Level.Monsters.Remove(kobold);
        body.Hp = body.MaxHp / 2;
        Hold(game, 1);
        Assert.False(game.Hireling.Fleeing);
        Assert.Contains(said, s => s == "Bergil is ready to fight again.");
    }

    // --- Your attacks and the hireling ----------------------------------------------------------------

    [Fact]
    public void Your_missiles_and_bolts_pass_them_by()
    {
        var (game, body, _) = WithHireling();
        game.Level.Monsters.Move(body, game.Player.Position + new Loc(1, 0));
        body.Hp = body.MaxHp = 10_000;
        var kobold = Arena.AddMonster(game, "kobold", game.Player.Position + new Loc(4, 0));
        kobold.Hp = kobold.MaxHp = 10_000;
        kobold.Held = 1000;
        for (var i = 0; i < 20 && kobold.Hp == kobold.MaxHp; i++) game.Execute(new FireCommand(kobold.Position));
        Assert.True(kobold.Hp < kobold.MaxHp);

        game.Player.SkillDevice = 500;
        var wand = game.Objects.Create("wand_of_magic_missile");
        wand.Charges = 10;
        game.Knowledge.LearnKind(wand.Kind);
        var held = game.Player.Inventory.Add(wand)!;
        var hp = kobold.Hp;
        for (var i = 0; i < 5 && kobold.Hp == hp; i++) game.Execute(new UseCommand(held, kobold.Position));
        Assert.True(kobold.Hp < hp);
        Assert.Equal(body.MaxHp, body.Hp);
        Assert.False(body.HatesAlly);
    }

    [Fact]
    public void Your_blasts_catch_them()
    {
        var (game, body, said) = WithHireling();
        body.Hp = body.MaxHp = 10_000;
        var kobold = Arena.AddMonster(game, "kobold", body.Position + new Loc(1, 0));
        kobold.Held = 1000;
        game.Player.SkillDevice = 500;
        var wand = game.Objects.Create("wand_of_stinking_cloud");
        wand.Charges = 10;
        game.Knowledge.LearnKind(wand.Kind);
        var held = game.Player.Inventory.Add(wand)!;
        for (var i = 0; i < 5 && body.Hp == body.MaxHp; i++) game.Execute(new UseCommand(held, kobold.Position));
        Assert.True(body.Hp < body.MaxHp);
        Assert.Contains(said, s => s.StartsWith("Bergil is caught in the blast", StringComparison.Ordinal));
        Assert.NotNull(game.Hireling);
    }

    [Fact]
    public void You_never_strike_them_and_they_are_no_target()
    {
        var (game, body, said) = WithHireling();
        game.Level.Monsters.Move(body, game.Player.Position + new Loc(1, 0));
        Assert.Null(game.NearestTarget());
        Assert.Empty(game.TargetableMonsters());
        Assert.False(game.TargetClosest(quiet: true));
        Assert.DoesNotContain(game.MonsterList(), r => r.Text.Contains("Bergil"));
        Assert.Contains("your hireling", game.LookDescription(body));
        game.SetTarget(body);
        Assert.False(game.TargetOkay());

        // Nor do they stop you resting.
        game.Player.Hp = game.Player.MaxHp / 2;
        Assert.True(game.Execute(new RestCommand()));
        Assert.DoesNotContain(said, s => s.Contains("cannot rest"));
        Assert.Equal(body.MaxHp, body.Hp);
    }

    // --- Orders ----------------------------------------------------------------------------------

    [Fact]
    public void Holding_they_stay_put_and_following_they_come_back()
    {
        var (game, body, said) = WithHireling();
        var spot = new Loc(20, 5);
        game.Level.Monsters.Move(body, spot);
        game.Execute(new HirelingOrderCommand(HirelingOrder.Hold));
        Assert.Contains(said, s => s == "Bergil holds this spot.");
        Hold(game, 10);
        Assert.Equal(spot, body.Position);
        game.Execute(new HirelingOrderCommand(HirelingOrder.Follow));
        Assert.Contains(said, s => s == "Bergil falls in close behind you.");
        Hold(game, 20);
        Assert.InRange(body.Position.ChebyshevTo(game.Player.Position), 1, GameSession.HirelingFollowDistance);
    }

    [Fact]
    public void Orders_need_a_hireling_and_attack_needs_a_target()
    {
        var game = Arena.Create();
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        game.Execute(new HirelingOrderCommand(HirelingOrder.Follow));
        Assert.Contains(said, s => s == "You have no hireling.");
        game.Join(Sword(level: 1));
        game.Execute(new HirelingOrderCommand(HirelingOrder.Attack));
        Assert.Contains(said, s => s.StartsWith("Target a monster first", StringComparison.Ordinal));
        Assert.Equal(HirelingOrder.Follow, game.Hireling!.Order);
    }

    // --- Experience and levels -------------------------------------------------------------------

    [Fact]
    public void They_level_slowly_growing_stronger_but_never_past_you()
    {
        var (game, body, said) = WithHireling(level: 5);
        var hp = body.MaxHp;
        game.GainHirelingExperience(Hireling.ExperienceFor(8) - game.Hireling!.Experience);
        Assert.Equal(8, game.Hireling.Level);
        Assert.Equal(Hireling.MaxHpFor(HirelingRole.Warrior, 8), body.MaxHp);
        Assert.Equal(body.MaxHp - hp, body.Hp - hp);
        Assert.Contains(said, s => s == "Bergil the warrior has reached level 8.");
        Assert.True(Hireling.ExperienceFor(8) > game.ExperienceForLevel(7)); // (slower than you)

        game.GainHirelingExperience(100_000_000);
        Assert.Equal(game.Player.Level, game.Hireling.Level); // (capped at yours)
        Assert.Equal(Hireling.BlowsFor(HirelingRole.Warrior, 20), body.Race.Blows.Count);
        Assert.True(Hireling.ArmourFor(HirelingRole.Warrior, 20) > Hireling.ArmourFor(HirelingRole.Warrior, 5));
    }

    // --- Death and parting -----------------------------------------------------------------------

    [Fact]
    public void A_hireling_who_falls_is_gone_for_good_and_remembered()
    {
        var (game, body, said) = WithHireling();
        var fallen = new List<HirelingFallenEvent>();
        game.Events.Subscribe<HirelingFallenEvent>(fallen.Add);
        game.ProjectileHitsMonster(body, "fire ball", "fire", 100_000);
        Assert.Null(game.Hireling);
        Assert.Null(game.HirelingHere);
        Assert.False(body.IsActive);
        Assert.DoesNotContain(game.Level.Monsters.All, m => m.IsAlly);
        Assert.Contains(said, s => s == "Bergil the warrior has fallen.");
        Assert.Contains(game.History, e => e.Text.StartsWith("Bergil the warrior fell", StringComparison.Ordinal));
        var memorial = Assert.Single(game.FallenCompanions);
        Assert.Equal(("Bergil", 10, 1), (memorial.Name, memorial.Level, memorial.Depth));
        Assert.Single(fallen);
        Hold(game, 3); // (the game goes on)

        // Never offered again.
        game.MarkDebugUsed();
        game.Execute(new DebugJumpCommand(0));
        for (var d = 0; d < 20; d++, game.Scheduler.SetGameTurn(game.GameTurn + game.Data.Constants.DayLength))
            Assert.DoesNotContain(game.HireCandidates(), c => c.Name == "Bergil");
    }

    [Fact]
    public void You_part_ways_in_town_but_not_below()
    {
        var (game, _, said) = WithHireling();
        game.Execute(new HirelingOrderCommand(HirelingOrder.Dismiss));
        Assert.NotNull(game.Hireling);
        Assert.Contains(said, s => s.StartsWith("Not down here", StringComparison.Ordinal));
        game.MarkDebugUsed();
        game.Execute(new DebugJumpCommand(0));
        game.Execute(new HirelingOrderCommand(HirelingOrder.Dismiss));
        Assert.Null(game.Hireling);
        Assert.DoesNotContain(game.Level.Monsters.All, m => m.IsAlly);
        Assert.Contains(said, s => s == "Bergil the warrior shakes your hand and heads home.");
        Assert.Empty(game.FallenCompanions);
    }

    // --- Saving, replays and feats -----------------------------------------------------------------

    private static GameSession RoundTrip(GameSession game)
    {
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        return SaveGame.Load(TestData.Game, stream);
    }

    [Fact]
    public void The_hireling_and_the_fallen_survive_a_save()
    {
        var (game, body, _) = WithHireling(HirelingRole.Archer, level: 7);
        game.FallenCompanions.Add(new FallenCompanion { Name = "Hirgon", Look = "dwarf", Role = HirelingRole.Warrior, Level = 3, Depth = 4, KilledBy = "a cave orc" });
        var h = game.Hireling!;
        (h.Kills, h.Earned, h.Order, h.DeepestTogether, h.Fleeing) = (4, 321, HirelingOrder.Hold, 6, true);
        body.Hp -= 7;
        var kobold = Arena.AddMonster(game, "kobold", new Loc(18, 1));
        kobold.HatesAlly = true;

        var loaded = RoundTrip(game);
        var lh = loaded.Hireling!;
        Assert.Equal((h.Name, h.Look, h.Role, h.Level, h.Experience, h.Share, h.Fee, h.Kills, h.Earned, h.Order, h.DeepestTogether, h.Fleeing),
            (lh.Name, lh.Look, lh.Role, lh.Level, lh.Experience, lh.Share, lh.Fee, lh.Kills, lh.Earned, lh.Order, lh.DeepestTogether, lh.Fleeing));
        var lb = loaded.HirelingHere!;
        Assert.True(lb.IsAlly);
        Assert.Same(lb, lh.Body);
        Assert.Equal((body.Id, body.Position, body.Hp, body.MaxHp), (lb.Id, lb.Position, lb.Hp, lb.MaxHp));
        Assert.Equal("Bergil", lb.Race.Name);
        Assert.Equal(Hireling.ArmourFor(HirelingRole.Archer, 7), lb.Race.Armour);
        Assert.True(loaded.Level.Monsters.At(new Loc(18, 1))!.HatesAlly);
        Assert.Equal("Hirgon", Assert.Single(loaded.FallenCompanions).Name);

        // And the game goes on just as it would have.
        Hold(game, 10);
        Hold(loaded, 10);
        Assert.Equal((body.Position, body.Hp, game.GameTurn), (lb.Position, lb.Hp, loaded.GameTurn));
    }

    [Fact]
    public void A_game_with_a_hireling_plays_back_from_its_replay()
    {
        var (game, _, _) = AtTheInn(level: 10);
        game.Player.Gold = 100_000;
        game.Recorder = new ReplayRecorder(game);
        game.Execute(new EnterStoreCommand());
        game.Execute(new QuestChoiceCommand("inn:hire"));
        game.Execute(new QuestChoiceCommand("hire:take:0"));
        game.MarkDebugUsed();
        game.Execute(new DebugJumpCommand(3));
        game.Execute(new HirelingOrderCommand(HirelingOrder.Hold));
        for (var i = 0; i < 40 && !game.IsGameOver; i++) game.Execute(new WalkCommand(i % 2 == 0 ? Direction.East : Direction.West));
        game.Execute(new HirelingOrderCommand(HirelingOrder.Follow));
        for (var i = 0; i < 40 && !game.IsGameOver; i++) game.Execute(new HoldCommand());
        var file = game.Recorder.ToFile(game);

        var player = new ReplayPlayer(TestData.Game, file);
        while (player.Step()) { }
        Assert.True(player.Matches);
        Assert.Equal(game.HirelingHere?.Position, player.Game.HirelingHere?.Position);
        Assert.Equal(game.Hireling?.Experience, player.Game.Hireling?.Experience);
    }

    [Fact]
    public void Two_feats_are_for_hirelings()
    {
        var feats = Feats.All(TestData.Game);
        var old = feats.Single(f => f.Id == "hireling_20");
        var deep = feats.Single(f => f.Id == "hireling_deep");
        var (game, _, _) = WithHireling(level: 19);
        Assert.False(old.Earned(game));
        game.GainHirelingExperience(Hireling.ExperienceFor(20));
        Assert.True(old.Earned(game));

        Assert.False(deep.Earned(game));
        game.MarkDebugUsed();
        game.Execute(new DebugJumpCommand(20));
        Assert.False(deep.Earned(game));
        game.Execute(new DebugJumpCommand(0));
        Assert.True(deep.Earned(game));
        Assert.Contains("1000 ft", deep.Description);
    }
}
