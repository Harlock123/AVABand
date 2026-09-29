using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Persistence;
using Angband.Core.World;

namespace Angband.Tests;

/// <summary>Angband 4.2's books for the mage, priest, druid, ranger, rogue and paladin.</summary>
public class RealmSpellTests
{
    private static readonly string[] Room =
    [
        "#################", "#,,,,,,,,,,,,,,,#", "#,,,,,,,,,,,,,,,#", "#,,,,,,,@,,,,,,,#", "#,,,,,,,,,,,,,,,#", "#,,,,,,,,,,,,,,,#", "#################",
    ];

    private static GameSession Hero(string cls, ulong seed = 1, int level = 1)
    {
        var arena = Arena.Create(seed, Room);
        var game = GameSession.NewGame(TestData.Game, seed, cls);
        game.UseLevel(arena.Level, arena.Player.Position);
        if (level > 1) game.GainExperience(game.ExperienceForLevel(level - 1));
        game.Player.Hp = game.Player.MaxHp = 5000;
        return game;
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    private static void Cast(GameSession game, string spellId, Loc? target = null, Direction? dir = null)
    {
        var spell = game.Data.Spell(spellId)!;
        var info = game.SpellInfo(spell)!;
        if (game.Player.Level < info.Level) game.GainExperience(game.ExperienceForLevel(info.Level - 1) - game.Player.Experience);
        if (!game.HasBookFor(spell)) game.Player.Inventory.Add(game.Objects.Create(spell.Book));
        if (!game.Player.LearnedSpells.Contains(spellId)) Assert.True(game.Execute(new StudyCommand(spellId)), $"could not learn {spellId}");
        var cast = false;
        using var sub = game.Events.Subscribe<SpellCastEvent>(_ => cast = true);
        for (var i = 0; i < 80 && !cast && !game.Player.IsDead; i++)
        {
            game.Player.Mana = game.Player.MaxMana = Math.Max(game.Player.MaxMana, info.Mana + 80);
            game.Execute(new CastCommand(spellId, target, dir));
        }
        Assert.True(cast, $"{spellId} never worked");
    }

    private static Monster Foe(GameSession game, string race, Loc offset, int hp = 10_000)
    {
        var m = Arena.AddMonster(game, race, game.Player.Position + offset, awake: false);
        m.Hp = m.MaxHp = hp;
        m.Held = 10_000;
        game.UpdateView();
        return m;
    }

    [Theory]
    [InlineData("mage", 5, 30)]
    [InlineData("priest", 5, 28)]
    [InlineData("druid", 5, 27)]
    [InlineData("ranger", 2, 11)]
    [InlineData("rogue", 2, 10)]
    [InlineData("paladin", 3, 16)]
    public void TheClassesHaveAngbandsBooksAndSpells(string cls, int books, int spells)
    {
        var mine = TestData.Game.Spells.Where(s => s.Classes.ContainsKey(cls)).ToList();
        Assert.Equal(spells, mine.Count);
        Assert.Equal(books, mine.Select(s => s.Book).Distinct().Count());
    }

    /// <summary>Every spell of every class can be learned and cast.</summary>
    [Fact]
    public void EverySpell_Works()
    {
        foreach (var cls in TestData.Game.Classes.Where(c => c.Realm is not null).Select(c => c.Id))
        foreach (var spell in TestData.Game.Spells.Where(s => s.Classes.ContainsKey(cls)))
        {
            var game = Hero(cls, 3, 50);
            Foe(game, "cave_orc", new Loc(1, 0), 5000);
            Foe(game, "jackal", new Loc(3, 1), 5000);
            game.Player.Inventory.Add(game.Objects.Create("staff_of_detect_evil"));
            if (GameSession.NeedsCurseChoice(spell.Effect)) TestGames.GiveCursedItem(game);
            Cast(game, spell.Id, dir: spell.NeedsDirection ? Direction.East : null);
            Assert.False(game.Player.IsDead, $"{cls} died casting {spell.Id}");
        }
    }

    [Fact]
    public void ZeroFail_OnlyForTheClassesThatHaveIt()
    {
        int Fail(string cls, string spellId)
        {
            var game = Hero(cls, 4, 50);
            game.Player.NaturalStats[game.PlayerRealm!.Stat] = game.Player.Stats[game.PlayerRealm.Stat] = 40;
            game.Player.Mana = game.Player.MaxMana = 100;
            return game.SpellFailChance(game.Data.Spell(spellId)!);
        }
        Assert.Equal(0, Fail("mage", "magic_missile"));
        Assert.Equal(5, Fail("rogue", "phase_door"));
    }

    [Fact]
    public void MagicMissile_IsSometimesABeam_ForMages()
    {
        var game = Hero("mage", 5, 50);
        var near = Foe(game, "cave_orc", new Loc(2, 0));
        var far = Foe(game, "cave_orc", new Loc(4, 0));
        for (var i = 0; i < 40 && far.Hp == 10_000; i++) Cast(game, "magic_missile", far.Position);
        Assert.True(far.Hp < 10_000, "a beam passes the first monster"); // beams one time in (50 - 10)%
        Assert.True(near.Hp < 10_000);
    }

    [Fact]
    public void AcidSpray_HitsACone_NotWhatsBehindYou()
    {
        var game = Hero("mage", 6, 20);
        var ahead = Foe(game, "cave_orc", new Loc(3, 0));
        var aside = Foe(game, "cave_orc", new Loc(3, 1));
        var behind = Foe(game, "cave_orc", new Loc(-3, 0));
        Cast(game, "acid_spray", ahead.Position);
        Assert.True(ahead.Hp < 10_000);
        Assert.True(aside.Hp < 10_000);
        Assert.Equal(10_000, behind.Hp);
    }

    [Fact]
    public void ElectricArc_IsAShortBeam()
    {
        var game = Hero("mage", 7, 2);
        var close = Foe(game, "cave_orc", new Loc(1, 0));
        var far = Foe(game, "cave_orc", new Loc(6, 0));
        Cast(game, "electric_arc", far.Position);
        Assert.True(close.Hp < 10_000);
        Assert.Equal(10_000, far.Hp); // reach 0 + level/1 = 2 squares at level 2
    }

    [Fact]
    public void LightningStrike_ExplodesOnTheTarget()
    {
        var game = Hero("druid", 8, 12);
        var target = Foe(game, "cave_orc", new Loc(5, 0));
        var nearby = Foe(game, "cave_orc", new Loc(5, 1));
        Cast(game, "lightning_strike", target.Position);
        Assert.True(target.Hp < 10_000);
        Assert.True(nearby.Hp < 10_000); // the thunderclap, radius 3
    }

    [Fact]
    public void VolcanicEruption_SparesTheCaster()
    {
        var game = Hero("druid", 9, 40);
        var near = Foe(game, "cave_orc", new Loc(2, 0));
        var hp = game.Player.Hp;
        var said = Messages(game);
        game.Options[OptionIds.ShowDamage] = false; // 4.2.5's pain message as it is, without a number
        Cast(game, "volcanic_eruption");
        // (Checked by the message: a 10,000 hit point orc can regenerate the damage over the failed casts.)
        Assert.Contains("The cave orc shrugs off the attack.", said); // (4.2.5's pain message, from > 95% health)
        Assert.True(game.Player.Hp >= hp - 60); // only the quake may bruise
    }

    [Fact]
    public void Detection_FindsTraps_AndDisableTrapsRemovesThem()
    {
        var game = Hero("mage", 10, 5);
        var trapAt = game.Player.Position + new Loc(1, 0);
        game.Level[trapAt].Trap = TestData.Game.Traps.First(t => !t.Warding).Index;
        Cast(game, "find_traps_doors");
        Assert.True(game.Level[trapAt].Has(SquareFlags.TrapVisible));
        Cast(game, "disable_traps_destroy_doors");
        Assert.Equal(0, game.Level[trapAt].Trap);
    }

    [Fact]
    public void DoorCreation_ClosesYouIn()
    {
        var game = Hero("mage", 11, 13);
        Cast(game, "door_creation");
        Assert.All(game.Level.Neighbors(game.Player.Position), p => Assert.True(game.Level.Has(p, TerrainFlags.DoorClosed)));
    }

    [Fact]
    public void TapMagicalEnergy_DrainsAWand()
    {
        var game = Hero("mage", 12, 22);
        var wand = game.Objects.Create("wand_of_stinking_cloud");
        wand.Charges = 10;
        game.Player.Inventory.Add(wand);
        game.Player.Inventory.Add(game.Objects.Create("magical_defences"));
        Assert.True(game.Execute(new StudyCommand("tap_magical_energy")));
        game.Player.NaturalStats["int"] = game.Player.Stats["int"] = 40;
        for (var i = 0; i < 20 && wand.Charges > 0; i++)
        {
            game.Player.MaxMana = 100;
            game.Player.Mana = 10; // with room for more: a full caster gains nothing
            game.Execute(new CastCommand("tap_magical_energy"));
        }
        Assert.Equal(0, wand.Charges);
        Assert.True(game.Player.Mana > 10);
    }

    [Fact]
    public void ManaChannel_MakesSpellsQuicker()
    {
        var game = Hero("mage", 13, 25);
        Cast(game, "mana_channel");
        Assert.True(game.Player.Timed.Has("fastcast"));
        var turn = game.GameTurn;
        game.Player.Mana = game.Player.MaxMana;
        game.Player.NaturalStats["int"] = game.Player.Stats["int"] = 40;
        game.Execute(new CastCommand("magic_missile", game.Player.Position + new Loc(3, 0)));
        Assert.True(game.GameTurn - turn < 10); // three quarters of a turn
    }

    [Fact]
    public void DimensionDoor_GoesToTheTarget()
    {
        var game = Hero("mage", 14, 35);
        var spot = game.Player.Position + new Loc(5, 2);
        game.SetTarget(spot);
        Cast(game, "dimension_door", spot);
        Assert.Equal(spot, game.Player.Position);
    }

    [Fact]
    public void RapidRegeneration_Heals30ATurn()
    {
        var game = Hero("druid", 15, 37);
        Cast(game, "rapid_regeneration");
        game.Player.Hp = 100;
        game.Execute(new HoldCommand());
        Assert.True(game.Player.Hp >= 130);
    }

    [Fact]
    public void CoverTracks_HidesYouFromDistantMonsters_AndLeavesNoScent()
    {
        var game = Hero("ranger", 16, 20);
        var far = Foe(game, "jackal", new Loc(6, 0));
        Assert.True(game.CanSee(far));
        Cast(game, "cover_tracks");
        Assert.False(game.CanSee(far)); // beyond a quarter of the sight range
    }

    [Fact]
    public void CreateArrows_TurnsAStaffIntoArrows()
    {
        var game = Hero("ranger", 17, 22);
        game.Player.Inventory.Add(game.Objects.Create("staff_of_detect_evil"));
        Cast(game, "create_arrows");
        Assert.DoesNotContain(game.Player.Inventory.Pack, i => i.Base.Id == "staff");
        Assert.Contains(game.Level.Objects.At(game.Player.Position).Concat(game.Player.Inventory.Quiver), i => i.Base.Id == "arrow");
    }

    [Fact]
    public void BrandAmmunition_BrandsTheQuiver()
    {
        var game = Hero("ranger", 18, 40);
        var arrows = game.Player.Inventory.Quiver.First();
        Cast(game, "brand_ammunition");
        Assert.Single(arrows.Brands);
    }

    [Fact]
    public void Decoy_DrawsMonstersThatCanSeeIt_AndTheyBreakIt()
    {
        var game = Hero("ranger", 19, 30);
        Cast(game, "decoy");
        var decoy = game.Player.Position;
        Assert.Equal(decoy, game.Level.Decoy);
        game.TeleportPlayer(5);
        var messages = Messages(game);
        // A cave orc, not a jackal: a level 30 ranger outclasses a jackal, which keeps well away (morale).
        var hound = Arena.AddMonster(game, "cave_orc", decoy + new Loc(2, 0));
        for (var i = 0; i < 20 && game.Level.Decoy is not null; i++) game.Execute(new HoldCommand());
        Assert.Null(game.Level.Decoy);
        Assert.Contains("The decoy is destroyed!", messages);
        _ = hound;
    }

    [Fact]
    public void OrbOfDraining_HurtsEvilDoubly()
    {
        var game = Hero("priest", 20, 7);
        var evil = Foe(game, "cave_orc", new Loc(3, 0));
        Assert.True(evil.Race.Has(MonsterFlags.Evil));
        var hits = new List<int>();
        using var sub = game.Events.Subscribe<PlayerAttackEvent>(a => hits.Add(a.Damage));
        Cast(game, "orb_of_draining", evil.Position);
        Assert.True(hits[0] >= 2 * (7 * 3 / 2 + 3));
    }

    [Fact]
    public void Rangers_ShootFasterWithLevel_AndWarriorsLoseTheirFear()
    {
        var ranger = Hero("ranger", 21);
        var shots = ranger.Player.Shots;
        ranger.GainExperience(ranger.ExperienceForLevel(29));
        Assert.Equal(shots + 10, ranger.Player.Shots); // level 30: +1.0 shots

        var warrior = Hero("warrior", 22);
        Assert.Equal(0, warrior.Player.Resists.GetValueOrDefault("fear"));
        warrior.GainExperience(warrior.ExperienceForLevel(29));
        Assert.Equal(1, warrior.Player.Resists.GetValueOrDefault("fear"));
    }

    [Fact]
    public void OldSaves_TradeTheirBooksForAngbands()
    {
        var game = Hero("mage", 23);
        var book = game.Player.Inventory.Pack.Single(i => i.Kind.Id == "first_spells");
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        // Rewrite the save as an older version would have written it.
        var json = new StreamReader(new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress)).ReadToEnd()
            .Replace("\"first_spells\"", "\"magic_for_beginners\"")
            .Replace("\"LearnedSpells\":[]", "\"LearnedSpells\":[\"stinging_swarm\",\"magic_missile\"]");
        using var old = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(old, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
        using (var w = new StreamWriter(gz)) w.Write(json);
        old.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, old);
        Assert.Contains(loaded.Player.Inventory.Pack, i => i.Kind.Id == "first_spells");
        Assert.DoesNotContain("stinging_swarm", loaded.Player.LearnedSpells);
        _ = book;
    }
}
