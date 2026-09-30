using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>
/// The last of Angband 4.2's class spells: Hit and Run, Relentless Taunting, Bloodlust, [Corruption
/// of Spirit], Clairvoyance and [Battle Blessings].
/// </summary>
public class LateSpellTests
{
    private static readonly string[] Room =
    [
        "###############", "#,,,,,,,,,,,,,#", "#,,,,,,,,,,,,,#", "#,,,,,,@,,,,,,#", "#,,,,,,,,,,,,,#", "#,,,,,,,,,,,,,#", "###############",
    ];

    private static GameSession Hero(string cls, ulong seed = 1)
    {
        var arena = Arena.Create(seed, Room);
        var game = GameSession.NewGame(TestData.Game, seed, cls);
        game.UseLevel(arena.Level, arena.Player.Position);
        game.Options[OptionIds.AngbandBlows] = false; // (spells, not armour weight: that's AngbandBlowsTests')
        game.RecalculateBonuses();
        game.RecalculateMana();
        game.Player.Mana = game.Player.MaxMana;
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
        if (!game.Player.LearnedSpells.Contains(spellId)) Assert.True(game.Execute(new StudyCommand(spellId)));
        var cast = false;
        using var sub = game.Events.Subscribe<SpellCastEvent>(_ => cast = true);
        for (var i = 0; i < 60 && !cast; i++)
        {
            game.Player.Mana = game.Player.MaxMana = Math.Max(game.Player.MaxMana, info.Mana + 60);
            game.Execute(new CastCommand(spellId, target, dir));
        }
        Assert.True(cast, $"{spellId} never worked");
    }

    private static Monster Foe(GameSession game, string race, Loc offset, int hp = 10_000, bool awake = true)
    {
        var m = Arena.AddMonster(game, race, game.Player.Position + offset, awake);
        m.Hp = m.MaxHp = hp;
        game.UpdateView();
        return m;
    }

    [Fact]
    public void TheNewBooks_Exist_AndHoldTheSpells()
    {
        Assert.Equal("prayer_book", TestData.Game.Object("battle_blessings")!.Base);
        Assert.Equal("shadow_book", TestData.Game.Object("corruption_of_spirit")!.Base);
        foreach (var id in new[] { "hit_and_run", "relentless_taunting", "bloodlust", "power_sacrifice", "zone_of_unmagic",
                     "vampire_form", "curse", "command", "clairvoyance", "smite_evil", "demon_bane", "enchant_weapon",
                     "enchant_armour", "single_combat" })
            Assert.NotNull(TestData.Game.Spell(id));
    }

    [Fact]
    public void HitAndRun_VanishesAfterAStealAttempt()
    {
        var game = Hero("rogue", 2);
        Foe(game, TestData.Game.Monsters.First(r => !r.IsUnique && r.Has("ONLY_GOLD") && r.Has("DROP_1")).Id, new Loc(1, 0), awake: false);
        Cast(game, "hit_and_run");
        Assert.True(game.Player.Timed.Has("att_run"));
        var messages = Messages(game);
        var from = game.Player.Position;
        game.Execute(new StealCommand(Direction.East));
        Assert.Contains("You vanish into the shadows!", messages);
        Assert.NotEqual(from, game.Player.Position);
        Assert.False(game.Player.Timed.Has("att_run"));
    }

    [Fact]
    public void Bloodlust_AddsDamageAndBlows_GrowsWithKills_AndDefiesDeath()
    {
        var game = Hero("blackguard", 3);
        var blows = game.Player.Blows;
        var toDam = game.Player.ToDam;
        game.IncreaseTimed("bloodlust", 40);
        game.RecalculateBonuses();
        Assert.Equal(toDam + 20, game.Player.ToDam);
        Assert.Equal(blows + 200, game.Player.Blows);

        game.Player.Timed.Set(game.Data.Timed("bloodlust")!, 10);
        var weak = Foe(game, "jackal", new Loc(1, 0), hp: 1);
        for (var i = 0; i < 20 && weak.IsActive; i++) game.Execute(new WalkCommand(Direction.East));
        Assert.False(weak.IsActive);
        Assert.True(game.Player.Timed["bloodlust"] >= 15); // +10 for the kill, less the turns' decay

        // "Your lust for blood keeps you alive!": hit points + bloodlust + level must stay non-negative.
        game.Player.Timed.Set(game.Data.Timed("bloodlust")!, 50);
        game.Player.Hp = 10;
        game.TakeHit(20, "a test");
        Assert.False(game.Player.IsDead);
    }

    [Fact]
    public void RelentlessTaunting_IsATimedEffect()
    {
        var game = Hero("blackguard", 4);
        Cast(game, "relentless_taunting");
        Assert.True(game.Player.Timed.Has("taunt"));
    }

    [Fact]
    public void PowerSacrifice_TradesHitPointsForMana()
    {
        var game = Hero("necromancer", 5);
        game.GainExperience(game.ExperienceForLevel(26));
        var messages = Messages(game);
        var hp = game.Player.Hp;
        Cast(game, "power_sacrifice");
        Assert.InRange(game.Player.Hp, hp - 50, hp - 1); // less a turn's regeneration
        Assert.Contains("You feel your head clear.", messages);
    }

    [Fact]
    public void ZoneOfUnmagic_HurtsEveryoneNearby_TheCasterToo()
    {
        var game = Hero("necromancer", 6);
        var near = Foe(game, "cave_orc", new Loc(2, 0), awake: false);
        var far = Foe(game, "cave_orc", new Loc(6, 0), awake: false);
        game.GainExperience(game.ExperienceForLevel(31)); // level first, so the hit points compare
        var hurt = new List<int>();
        game.Events.Subscribe<PlayerHurtEvent>(e => hurt.Add(e.Damage));
        Cast(game, "zone_of_unmagic");
        Assert.True(near.Hp < 10_000);
        Assert.Equal(10_000, far.Hp);
        Assert.Contains(game.Player.Level * 3 / 10, hurt); // the caster too, a tenth as much (Angband project_p, self)
    }

    [Fact]
    public void Curse_RollsDiceWithFiftySidesForAnUnhurtMonster()
    {
        var game = Hero("necromancer", 7);
        var orc = Foe(game, "cave_orc", new Loc(3, 0), awake: false);
        orc.Held = 10_000; // it mustn't wander off the target while a failed cast is retried
        var hits = new List<int>();
        using var sub = game.Events.Subscribe<PlayerAttackEvent>(a => hits.Add(a.Damage));
        Cast(game, "curse", orc.Position);
        Assert.Single(hits);
        var dice = game.Player.Level / 12 + 1;
        Assert.InRange(hits[0], dice, dice * 50);
    }

    [Fact]
    public void VampireForm_LetsYouDrink()
    {
        var game = Hero("necromancer", 8);
        Foe(game, "jackal", new Loc(4, 0));
        Cast(game, "vampire_form");
        Assert.Equal("vampire", game.Player.Shape);
        Assert.True(game.Player.Timed.Has("att_vamp"));
    }

    [Fact]
    public void Command_LetsYouMoveAMonster_AndSetItOnOthers()
    {
        var game = Hero("necromancer", 9);
        game.GainExperience(game.ExperienceForLevel(49));
        var servant = Foe(game, "jackal", new Loc(2, 0), hp: 100, awake: false);
        for (var i = 0; i < 30 && game.Commanded != servant; i++) Cast(game, "command", servant.Position);
        Assert.Same(servant, game.Commanded);
        // (Failed casts can wake it to wander: put it back beside you, in the open.)
        if (servant.Position != game.Player.Position + new Loc(2, 0)) game.Level.Monsters.Move(servant, game.Player.Position + new Loc(2, 0));
        var victim = Foe(game, "jackal", servant.Position + new Loc(2, 0) - game.Player.Position, hp: 1000, awake: false);
        victim.Held = 10_000; // it stays put to be bitten

        var me = game.Player.Position;
        var was = servant.Position;
        Assert.True(game.Execute(new WalkCommand(Direction.East)));
        Assert.Equal(me, game.Player.Position); // it moved, not you
        Assert.Equal(was + new Loc(1, 0), servant.Position);
        var said = new List<string>();
        using (game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text)))
            for (var i = 0; i < 10 && victim.Hp == 1000; i++)
                game.Execute(new WalkCommand(Direction.East)); // into the other jackal: an attack (bites can miss)
        Assert.True(victim.Hp < 1000, string.Join(" / ", said));

        // Anything else lets it go.
        game.Execute(new RestCommand());
        Assert.Null(game.Commanded);
    }

    [Fact]
    public void SmiteEvil_SlaysEvilWithEveryBlow_AndDemonBaneIsReady()
    {
        var game = Hero("paladin", 10);
        var orc = Foe(game, "cave_orc", new Loc(1, 0));
        Assert.True(orc.Race.Has(MonsterFlags.Evil));
        Cast(game, "smite_evil");
        var messages = Messages(game);
        for (var i = 0; i < 20 && !messages.Any(m => m.StartsWith("You smite")); i++) game.Execute(new WalkCommand(Direction.East));
        Assert.Contains(messages, m => m.StartsWith("You smite the cave orc"));
        Cast(game, "demon_bane");
        Assert.True(game.Player.Timed.Has("att_demon"));
    }

    [Fact]
    public void Clairvoyance_MapsTheWholeLevel()
    {
        var game = GameSession.NewGame(TestData.Game, 11, "paladin");
        game.Player.Hp = game.Player.MaxHp = 5000;
        game.Player.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();
        game.Execute(new TakeStairsCommand(Down: true));
        Cast(game, "clairvoyance");
        Assert.All(game.Level.AllLocs().Where(game.Level.IsFloor), p => Assert.True(game.Known.IsKnown(p)));
    }

    [Fact]
    public void EnchantWeapon_UsesItsDice()
    {
        var game = Hero("paladin", 12);
        var weapon = game.Player.Inventory.Weapon!;
        weapon.ToHit = weapon.ToDam = -5; // negative bonuses always take
        Cast(game, "enchant_weapon");
        Assert.InRange(weapon.ToHit, -4, -1); // 1d4 each
        Assert.InRange(weapon.ToDam, -4, -1);
    }

    [Fact]
    public void SingleCombat_SealsYouInWithTheFoe_UntilItDies()
    {
        var game = Hero("paladin", 13);
        var level = game.Level;
        var foe = Foe(game, "jackal", new Loc(3, 0), hp: 5, awake: false);
        foe.Held = 10_000; // stays where it's aimed at, however many tries the prayer takes
        game.GainExperience(game.ExperienceForLevel(39));
        var start = game.Player.Position;
        Cast(game, "single_combat", foe.Position);
        Assert.True(game.InArena);
        Assert.NotSame(level, game.Level);
        Assert.Same(foe, game.Level.Monsters.All.Single());
        game.ToggleRecall();
        Assert.Equal(0, game.Player.RecallTimer); // no recall from a duel

        // Saved mid-duel, the level outside comes back too.
        using (var stream = new MemoryStream())
        {
            SaveGame.Save(game, stream);
            stream.Position = 0;
            var loaded = SaveGame.Load(TestData.Game, stream);
            Assert.True(loaded.InArena);
            Assert.Equal(start, loaded.ArenaReturn!.Value.Position);
        }

        game.DamageMonster(foe, 1000);
        Assert.False(game.InArena);
        Assert.Same(level, game.Level);
        Assert.Equal(start, game.Player.Position);
    }
}
