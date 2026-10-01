using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>
/// AVABand's added loot (ava_objects.json, ava_egos.json, ava_artifacts.json, ava_curses.json):
/// weapon oils, egos for bracers and bags, its own artifacts, curses for the new slots, gem sets,
/// monster trophies the Armoury works, richer lairs and the new lights and helms.
/// </summary>
public class AvaLootTests
{
    private static GameSession NewGame(string cls = "warrior", ulong seed = 7) => GameSession.NewGame(TestData.Game, seed, cls);

    private static Item Carry(GameSession game, string kind, int number = 1)
    {
        var item = game.Objects.Create(kind, number);
        game.Knowledge.LearnKind(item.Kind);
        return game.Player.Inventory.Add(item)!;
    }

    // --- Weapon oils ---------------------------------------------------------------------------

    [Theory]
    [InlineData("oil_of_venom", "att_pois")]
    [InlineData("oil_of_burning", "att_fire")]
    [InlineData("oil_of_frost", "att_cold")]
    [InlineData("oil_of_storms", "att_elec")]
    [InlineData("oil_of_corrosion", "att_acid")]
    [InlineData("holy_water", "att_evil")]
    public void A_weapon_oil_brands_your_blows_for_a_while(string kind, string timed)
    {
        var game = NewGame();
        var oil = Carry(game, kind, 2);
        Assert.Equal("oil", oil.Base.Id);
        Assert.True(game.Execute(new UseCommand(oil)));
        Assert.True(game.Player.Timed.Has(timed));
        Assert.Equal(1, oil.Number);
        Assert.InRange(game.Player.Timed[timed], 20, 40);
        Assert.Contains("When applied, it makes your blows", ObjectInfo.DescribeItem(game, oil));
    }

    [Theory]
    [InlineData("lesser_oil_of_burning", 10, 20, 2)]
    [InlineData("oil_of_burning", 20, 40, 3)]
    [InlineData("greater_oil_of_burning", 40, 80, 4)]
    public void An_oils_grade_sets_how_long_and_how_hard_it_burns(string kind, int least, int most, int multiplier)
    {
        var game = NewGame();
        var oil = Carry(game, kind);
        Assert.Contains($"(x{multiplier} against what doesn't resist it)", ObjectInfo.DescribeItem(game, oil));
        game.Execute(new UseCommand(oil));
        Assert.InRange(game.Player.Timed["att_fire"], least, most);
        var brand = Assert.Single(game.TemporaryAttackModifiers().Brands);
        Assert.Equal("fire", brand.Element);
        Assert.Equal(multiplier, brand.Multiplier);
    }

    [Theory]
    [InlineData("lesser_holy_water", 2)]
    [InlineData("holy_water", 2)]
    [InlineData("greater_holy_water", 3)]
    public void Holy_waters_grade_strengthens_its_smiting(string kind, int multiplier)
    {
        var game = NewGame();
        game.Execute(new UseCommand(Carry(game, kind)));
        Assert.Equal(multiplier, Assert.Single(game.TemporaryAttackModifiers().Slays).Multiplier);
    }

    [Fact]
    public void An_oils_grade_lasts_as_long_as_its_brand_and_is_saved()
    {
        var game = NewGame();
        game.Execute(new UseCommand(Carry(game, "greater_oil_of_venom")));
        Assert.Equal(1, game.Player.OilGrades["att_pois"]);

        using var stream = new MemoryStream();
        Angband.Core.Persistence.SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = Angband.Core.Persistence.SaveGame.Load(TestData.Game, stream);
        Assert.Equal(4, Assert.Single(loaded.TemporaryAttackModifiers().Brands).Multiplier);

        // Worn off, the grade goes with it.
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        for (var i = 0; i < 100 && game.Player.Timed.Has("att_pois"); i++) game.Execute(new HoldCommand());
        Assert.False(game.Player.Timed.Has("att_pois"));
        Assert.Empty(game.Player.OilGrades);
    }

    [Fact]
    public void Venom_oil_uses_angbands_own_poison_brand()
    {
        var game = NewGame();
        game.Execute(new UseCommand(Carry(game, "oil_of_venom")));
        var brands = game.Data.Timed("att_pois")!.Brand!;
        Assert.Equal("pois", brands.Element);
        Assert.Equal(3, brands.Multiplier);
    }

    [Fact]
    public void The_alchemist_stocks_and_buys_oils()
    {
        var alchemist = TestData.Game.Stores.Single(s => s.Id == "alchemist");
        Assert.Contains("lesser_oil_of_venom", alchemist.Stocked);
        Assert.Contains("lesser_oil_of_burning", alchemist.Stocked);
        Assert.Contains("holy_water", alchemist.Stocked);
        Assert.Contains("oil", alchemist.AvabandBuys);
    }

    // --- Egos for bracers and bags ---------------------------------------------------------------

    private static Item WithEgo(GameSession game, string kind, string ego)
    {
        var item = game.Objects.Create(kind);
        ObjectFactory.ApplyEgo(game.Rng, item, game.Data.Egos.Single(e => e.Id == ego), 30);
        game.Knowledge.LearnKind(item.Kind);
        foreach (var rune in item.Runes()) game.Knowledge.LearnRune(rune);
        return game.Player.Inventory.Add(item)!;
    }

    [Theory]
    [InlineData("archer", "bracers")]
    [InlineData("warding", "bracers")]
    [InlineData("duelist", "bracers")]
    [InlineData("celebrimbor", "bracers")]
    [InlineData("fireproof", "bag")]
    [InlineData("insulated", "bag")]
    public void AVABands_egos_fit_their_bases(string ego, string baseId)
    {
        var def = TestData.Game.Egos.Single(e => e.Id == ego);
        Assert.Equal([baseId], def.Bases);
        var kind = TestData.Game.Objects.First(k => k.Base == baseId && k.Id != "bag_of_devouring");
        Assert.True(def.Fits(kind));
    }

    [Fact]
    public void Celebrimbors_bracers_have_one_more_socket()
    {
        var game = NewGame();
        var plain = game.Objects.Create("iron_bracers");
        var bracers = WithEgo(game, "iron_bracers", "celebrimbor");
        Assert.Equal(plain.Sockets + 1, bracers.Sockets);
        foreach (var gem in new[] { "ruby", "sapphire", "topaz" })
            Assert.True(game.Execute(new SetGemCommand(bracers, Carry(game, gem))));
        Assert.Equal(3, bracers.Gems.Count);
        Assert.Contains("of Celebrimbor's", game.Describe(bracers).Replace("(Celebrimbor's)", "of Celebrimbor's"));
    }

    [Fact]
    public void Bracers_of_the_Duelist_ease_the_off_hand()
    {
        var game = NewGame();
        Assert.Equal(GameSession.OffHandToHitPenalty, game.OffHandPenalty);
        var bracers = WithEgo(game, "leather_bracers", "duelist");
        game.Execute(new WieldCommand(bracers));
        Assert.Equal(GameSession.OffHandToHitPenalty - 10, game.OffHandPenalty);
        Assert.Contains("off hand's blow 10 easier to land", ObjectInfo.DescribeItem(game, bracers));
    }

    [Fact]
    public void Bracers_of_the_Archer_and_of_Warding_roll_their_bonuses()
    {
        var game = NewGame();
        var archer = WithEgo(game, "leather_bracers", "archer");
        Assert.InRange(archer.Modifier(ItemModifiers.Dexterity), 1, 2);
        Assert.True(archer.ToHit >= 3);
        var warding = WithEgo(game, "leather_bracers", "warding");
        Assert.True(warding.ToAc >= 4);
        Assert.Single(warding.Resists, r => r is "acid" or "elec" or "fire" or "cold");
    }

    [Theory]
    [InlineData("fireproof", "fire", "phase_door")]
    [InlineData("insulated", "cold", "cure_light_wounds")]
    public void A_guarding_bag_keeps_the_pack_safe_from_its_element(string ego, string element, string kind)
    {
        // Without the bag, a sure hit destroys them all; with it, none.
        var bare = NewGame();
        var lost = Carry(bare, kind, 5);
        Assert.True(bare.InventoryDamage(element, 10_000) >= 5);
        Assert.DoesNotContain(lost, bare.Player.Inventory.Pack);

        var game = NewGame();
        var stack = Carry(game, kind, 5);
        var bag = WithEgo(game, "sack_of_holding", ego);
        Assert.Equal(0, game.InventoryDamage(element, 10_000));
        Assert.Equal(5, stack.Number);
        Assert.Contains("keeps what's in your pack safe from", ObjectInfo.DescribeItem(game, bag));
    }

    // --- AVABand's artifacts ---------------------------------------------------------------------

    private static Item CarryArtifact(GameSession game, string id)
    {
        var item = game.Objects.CreateArtifact(game.Data.Artifacts.Single(a => a.Id == id));
        game.Knowledge.LearnKind(item.Kind);
        foreach (var rune in item.Runes()) game.Knowledge.LearnRune(rune);
        return game.Player.Inventory.Add(item)!;
    }

    [Fact]
    public void AVABands_artifacts_load_beside_Angbands()
    {
        var ids = new[]
        {
            "bracers_of_narvi", "bracers_of_beorn", "bracers_of_the_hornburg", "bracers_of_haldir", "pack_of_bilbo", "thorn",
            "main_gauche_of_dol_amroth", "mattock_of_narvi", "boots_of_strider",
        };
        foreach (var id in ids) Assert.Contains(TestData.Game.Artifacts, a => a.Id == id);
        Assert.Contains(TestData.Game.Artifacts, a => a.Id == "sting"); // Angband's still there
        // Every one has a description, and a name no Angband artifact has.
        var angband = TestData.Game.Artifacts.Where(a => !ids.Contains(a.Id)).Select(a => a.Name).ToHashSet();
        foreach (var id in ids)
        {
            var art = TestData.Game.Artifacts.Single(a => a.Id == id);
            Assert.NotEmpty(art.Description);
            Assert.DoesNotContain(art.Name, angband);
        }
    }

    [Fact]
    public void The_boots_of_Strider_let_you_carry_more()
    {
        var game = NewGame();
        var before = game.Player.WeightLimit;
        var boots = CarryArtifact(game, "boots_of_strider");
        game.Execute(new WieldCommand(boots));
        Assert.True(game.Player.WeightLimit > before);
        Assert.Contains("carry 30% more", ObjectInfo.DescribeItem(game, boots));
    }

    [Fact]
    public void Bilbos_pack_keeps_fire_and_frost_from_the_pack()
    {
        var game = NewGame();
        var scrolls = Carry(game, "phase_door", 5);
        var potions = Carry(game, "cure_light_wounds", 5);
        var pack = CarryArtifact(game, "pack_of_bilbo");
        Assert.Equal(0, game.InventoryDamage("fire", 10_000));
        Assert.Equal(0, game.InventoryDamage("cold", 10_000));
        Assert.Equal(5, scrolls.Number);
        Assert.Equal(5, potions.Number);
        Assert.True(pack.Kind.CarryPercent > 0); // still a bag of holding
    }

    [Fact]
    public void The_Mattock_of_Narvi_finds_more_gold()
    {
        var game = NewGame();
        CarryArtifact(game, "mattock_of_narvi");
        game.RecalculateBonuses();
        Assert.Equal("mattock", game.BestDigger!.Kind.Id);
        var gold = game.Objects.Create(game.Data.Objects.First(k => k.Base == "gold").Id);
        gold.GoldValue = 100;
        game.VeinGold(gold);
        Assert.Equal(150, gold.GoldValue);
    }

    // --- AVABand's curses for its slots ----------------------------------------------------------

    private static Item Cursed(GameSession game, string kind, string curse)
    {
        var item = game.Objects.Create(kind);
        item.Curses.Add(curse);
        item.CursePowers[curse] = 20;
        game.Knowledge.LearnKind(item.Kind);
        return game.Player.Inventory.Add(item)!;
    }

    private static GameSession Quiet()
    {
        var game = NewGame();
        game.Player.MaxHp = game.Player.Hp = 100_000; // (whatever lives here, this is about the curses)
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        return game;
    }

    [Fact]
    public void A_leaden_thing_weighs_twice_as_much_and_is_soon_known()
    {
        var game = Quiet();
        var plain = game.Objects.Create("iron_bracers");
        var leaden = Cursed(game, "iron_bracers", "leaden");
        Assert.Equal(plain.Weight * 2, leaden.Weight);
        Assert.False(game.Knowledge.KnowsRune(RuneIds.Curse("leaden")));
        game.Execute(new HoldCommand());
        Assert.True(game.Knowledge.KnowsRune(RuneIds.Curse("leaden")));
    }

    [Fact]
    public void A_greedy_bag_eats_gold_now_and_then()
    {
        var game = Quiet();
        Cursed(game, "sack_of_holding", "greedy");
        game.Player.Gold = 10_000;
        for (var i = 0; i < 30 * GameSession.GreedChance && game.Player.Gold == 10_000; i++) game.Execute(new HoldCommand());
        Assert.Equal(10_000 - 10_000 / 20, game.Player.Gold);
        Assert.True(game.Knowledge.KnowsRune(RuneIds.Curse("greedy")));
    }

    [Fact]
    public void Loose_settings_drop_a_stone_at_your_feet()
    {
        var game = Quiet();
        var bracers = Cursed(game, "iron_bracers", "loose_settings");
        game.Execute(new WieldCommand(bracers));
        game.Execute(new SetGemCommand(bracers, Carry(game, "ruby")));
        Assert.Contains("fire", bracers.Resists);
        for (var i = 0; i < 30 * GameSession.LooseSettingsChance && bracers.Gems.Count > 0; i++) game.Execute(new HoldCommand());
        Assert.Empty(bracers.Gems);
        Assert.DoesNotContain("fire", bracers.Resists); // what the stone gave went with it
        Assert.Contains(game.Level.Objects.All, o => o.Item.Kind.Id == "ruby" && o.Loc.DistanceTo(game.Player.Position) <= 2);
        Assert.True(game.Knowledge.KnowsRune(RuneIds.Curse("loose_settings")));
    }

    [Fact]
    public void A_cursed_bag_gets_only_a_bags_curse()
    {
        var game = NewGame();
        for (var i = 0; i < 40; i++)
        {
            var bag = game.Objects.Create("sack_of_holding");
            game.Objects.ApplyCurse(game.Rng, bag, 20);
            Assert.All(bag.Curses, c => Assert.Contains(c, new[] { "greedy", "leaden", "devouring" }));
        }
    }

    // --- Gem sets --------------------------------------------------------------------------------

    private static Item WornBracers(GameSession game, params string[] gems)
    {
        var bracers = Carry(game, "mithril_bracers");
        game.Execute(new WieldCommand(bracers));
        foreach (var gem in gems) game.Execute(new SetGemCommand(bracers, Carry(game, gem)));
        return bracers;
    }

    [Fact]
    public void Three_rubies_of_any_quality_make_you_immune_to_fire()
    {
        var game = NewGame();
        var messages = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));
        var bracers = WornBracers(game, "chipped_ruby", "flawed_ruby");
        Assert.Equal(1, game.Player.Resists.GetValueOrDefault("fire"));
        game.Execute(new SetGemCommand(bracers, Carry(game, "ruby")));
        Assert.Equal(3, game.Player.Resists["fire"]);
        Assert.Contains(messages, m => m.Contains("The stones answer one another — three rubies: immunity to fire"));
        Assert.Contains("its stones make a set — three rubies: immunity to fire", ObjectInfo.DescribeItem(game, bracers));

        // Off the arm, the set counts for nothing.
        game.Execute(new TakeOffCommand(bracers));
        Assert.Equal(0, game.Player.Resists.GetValueOrDefault("fire"));
    }

    [Fact]
    public void A_ruby_a_sapphire_and_a_topaz_resist_acid_too()
    {
        var game = NewGame();
        WornBracers(game, "chipped_ruby", "chipped_sapphire", "chipped_topaz");
        foreach (var element in new[] { "fire", "cold", "elec", "acid" })
            Assert.Equal(1, game.Player.Resists.GetValueOrDefault(element));
    }

    [Fact]
    public void Three_emeralds_add_constitution_and_three_diamonds_armour()
    {
        var game = NewGame();
        var before = game.Player.Stats["con"];
        WornBracers(game, "chipped_emerald", "chipped_emerald", "chipped_emerald");
        Assert.Equal(before + 3 + 2, game.Player.Stats["con"]); // +1 each, and +2 for the set

        var diamonds = NewGame();
        var bracers = WornBracers(diamonds, "chipped_diamond", "chipped_diamond");
        var armour = diamonds.Player.Armour;
        diamonds.Execute(new SetGemCommand(bracers, Carry(diamonds, "chipped_diamond")));
        Assert.Equal(armour + 4 + 10, diamonds.Player.Armour); // the stone's own 4, and 10 for the set
    }

    // --- Lamps, a helm and gloves for delvers ----------------------------------------------------

    [Fact]
    public void A_dwarven_lamp_lights_needs_no_oil_and_helps_you_dig()
    {
        var game = NewGame();
        var dig = game.DiggingSkill;
        var lamp = Carry(game, "dwarven_lamp");
        game.Execute(new WieldCommand(lamp));
        Assert.Equal(lamp, game.Player.Inventory.InSlot(EquipSlot.Light));
        Assert.Equal(2, game.Player.LightRadius);
        Assert.False(lamp.UsesFuel);
        Assert.True(game.DiggingSkill > dig);
        for (var i = 0; i < 50; i++) game.Execute(new HoldCommand());
        Assert.Equal(2, game.Player.LightRadius); // still burning, with nothing to burn
    }

    [Fact]
    public void An_elven_lantern_is_quiet_and_a_miners_helm_adds_light()
    {
        var game = NewGame();
        var stealth = game.Player.Stealth;
        game.Execute(new WieldCommand(Carry(game, "elven_lantern")));
        Assert.Equal(stealth + 1, game.Player.Stealth);
        game.Execute(new WieldCommand(Carry(game, "miners_helm")));
        Assert.Equal(3, game.Player.LightRadius);
        var dig = game.DiggingSkill;
        game.Execute(new WieldCommand(Carry(game, "delvers_gloves")));
        Assert.True(game.DiggingSkill > dig);
        Assert.Contains("miners_helm", TestData.Game.Stores.Single(st => st.Id == "armoury").Stocked);
    }

    /// <summary>Angband's Ring of Digging counts, as every worn thing's tunnelling does in 4.2.5 (it didn't, before).</summary>
    [Fact]
    public void A_ring_of_digging_helps_you_dig()
    {
        var game = NewGame();
        var dig = game.DiggingSkill;
        var ring = Carry(game, "ring_of_digging");
        game.Execute(new WieldCommand(ring));
        Assert.Equal(dig + 20 * ring.Modifier(ItemModifiers.Tunnel), game.DiggingSkill);
    }

    // --- Hoards in lairs and gauntlets ---------------------------------------------------------

    [Theory]
    [InlineData(11UL), InlineData(12UL), InlineData(13UL)]
    public void A_lair_has_a_hoard_at_its_heart(ulong seed)
    {
        var generator = new Angband.Core.Generation.DungeonGenerator(TestData.Game);
        var level = generator.Generate(new Angband.Core.Generation.LevelRequest(30, seed, ProfileId: "lair")).Level;
        var great = level.SpawnHints.Where(h => h.Kind == Angband.Core.World.SpawnKind.GreatObject && h.DepthBonus == 10).ToList();
        var heart = Assert.Single(great).Loc;
        var gold = level.SpawnHints.Where(h => h.Kind == Angband.Core.World.SpawnKind.Gold && h.DepthBonus == 10).ToList();
        Assert.Equal(4, gold.Count);
        Assert.All(gold, g => Assert.True(g.Loc.DistanceTo(heart) <= 6, $"gold at {g.Loc}, heart at {heart}"));
    }

    [Fact]
    public void Running_the_gauntlet_pays_on_the_far_side()
    {
        var generator = new Angband.Core.Generation.DungeonGenerator(TestData.Game);
        var level = generator.Generate(new Angband.Core.Generation.LevelRequest(40, 11, Angband.Core.Generation.StairArrival.Descended,
            ProfileId: "gauntlet")).Level;
        var reward = Assert.Single(level.SpawnHints, h => h.Kind == Angband.Core.World.SpawnKind.GoodObject && h.DepthBonus == 5);
        // Come down the stairs, you arrive on the left (the up stairs' side): the reward is on the right.
        var downs = level.AllLocs().Where(p => level.Has(p, Angband.Core.Definitions.TerrainFlags.DownStair)).ToList();
        Assert.True(reward.Loc.X > level.Width / 2);
        Assert.All(downs, d => Assert.True(d.X > level.Width / 2));
    }

    // --- Monster trophies, and the Armoury's work ----------------------------------------------

    [Theory]
    [InlineData("mature_red_dragon", "red_dragon_scale")]
    [InlineData("baby_multi_hued_dragon", "black_dragon_scale,blue_dragon_scale,green_dragon_scale,red_dragon_scale,white_dragon_scale")]
    [InlineData("stone_troll", "troll_hide")]
    [InlineData("giant_tarantula", "spider_silk")]
    [InlineData("cave_orc", "")]
    public void Dragons_trolls_and_great_spiders_leave_trophies(string race, string trophies)
    {
        var (kinds, _) = GameSession.TrophiesOf(TestData.Game.Monster(race)!);
        Assert.Equal(trophies, string.Join(",", kinds.Order()));
        foreach (var kind in kinds) Assert.Equal(0, TestData.Game.Object(kind)!.Commonness); // only ever taken, never found
    }

    [Fact]
    public void A_unique_dragon_always_leaves_its_scale()
    {
        var game = NewGame();
        game.Execute(new DebugJumpCommand(50));
        var at = game.Level.AllLocs().First(l => game.Level.IsEmptyFloor(l) && l.DistanceTo(game.Player.Position) > 5);
        var smaug = new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster("smaug_the_golden")!, at);
        game.DamageMonster(smaug, 1_000_000);
        Assert.Contains(game.Level.Objects.All, o => o.Item.Kind.Id == "red_dragon_scale" && o.Loc.DistanceTo(at) <= 3);
    }

    [Fact]
    public void The_armourer_works_a_scale_into_your_armour_for_gold()
    {
        var game = NewGame();
        var prompts = new List<QuestPromptEvent>();
        game.Events.Subscribe<QuestPromptEvent>(prompts.Add);
        var armour = game.Player.Inventory.InSlot(EquipSlot.Body)!;
        var scale = Carry(game, "red_dragon_scale", 2);
        game.Player.Gold = 2000;
        game.Player.Position = game.Level.AllLocs().Single(l => game.Level.FeatureAt(l).Shop == "armoury");
        game.Execute(new EnterStoreCommand());
        Assert.Equal("Work a trophy", game.StoreServiceLabel);

        game.Execute(new StoreServicesCommand());
        game.Execute(new QuestChoiceCommand(prompts.Last().Choices.First(c => c.Id.StartsWith("trophy:pick:")).Id));
        var into = prompts.Last().Choices.First(c => c.Id.EndsWith(":" + armour.Serial));
        Assert.Contains("resist fire", into.Label);
        game.Execute(new QuestChoiceCommand(into.Id));
        Assert.Contains("fire", armour.Resists);
        Assert.Equal("red_dragon_scale", armour.Trophy);
        Assert.Equal(2000 - 600, game.Player.Gold);
        Assert.Equal(1, scale.Number);
        Assert.Contains("the armourer worked red dragon scale into it", ObjectInfo.DescribeItem(game, armour));
        Assert.Equal(1, game.Player.Resists.GetValueOrDefault("fire"));

        // One to a piece: the other scale can't go into it again.
        Assert.Null(game.StoreServiceLabel); // (the other scale has nowhere left to go)

        // And it's kept.
        using var stream = new MemoryStream();
        Angband.Core.Persistence.SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = Angband.Core.Persistence.SaveGame.Load(TestData.Game, stream);
        Assert.Equal("red_dragon_scale", loaded.Player.Inventory.InSlot(EquipSlot.Body)!.Trophy);
    }

    [Fact]
    public void A_troll_hide_lends_regeneration_and_an_artifact_takes_no_trophy()
    {
        var game = NewGame();
        var prompts = new List<QuestPromptEvent>();
        game.Events.Subscribe<QuestPromptEvent>(prompts.Add);
        var hide = Carry(game, "troll_hide");
        var boots = CarryArtifact(game, "boots_of_strider");
        game.Player.Gold = 5000;
        game.Player.Position = game.Level.AllLocs().Single(l => game.Level.FeatureAt(l).Shop == "armoury");
        game.Execute(new EnterStoreCommand());
        game.Execute(new StoreServicesCommand());
        game.Execute(new QuestChoiceCommand(prompts.Last().Choices.First(c => c.Id == "trophy:pick:" + hide.Serial).Id));
        Assert.DoesNotContain(prompts.Last().Choices, c => c.Id.EndsWith(":" + boots.Serial));
        var armour = game.Player.Inventory.InSlot(EquipSlot.Body)!;
        game.Execute(new QuestChoiceCommand(prompts.Last().Choices.First(c => c.Id.EndsWith(":" + armour.Serial)).Id));
        Assert.Contains(ItemFlags.Regen, armour.Flags);
        Assert.Contains(ItemFlags.Regen, game.Player.GearFlags);
    }
}
