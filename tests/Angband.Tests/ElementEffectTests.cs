using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;

namespace Angband.Tests;

/// <summary>
/// What breaths, balls and bolts do besides damage (Angband 4.2 project-player.c), and the pack
/// damage from fire, acid, lightning and cold (project-obj.c inven_damage).
/// </summary>
public class ElementEffectTests
{
    private static GameSession Game()
    {
        var game = Arena.Create(6,
            "###############",
            "#,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,#",
            "#,,,,,,@,,,,,,#",
            "#,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,#",
            "###############");
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        return game;
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    private static Item Carry(GameSession game, string kind, int number = 1)
    {
        var item = game.Objects.Create(kind, number);
        game.Knowledge.LearnKind(item.Kind);
        return game.Player.Inventory.Add(item)!;
    }

    // --- The pack -----------------------------------------------------------------------------

    [Fact]
    public void Fire_BurnsScrolls_ButNotPotions_NorWhatIsProofAgainstIt()
    {
        var game = Game();
        var scrolls = Carry(game, "phase_door", 20);
        var potions = Carry(game, "cure_light_wounds", 20); // (joins the kit's potions)
        var potionCount = potions.Number;
        var arrows = game.Objects.Create("mithril_arrow", 20); // IGNORE_ACID | IGNORE_FIRE
        game.Player.Inventory.Add(arrows);
        var messages = Messages(game);

        for (var i = 0; i < 10; i++) game.InventoryDamage("fire", 300);

        Assert.Contains(messages, m => m.Contains("destroyed"));
        Assert.Equal(potionCount, potions.Number);
        Assert.Equal(20, arrows.Number);
        Assert.Contains(messages, m => m.Contains("Phase Door") && m.EndsWith("destroyed!"));
    }

    [Fact]
    public void Cold_ShattersPotions_AndWeaponsInThePackAreDamagedNotDestroyed()
    {
        var game = Game();
        var potions = Carry(game, "cure_light_wounds", 30);
        var potionCount = potions.Number;
        var spear = game.Objects.Create("spear");
        spear.ToHit = spear.ToDam = 5;
        game.Player.Inventory.Add(spear);
        for (var i = 0; i < 40; i++) game.InventoryDamage("cold", 300);
        Assert.True(potions.Number < potionCount || !game.Player.Inventory.Pack.Contains(potions));

        for (var i = 0; i < 200; i++) game.InventoryDamage("acid", 300);
        Assert.Contains(spear, game.Player.Inventory.Pack); // still there…
        Assert.True(spear.ToHit < 5 && spear.ToDam < 5);  // …but the worse for it
    }

    [Fact]
    public void WornGear_AndArtifacts_AreSafe_AndImmunityProtectsThePack()
    {
        var game = Game();
        var body = game.Player.Inventory.Equipped.FirstOrDefault(i => i.Base.Slot == Angband.Core.Definitions.EquipSlot.Body);
        var toAc = body?.ToAc;
        for (var i = 0; i < 200; i++) game.InventoryDamage("acid", 300);
        Assert.Equal(toAc, body?.ToAc);

        var scrolls = Carry(game, "phase_door", 20);
        var count = scrolls.Number;
        game.Player.Resists["fire"] = 3; // immune
        for (var i = 0; i < 20; i++) game.ElementalHit("fire", 60, "a fire breath");
        Assert.Equal(count, scrolls.Number);
    }

    [Fact]
    public void AFireBreath_BurnsThePack()
    {
        var game = Game();
        var scrolls = Carry(game, "phase_door", 40);
        var count = scrolls.Number;
        for (var i = 0; i < 10; i++) game.ElementalHit("fire", 60, "a fire breath");
        Assert.True(scrolls.Number < count || !game.Player.Inventory.Pack.Contains(scrolls));
    }

    // --- What each element does -----------------------------------------------------------------

    [Fact]
    public void Water_Confuses_AndStuns()
    {
        var game = Game();
        game.ElementalHit("water", 50, "a water bolt");
        Assert.True(game.Player.Timed.Has(TimedIds.Confused));
        Assert.True(game.Player.Timed.Has(TimedIds.Stun));
    }

    [Fact]
    public void Gravity_Slows_Stuns_AndMayBlinkYou()
    {
        var game = Game();
        var messages = Messages(game);
        var moved = false;
        for (var i = 0; i < 10 && !moved; i++)
        {
            var at = game.Player.Position;
            game.ElementalHit("gravity", 30, "a gravity breath");
            moved = game.Player.Position != at;
        }
        Assert.Contains("Gravity warps around you.", messages);
        Assert.True(game.Player.Timed.Has(TimedIds.Slow));
        Assert.True(game.Player.Timed.Has(TimedIds.Stun));
        Assert.True(moved);
    }

    [Fact]
    public void Inertia_Slows_UnlessYouHaveFreeAction()
    {
        var game = Game();
        game.ElementalHit("inertia", 30, "an inertia breath");
        Assert.True(game.Player.Timed.Has(TimedIds.Slow));

        var free = Game();
        free.Player.Resists["free_act"] = 1;
        free.ElementalHit("inertia", 30, "an inertia breath");
        Assert.False(free.Player.Timed.Has(TimedIds.Slow));
    }

    [Fact]
    public void Force_ThrowsYouBack_AwayFromTheBreather()
    {
        var game = Game();
        var start = game.Player.Position;
        game.ElementalHit("force", 40, "a force breath", 30, start + new Loc(-3, 0));
        Assert.True(game.Player.Position.X > start.X);
        Assert.Equal(start.Y, game.Player.Position.Y);
        Assert.True(game.Player.Timed.Has(TimedIds.Stun));
    }

    [Fact]
    public void Nexus_Teleports_UnlessResisted()
    {
        var game = Game();
        game.Player.Level = 1; // (a low saving throw keeps the level teleport test-proof: the arena has no stairs to lose)
        var moved = 0;
        for (var i = 0; i < 12; i++)
        {
            var at = game.Player.Position;
            var depth = game.Player.Depth;
            game.ElementalHit("nexus", 20, "a nexus breath", 30, new Loc(1, 1));
            if (game.Player.Position != at || game.Player.Depth != depth) moved++;
        }
        Assert.True(moved > 0);

        var resistant = Game();
        resistant.Player.Resists["nexus"] = 1;
        var start = resistant.Player.Position;
        resistant.ElementalHit("nexus", 20, "a nexus breath", 30, new Loc(1, 1));
        Assert.Equal(start, resistant.Player.Position);
        Assert.False(resistant.Player.Timed.Has("scrambled"));
    }

    [Fact]
    public void Nether_DrainsExperience_UnlessResistedOrHoldingLife()
    {
        var game = Game();
        game.GainExperience(50_000);
        var exp = game.Player.Experience;
        game.ElementalHit("nether", 30, "a nether bolt");
        Assert.True(game.Player.Experience < exp);

        var held = Game();
        held.GainExperience(50_000);
        held.Player.Resists["hold_life"] = 1;
        var kept = held.Player.Experience;
        held.ElementalHit("nether", 30, "a nether bolt");
        Assert.Equal(kept, held.Player.Experience);
    }

    [Fact]
    public void Time_TakesExperienceOrStats()
    {
        var game = Game();
        game.GainExperience(50_000);
        var exp = game.Player.Experience;
        var drained = () => game.Player.StatDrain.Values.Sum();
        for (var i = 0; i < 10; i++) game.ElementalHit("time", 20, "a time breath");
        Assert.True(game.Player.Experience < exp || drained() > 0);
    }

    [Fact]
    public void Sound_Stuns_Shards_Cut_Light_Blinds_Plasma_Stuns()
    {
        var game = Game();
        game.ElementalHit("sound", 30, "a sound breath");
        Assert.True(game.Player.Timed.Has(TimedIds.Stun));

        game = Game();
        game.ElementalHit("shards", 30, "a shard breath");
        Assert.True(game.Player.Timed.Has(TimedIds.Cut));

        game = Game();
        game.ElementalHit("light", 30, "a light breath");
        Assert.True(game.Player.IsBlind);

        game = Game();
        game.ElementalHit("plasma", 30, "a plasma bolt");
        Assert.True(game.Player.Timed.Has(TimedIds.Stun));
    }

    [Fact]
    public void Resisting_TheElement_StopsItsSideEffect()
    {
        var game = Game();
        foreach (var r in new[] { "sound", "shards", "light", "dark" }) game.Player.Resists[r] = 1;
        game.ElementalHit("sound", 30, "a sound breath");
        game.ElementalHit("shards", 30, "a shard breath");
        game.ElementalHit("light", 30, "a light breath");
        game.ElementalHit("dark", 30, "a dark breath");
        Assert.False(game.Player.Timed.Has(TimedIds.Stun));
        Assert.False(game.Player.Timed.Has(TimedIds.Cut));
        Assert.False(game.Player.IsBlind);
    }

    [Fact]
    public void Plasma_AndIce_AreTheirOwnKinds_NotFireAndCold()
    {
        Assert.Equal("plasma", TestData.Game.MonsterSpell("BR_PLAS")!.Element);
        Assert.Equal("ice", TestData.Game.MonsterSpell("BO_ICE")!.Element);
        Assert.Equal("force", TestData.Game.MonsterSpell("BR_WALL")!.Element);

        // So resisting fire doesn't halve a plasma bolt.
        var game = Game();
        game.Player.Resists["fire"] = 3;
        var hp = game.Player.Hp;
        game.ElementalHit("plasma", 100, "a plasma bolt");
        Assert.Equal(hp - 100, game.Player.Hp);
    }
}
