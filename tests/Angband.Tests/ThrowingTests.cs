using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>Throwing weapons (Angband 4.2 obj-gear.c quiver rules, player-attack.c throws).</summary>
public class ThrowingTests
{
    private static Item Carry(GameSession game, string kind, int number = 1, string? note = null)
    {
        var item = game.Objects.Create(kind, number);
        item.Note = note;
        return game.Player.Inventory.Add(item)!;
    }

    [Fact]
    public void AnInscribedThrowingWeapon_GoesInTheQuiver_AndCountsAsFive()
    {
        var game = Arena.Create(1);
        var inv = game.Player.Inventory;
        var before = inv.QuiverCount;
        var axes = Carry(game, "throwing_axe", 3);
        Assert.Contains(axes, inv.Pack); // not inscribed: an ordinary pack item

        game.Execute(new InscribeCommand(axes, "@v1"));
        Assert.Contains(axes, inv.Quiver);
        Assert.DoesNotContain(axes, inv.Pack);
        Assert.Equal(before + 15, inv.QuiverCount);

        game.Execute(new UninscribeCommand(axes));
        Assert.Contains(axes, inv.Pack);
        Assert.Equal(before, inv.QuiverCount);
    }

    [Fact]
    public void AQuiverStack_HoldsEightThrowingWeapons_AndEachSlotTakesOneKind()
    {
        var game = Arena.Create(2);
        var inv = game.Player.Inventory;
        Assert.Contains(Carry(game, "throwing_axe", 8, "@v1"), inv.Quiver);
        Assert.Contains(Carry(game, "throwing_axe", 1, "@v1"), inv.Pack); // a ninth won't fit the slot
        Assert.Contains(Carry(game, "throwing_hammer", 1, "@v1"), inv.Pack); // slot 1 is taken
        Assert.Contains(Carry(game, "throwing_hammer", 1, "@v2"), inv.Quiver);
    }

    [Fact]
    public void PickingUpATaggedThrowingWeapon_PutsItInTheQuiver()
    {
        var game = Arena.Create(3);
        var dagger = game.Objects.Create("dagger");
        dagger.Note = "@v3";
        game.Level.Objects.Add(game.Player.Position, dagger);
        game.Execute(new PickupCommand(dagger));
        Assert.Contains(game.Player.Inventory.Quiver, i => i.Kind.Id == "dagger");
    }

    [Theory]
    [InlineData("dagger", 3)]         // 1.2 lb: 2 + 12/12
    [InlineData("spear", 6)]          // 5 lb: 2 + 50/12
    [InlineData("throwing_axe", 5)]   // 4 lb
    [InlineData("mace", 1)]           // not made for throwing
    public void ThrowingWeapons_DoMoreDamageThrown(string kind, int multiplier) =>
        Assert.Equal(multiplier, GameSession.ThrowMultiplier(new ObjectFactory(TestData.Game).Create(kind)));

    [Fact]
    public void ThrowRange_FollowsStrengthAndWeight()
    {
        var game = Arena.Create(4);
        game.Player.Stats["str"] = 18; // adj_str_blow 20
        var dagger = game.Objects.Create("dagger");   // 1.2 lb
        var hammer = game.Objects.Create("throwing_hammer"); // 5.5 lb
        Assert.Equal(10, game.ThrowRange(dagger));    // (20+20)*10/12 = 33, capped
        Assert.Equal(7, game.ThrowRange(hammer));     // 400/55
    }

    [Fact]
    public void AThrownDagger_HitsForThreeTimesItsDice()
    {
        var game = Arena.Create(5);
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(3, 0));
        orc.Hp = orc.MaxHp = 100_000;
        game.UpdateView();
        var hits = new List<int>();
        using var sub = game.Events.Subscribe<PlayerAttackEvent>(a => { if (a.Hit) hits.Add(a.Damage); });
        game.Player.SkillThrow = 500;
        for (var i = 0; i < 30 && hits.Count < 5; i++)
        {
            var dagger = Carry(game, "dagger", 1, "@v1");
            game.Execute(new ThrowCommand(dagger, orc.Position));
        }
        Assert.NotEmpty(hits);
        Assert.All(hits, d => Assert.True(d >= 3, $"{d}"));
    }

    [Fact]
    public void TheWieldedWeapon_CanBeThrown()
    {
        var game = Arena.Create(6);
        var weapon = game.Player.Inventory.Weapon!;
        var target = game.Player.Position + new Loc(3, 0);
        Assert.True(game.Execute(new ThrowCommand(weapon, target)));
        Assert.Null(game.Player.Inventory.Weapon);
        var armour = game.Player.Inventory.Equipped.First(i => i.Base.Id == "soft_armour");
        Assert.False(game.Execute(new ThrowCommand(armour, target)));
    }

    [Fact]
    public void TheQuiver_IsSaved()
    {
        var game = Arena.Create(7);
        Carry(game, "throwing_axe", 2, "@v1");
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Contains(loaded.Player.Inventory.Quiver, i => i.Kind.Id == "throwing_axe" && i.Number == 2);
        Assert.Equal(game.Player.Inventory.QuiverCount, loaded.Player.Inventory.QuiverCount);
    }
}
