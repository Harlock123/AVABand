using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;

namespace Angband.Tests;

/// <summary>Items in play: starting kit, equipment bonuses, commands, identification, curses.</summary>
public class ItemPlayTests
{
    private static readonly Loc Start = new(5, 2);

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    [Fact]
    public void StartingKit_IsEquippedAndKnown()
    {
        var game = Arena.Create();
        var inv = game.Player.Inventory;
        Assert.Equal("dagger", inv.Weapon!.Kind.Id);
        Assert.Equal("sling", inv.Bow!.Kind.Id);
        Assert.Equal(40, inv.Quiver.Sum(q => q.Number));
        Assert.Equal(8 + 1, game.Player.Armour); // soft leather, plus 1 for the warrior's DEX 17
        Assert.Equal(2, game.Player.LightRadius);
        Assert.Equal("a Dagger (1d4) (+0,+0)", game.Describe(inv.Weapon));
        Assert.Contains(inv.Pack, i => game.Describe(i) == "2 Potions of Cure Light Wounds");
    }

    [Fact]
    public void Equipment_ChangesStats()
    {
        var game = Arena.Create();
        var ring = game.Objects.Create("ring_of_protection");
        ring.ToAc = 10;
        game.Player.Inventory.Add(ring);
        var armour = game.Player.Armour;
        game.Execute(new WieldCommand(ring));
        Assert.Equal(armour + 10, game.Player.Armour);

        var boots = game.Objects.Create("leather_boots");
        boots.Modifiers["speed"] = 5;
        game.Player.Inventory.Add(boots);
        game.Execute(new WieldCommand(boots));
        Assert.Equal(5, game.Player.Speed);
    }

    [Fact]
    public void Burden_SlowsThePlayer()
    {
        var game = Arena.Create();
        Assert.Equal(0, game.Player.Speed);
        for (var i = 0; i < 6; i++) game.Player.Inventory.Add(game.Objects.Create("metal_scale_mail"));
        game.RecalculateBonuses();
        Assert.True(game.Player.Speed < 0);
    }

    [Fact]
    public void Gold_IsPickedUpByWalkingOverIt()
    {
        var game = Arena.Create();
        var gold = game.Objects.MakeGold(game.Rng, 5);
        game.Level.Objects.Add(Start.Step(Direction.East), gold);
        var before = game.Player.Gold;

        game.Execute(new WalkCommand(Direction.East));

        Assert.Equal(before + gold.GoldValue, game.Player.Gold);
        Assert.Empty(game.Level.Objects.At(game.Player.Position));
    }

    [Fact]
    public void MatchingAmmo_IsPickedUpAutomatically_OtherItemsAreReported()
    {
        var game = Arena.Create();
        var messages = Messages(game);
        game.Level.Objects.Add(Start.Step(Direction.East), game.Objects.Create("iron_shot", 5));
        game.Level.Objects.Add(Start.Step(Direction.East), game.Objects.Create("whip"));

        game.Execute(new WalkCommand(Direction.East));

        Assert.Equal(45, game.Player.Inventory.Quiver.Sum(q => q.Number));
        Assert.Single(game.Level.Objects.At(game.Player.Position));
        Assert.Contains(messages, m => m.StartsWith("You see a Whip"));
    }

    [Fact]
    public void PickupAndDrop_MoveItemsBetweenFloorAndPack()
    {
        var game = Arena.Create();
        var whip = game.Objects.Create("whip");
        game.Level.Objects.Add(Start, whip);

        Assert.True(game.Execute(new PickupCommand()));
        Assert.Contains(whip, game.Player.Inventory.Pack);

        Assert.True(game.Execute(new DropCommand(whip)));
        Assert.Contains(whip, game.Level.Objects.At(Start));
        Assert.DoesNotContain(whip, game.Player.Inventory.Pack);
    }

    [Fact]
    public void QuaffingAnUnknownPotion_TeachesWhatItIs()
    {
        var game = Arena.Create();
        var messages = Messages(game);
        var potion = game.Objects.Create("cure_serious_wounds");
        game.Player.Inventory.Add(potion);
        game.Player.Hp = 5;
        Assert.False(game.Knowledge.IsAware(potion.Kind));

        game.Execute(new UseCommand(potion));

        Assert.True(game.Knowledge.IsAware(potion.Kind));
        Assert.True(game.Player.Hp > 5);
        Assert.Contains(messages, m => m.Contains("Potion of Cure Serious Wounds"));
        Assert.DoesNotContain(potion, game.Player.Inventory.Pack);
    }

    [Fact]
    public void UselessUse_MarksThePotionTried()
    {
        var game = Arena.Create();
        game.Player.Inventory.Add(game.Objects.Create("neutralize_poison", 2));
        var potion = game.Player.Inventory.Pack.First(i => i.Kind.Id == "neutralize_poison");

        game.Execute(new UseCommand(potion));

        Assert.False(game.Knowledge.IsAware(potion.Kind));
        Assert.EndsWith("{tried}", game.Describe(game.Player.Inventory.Pack.First(i => i.Kind == potion.Kind)));
    }

    [Fact]
    public void PhaseDoor_Teleports()
    {
        var game = Arena.Create(1,
            "#####################",
            "#,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,@,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,#",
            "#####################");
        var scroll = game.Player.Inventory.Pack.First(i => i.Kind.Id == "phase_door");
        var before = game.Player.Position;

        game.Execute(new UseCommand(scroll));

        Assert.NotEqual(before, game.Player.Position);
        Assert.True(before.DistanceTo(game.Player.Position) <= 10);
    }

    [Fact]
    public void HittingWithAnUnknownWeapon_TeachesItsRunes()
    {
        var game = Arena.Create();
        var messages = Messages(game);
        var sword = game.Objects.Create("long_sword");
        sword.Ego = TestData.Game.Egos.Single(e => e.Id == "slay_animal");
        sword.Slays.AddRange(sword.Ego.Slays);
        game.Player.Inventory.Add(sword);
        game.Execute(new WieldCommand(sword));
        game.Player.SkillMelee = 1000;

        // Forget the starting kit's to-hit/to-dam runes to watch them being learned.
        var fresh = new PlayerKnowledge(TestData.Game, game.Seed);
        typeof(GameSession).GetProperty(nameof(GameSession.Knowledge))!.SetValue(game, fresh);
        Assert.False(game.Knowledge.IsFullyKnown(sword));

        var jackal = Arena.AddMonster(game, "jackal", Start.Step(Direction.East));
        jackal.Hp = jackal.MaxHp = 10_000;
        for (var i = 0; i < 20 && !game.Knowledge.IsFullyKnown(sword); i++) game.Execute(new WalkCommand(Direction.East));

        Assert.True(game.Knowledge.IsFullyKnown(sword));
        Assert.Contains(messages, m => m == "You have learned the rune of slay animal.");
        Assert.Contains(messages, m => m.Contains("Long Sword of Slay Animal"));
    }

    [Fact]
    public void PassiveCurses_AreNoticedOnWielding_AndApplyPenalties()
    {
        var game = Arena.Create();
        var cloak = game.Objects.Create("cloak");
        cloak.Curses.Add("vulnerability");
        game.Player.Inventory.Add(cloak);
        var armour = game.Player.Armour;

        game.Execute(new WieldCommand(cloak));

        Assert.True(game.Knowledge.KnowsRune(RuneIds.Curse("vulnerability")));
        Assert.Equal(armour + 1 - 20, game.Player.Armour);
        Assert.Contains("{cursed}", game.Describe(cloak));
    }

    [Fact]
    public void ActiveCurses_EventuallyFire_AndRemoveCurseBreaksThem()
    {
        var game = Arena.Create(2,
            "#####################",
            "#,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,@,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,#",
            "#####################");
        var ring = game.Objects.Create("ring_of_teleportation");
        game.Player.Inventory.Add(ring);
        game.Execute(new WieldCommand(ring));
        var teleported = false;
        game.Events.Subscribe<PlayerMovedEvent>(_ => teleported = true);

        TestGames.HoldUntil(game, game.GameTurn + 10 * 3000);

        Assert.True(teleported);
        Assert.True(game.Knowledge.KnowsRune(RuneIds.Curse("teleportation")));

        var scroll = game.Objects.Create("remove_curse");
        game.Player.Inventory.Add(scroll);
        game.Execute(new UseCommand(scroll));
        Assert.False(ring.IsCursed);
    }

    [Fact]
    public void Torches_BurnOut()
    {
        var game = Arena.Create();
        var messages = Messages(game);
        game.Player.Inventory.Light!.Fuel = 102;

        TestGames.HoldUntil(game, game.GameTurn + 10 * 110);

        Assert.Equal(0, game.Player.Inventory.Light.Fuel);
        Assert.Equal(0, game.Player.LightRadius);
        Assert.Contains("Your light is growing faint.", messages);
        Assert.Contains("Your light has gone out!", messages);
    }

    [Fact]
    public void Lanterns_AreRefuelledWithOil()
    {
        var game = Arena.Create();
        var lantern = game.Objects.Create("lantern");
        lantern.Fuel = 100;
        game.Player.Inventory.Add(lantern);
        game.Execute(new WieldCommand(lantern));
        var flask = game.Player.Inventory.Pack.First(i => i.Kind.Id == "flask_of_oil");
        var before = lantern.Fuel; // a turn of fuel burned while putting it on

        Assert.True(game.Execute(new RefuelCommand(flask)));

        Assert.Equal(before + 7500, lantern.Fuel);
        Assert.Equal(1, flask.Number);
    }

    [Fact]
    public void ThrownOil_BurnsMonsters_AndShatters()
    {
        var game = Arena.Create();
        game.Player.SkillThrow = 1000;
        var mold = Arena.AddMonster(game, "grey_mold", new Loc(8, 2));
        mold.Hp = mold.MaxHp = 10_000;
        var flask = game.Player.Inventory.Pack.First(i => i.Kind.Id == "flask_of_oil");
        var hits = new List<PlayerAttackEvent>();
        game.Events.Subscribe<PlayerAttackEvent>(hits.Add);

        // Even a perfect thrower misses now and then (Angband's 5% automatic miss): throw until one lands.
        flask.Number = 5;
        for (var i = 0; i < 5 && hits.Count == 0; i++) game.Execute(new ThrowCommand(flask));

        Assert.Single(hits);
        Assert.True(hits[0].Damage >= 2 * 3, $"{hits[0].Damage}"); // 2d6 x3 fire brand, at least 6
        Assert.Empty(game.Level.Objects.All); // flasks always break, hit or miss
    }

    [Fact]
    public void FiredShots_LandOnTheFloor_OrBreak()
    {
        var game = Arena.Create(4);
        game.Player.SkillBow = -1000; // nearly always miss (12% always hit), so most fly on and land
        var mold = Arena.AddMonster(game, "grey_mold", new Loc(9, 2));
        mold.Hp = mold.MaxHp = 100_000;

        var messages = Messages(game);
        for (var i = 0; i < 10; i++) Assert.True(game.Execute(new FireCommand()), string.Join(" / ", messages.TakeLast(3)));

        var landed = game.Level.Objects.All.Sum(o => o.Item.Number);
        Assert.Equal(30, game.Player.Inventory.Quiver.Sum(q => q.Number));
        Assert.InRange(landed, 5, 10); // misses break 12% of the time, hits 25%
    }

    [Fact]
    public void MonstersWithDropFlags_DropLoot()
    {
        var game = Arena.Create();
        var bullroarer = Arena.AddMonster(game, "bullroarer", new Loc(8, 2));
        game.DamageMonster(bullroarer, 100_000);
        Assert.True(game.Level.Objects.Count >= 1);
    }

    [Fact]
    public void NewLevels_HaveObjectsAndGold()
    {
        var game = GameSession.NewGame(TestData.Game, 21);
        var sawGold = false;
        for (var depth = 3; depth <= 7; depth++)
        {
            game.Execute(new DebugJumpCommand(depth));
            var all = game.Level.Objects.All.ToList();
            Assert.True(all.Count >= 3, $"{all.Count} objects at depth {depth}");
            Assert.All(all, o => Assert.True(game.Level.Has(o.Loc, TerrainFlags.Object)));
            sawGold |= all.Any(o => o.Item.IsGold);
        }
        Assert.True(sawGold);
    }

    [Fact]
    public void ItemsAreDeterministic()
    {
        static string Play()
        {
            var game = GameSession.NewGame(TestData.Game, 99);
            game.Execute(new DebugJumpCommand(10));
            return string.Join("|", game.Level.Objects.All.Select(o => $"{o.Loc}:{game.Describe(o.Item)}:{o.Item.ToHit}:{o.Item.Ego?.Id}"));
        }
        Assert.Equal(Play(), Play());
    }
}
