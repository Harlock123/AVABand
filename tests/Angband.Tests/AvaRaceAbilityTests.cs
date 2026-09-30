using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Monsters;

namespace Angband.Tests;

/// <summary>AVABand's racial abilities (ava_races.json), each against the same character without them.</summary>
public class AvaRaceAbilityTests
{
    private static GameSession Make(string race, string cls = "warrior", bool abilities = true, ulong seed = 9) =>
        GameSession.NewGame(TestData.Game, seed,
            CharacterSpec.Default(race, cls) with { Options = new Dictionary<string, bool> { [OptionIds.AvaRaces] = abilities } });

    private static Item Carry(GameSession game, string kind)
    {
        var item = game.Objects.Create(kind);
        game.Knowledge.LearnKind(item.Kind);
        foreach (var rune in item.Runes()) game.Knowledge.LearnRune(rune);
        return game.Player.Inventory.Add(item)!;
    }

    private static Monster Beside(GameSession game, string race)
    {
        var at = game.Level.AllLocs().First(l => l.ChebyshevTo(game.Player.Position) == 1 && game.Level.IsEmptyFloor(l));
        var m = new MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster(race)!, at);
        m.Hp = m.MaxHp = 100_000;
        return m;
    }

    [Fact]
    public void Every_race_has_an_ability_and_the_option_turns_them_off()
    {
        Assert.All(TestData.Game.Races, r => Assert.NotEmpty(r.AvaAbilities));
        Assert.All(TestData.Game.Races, r => Assert.NotEqual("", r.AvaAbilityText));
        Assert.True(Make("dwarf").HasRaceAbility("DELVER"));
        Assert.False(Make("dwarf", abilities: false).HasRaceAbility("DELVER"));
        Assert.False(Make("human").HasRaceAbility("DELVER"));
        Assert.Contains("Racial ability (AVABand)", Angband.Core.Records.CharacterDump.Build(Make("dwarf")));
        Assert.DoesNotContain("Racial ability (AVABand)", Angband.Core.Records.CharacterDump.Build(Make("dwarf", abilities: false)));
    }

    // --- Human ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_Human_wields_a_light_weapon_in_the_off_hand_for_an_extra_blow()
    {
        var game = Make("human");
        var dagger = Carry(game, "dagger");
        Assert.True(game.CanWieldOffHand(dagger));
        Assert.False(game.CanWieldOffHand(Carry(game, "battle_axe"))); // 17 lb: too heavy
        Assert.False(Make("dwarf").CanWieldOffHand(Carry(Make("dwarf"), "dagger")));
        var toHit = game.Player.ToHit;
        Assert.True(game.Execute(new WieldOffHandCommand(dagger)));
        Assert.Equal("dagger", game.OffHand!.Kind.Id);
        Assert.Equal(toHit, game.Player.ToHit); // its bonuses are its own

        var blows = new List<PlayerAttackEvent>();
        game.Events.Subscribe<PlayerAttackEvent>(blows.Add);
        game.Player.Hp = game.Player.MaxHp = 10_000;
        var target = Beside(game, "cave_bear");
        game.Execute(new WalkCommand(DirectionExtensions.FromOffset(target.Position.X - game.Player.Position.X, target.Position.Y - game.Player.Position.Y)));
        Assert.Equal(game.Player.Blows / 100 + 1, blows.Count);

        // A shield takes the off hand back.
        var shield = Carry(game, "leather_shield");
        game.Execute(new WieldCommand(shield));
        Assert.Null(game.OffHand);
        Assert.Contains(game.Player.Inventory.Pack, i => i.Kind.Id == "dagger");
    }

    // --- Half-Elf ------------------------------------------------------------------------------------------

    [Fact]
    public void A_Half_Elf_gets_better_prices_and_a_keen_eye()
    {
        var elf = Make("half_elf");
        var man = Make("half_elf", abilities: false);
        var store = elf.Stores["general"];
        var item = store.Stock.First(i => elf.BuyPrice(store, i) > 20);
        Assert.Equal(man.BuyPrice(man.Stores["general"], man.Stores["general"].Stock.First(i => i.Kind == item.Kind)) * 9 / 10, elf.BuyPrice(store, item));

        var ring = elf.Objects.Create("ring_of_resist_fire_and_cold");
        elf.Knowledge.LearnKind(ring.Kind);
        elf.Player.Inventory.Add(ring);
        var unknown = elf.Knowledge.UnknownRunes(ring).Count();
        elf.Execute(new WieldCommand(ring));
        Assert.Equal(unknown - 1, elf.Knowledge.UnknownRunes(ring).Count());
    }

    // --- Elf, Hobbit, Gnome, Kobold: skills ------------------------------------------------------------------

    [Fact]
    public void Elves_shoot_bows_Hobbits_slings_and_throws_better()
    {
        var elf = Make("elf");
        var plainElf = Make("elf", abilities: false);
        Assert.Equal(plainElf.LauncherSkill(Carry(plainElf, "long_bow")) + 15, elf.LauncherSkill(Carry(elf, "long_bow")));
        Assert.Equal(plainElf.LauncherSkill(Carry(plainElf, "sling")), elf.LauncherSkill(Carry(elf, "sling")));

        var hobbit = Make("hobbit");
        var plainHobbit = Make("hobbit", abilities: false);
        Assert.Equal(plainHobbit.LauncherSkill(Carry(plainHobbit, "sling")) + 20, hobbit.LauncherSkill(Carry(hobbit, "sling")));
        Assert.Equal(plainHobbit.Player.SkillThrow + 20, hobbit.Player.SkillThrow);
    }

    [Fact]
    public void Gnomes_use_devices_and_Kobolds_find_and_disarm_traps_better()
    {
        Assert.Equal(Make("gnome", abilities: false).Player.SkillDevice + 10, Make("gnome").Player.SkillDevice);
        var kobold = Make("kobold").Player;
        var plain = Make("kobold", abilities: false).Player;
        Assert.Equal(plain.SkillSearch + 10, kobold.SkillSearch);
        Assert.Equal(plain.DisarmSkill + 15, kobold.DisarmSkill);
        Assert.Equal(plain.DisarmMagicSkill + 15, kobold.DisarmMagicSkill);
    }

    [Fact]
    public void A_Hobbit_goes_hungry_more_slowly()
    {
        var hobbit = Make("hobbit");
        var plain = Make("hobbit", abilities: false);
        foreach (var g in new[] { hobbit, plain })
            for (var i = 0; i < 300; i++) g.Execute(new HoldCommand());
        Assert.True(9999 - hobbit.Player.Food < (9999 - plain.Player.Food) * 3 / 4,
            $"hobbit ate {9999 - hobbit.Player.Food}, without the ability {9999 - plain.Player.Food}");
    }

    [Fact]
    public void A_Kobolds_bare_hands_are_venomous()
    {
        int MostDamage(GameSession game)
        {
            game.Player.Inventory.TakeOff(game.Player.Inventory.Weapon!);
            game.RecalculateBonuses();
            game.Player.Hp = game.Player.MaxHp = 10_000;
            var target = Beside(game, "cave_bear");
            var damage = new List<int>();
            game.Events.Subscribe<PlayerAttackEvent>(a => { if (a.Hit) damage.Add(a.Damage); });
            var dir = DirectionExtensions.FromOffset(target.Position.X - game.Player.Position.X, target.Position.Y - game.Player.Position.Y);
            for (var i = 0; i < 30; i++) game.Execute(new WalkCommand(dir));
            return damage.DefaultIfEmpty(0).Max();
        }
        Assert.True(MostDamage(Make("kobold")) > MostDamage(Make("kobold", abilities: false)));
    }

    // --- Dwarf ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_Dwarf_digs_with_anything_and_gets_more_gold_with_a_pick()
    {
        Assert.Equal(Make("dwarf", abilities: false).DiggingSkill + 20, Make("dwarf").DiggingSkill);

        long VeinGold(bool abilities)
        {
            var game = Make("dwarf", abilities: abilities, seed: 21);
            Carry(game, "shovel"); // a proper digger: the same skill either way, so the same rolls
            game.Player.Stats["str"] = 18;
            var at = game.Level.AllLocs().First(l => l.ChebyshevTo(game.Player.Position) == 1 && game.Level.IsEmptyFloor(l));
            game.Level[at].Feature = game.Data.Terrain.Ids.MagmaTreasure;
            var dir = DirectionExtensions.FromOffset(at.X - game.Player.Position.X, at.Y - game.Player.Position.Y);
            for (var i = 0; i < 20 && game.Level.FeatureAt(at).Id != "floor"; i++) game.Execute(new TunnelCommand(dir));
            return game.Level.Objects.At(at).Where(i => i.IsGold).Sum(i => (long)i.GoldValue);
        }
        var plain = VeinGold(false);
        Assert.True(plain > 0);
        Assert.Equal(plain * 3 / 2, VeinGold(true));
    }

    // --- Half-Orc, Half-Troll, Dúnadan, High-Elf -------------------------------------------------------------------

    [Fact]
    public void A_Half_Orc_knows_no_fear()
    {
        Assert.Contains("fear", Make("half_orc").Player.Resists.Keys);
        Assert.DoesNotContain("fear", Make("half_orc", abilities: false).Player.Resists.Keys);
    }

    [Fact]
    public void A_Half_Troll_rages_once_a_level_and_smashes_rubble()
    {
        var troll = Make("half_troll");
        var said = new List<string>();
        troll.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        troll.Execute(new DebugJumpCommand(5));
        troll.TakeHit(troll.Player.MaxHp * 4 / 5, "a test");
        Assert.Contains(said, s => s.StartsWith("A troll's rage takes you!"));
        Assert.True(troll.Player.Timed.Has("berserk"));
        var hp = troll.Player.Hp;
        troll.TakeHit(hp - 1, "a test");
        Assert.Single(said, s => s.StartsWith("A troll's rage")); // once a level
        troll.Execute(new DebugJumpCommand(6));
        troll.Player.Hp = troll.Player.MaxHp;
        troll.TakeHit(troll.Player.MaxHp * 4 / 5, "a test");
        Assert.Equal(2, said.Count(s => s.StartsWith("A troll's rage")));

        Assert.Equal(1600, troll.DiggingChance(DiggingKind.Rubble));
        Assert.True(Make("half_troll", abilities: false).DiggingChance(DiggingKind.Rubble) < 1600);
    }

    [Fact]
    public void A_Dunadan_foresees_the_level_and_holds_their_life()
    {
        var man = Make("dunadan");
        man.Execute(new DebugJumpCommand(5));
        Assert.Equal(LevelFeelings.FeelingNeed, man.Level.FeelingSquaresSeen);
        Assert.Contains("hold_life", man.Player.Resists.Keys);
        var plain = Make("dunadan", abilities: false);
        plain.Execute(new DebugJumpCommand(5));
        Assert.True(plain.Level.FeelingSquaresSeen < LevelFeelings.FeelingNeed);
    }

    [Fact]
    public void A_High_Elfs_light_reaches_further_and_slows_the_undead()
    {
        var elf = Make("high_elf");
        Assert.Equal(Make("high_elf", abilities: false).Player.LightRadius + 1, elf.Player.LightRadius);
        var ghost = Beside(elf, "poltergeist");
        var dog = Beside(elf, "jackal");
        elf.UpdateView();
        Assert.Equal(2, ghost.AuraSlow);
        Assert.Equal(0, dog.AuraSlow);
    }

    [Fact]
    public void An_off_hand_weapon_is_saved()
    {
        var game = Make("human");
        game.Execute(new WieldOffHandCommand(Carry(game, "main_gauche")));
        using var stream = new MemoryStream();
        Angband.Core.Persistence.SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = Angband.Core.Persistence.SaveGame.Load(TestData.Game, stream);
        Assert.Equal("main_gauche", loaded.OffHand?.Kind.Id);
        Assert.Equal(game.Player.ToHit, loaded.Player.ToHit);
        Assert.Equal(game.Player.Blows, loaded.Player.Blows);
    }
}
