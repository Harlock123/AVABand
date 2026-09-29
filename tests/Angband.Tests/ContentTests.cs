using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Generation;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Persistence;
using Angband.Core.World;

namespace Angband.Tests;

/// <summary>The imported Angband content and the mechanics it relies on.</summary>
public class ContentTests
{
    private static readonly string[] Room =
    [
        "#################",
        "#,,,,,,,,,,,,,,,#",
        "#,,@,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,#",
        "#################",
    ];

    private static GameSession Game(ulong seed = 3)
    {
        var game = Arena.Create(seed, Room);
        TestGames.ClearMonsters(game);
        game.Player.SkillDevice = 500; // devices always work
        return game;
    }

    private static Item Carry(GameSession game, string kindId, int charges = 0)
    {
        var item = game.Objects.Create(kindId);
        item.Charges = charges;
        game.Knowledge.LearnKind(item.Kind);
        return game.Player.Inventory.Add(item)!;
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    // --- Data --------------------------------------------------------------------------------

    [Fact]
    public void Angband_content_is_imported()
    {
        var data = TestData.Game;
        Assert.True(data.Monsters.Count >= 600);
        Assert.True(data.Objects.Count >= 340);
        Assert.True(data.Egos.Count >= 80);
        Assert.True(data.Artifacts.Count >= 120);
        Assert.Equal(96, data.Vaults.Count(v => v.Type is not ("Interesting room" or "AVABand quest")));
        Assert.Contains(data.Monsters, m => m.Name == "Morgoth, Lord of Darkness");
        Assert.Contains(data.Artifacts, a => a.Name == "'Ringil'");
        Assert.Contains(data.Objects, k => k.Id == "wand_of_magic_missile");
    }

    [Fact]
    public void Stores_stock_the_new_items()
    {
        var alchemist = TestData.Game.Stores.Single(s => s.Id == "alchemist");
        Assert.Contains("potion_of_cure_critical_wounds", alchemist.Always.Concat(alchemist.Normal));
        var magic = TestData.Game.Stores.Single(s => s.Id == "magic");
        Assert.Contains(magic.Always.Concat(magic.Normal), id => id.StartsWith("wand_of_"));
    }

    [Fact]
    public void Kind_rolls_give_rings_their_bonuses()
    {
        var game = Game();
        var ring = game.Objects.Create("ring_of_strength");
        ObjectFactory.ApplyKindRolls(game.Rng, ring, 40);
        Assert.InRange(ring.Modifier("str"), 1, 6);
        Assert.Contains("resist:sust_str", ring.Runes());

        var wand = game.Objects.Create("wand_of_magic_missile");
        game.Objects.ApplyMagic(game.Rng, wand, 5);
        Assert.InRange(wand.Charges, 7, 16);
    }

    [Theory]
    [InlineData("5+d5M10", 6, 25)]
    [InlineData("1+M5", 1, 6)]
    [InlineData("d4", 1, 4)]
    [InlineData("3", 3, 3)]
    [InlineData("-d4", -4, -1)]
    public void Random_values_follow_angband(string text, int min, int max)
    {
        var rng = new Angband.Core.Randomness.GameRandom(1);
        var value = RandomValue.Parse(text);
        for (var i = 0; i < 200; i++) Assert.InRange(value.Roll(rng, 60), min, max);
    }

    // --- Devices -----------------------------------------------------------------------------

    [Fact]
    public void Wand_of_magic_missile_hits_and_uses_a_charge()
    {
        var game = Game();
        var orc = Arena.AddMonster(game, "cave_orc", new Loc(8, 2));
        orc.Hp = orc.MaxHp = 1000;
        var wand = Carry(game, "wand_of_magic_missile", charges: 3);

        Assert.True(game.Execute(new UseCommand(wand)));
        Assert.True(orc.Hp < 1000);
        Assert.Equal(2, wand.Charges);
        Assert.Contains("(2 charges)", game.Describe(wand));
    }

    [Fact]
    public void Empty_wands_and_missing_targets_waste_nothing()
    {
        var game = Game();
        var messages = Messages(game);
        var wand = Carry(game, "wand_of_magic_missile", charges: 2);
        Assert.False(game.Execute(new UseCommand(wand)));
        Assert.Contains("You have no target.", messages);
        Assert.Equal(2, wand.Charges);

        Arena.AddMonster(game, "cave_orc", new Loc(8, 2));
        wand.Charges = 0;
        Assert.False(game.Execute(new UseCommand(wand)));
        Assert.Contains("The wand has no charges left.", messages);
    }

    [Fact]
    public void Rods_recharge_over_time()
    {
        var game = Game();
        var messages = Messages(game);
        var rod = Carry(game, "rod_of_treasure_location");
        Assert.True(game.Execute(new UseCommand(rod)));
        Assert.True(rod.Timeout > 0);
        Assert.Contains("(charging)", game.Describe(rod));

        Assert.False(game.Execute(new UseCommand(rod)));
        Assert.Contains("The rod is still charging.", messages);
        TestGames.HoldUntil(game, game.GameTurn + rod.Timeout * 10 + 20);
        Assert.Equal(0, rod.Timeout);
    }

    [Fact]
    public void Stone_to_mud_wand_needs_a_direction()
    {
        var game = Game();
        var wand = Carry(game, "wand_of_stone_to_mud", charges: 2);
        Assert.False(game.Execute(new UseCommand(wand)));
        Assert.True(game.Execute(new UseCommand(wand, Direction: Direction.North)));
        Assert.True(game.Level.IsFloor(new Loc(3, 0)) || !game.Level.InBoundsFully(new Loc(3, 0)));
    }

    [Fact]
    public void Staff_of_slow_monsters_slows_what_it_sees()
    {
        var game = Game();
        var jackal = Arena.AddMonster(game, "jackal", new Loc(8, 2));
        var staff = Carry(game, "staff_of_slow_monsters", charges: 5);
        for (var i = 0; i < 10 && jackal.Slow == 0; i++) game.Execute(new UseCommand(staff));
        Assert.True(jackal.Slow > 0);
    }

    [Fact]
    public void Device_skill_decides_how_often_devices_fail()
    {
        var game = Game();
        var wand = Carry(game, "wand_of_fire_balls", charges: 5);
        game.Player.SkillDevice = 20;
        var hard = game.DeviceFailChance(wand);
        game.Player.SkillDevice = 120;
        Assert.True(game.DeviceFailChance(wand) < hard);
    }

    // --- Scrolls and potions -------------------------------------------------------------------

    [Fact]
    public void Word_of_recall_goes_to_the_deepest_level_and_back()
    {
        var game = GameSession.NewGame(TestData.Game, 5);
        game.Player.MaxDepth = 7;
        var scroll = Carry(game, "scroll_of_word_of_recall");
        game.Execute(new UseCommand(scroll));
        Assert.True(game.Player.RecallTimer > 0);
        for (var i = 0; i < 400 && game.Player.Depth == 0; i++)
        {
            game.Player.Hp = game.Player.MaxHp; // (the town's residents may not be friendly)
            game.Execute(new HoldCommand());
        }
        Assert.Equal(7, game.Player.Depth);

        game.ToggleRecall();
        for (var i = 0; i < 400 && game.Player.Depth != 0; i++)
        {
            game.Player.Hp = game.Player.MaxHp;
            game.Execute(new HoldCommand());
        }
        Assert.Equal(0, game.Player.Depth);
    }

    [Fact]
    public void Deep_descent_goes_five_levels_down()
    {
        var game = GameSession.NewGame(TestData.Game, 6);
        game.Execute(new DebugJumpCommand(3));
        game.Execute(new UseCommand(Carry(game, "scroll_of_deep_descent")));
        for (var i = 0; i < 50 && game.Player.Depth == 3; i++)
        {
            game.Player.Hp = game.Player.MaxHp = 5000;
            game.Execute(new HoldCommand());
        }
        Assert.Equal(8, game.Player.Depth);
    }

    [Fact]
    public void Enchant_scroll_improves_the_weapon()
    {
        var game = Game();
        var weapon = game.Player.Inventory.Weapon!;
        weapon.ToHit = 0;
        game.Execute(new UseCommand(Carry(game, "scroll_of_enchant_weapon_to_hit")));
        Assert.Equal(1, weapon.ToHit);
    }

    [Fact]
    public void Recharging_adds_charges()
    {
        var game = Game(9);
        var wand = Carry(game, "wand_of_magic_missile", charges: 0);
        game.Execute(new UseCommand(Carry(game, "scroll_of_recharging")));
        Assert.True(wand.Charges > 0 || !game.Player.Inventory.Contains(wand)); // recharged, or it blew up
    }

    // --- Stats and experience --------------------------------------------------------------------

    [Fact]
    public void Gear_raises_stats_and_drain_lowers_them()
    {
        var game = Game();
        var before = game.Player.Stats["str"];
        var ring = game.Objects.Create("ring_of_strength");
        ring.Modifiers["str"] = 3;
        game.Execute(new WieldCommand(game.Player.Inventory.Add(ring)!));
        Assert.Equal(before + 3, game.Player.Stats["str"]);

        Assert.True(game.DrainStat("dex"));
        var drained = game.Player.Stats["dex"];
        Assert.True(game.RestoreStat("dex"));
        Assert.Equal(drained + 1, game.Player.Stats["dex"]);
    }

    [Fact]
    public void Sustains_protect_against_drain()
    {
        var game = Game();
        game.Player.IntrinsicResists["sust_con"] = 1;
        game.RecalculateBonuses();
        Assert.False(game.DrainStat("con"));
    }

    [Fact]
    public void Stat_potions_raise_the_natural_stat()
    {
        var game = Game();
        var before = game.Player.NaturalStats["str"];
        game.Execute(new UseCommand(Carry(game, "potion_of_strength")));
        Assert.Equal(Math.Min(28, before + 1), game.Player.NaturalStats["str"]);
    }

    [Fact]
    public void Life_drain_loses_levels_that_restoring_brings_back()
    {
        var game = Game();
        game.GainExperience(game.ExperienceForLevel(9));
        Assert.Equal(10, game.Player.Level);
        var maxHp = game.Player.MaxHp;

        game.LoseExperience(game.Player.Experience - game.ExperienceForLevel(4));
        Assert.Equal(5, game.Player.Level);
        Assert.True(game.Player.MaxHp < maxHp);
        Assert.Equal(game.ExperienceForLevel(9), game.Player.MaxExperience);

        game.Execute(new UseCommand(Carry(game, "potion_of_restore_life_levels")));
        Assert.Equal(10, game.Player.Level);
        Assert.Equal(maxHp, game.Player.MaxHp);
    }

    // --- Monsters ----------------------------------------------------------------------------

    [Fact]
    public void Thieves_steal_gold_and_drop_it_when_killed()
    {
        var game = Game(11);
        game.Player.Gold = 5000;
        game.Player.NaturalStats["dex"] = game.Player.Stats["dex"] = 3;
        game.RecalculateBonuses();
        Monster? thief = null;
        for (var attempt = 0; attempt < 30 && game.Player.Gold == 5000; attempt++)
        {
            TestGames.ClearMonsters(game);
            thief = Arena.AddMonster(game, "cutpurse", game.Player.Position + new Loc(1, 0));
            game.Player.Hp = game.Player.MaxHp = 5000;
            for (var i = 0; i < 5 && game.Player.Gold == 5000 && thief.IsActive; i++) game.Execute(new HoldCommand());
        }
        Assert.True(game.Player.Gold < 5000);
        var stolen = 5000 - game.Player.Gold;
        Assert.Equal(stolen, thief!.Carried.Sum(i => i.GoldValue));

        var at = thief.Position;
        game.DamageMonster(thief, 10_000);
        Assert.Contains(game.Level.Objects.All, o => o.Item.IsGold && o.Item.GoldValue == stolen && o.Loc.ChebyshevTo(at) <= 3);
    }

    [Fact]
    public void Invisible_monsters_need_see_invisible()
    {
        var game = Game();
        var ghost = Arena.AddMonster(game, "poltergeist", new Loc(8, 2), awake: false);
        game.UpdateView();
        Assert.False(ghost.IsVisible);

        game.IncreaseTimed("see_invisible", 50);
        game.UpdateView();
        Assert.True(ghost.IsVisible);
    }

    [Fact]
    public void Telepathy_senses_minds_through_walls()
    {
        var game = Game();
        var orc = Arena.AddMonster(game, "cave_orc", new Loc(8, 2), awake: false);
        game.Level.Monsters.Move(orc, new Loc(8, 0)); // inside the wall, out of sight
        game.UpdateView();
        Assert.False(orc.IsVisible);
        game.IncreaseTimed("telepathy", 50);
        game.UpdateView();
        Assert.True(orc.IsVisible);
    }

    [Fact]
    public void Ghosts_pass_through_walls()
    {
        var game = Arena.Create(4,
            "#########",
            "#,,,#,,,#",
            "#,@,#,,,#",
            "#,,,#,,,#",
            "#########");
        TestGames.ClearMonsters(game);
        var ghost = Arena.AddMonster(game, "moaning_spirit", new Loc(6, 2));
        ghost.Hp = ghost.MaxHp = 10_000;
        var crossed = false;
        for (var i = 0; i < 100 && !crossed; i++)
        {
            game.Player.Hp = game.Player.MaxHp = 10_000;
            game.Execute(new HoldCommand());
            crossed = ghost.Position.X <= 4;
        }
        Assert.True(crossed);
    }

    [Fact]
    public void Protection_from_evil_repels_weaker_evil_monsters()
    {
        var game = Game();
        game.Player.Level = 40;
        var messages = Messages(game);
        game.IncreaseTimed("prot_evil", 500);
        // An uruk (level 16): weaker than the player, but not so far outclassed that its morale breaks
        // and it keeps away (a cave orc would, against a level 40 player).
        var orc = Arena.AddMonster(game, "uruk", game.Player.Position + new Loc(1, 0));
        for (var i = 0; i < 30; i++)
        {
            game.Player.Hp = game.Player.MaxHp = 5000;
            game.Execute(new HoldCommand());
        }
        Assert.Contains(messages, m => m.EndsWith("is repelled."));
    }

    // --- Artifacts and vaults --------------------------------------------------------------------

    [Fact]
    public void Special_artifacts_have_their_own_unflavoured_kind()
    {
        var game = Game();
        var art = game.Data.Artifacts.Single(a => a.Id == "of_the_dwarves");
        var necklace = game.Objects.CreateArtifact(art);
        Assert.False(necklace.IsFlavored);
        foreach (var rune in necklace.Runes()) game.Knowledge.LearnRune(rune);
        Assert.StartsWith("the Necklace of the Dwarves", game.Describe(necklace));
    }

    [Fact]
    public void Vault_letters_are_monster_base_symbols()
    {
        // Angband build_vault: letters other than x and X are the symbols of monster bases.
        var symbols = TestData.Game.MonsterBases.Select(b => b.Glyph[0]).ToHashSet();
        var letters = TestData.Game.Vaults.SelectMany(v => v.Rows).SelectMany(r => r)
            .Where(c => char.IsAsciiLetter(c) && c is not ('x' or 'X')).Distinct().ToList();
        Assert.NotEmpty(letters);
        Assert.All(letters, l => Assert.Contains(l, symbols));
    }

    [Fact]
    public void Deep_levels_get_vaults()
    {
        var generator = new DungeonGenerator(TestData.Game);
        var withVaults = Enumerable.Range(1, 25).Count(seed =>
        {
            var level = generator.Generate(new LevelRequest(35, (ulong)seed)).Level;
            return level.AllLocs().Any(p => level[p].Has(SquareFlags.Vault));
        });
        Assert.True(withVaults > 0);
    }

    // --- Saving ----------------------------------------------------------------------------------

    [Fact]
    public void New_state_survives_saving()
    {
        var game = Game();
        var wand = Carry(game, "wand_of_magic_missile", charges: 4);
        var thief = Arena.AddMonster(game, "cutpurse", new Loc(10, 2));
        thief.Carried.Add(game.Objects.Create("potion_of_strength"));
        thief.Slow = 7;
        game.DrainStat("wis");
        game.Player.RecallTimer = 12;

        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);

        Assert.Equal(4, loaded.Player.Inventory.Pack.Single(i => i.Serial == wand.Serial).Charges);
        var copy = loaded.Level.Monsters.All.Single(m => m.Race.Id == "cutpurse");
        Assert.Equal("potion_of_strength", Assert.Single(copy.Carried).Kind.Id);
        Assert.Equal(7, copy.Slow);
        Assert.Equal(game.Player.Stats["wis"], loaded.Player.Stats["wis"]);
        Assert.Equal(12, loaded.Player.RecallTimer);
    }
}
