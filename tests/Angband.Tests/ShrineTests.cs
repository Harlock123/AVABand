using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Persistence;
using Angband.Core.Randomness;
using Angband.Core.Records;
using Angband.Core.World;

namespace Angband.Tests;

/// <summary>AVABand's shrines: where they're found, what each choice at one does, and that each answers once.</summary>
public class ShrineTests
{
    private static GameData Data => TestData.Game;

    /// <summary>A wide lit room 500 ft down, the player at '@' (8,3).</summary>
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

    private sealed class Pilgrim(GameSession game)
    {
        public GameSession Game { get; } = game;
        public List<QuestPromptEvent> Prompts { get; } = [];
        public List<string> Said { get; } = [];
        public QuestPromptEvent Last => Prompts[^1];
        public void Choose(string id) => Game.Execute(new QuestChoiceCommand(id));
    }

    /// <summary>A character in the room with a shrine of the power just east of them; prompts and messages caught.</summary>
    private static Pilgrim AtAShrine(string power = "elbereth", ulong seed = 5, int depth = 10, string cls = "warrior")
    {
        var game = GameSession.NewGame(Data, seed, cls);
        Arena.GiveTestKit(game);
        var level = TestLevels.FromAscii(depth, out var eye, Room);
        foreach (var p in level.AllLocs()) level[p].Flags |= SquareFlags.Room | SquareFlags.Glow;
        game.UseLevel(level, eye);
        game.Player.Hp = game.Player.MaxHp = 5000;
        game.PutShrine(GameSession.ShrinePowers.Single(p => p.Id == power), ShrineSpot);
        var q = new Pilgrim(game);
        game.Events.Subscribe<QuestPromptEvent>(q.Prompts.Add);
        game.Events.Subscribe<MessageEvent>(m => q.Said.Add(m.Text));
        game.UpdateView();
        return q;
    }

    private static readonly Loc ShrineSpot = new(10, 3);

    private static void StepOn(Pilgrim q) => q.Game.Execute(new WalkCommand(Direction.East));

    private static void StepOnAndChoose(Pilgrim q, string choice)
    {
        StepOn(q);
        q.Choose(choice);
    }

    private static string FeatureAt(GameSession game, Loc at) => game.Level.FeatureAt(at).Id;

    private static GameSession RoundTrip(GameSession game)
    {
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        return SaveGame.Load(Data, stream);
    }

    // --- The option, and where shrines are found ------------------------------------------------------

    [Fact]
    public void Shrines_are_a_birth_option_on_by_default_but_never_in_the_daily_dungeon_the_weekly_challenge_or_the_tutorial()
    {
        var option = OptionCatalog.Find(OptionIds.AvaShrines)!;
        Assert.Equal(OptionKind.Birth, option.Kind);
        Assert.True(option.Default);
        var game = GameSession.NewGame(Data, 3);
        Assert.True(game.ShrinesOn);
        game.Player.DailyDate = "2026-10-26";
        Assert.False(game.ShrinesOn);
        game.Player.DailyDate = null;
        game.Player.WeeklyWeek = "2026-W43";
        Assert.False(game.ShrinesOn);
        Assert.False(Tutorial.Create(Data).ShrinesOn);
        var off = GameSession.NewGame(Data, 3, CharacterSpec.Default("human", "warrior") with { Options = new Dictionary<string, bool> { [OptionIds.AvaShrines] = false } });
        Assert.False(off.ShrinesOn);
    }

    /// <summary>The shrines on a level, by feature.</summary>
    private static List<string> ShrinesOn(Level level) =>
        [.. level.AllLocs().Select(p => level.FeatureAt(p).Id).Where(id => GameSession.ShrinePowerOf(id) is not null)];

    [Fact]
    public void About_one_new_level_in_six_or_seven_from_100ft_has_a_shrine_in_a_room()
    {
        var game = GameSession.NewGame(Data, 21);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        var levels = 0;
        var with = 0;
        var powers = new HashSet<string>();
        for (var i = 0; i < 300; i++)
        {
            game.Execute(new DebugJumpCommand(2 + i % 30));
            levels++;
            var found = ShrinesOn(game.Level);
            Assert.True(found.Count <= 1);
            if (found.Count == 0) continue;
            with++;
            powers.Add(found[0]);
            var at = game.Level.AllLocs().First(p => GameSession.ShrinePowerOf(game.Level.FeatureAt(p).Id) is not null);
            Assert.True(game.Level[at].Has(SquareFlags.Room));
            Assert.False(game.Level[at].Has(SquareFlags.Vault));
            Assert.True(game.Level.Depth >= GameSession.ShrinePowerOf(game.Level.FeatureAt(at).Id)!.MinDepth);
        }
        var rate = (double)with / levels;
        Assert.InRange(rate, 0.08, 0.25);
        Assert.True(powers.Count >= 4, string.Join(", ", powers));

        // Never at 50 ft, nor in town.
        for (var i = 0; i < 60; i++)
        {
            game.Execute(new DebugJumpCommand(i % 2));
            Assert.Empty(ShrinesOn(game.Level));
        }
    }

    [Fact]
    public void The_dark_altar_is_only_found_from_500ft()
    {
        for (var seed = 0UL; seed < 400; seed++)
        {
            Assert.NotEqual("morgoth", GameSession.PickShrinePower(new GameRandom(seed), 9).Id);
            Assert.NotEqual("mandos", GameSession.PickShrinePower(new GameRandom(seed), 4).Id);
        }
        Assert.Contains(Enumerable.Range(0, 400), s => GameSession.PickShrinePower(new GameRandom((ulong)s), 10).Id == "morgoth");
    }

    [Fact]
    public void Where_a_shrine_goes_leaves_the_games_own_dice_alone()
    {
        // The same game with and without shrines rolls the same levels: only the shrine square differs.
        GameSession Make(bool shrines) => GameSession.NewGame(Data, 44,
            CharacterSpec.Default("human", "warrior") with { Options = new Dictionary<string, bool> { [OptionIds.AvaShrines] = shrines } });
        var with = Make(true);
        var without = Make(false);
        var differed = 0;
        for (var i = 0; i < 40; i++)
        {
            with.Execute(new DebugJumpCommand(3 + i % 10));
            without.Execute(new DebugJumpCommand(3 + i % 10));
            Assert.Equal(without.Rng.NextULong(), with.Rng.NextULong());
            var diff = with.Level.AllLocs().Where(p => with.Level[p].Feature != without.Level[p].Feature).ToList();
            Assert.True(diff.Count <= 1);
            differed += diff.Count;
            Assert.Equal(without.Level.Monsters.All.Count(), with.Level.Monsters.All.Count());
        }
        Assert.True(differed > 0);
    }

    [Fact]
    public void A_shrine_first_seen_says_whose_it_is_and_looking_names_it()
    {
        var game = GameSession.NewGame(Data, 21);
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        var level = TestLevels.FromAscii(10, out var eye, Room);
        game.UseLevel(level, eye); // (the room is lit: its floor is seen at once, the shrine not yet raised)
        game.PutShrine(GameSession.ShrinePowers.Single(p => p.Id == "yavanna"), new Loc(20, 3));
        game.UpdateView();
        Assert.Contains(said, s => s.StartsWith("You see a shrine of Yavanna. A low altar", StringComparison.Ordinal));
        var count = said.Count(s => s.StartsWith("You see a shrine of Yavanna", StringComparison.Ordinal));
        game.UpdateView();
        game.Execute(new HoldCommand());
        Assert.Equal(count, said.Count(s => s.StartsWith("You see a shrine of Yavanna", StringComparison.Ordinal)));
        Assert.Equal("shrine of Yavanna", Data.Terrain["shrine_yavanna"].Name);
        Assert.Equal("dark altar of Morgoth", Data.Terrain["shrine_morgoth"].Name);
    }

    // --- Asking -------------------------------------------------------------------------------------

    [Fact]
    public void Stepping_onto_a_shrine_asks_what_youll_do_and_underscore_asks_again()
    {
        var q = AtAShrine("aule");
        StepOn(q);
        Assert.Equal("Shrine of Aulë", q.Last.Title);
        Assert.Contains("a piece of good work", q.Last.Text);
        Assert.Equal(["shrine:pray", "shrine:gold", "shrine:item", "shrine:desecrate", "none"], q.Last.Choices.Select(c => c.Id));
        q.Choose("none");
        Assert.Equal("shrine_aule", FeatureAt(q.Game, ShrineSpot)); // (leaving it uses nothing)
        var asked = q.Prompts.Count;
        q.Game.Execute(new EnterStoreCommand());
        Assert.Equal(asked + 1, q.Prompts.Count);
        Assert.Equal("Shrine of Aulë", q.Last.Title);
    }

    // --- Praying ------------------------------------------------------------------------------------

    [Fact]
    public void Praying_is_answered_once_and_the_shrine_goes_dark()
    {
        var q = AtAShrine("elbereth");
        StepOnAndChoose(q, "shrine:pray");
        Assert.Contains("You kneel and pray to Elbereth.", q.Said);
        Assert.Equal("shrine_spent", FeatureAt(q.Game, ShrineSpot));
        Assert.Equal(1, q.Game.Shrines.Used);
        Assert.Contains(q.Said, s => s.Contains("shrine fades, and it is dark"));

        // Answered once: the darkened altar asks nothing, and an old answer does nothing.
        var asked = q.Prompts.Count;
        q.Game.Execute(new WalkCommand(Direction.West));
        StepOn(q);
        Assert.Equal(asked, q.Prompts.Count);
        Assert.Contains("The altar is dark and cold. Its power is spent.", q.Said);
        q.Choose("shrine:pray");
        Assert.Equal(1, q.Game.Shrines.Used);
    }

    /// <summary>How many of so many prayers at a power's shrine were blessed (great or not), and how many cursed.</summary>
    private static (int Great, int Blessed, int Cursed) Prayers(string power, int tries)
    {
        int great = 0, blessed = 0, cursed = 0;
        for (var seed = 1UL; seed <= (ulong)tries; seed++)
        {
            var q = AtAShrine(power, seed);
            StepOnAndChoose(q, "shrine:pray");
            if (q.Game.History.Any(h => h.Text.StartsWith("Was blessed by", StringComparison.Ordinal))) great++;
            else if (q.Said.Any(s => s.EndsWith("is displeased.", StringComparison.Ordinal) || s.Contains("he is not kind"))) cursed++;
            else blessed++;
        }
        return (great, blessed, cursed);
    }

    [Fact]
    public void Prayers_are_mostly_blessed_but_the_dark_altar_curses_far_more_often()
    {
        var elbereth = Prayers("elbereth", 120);
        var morgoth = Prayers("morgoth", 120);
        Assert.True(elbereth.Blessed > elbereth.Cursed * 2, $"{elbereth}");
        Assert.InRange(elbereth.Cursed, 8, 45);
        Assert.True(morgoth.Cursed > elbereth.Cursed, $"{morgoth} vs {elbereth}");
        Assert.True(morgoth.Great > elbereth.Great, $"{morgoth} vs {elbereth}");
    }

    [Fact]
    public void A_great_answer_is_a_stat_gained_and_a_line_in_the_history()
    {
        for (var seed = 1UL; seed < 400; seed++)
        {
            var q = AtAShrine("morgoth", seed);
            var before = CharacterSpec.StatIds.Sum(s => q.Game.Player.NaturalStats.GetValueOrDefault(s));
            StepOnAndChoose(q, "shrine:pray");
            if (!q.Game.History.Any(h => h.Text == "Was blessed by Morgoth at 500 ft.")) continue;
            Assert.True(CharacterSpec.StatIds.Sum(s => q.Game.Player.NaturalStats.GetValueOrDefault(s)) > before);
            return;
        }
        Assert.Fail("No prayer was greatly answered.");
    }

    [Fact]
    public void A_curse_can_be_a_summons_of_the_dead()
    {
        for (var seed = 1UL; seed < 400; seed++)
        {
            var q = AtAShrine("mandos", seed);
            StepOnAndChoose(q, "shrine:pray");
            if (!q.Said.Contains("The dead answer your prayer instead!")) continue;
            Assert.Contains(q.Game.Level.Monsters.All, m => m.Race.Has("UNDEAD"));
            return;
        }
        Assert.Fail("No prayer called up the dead.");
    }

    // --- Offerings ----------------------------------------------------------------------------------

    [Fact]
    public void An_offering_of_gold_costs_more_deeper_and_twice_as_much_at_the_dark_altar()
    {
        var shallow = AtAShrine("yavanna", depth: 2).Game;
        var deep = AtAShrine("yavanna", depth: 30).Game;
        var yavanna = GameSession.ShrinePowerOf("shrine_yavanna")!;
        var morgoth = GameSession.ShrinePowerOf("shrine_morgoth")!;
        Assert.Equal(GameSession.ShrineGoldBase + 2 * GameSession.ShrineGoldPerLevel, shallow.ShrineOfferingGold(yavanna));
        Assert.True(deep.ShrineOfferingGold(yavanna) > shallow.ShrineOfferingGold(yavanna));
        Assert.Equal(2 * deep.ShrineOfferingGold(yavanna), deep.ShrineOfferingGold(morgoth));
    }

    [Fact]
    public void Yavannas_gift_for_gold_heals_and_cures_everything()
    {
        var q = AtAShrine("yavanna");
        var game = q.Game;
        var price = game.ShrineOfferingGold(GameSession.ShrinePowerOf("shrine_yavanna")!);
        game.Player.Gold = price - 1;
        StepOnAndChoose(q, "shrine:gold");
        Assert.Contains($"You haven't {price} gold to offer.", q.Said);
        Assert.Equal("shrine_yavanna", FeatureAt(game, ShrineSpot));

        game.Player.Gold = price + 5;
        game.Player.Hp = 10;
        game.IncreaseTimed("poisoned", 20);
        game.IncreaseTimed("confused", 20);
        q.Choose("shrine:gold");
        Assert.Equal(5, game.Player.Gold);
        Assert.Equal(game.Player.MaxHp, game.Player.Hp);
        Assert.False(game.Player.Timed.Has("poisoned"));
        Assert.False(game.Player.Timed.Has("confused"));
        Assert.Equal("shrine_spent", FeatureAt(game, ShrineSpot));
    }

    [Fact]
    public void Elbereths_gift_maps_the_level()
    {
        var q = AtAShrine("elbereth");
        q.Game.Player.Gold = 10_000;
        StepOnAndChoose(q, "shrine:gold");
        Assert.All(q.Game.Level.AllLocs(), p => Assert.True(q.Game.Known.IsKnown(p)));
    }

    [Fact]
    public void Aules_gift_is_a_good_thing_for_your_class_beside_the_altar()
    {
        var q = AtAShrine("aule", cls: "mage");
        q.Game.Player.Gold = 10_000;
        var before = q.Game.Level.Objects.All.Count();
        StepOnAndChoose(q, "shrine:gold");
        var gifts = q.Game.Level.Objects.All.Skip(before).Select(o => o.Item).ToList();
        var gift = Assert.Single(gifts);
        Assert.Contains(gift.Base.Id, new[] { "ring", "amulet", "wand", "staff", "rod", "cloak" });
        Assert.Contains("Something lies beside the altar.", q.Said);
    }

    [Fact]
    public void Mandos_gift_makes_known_every_rune_on_what_you_wear()
    {
        var q = AtAShrine("mandos");
        var game = q.Game;
        game.Player.Gold = 10_000;
        game.Player.Inventory.Add(game.Objects.Create("ring_of_resist_poison"));
        game.Execute(new WieldCommand(game.Player.Inventory.Pack.Last(i => i.Kind.Id == "ring_of_resist_poison")));
        Assert.Contains(game.Player.Inventory.Equipped, i => game.Knowledge.UnknownRunes(i).Any());
        StepOnAndChoose(q, "shrine:gold");
        Assert.All(game.Player.Inventory.Equipped, i => Assert.Empty(game.Knowledge.UnknownRunes(i)));
    }

    [Fact]
    public void An_item_offered_is_worth_what_it_earns_and_too_little_is_refused()
    {
        var q = AtAShrine("yavanna");
        var game = q.Game;
        var price = game.ShrineOfferingGold(GameSession.ShrinePowerOf("shrine_yavanna")!);
        StepOnAndChoose(q, "shrine:item");
        Assert.Equal("An offering to Yavanna", q.Last.Title);
        var cheap = game.Player.Inventory.Pack.First(i => i.Kind.Id == "flask_of_oil");
        Assert.Contains(q.Last.Choices, c => c.Id == $"shrine:give:{cheap.Serial}" && c.Label.EndsWith("not enough", StringComparison.Ordinal));
        q.Choose($"shrine:give:{cheap.Serial}");
        Assert.Contains(q.Said, s => s.Contains("it isn't enough"));
        Assert.Contains(cheap, game.Player.Inventory.Pack);
        Assert.Equal("shrine_yavanna", FeatureAt(game, ShrineSpot));

        // Something worth the price earns the gift, and is gone.
        var potion = game.Objects.Create("potion_of_strength");
        game.Knowledge.LearnKind(potion.Kind);
        game.Player.Inventory.Add(potion);
        var held = game.Player.Inventory.Pack.First(i => i.Kind.Id == "potion_of_strength");
        Assert.True(game.ShrineWorth(held) >= 3L * price);
        Assert.Equal(3, game.ShrineOfferingTier(GameSession.ShrinePowerOf("shrine_yavanna")!, game.ShrineWorth(held)));
        game.Player.Hp = 10;
        q.Choose("shrine:back");
        q.Choose("shrine:item");
        q.Choose($"shrine:give:{held.Serial}");
        Assert.DoesNotContain(game.Player.Inventory.Pack, i => i.Kind.Id == "potion_of_strength");
        Assert.Equal(game.Player.MaxHp, game.Player.Hp);
        Assert.Equal("shrine_spent", FeatureAt(game, ShrineSpot));
        Assert.Equal(1, game.Shrines.Used);
    }

    [Fact]
    public void Offering_tiers_go_by_worth()
    {
        var game = AtAShrine("aule").Game;
        var aule = GameSession.ShrinePowerOf("shrine_aule")!;
        var price = game.ShrineOfferingGold(aule);
        Assert.Equal(0, game.ShrineOfferingTier(aule, price / 2 - 1));
        Assert.Equal(1, game.ShrineOfferingTier(aule, (price + 1) / 2));
        Assert.Equal(2, game.ShrineOfferingTier(aule, price));
        Assert.Equal(3, game.ShrineOfferingTier(aule, 3L * price));
    }

    // --- Desecrating --------------------------------------------------------------------------------

    [Fact]
    public void Desecrating_pays_or_brings_wrath_and_living_through_it_is_the_blasphemers_feat()
    {
        var paid = false;
        var wrath = false;
        var blasphemer = Feats.All(Data).Single(f => f.Id == "blasphemer");
        for (var seed = 1UL; seed < 60 && !(paid && wrath); seed++)
        {
            var q = AtAShrine("morgoth", seed);
            var gold = q.Game.Player.Gold;
            StepOnAndChoose(q, "shrine:desecrate");
            Assert.Equal("shrine_spent", FeatureAt(q.Game, ShrineSpot));
            Assert.Equal(1, q.Game.Shrines.Used);
            Assert.Equal(1, q.Game.Shrines.Desecrated);
            Assert.True(blasphemer.Earned(q.Game));
            Assert.Contains(q.Game.History, h => h.Text.StartsWith("Desecrated a dark altar of Morgoth at 500 ft", StringComparison.Ordinal));
            if (q.Game.Player.Gold > gold) paid = true;
            if (q.Said.Contains("The altar's master is not mocked!")) wrath = true;
        }
        Assert.True(paid);
        Assert.True(wrath);
    }

    [Fact]
    public void The_pilgrims_feat_is_ten_shrines_used()
    {
        var q = AtAShrine("elbereth");
        var pilgrim = Feats.All(Data).Single(f => f.Id == "shrines_10");
        q.Game.Shrines.Used = 9;
        Assert.False(pilgrim.Earned(q.Game));
        StepOnAndChoose(q, "shrine:pray");
        Assert.True(pilgrim.Earned(q.Game));
    }

    // --- Kept -----------------------------------------------------------------------------------------

    [Fact]
    public void Shrines_used_and_the_darkened_altar_are_kept_in_the_save()
    {
        var q = AtAShrine("elbereth");
        StepOnAndChoose(q, "shrine:desecrate");
        var loaded = RoundTrip(q.Game);
        Assert.Equal(1, loaded.Shrines.Used);
        Assert.Equal(q.Game.Shrines.Desecrated, loaded.Shrines.Desecrated);
        Assert.Equal("shrine_spent", FeatureAt(loaded, ShrineSpot));

        // An unused shrine is kept too.
        var fresh = AtAShrine("mandos");
        Assert.Equal("shrine_mandos", FeatureAt(RoundTrip(fresh.Game), ShrineSpot));
    }

    [Fact]
    public void An_older_save_has_used_no_shrines()
    {
        foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Saves"), "*" + SaveGame.Extension))
        {
            var game = SaveGame.LoadFromFile(Data, path);
            Assert.Equal(0, game.Shrines.Used);
            Assert.Equal(0, game.Shrines.Desecrated);
        }
    }

    // --- Replays ------------------------------------------------------------------------------------

    [Fact]
    public void A_game_that_used_a_shrine_plays_back_exactly()
    {
        var q = AtAShrine("morgoth", seed: 8);
        var game = q.Game;
        game.Player.Gold = 50_000;
        game.Recorder = new ReplayRecorder(game);
        StepOn(q);
        q.Choose("shrine:item");
        q.Choose("shrine:back");
        q.Choose("shrine:pray");
        for (var i = 0; i < 20; i++) game.Execute(new HoldCommand());
        var file = game.Recorder.ToFile(game);
        Assert.Equal(ReplayFile.CurrentVersion, file.Version);
        var player = new ReplayPlayer(Data, file);
        Assert.True(player.Game.ShrinesOn);
        while (player.Step()) { }
        Assert.True(player.Matches);
        Assert.Equal("shrine_spent", FeatureAt(player.Game, ShrineSpot));
        Assert.Equal(game.History.Select(h => h.Text), player.Game.History.Select(h => h.Text));
        Assert.Equal(game.Player.Experience, player.Game.Player.Experience);
    }

    [Fact]
    public void A_replay_recorded_before_there_were_shrines_finds_none()
    {
        var game = GameSession.NewGame(Data, 9);
        var file = new ReplayRecorder(game).ToFile(game);
        file.Version = 3;
        var old = new ReplayPlayer(Data, file).Game;
        Assert.False(old.ShrinesOn);
        for (var i = 0; i < 60; i++)
        {
            old.Execute(new DebugJumpCommand(2 + i % 20));
            Assert.Empty(ShrinesOn(old.Level));
        }
        file.Version = ReplayFile.CurrentVersion;
        Assert.True(new ReplayPlayer(Data, file).Game.ShrinesOn);
    }
}
