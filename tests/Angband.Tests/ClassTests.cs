using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>The rogue, paladin, necromancer and blackguard (Angband 4.2 class.txt).</summary>
public class ClassTests
{
    private static readonly string[] NewClasses = ["rogue", "paladin", "necromancer", "blackguard"];

    private static GameSession Hero(string cls, ulong seed = 1, params string[] rows)
    {
        var arena = Arena.Create(seed, rows);
        var game = GameSession.NewGame(TestData.Game, seed, cls);
        game.UseLevel(arena.Level, arena.Player.Position);
        return game;
    }

    private static void ReachLevel(GameSession game, int level)
    {
        if (game.Player.Level < level) game.GainExperience(game.ExperienceForLevel(level - 1) - game.Player.Experience);
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    /// <summary>Learns and casts a spell (with its book, the level and mana), retrying failures.</summary>
    private static void CastUntilItWorks(GameSession game, string spellId, Loc? target = null, Direction? dir = null)
    {
        var spell = game.Data.Spell(spellId)!;
        var info = game.SpellInfo(spell)!;
        ReachLevel(game, info.Level);
        if (!game.HasBookFor(spell)) game.Player.Inventory.Add(game.Objects.Create(spell.Book));
        if (!game.Player.LearnedSpells.Contains(spellId)) Assert.True(game.Execute(new StudyCommand(spellId)), $"could not learn {spellId}");
        var cast = false;
        using var sub = game.Events.Subscribe<SpellCastEvent>(_ => cast = true);
        for (var i = 0; i < 50 && !cast && !game.Player.IsDead; i++)
        {
            game.Player.Mana = game.Player.MaxMana = Math.Max(game.Player.MaxMana, info.Mana);
            game.Execute(new CastCommand(spellId, target, dir));
        }
        Assert.True(cast, $"{spellId} never worked");
    }

    private static MonsterRaceDef Race(Func<MonsterRaceDef, bool> match) =>
        TestData.Game.Monsters.Where(r => !r.IsUnique && match(r)).OrderBy(r => r.Depth).ThenBy(r => r.Id).First();

    // --- Birth ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("rogue", "dagger", "arcane", 0)]
    [InlineData("paladin", "main_gauche", "divine", 1)]
    [InlineData("necromancer", "main_gauche", "shadow", 1)]
    [InlineData("blackguard", "tulwar", "shadow", 1)]
    public void NewClasses_StartWithTheirKitAndRealm(string cls, string weapon, string realm, int minMana)
    {
        var game = GameSession.NewGame(TestData.Game, 1, cls);
        Assert.Equal(cls, game.Player.Class!.Id);
        Assert.Equal(weapon, game.Player.Inventory.Weapon?.Kind.Id);
        Assert.Equal(realm, game.PlayerRealm!.Id);
        Assert.True(game.Player.MaxMana >= minMana);
        Assert.Contains(game.Player.Inventory.Pack, i => i.Kind.Base == game.PlayerRealm.BookBase);
    }

    [Fact]
    public void Rogues_CastFromLevelFive()
    {
        var game = GameSession.NewGame(TestData.Game, 1, "rogue");
        Assert.Equal(0, game.Player.MaxMana);
        ReachLevel(game, 5);
        Assert.True(game.Player.MaxMana > 0);
        Assert.Contains(game.StudyableSpells(), s => s.Id == "detect_monsters");
    }

    [Fact]
    public void TheBooksellerStocksShadowBooks()
    {
        var store = TestData.Game.Stores.Single(s => s.Id == "bookseller");
        Assert.Contains("into_the_shadows", store.Always);
        Assert.Contains("shadow_book", store.Buys);
    }

    [Fact]
    public void SpellArguments_CanGrowWithLevel()
    {
        var bolt = SpellEffects.Parse("bolt:nether:{L/4+3}d4; timed:blessed:{L+10}+1d{L+10}", 20).ToList();
        Assert.Equal("8d4", bolt[0].Arg(1));
        Assert.Equal("30+1d30", bolt[1].Arg(1));
    }

    /// <summary>Every spell of every new class can be learned and cast, with a monster to aim at.</summary>
    [Fact]
    public void EveryNewClassSpell_Works()
    {
        foreach (var cls in NewClasses)
        foreach (var spell in TestData.Game.Spells.Where(s => s.Classes.ContainsKey(cls)))
        {
            var game = Hero(cls, 3, "###########", "#,,,,,,,,,#", "#,,,,@,,,,#", "#,,,,,,,,,#", "###########");
            ReachLevel(game, 50);
            var foe = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(1, 0));
            foe.Hp = foe.MaxHp = 5000;
            game.UpdateView();
            if (GameSession.NeedsCurseChoice(spell.Effect)) TestGames.GiveCursedItem(game);
            CastUntilItWorks(game, spell.Id, dir: spell.NeedsDirection ? Direction.East : null);
            Assert.False(game.Player.IsDead, $"{cls} died casting {spell.Id}");
        }
    }

    // --- Necromancers -----------------------------------------------------------------------------

    [Fact]
    public void Necromancers_SeeWithoutLight_AndFumbleOnLitGround()
    {
        var game = Hero("necromancer", 1, "#########", "#.......#", "#...@...#", "#.......#", "#,,,,,,,#", "#########");
        // 4.2.5's necromancer starts without a light at all.
        Assert.Null(game.Player.Inventory.Equipped.FirstOrDefault(i => i.Base.Slot == EquipSlot.Light));
        Assert.Equal(1, game.Player.Resists.GetValueOrDefault("dark"));

        var rat = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(1, 0), awake: false);
        game.UpdateView();
        Assert.True(rat.IsVisible, "unlight shows the square next door");

        var bolt = game.Data.Spell("nether_bolt")!;
        var inDark = game.SpellFailChance(bolt);
        game.Level[game.Player.Position].Flags |= Angband.Core.World.SquareFlags.Glow;
        Assert.Equal(Math.Min(95, inDark + 25), game.SpellFailChance(bolt));
    }

    [Fact]
    public void TapUnlife_TurnsUndeadDamageIntoMana()
    {
        var game = Hero("necromancer", 2);
        var ghost = Arena.AddMonster(game, Race(r => r.Has(MonsterFlags.Undead) && !r.Has(MonsterFlags.Invisible)).Id, game.Player.Position + new Loc(2, 0));
        ghost.Hp = ghost.MaxHp = 1000;
        game.UpdateView();
        CastUntilItWorks(game, "tap_unlife");
        Assert.True(ghost.Hp < 1000);
        Assert.True(game.Player.Mana > 0);
    }

    [Fact]
    public void Crush_KillsOnlyTheWeak()
    {
        var game = Hero("necromancer", 3);
        var weak = Arena.AddMonster(game, "jackal", game.Player.Position + new Loc(2, 0));
        var strong = Arena.AddMonster(game, "jackal", game.Player.Position + new Loc(-2, 0));
        ReachLevel(game, 10);
        weak.Hp = 5;
        strong.Hp = strong.MaxHp = 500;
        game.UpdateView();
        CastUntilItWorks(game, "crush");
        Assert.True(weak.IsRemoved);
        Assert.False(strong.IsRemoved);
    }

    [Fact]
    public void VampireStrike_LeapsToTheVictim_AndFeeds()
    {
        var game = Hero("necromancer", 4);
        var victim = Arena.AddMonster(game, "jackal", game.Player.Position + new Loc(4, 0), awake: false);
        victim.Hp = victim.MaxHp = 1000;
        game.UpdateView();
        ReachLevel(game, 20);
        game.Player.Hp = game.Player.MaxHp / 2;
        var hurt = game.Player.Hp;
        CastUntilItWorks(game, "vampire_strike");
        Assert.True(game.Player.Position.ChebyshevTo(victim.Position) <= 1);
        Assert.True(victim.Hp < 1000);
        Assert.True(game.Player.Hp > hurt);
    }

    [Fact]
    public void DispelLife_SparesTheUndead()
    {
        var game = Hero("necromancer", 5);
        var living = Arena.AddMonster(game, "jackal", game.Player.Position + new Loc(2, 0));
        var undead = Arena.AddMonster(game, Race(r => r.Has(MonsterFlags.Undead) && !r.Has(MonsterFlags.Invisible)).Id, game.Player.Position + new Loc(-2, 0));
        living.Hp = living.MaxHp = undead.Hp = undead.MaxHp = 1000;
        game.UpdateView();
        CastUntilItWorks(game, "dispel_life");
        Assert.True(living.Hp < 1000);
        Assert.Equal(1000, undead.Hp);
    }

    // --- Blackguards ------------------------------------------------------------------------------

    [Fact]
    public void Blackguards_ManaComesFromFighting()
    {
        var game = Hero("blackguard", 6);
        ReachLevel(game, 20);
        Assert.True(game.Player.MaxMana > 0);

        // It ebbs away at rest...
        game.Player.Mana = game.Player.MaxMana;
        TestGames.HoldUntil(game, game.GameTurn + 2000);
        Assert.True(game.Player.Mana < game.Player.MaxMana);

        // ...and comes back from attacking and from being hurt.
        game.Player.Mana = 0;
        var foe = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(1, 0));
        foe.Hp = foe.MaxHp = 10_000;
        game.Execute(new WalkCommand(Direction.East));
        var afterAttack = game.Player.Mana;
        Assert.True(afterAttack > 0);
        game.TakeHit(game.Player.MaxHp / 4, "a test");
        Assert.True(game.Player.Mana > afterAttack);
    }

    [Fact]
    public void ShieldBearers_SometimesBash()
    {
        var game = Hero("blackguard", 7);
        var messages = Messages(game);
        var foe = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(1, 0));
        for (var i = 0; i < 300 && !messages.Contains("You get in a shield bash!"); i++)
        {
            foe.Hp = foe.MaxHp = 10_000;
            game.Player.Hp = game.Player.MaxHp;
            game.Execute(new WalkCommand(Direction.East));
        }
        Assert.Contains("You get in a shield bash!", messages);
    }

    [Fact]
    public void WhirlwindAttack_StrikesEveryNeighbour()
    {
        var game = Hero("blackguard", 8);
        var foes = new[] { new Loc(1, 0), new Loc(-1, 0), new Loc(0, 1) }
            .Select(d => Arena.AddMonster(game, "cave_orc", game.Player.Position + d)).ToList();
        foreach (var f in foes) f.Hp = f.MaxHp = 10_000;
        game.UpdateView();
        var attacked = new HashSet<int>();
        using var sub = game.Events.Subscribe<PlayerAttackEvent>(a => attacked.Add(a.MonsterId));
        CastUntilItWorks(game, "whirlwind_attack");
        Assert.Equal(foes.Select(f => f.Id).Order(), attacked.Order());
    }

    // --- Paladins ---------------------------------------------------------------------------------

    [Fact]
    public void Paladins_FightBetterWithHaftedWeapons()
    {
        var game = GameSession.NewGame(TestData.Game, 9, "paladin");
        var withBlade = game.Player.ToHit;
        game.Execute(new WieldCommand(game.Player.Inventory.Add(game.Objects.Create("mace"))!));
        Assert.Equal(withBlade + 2, game.Player.ToHit);
    }

    // --- Rogues -----------------------------------------------------------------------------------

    private static MonsterRaceDef Mark => Race(r => r.Has("DROP_1") && r.Has("ONLY_GOLD") && !r.Has("NEVER_MOVE"));

    [Fact]
    public void Rogues_StealFromSleepingMonsters()
    {
        var game = Hero("rogue", 10);
        var mark = Arena.AddMonster(game, Mark.Id, game.Player.Position + new Loc(1, 0), awake: false);
        game.UpdateView();
        game.Player.BaseStealth = 50;
        game.RecalculateBonuses();
        var gold = game.Player.Gold;
        Assert.True(game.Execute(new StealCommand(Direction.East)));
        Assert.True(game.Player.Gold > gold);

        // What was stolen is gone: the loot is rolled once, and carried (and saved) from then on.
        Assert.True(mark.LootRolled);
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.True(loaded.Level.Monsters.All.Single(m => m.Race.Id == mark.Race.Id).LootRolled);
    }

    [Fact]
    public void ClumsyThieves_AreCaught()
    {
        var game = Hero("rogue", 11);
        var messages = Messages(game);
        var mark = Arena.AddMonster(game, Mark.Id, game.Player.Position + new Loc(1, 0), awake: false);
        game.UpdateView();
        game.Player.BaseStealth = -100;
        game.RecalculateBonuses();
        game.Execute(new StealCommand(Direction.East));
        Assert.Equal(0, mark.Sleep);
        Assert.Contains(messages, m => m.Contains("cries out") || m.StartsWith("You fail to steal"));
    }

    [Fact]
    public void OnlyRogues_Steal()
    {
        var game = Hero("warrior", 12);
        Arena.AddMonster(game, Mark.Id, game.Player.Position + new Loc(1, 0), awake: false);
        game.UpdateView();
        Assert.False(game.Execute(new StealCommand(Direction.East)));
    }
}
