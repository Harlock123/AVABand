using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Randomness;

namespace Angband.Tests;

/// <summary>
/// The object kinds brought into line with Angband 4.2.5's object.txt, and what came with them:
/// allocation by the alloc range alone, exploding flasks, M-percentage teleports, the rings' own
/// flags (fear, slow healing, feather falling) and the gem treasures.
/// </summary>
public class ObjectData425Tests
{
    private static Item Wear(GameSession game, string kindId)
    {
        var item = game.Player.Inventory.Add(game.Objects.Create(kindId, 1))!;
        game.Execute(new WieldCommand(item));
        Assert.Contains(item, game.Player.Inventory.Equipped);
        return item;
    }

    [Fact]
    public void A_kind_is_made_across_its_alloc_range_whatever_its_level()
    {
        // 4.2.5: mithril arrows are level 55 but allocated from 50.
        var data = TestData.Game;
        var arrows = data.Object("mithril_arrow")!;
        Assert.Equal((55, 50), (arrows.Level, arrows.MinDepth!.Value));
        var factory = new ObjectFactory(data);
        Assert.NotNull(factory.PickKind(new GameRandom(3), 50, k => k.Id == "mithril_arrow"));
    }

    [Fact]
    public void A_thrown_flask_explodes_for_three_times_the_damage_and_is_gone()
    {
        var game = Arena.Create(5);
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(3, 0));
        orc.Hp = orc.MaxHp = 100_000;
        game.UpdateView();
        var hits = new List<int>();
        using var sub = game.Events.Subscribe<PlayerAttackEvent>(a => { if (a.Hit) hits.Add(a.Damage); });
        game.Player.SkillThrow = 500;
        for (var i = 0; i < 20 && hits.Count < 5; i++)
            game.Execute(new ThrowCommand(game.Player.Inventory.Add(game.Objects.Create("flask_of_oil", 1))!, orc.Position));
        Assert.NotEmpty(hits);
        // 1d4, times 3 as a throwing weapon (2 + 20/12), times 3 again for exploding.
        Assert.All(hits, d => Assert.True(d >= 9 && d % 3 == 0, $"{d}"));
        Assert.DoesNotContain(game.Level.Objects.All.Select(p => p.Item), i => i.Kind.Id == "flask_of_oil");
    }

    [Fact]
    public void Throwing_weapons_seldom_break()
    {
        var game = Arena.Create(7);
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(3, 0));
        orc.Hp = orc.MaxHp = 100_000;
        game.UpdateView();
        game.Player.SkillThrow = 500;
        for (var i = 0; i < 30; i++)
            game.Execute(new ThrowCommand(game.Player.Inventory.Add(game.Objects.Create("dagger", 1))!, orc.Position));
        var landed = game.Level.Objects.All.Select(p => p.Item).Where(i => i.Kind.Id == "dagger").Sum(i => i.Number);
        var carried = game.Player.Inventory.Pack.Concat(game.Player.Inventory.Quiver).Where(i => i.Kind.Id == "dagger").Sum(i => i.Number);
        Assert.True(landed + carried >= 27, $"{landed} landed and {carried} carried of 30 daggers"); // 1 in 100 breaks
    }

    [Fact]
    public void The_Ring_of_Escaping_keeps_you_afraid()
    {
        var game = Arena.Create(9);
        var toHit = game.Player.ToHit;
        var armour = game.Player.Armour;
        Wear(game, "ring_of_escaping");
        Assert.True(game.PlayerAfraid);
        Assert.Equal(toHit - 20, game.Player.ToHit);
        Assert.Equal(armour + 8, game.Player.Armour);

        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(1, 0));
        var hp = orc.Hp;
        var messages = new List<string>();
        using var sub = game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));
        game.Execute(new WalkCommand(Direction.East));
        Assert.Contains(messages, m => m.StartsWith("You are too afraid to attack", StringComparison.Ordinal));
        Assert.Equal(hp, orc.Hp);
    }

    [Fact]
    public void The_Ring_of_Open_Wounds_slows_healing()
    {
        int Healed(string? ring)
        {
            var game = Arena.Create(11);
            if (ring is not null) Wear(game, ring);
            game.Player.MaxHp = 500;
            game.Player.Hp = 100;
            for (var i = 0; i < 50; i++) game.Execute(new HoldCommand());
            return game.Player.Hp - 100;
        }
        var normal = Healed(null);
        var impaired = Healed("ring_of_open_wounds");
        Assert.True(impaired < normal * 3 / 4, $"impaired {impaired}, normal {normal}");
    }

    [Fact]
    public void The_rings_flags_are_4_2_5s()
    {
        var data = TestData.Game;
        Assert.Contains(ItemFlags.Feather, data.Object("ring_of_feather_falling")!.Flags);
        Assert.Contains(ItemFlags.Afraid, data.Object("ring_of_escaping")!.Flags);
        Assert.Contains(ItemFlags.ImpairHp, data.Object("ring_of_open_wounds")!.Flags);
        Assert.Equal(4, data.Object("heavy_crossbow")!.Multiplier);
    }

    [Fact]
    public void Teleportation_goes_a_share_of_the_level()
    {
        // 4.2.5's Scroll of Teleportation is "M60": 60% of the level's larger side, give or take.
        var game = GameSession.NewGame(TestData.Game, 13);
        game.Execute(new DebugJumpCommand(5));
        for (var depth = 6; game.Level.Width < 150; depth++) game.Execute(new DebugJumpCommand(depth)); // a full-sized level
        Assert.Equal("teleport:M60", game.Data.Object("teleportation")!.Effect);
        var reach = Math.Max(game.Level.Width, game.Level.Height) * 60 / 100;
        var far = 0;
        for (var i = 0; i < 20; i++)
        {
            var from = game.Player.Position;
            game.Execute(new UseCommand(game.Player.Inventory.Add(game.Objects.Create("teleportation", 1))!));
            if (from.DistanceTo(game.Player.Position) > 20) far++;
        }
        Assert.True(reach > 40);
        Assert.True(far >= 15, $"{far} of 20 went more than 20 squares");
    }

    [Fact]
    public void Potions_come_in_piles_as_often_as_4_2_5_says()
    {
        // Cure Light Wounds: pile:90:2d3 — nine times in ten a pile of 2 to 6, else one.
        var data = TestData.Game;
        var clw = data.Object("cure_light_wounds")!;
        Assert.Equal(90, clw.PileChance);
        var factory = new ObjectFactory(data);
        var rng = new GameRandom(17);
        var numbers = Enumerable.Range(0, 2000)
            .Select(_ => factory.Make(rng, 5, good: false, great: false))
            .Where(i => i?.Kind == clw).Select(i => i!.Number).ToList();
        Assert.True(numbers.Count > 20, $"{numbers.Count} made");
        Assert.All(numbers, n => Assert.InRange(n, 1, 6));
        var ones = numbers.Count(n => n == 1);
        Assert.InRange(ones, 1, numbers.Count / 3);
    }

    [Fact]
    public void Gems_are_treasure_in_4_2_5s_order()
    {
        var golds = TestData.Game.Objects.Where(k => k.Base == "gold").Select(k => k.Id).ToList();
        Assert.Equal(["copper", "silver", "garnets", "gold", "opals", "sapphires", "rubies", "diamonds", "emeralds", "mithril", "adamantite"], golds);
    }
}
