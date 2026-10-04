using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>AVABand's quests: the Prancing Pony, the story quests, and the notice board (and AvaQuestTests.More.cs).</summary>
public partial class AvaQuestTests
{
    private sealed class Quester
    {
        public required GameSession Game { get; init; }
        public List<QuestPromptEvent> Prompts { get; } = [];
        public List<string> Said { get; } = [];
        public QuestPromptEvent Last => Prompts[^1];

        public void Choose(string id) => Game.Execute(new QuestChoiceCommand(id));

        /// <summary>Picks the choice whose label starts so (it must be offered).</summary>
        public void ChooseLabel(string label)
        {
            var choice = Last.Choices.First(c => c.Label.StartsWith(label));
            Choose(choice.Id);
        }

        public void Jump(int depth)
        {
            Game.Execute(new DebugJumpCommand(depth));
            Game.Player.Hp = Game.Player.MaxHp;
        }

        public void EnterShop(string shopId)
        {
            if (Game.Player.Depth != 0) Jump(0);
            Game.Player.Position = Game.Level.AllLocs().First(l => Game.Level.FeatureAt(l).Shop == shopId);
            Game.Execute(new EnterStoreCommand());
        }

        public void TakeQuest(string id)
        {
            EnterShop("inn");
            ChooseLabel("Ask who has work");
            Choose($"offer:{id}");
            Choose($"accept:{id}");
        }

        public Monster? Find(string race) => Game.Level.Monsters.All.FirstOrDefault(m => m.Race.Id == race);

        public Item? Carried(string kind) =>
            Game.Player.Inventory.Pack.Concat(Game.Player.Inventory.Equipped).FirstOrDefault(i => i.Kind.Id == kind);

        /// <summary>Stands next to a square and walks into (or onto) it.</summary>
        public void WalkInto(Loc target)
        {
            var from = Game.Level.AllLocs().First(l => l.ChebyshevTo(target) == 1 && Game.Level.IsPassable(l)
                                                      && Game.Level.Monsters.At(l) is null && Game.Level.FeatureAt(l).Id is "floor" or "open_door");
            Game.Player.Position = from;
            Game.Execute(new WalkCommand(DirectionExtensions.FromOffset(target.X - from.X, target.Y - from.Y)));
        }

        public Loc FeatureLoc(string id) => Game.Level.AllLocs().First(l => Game.Level.FeatureAt(l).Id == id);
    }

    private static Quester Start(int level = 20, ulong seed = 3)
    {
        var game = GameSession.NewGame(TestData.Game, seed, "warrior");
        game.MarkDebugUsed();
        if (level > 1) game.GainExperience(game.ExperienceForLevel(level - 1));
        game.Player.Hp = game.Player.MaxHp = 100_000;
        var q = new Quester { Game = game };
        game.Events.Subscribe<QuestPromptEvent>(q.Prompts.Add);
        game.Events.Subscribe<MessageEvent>(m => q.Said.Add(m.Text));
        return q;
    }

    [Fact]
    public void The_quests_data_is_AVABands_own_and_stays_out_of_random_generation()
    {
        var data = TestData.Game;
        Assert.Equal(["sealed_door", "burden", "broken_blade", "consecration", "letter", "thief", "apprentice", "cartographer", "warden",
                "heart", "watch", "stone", "chisel"],
            data.AvaQuests.Select(q => q.Id));
        Assert.Equal(13, data.Vaults.Count(v => v.Type == "AVABand quest"));
        Assert.All(data.Monsters.Where(m => m.Id is "durgash_the_keybearer" or "hathol_lord_of_the_barrow" or "the_shade_of_the_stair"
                                                   or "skorvath_the_cold_drake" or "grishnag_the_warchief" or "the_keeper_of_the_stone"),
            m => Assert.True(m.Has(MonsterFlags.Questor)));
        Assert.All(data.Objects.Where(k => k.Base is "quest" or "relic"), k => Assert.Equal(0, k.Commonness));
        Assert.Contains(data.Shops, s => s.Id == "inn");
    }

    [Theory]
    [InlineData("monsters.json", "ava_monsters.json")]
    [InlineData("objects.json", "ava_objects.json")]
    [InlineData("object_bases.json", "ava_object_bases.json")]
    [InlineData("terrain.json", "ava_terrain.json")]
    public void AVABands_own_data_never_takes_an_Angband_id(string angband, string ours)
    {
        // Later files replace earlier ones by id (so mods can), so a clash would quietly replace Angband's own.
        static HashSet<string> Ids(string file) =>
            System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Angband.Data.DataLoader.DefaultDataDirectory, file)))
                .RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToHashSet();
        Assert.Empty(Ids(ours).Intersect(Ids(angband)));
    }

    [Fact]
    public void The_inn_offers_what_your_level_allows()
    {
        var q = Start(level: 6);
        q.EnterShop("inn");
        Assert.Equal("The Prancing Pony", q.Last.Title);
        q.ChooseLabel("Ask who has work");
        Assert.Equal(["Ask about: The Sealed Door", "Ask about: The Cartographer", "Back"], q.Last.Choices.Select(c => c.Label));

        // Only answers the last question offered count.
        q.Choose("accept:consecration");
        Assert.Null(q.Game.AvaQuests.Get("consecration"));
    }

    [Fact]
    public void The_Sealed_Door_key_from_Durgash_opens_the_door_below()
    {
        var q = Start();
        q.TakeQuest("sealed_door");
        var state = q.Game.AvaQuests.Get("sealed_door")!;
        Assert.Equal("hunt", state.Stage);

        q.Jump(state.N("keydepth"));
        var durgash = q.Find("durgash_the_keybearer");
        Assert.NotNull(durgash);
        q.Game.DamageMonster(durgash, 100_000);
        Assert.NotNull(q.Carried("key_of_belegost"));
        Assert.Equal("key", state.Stage);

        q.Jump(state.N("doordepth"));
        var door = q.FeatureLoc("sealed_door");
        q.WalkInto(door);
        Assert.Equal("The sealed door", q.Last.Title);
        q.Choose("door:unlock");
        Assert.Equal("open_door", q.Game.Level.FeatureAt(door).Id);
        Assert.Null(q.Carried("key_of_belegost"));
        Assert.Equal("done", state.Stage);
    }

    [Fact]
    public void Without_the_key_the_door_stays_shut()
    {
        var q = Start();
        q.TakeQuest("sealed_door");
        var state = q.Game.AvaQuests.Get("sealed_door")!;
        state.Stage = "key"; // (as if the key had been lost somehow)
        q.Jump(state.N("doordepth"));
        q.WalkInto(q.FeatureLoc("sealed_door"));
        Assert.Contains("The door is sealed fast. It was made to open for one key alone.", q.Said);
        Assert.Equal("sealed_door", q.Game.Level.FeatureAt(q.FeatureLoc("sealed_door")).Id);
    }

    [Fact]
    public void The_Burden_is_found_worn_at_a_cost_and_unmade_at_the_forge()
    {
        var q = Start();
        var game = q.Game;
        Item? seal = null;
        for (var d = game.BurdenDepth; d < game.BurdenDepth + 10 && seal is null; d++)
        {
            q.Jump(d);
            seal = game.Level.Objects.All.Select(o => o.Item).FirstOrDefault(i => i.Kind.Id == "seal_of_angmar");
        }
        Assert.NotNull(seal);
        var at = game.Level.Objects.All.First(o => o.Item == seal).Loc;
        game.Player.Position = at;
        game.Execute(new PickupCommand(seal));
        var state = game.AvaQuests.Get("burden")!;
        Assert.Equal("carry", state.Stage);

        // Worn, it gives speed and won't come off; it can't be dropped either.
        var speed = game.Player.Speed;
        game.Execute(new WieldCommand(q.Carried("seal_of_angmar")!));
        Assert.True(game.Player.Speed > speed);
        Assert.True(q.Carried("seal_of_angmar")!.IsSticky);
        game.Execute(new DropCommand(q.Carried("seal_of_angmar")!));
        Assert.NotNull(q.Carried("seal_of_angmar"));

        q.Jump(state.N("forgedepth"));
        q.WalkInto(q.FeatureLoc("dwarven_forge"));
        Assert.Equal("The dwarven forge", q.Last.Title);
        q.Choose("forge:unmake");
        Assert.Null(q.Carried("seal_of_angmar"));
        Assert.Equal("done", state.Stage);
        Assert.Equal(speed, game.Player.Speed);
    }

    [Fact]
    public void The_Broken_Blade_three_shards_and_the_powers_you_know()
    {
        var q = Start();
        var game = q.Game;
        q.TakeQuest("broken_blade");
        var state = game.AvaQuests.Get("broken_blade")!;
        foreach (var i in new[] { 1, 2, 3 })
        {
            q.Jump(state.N($"depth{i}"));
            var shard = game.Level.Objects.All.First(o => o.Item.Kind.Id.StartsWith("shard_of_the"));
            game.Player.Position = shard.Loc;
            game.Execute(new PickupCommand(shard.Item));
        }
        Assert.Equal("reforge", state.Stage);

        game.Knowledge.LearnRune(RuneIds.Slay("TROLL"));
        game.Knowledge.LearnRune(RuneIds.Brand("cold"));
        game.Knowledge.LearnRune(RuneIds.Resist("fire"));
        q.EnterShop("weaponsmith");
        Assert.Equal("The Weapon Smiths", q.Last.Title);
        Assert.Contains(q.Last.Choices, c => c.Label == "Give it slay trolls");
        Assert.DoesNotContain(q.Last.Choices, c => c.Label.Contains("slay dragons")); // not a rune you know
        q.Choose("blade:pick:slay_troll");
        q.Choose("blade:make:slay_troll:brand_cold");
        var blade = q.Carried("reforged_blade")!;
        Assert.Contains(blade.Slays, s => s.MonsterFlag == "TROLL");
        Assert.Contains(blade.Brands, b => b.Element == "cold");
        Assert.True(blade.ToHit >= 10 && blade.ToDam >= 10);
        Assert.Null(q.Carried("shard_of_the_hilt"));
        Assert.Equal("done", state.Stage);
    }

    [Fact]
    public void Consecration_Hathol_rises_again_until_the_altar_is_hallowed()
    {
        var q = Start();
        var game = q.Game;
        q.TakeQuest("consecration");
        Assert.NotNull(q.Carried("water_of_ulmo"));
        var state = game.AvaQuests.Get("consecration")!;
        q.Jump(state.N("depth"));
        var hathol = q.Find("hathol_lord_of_the_barrow")!;
        Assert.False(game.DamageMonster(hathol, 100_000));
        Assert.Equal(hathol.MaxHp, hathol.Hp);
        Assert.Contains(game.Level.Monsters.All, m => m == hathol);

        q.WalkInto(q.FeatureLoc("quest_altar"));
        q.Choose("altar:hallow");
        Assert.Equal("slay", state.Stage);
        Assert.True(game.DamageMonster(hathol, 100_000));
        Assert.Equal("done", state.Stage);
    }

    [Fact]
    public void The_Letter_read_and_shown_to_the_Alchemist_changes_the_prices()
    {
        var q = Start();
        var game = q.Game;
        q.TakeQuest("letter");
        var letter = q.Carried("sealed_letter")!;
        game.Execute(new UseCommand(letter));
        Assert.Equal("The sealed letter", q.Last.Title);
        q.Choose("letter:read");
        Assert.NotNull(q.Carried("opened_letter"));
        Assert.Equal("opened", game.AvaQuests.Get("letter")!.Stage);

        var magic = game.Stores["magic"];
        var wand = magic.Stock.First();
        var before = game.BuyPrice(magic, wand);
        q.EnterShop("alchemist");
        q.Choose("letter:expose");
        Assert.Equal("exposed", game.AvaQuests.Get("letter")!.Stage);
        Assert.True(game.BuyPrice(magic, wand) > before);
        Assert.Equal(-20, game.AvaQuests.PriceAdjust["alchemist"]);
    }

    [Fact]
    public void The_Letter_delivered_sealed_to_the_hermit()
    {
        var q = Start();
        var game = q.Game;
        q.TakeQuest("letter");
        var state = game.AvaQuests.Get("letter")!;
        q.Jump(state.N("depth"));
        var gold = game.Player.Gold;
        q.WalkInto(q.FeatureLoc("hermitage"));
        q.Choose("hermit:give");
        Assert.Equal("delivered", state.Stage);
        Assert.True(game.Player.Gold > gold);
        Assert.Null(q.Carried("sealed_letter"));
    }

    [Fact]
    public void The_Thief_clues_point_to_one_suspect_and_only_the_thief_has_the_box()
    {
        var q = Start(seed: 8);
        var game = q.Game;
        q.TakeQuest("thief");
        var state = game.AvaQuests.Get("thief")!;
        var thief = TestData.Game.Monster(state.Texts["thief"])!;
        var clues = Enumerable.Range(1, 3).Select(i => state.Texts[$"clue{i}"]).ToList();
        Assert.Equal(3, clues.Distinct().Count());
        var suspects = new[] { "fengel_the_fence", "nar_the_red_handed", "skarn_quickfingers", "ilse_shadowcloak" };
        Assert.Contains(thief.Id, suspects);

        // Pages turn up on monsters; each tells a clue.
        for (var i = 0; i < 60 && state.N("pages") < 3; i++)
        {
            q.Jump(3 + i % 15);
            foreach (var carrier in game.Level.Monsters.All.Where(m => m.Carried.Any(c => c.Kind.Id == "journal_page")).ToList())
                game.DamageMonster(carrier, 100_000);
        }
        Assert.Equal(3, state.N("pages"));
        Assert.Contains("What the pages say:", game.QuestText("thief"));

        // The thief, when met, has the strongbox.
        for (var i = 0; i < 80 && state.Stage == "hunt"; i++)
        {
            q.Jump(Math.Max(1, thief.Depth - 2 + i % 5));
            if (q.Find(thief.Id) is { } found) game.DamageMonster(found, 1_000_000);
        }
        Assert.Equal("return", state.Stage);
        q.EnterShop("black_market");
        q.Choose("thief:return");
        Assert.Equal("done", state.Stage);
        Assert.Equal(-30, game.AvaQuests.PriceAdjust["black_market"]);
    }

    [Fact]
    public void The_notice_board_hunts_count_kills_and_pay_at_the_inn()
    {
        var q = Start(level: 5);
        var game = q.Game;
        q.Jump(3);
        q.EnterShop("inn"); // coming back up renews the board
        q.ChooseLabel("Read the notice board");
        Assert.Equal("The notice board", q.Last.Title);
        var take = q.Last.Choices.First(c => c.Id.StartsWith("board:take"));
        q.Choose(take.Id);
        var job = game.AvaQuests.Board.Single(j => j.Taken);

        if (job.Kind == "scout") q.Jump(job.Count);
        else if (job.Kind == "bounty")
        {
            var unique = game.Data.Monster(job.Target)!;
            q.Jump(Math.Max(1, unique.Depth));
            var spot = game.Level.AllLocs().First(l => game.Level.IsEmptyFloor(l) && l.DistanceTo(game.Player.Position) > 3);
            game.DamageMonster(new MonsterSpawner(game.Data).Place(game.Level, game.Rng, unique, spot), 1_000_000);
        }
        else if (job.Kind == "hunt")
        {
            var race = game.Data.Monster(job.Target)!;
            q.Jump(Math.Max(1, race.Depth));
            for (var i = 0; i < job.Count; i++)
            {
                var spot = game.Level.AllLocs().First(l => game.Level.IsEmptyFloor(l) && l.DistanceTo(game.Player.Position) > 3);
                var m = new MonsterSpawner(game.Data).Place(game.Level, game.Rng, race, spot);
                game.DamageMonster(m, 1_000_000);
            }
        }
        else
            for (var i = 0; i < job.Count; i++) game.Player.Inventory.Add(game.Objects.Create(job.Target));

        var gold = game.Player.Gold;
        q.EnterShop("inn");
        q.ChooseLabel("Collect your pay");
        Assert.Equal(gold + job.Reward, game.Player.Gold);
        Assert.DoesNotContain(job, game.AvaQuests.Board);
    }

    [Fact]
    public void Quest_items_stay_with_you()
    {
        var q = Start();
        var game = q.Game;
        q.TakeQuest("letter");
        var letter = q.Carried("sealed_letter")!;
        game.Execute(new DropCommand(letter));
        game.Execute(new ThrowCommand(letter));
        game.Execute(new IgnoreCommand(letter, IgnoreChoice.ThisItem));
        Assert.Same(letter, q.Carried("sealed_letter"));
        Assert.False(game.StoreWillBuy(game.Stores["general"], letter));
        Assert.False(game.IsIgnored(letter));
    }

    /// <summary>A quest's monster is always in its room, even when one of the level's own stood on its spot.</summary>
    [Fact]
    public void The_quests_monster_is_always_there_to_meet()
    {
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var q = Start(seed: seed);
            q.TakeQuest("sealed_door");
            q.Jump(q.Game.AvaQuests.Get("sealed_door")!.N("keydepth"));
            Assert.True(q.Find("durgash_the_keybearer") is not null, $"no Durgash on seed {seed}");
        }
    }

    [Fact]
    public void Quests_are_saved()
    {
        var q = Start();
        q.TakeQuest("sealed_door");
        var state = q.Game.AvaQuests.Get("sealed_door")!;
        q.Jump(state.N("keydepth"));
        q.Game.DamageMonster(q.Find("durgash_the_keybearer")!, 100_000);

        var stream = new MemoryStream();
        SaveGame.Save(q.Game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal("key", loaded.AvaQuests.Get("sealed_door")!.Stage);
        Assert.Equal(state.N("doordepth"), loaded.AvaQuests.Get("sealed_door")!.N("doordepth"));
        Assert.Equal("sealed_door", loaded.Player.Inventory.Pack.First(i => i.Kind.Id == "key_of_belegost").QuestTag);
    }

    [Fact]
    public void Without_the_birth_option_there_are_no_quests()
    {
        var game = GameSession.NewGame(TestData.Game, 3, "warrior");
        game.Options[OptionIds.AvaQuests] = false;
        game.MarkDebugUsed();
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        game.Player.Position = game.Level.AllLocs().First(l => game.Level.FeatureAt(l).Shop == "inn");
        game.Execute(new EnterStoreCommand());
        Assert.Contains(said, s => s.Contains("no work to be had"));
        game.Execute(new DebugJumpCommand(game.BurdenDepth));
        Assert.DoesNotContain(game.Level.Objects.All, o => o.Item.Kind.Id == "seal_of_angmar");
    }

    [Fact]
    public void A_quest_item_left_on_the_floor_turns_up_at_the_inn()
    {
        var q = Start();
        var game = q.Game;
        q.TakeQuest("sealed_door");
        var state = game.AvaQuests.Get("sealed_door")!;
        q.Jump(state.N("keydepth"));
        // A full pack: the key still goes in, to the quest satchel, which takes no slot.
        var key = game.Objects.Create("key_of_belegost");
        foreach (var kind in game.Data.Objects.Where(k => k.Base is "scroll" or "potion").Take(40))
            if (game.Player.Inventory.SlotsUsed < game.Player.Inventory.PackSize) game.Player.Inventory.Add(game.Objects.Create(kind.Id));
        Assert.Equal(game.Player.Inventory.PackSize, game.Player.Inventory.SlotsUsed);
        Assert.True(game.Player.Inventory.CanCarry(key));
        q.Game.DamageMonster(q.Find("durgash_the_keybearer")!, 100_000);
        var carried = q.Carried("key_of_belegost")!;
        Assert.Contains(carried, game.Player.Inventory.Satchel);
        Assert.Equal(game.Player.Inventory.PackSize, game.Player.Inventory.SlotsUsed);
        Assert.Same(carried, game.Player.Inventory.Pack[^1]);              // (the satchel comes last)

        // One left on a floor all the same (an older save's): it turns up at the Prancing Pony.
        game.Player.Inventory.Remove(carried, 1, () => game.Objects.NextSerial++);
        game.Level.Objects.Add(game.Player.Position, carried);
        q.Jump(1); // left behind
        q.EnterShop("inn");
        Assert.Contains(q.Said, s => s.Contains("with your name on it"));
        Assert.NotNull(q.Carried("key_of_belegost"));
        Assert.Equal("key", state.Stage);
    }
}
