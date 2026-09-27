using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>Inscriptions (Angband obj-util.c, cmd-pickup.c, obj-gear.c).</summary>
public class InscriptionTests
{
    private static Item Carry(GameSession game, string kind, int number = 1) =>
        game.Player.Inventory.Add(game.Objects.Create(kind, number))!;

    [Fact]
    public void Inscriptions_ShowInBraces_WithTheOtherNotes()
    {
        var knowledge = new PlayerKnowledge(TestData.Game, 1);
        var dagger = new ObjectFactory(TestData.Game).Create("dagger");
        dagger.ToHit = 2;
        dagger.Note = "@w1";
        Assert.Equal("a Dagger (1d4) {@w1, ??}", ItemNaming.Describe(dagger, knowledge));
        knowledge.LearnRune(Angband.Core.Definitions.RuneIds.ToHit);
        knowledge.LearnRune(Angband.Core.Definitions.RuneIds.ToDam);
        Assert.Equal("a Dagger (1d4) (+2,+0) {@w1}", ItemNaming.Describe(dagger, knowledge));
    }

    [Fact]
    public void DifferentInscriptions_KeepStacksApart_ButABlankOneJoins()
    {
        var game = Arena.Create(2);
        var a = game.Objects.Create("cure_light_wounds");
        var b = game.Objects.Create("cure_light_wounds");
        a.Note = "@q1";
        b.Note = "!q";
        Assert.False(a.CanStackWith(b));
        b.Note = null;
        Assert.True(a.CanStackWith(b));

        // Merging keeps the inscription (Angband object_absorb).
        var inv = game.Player.Inventory;
        foreach (var p in inv.Pack.Where(i => i.Kind == a.Kind).ToList()) inv.Remove(p, p.Number, () => game.Objects.NextSerial++);
        var stack = inv.Add(b)!;
        Assert.Same(stack, inv.Add(a));
        Assert.Equal("@q1", stack.Note);
    }

    [Fact]
    public void InscribingTakesNoTime_AndEmptyTextRemovesIt()
    {
        var game = Arena.Create(3);
        var potion = Carry(game, "speed");
        var turn = game.GameTurn;
        Assert.False(game.Execute(new InscribeCommand(potion, "  @q1 ")));
        Assert.Equal("@q1", potion.Note);
        Assert.Equal(turn, game.GameTurn);
        game.Execute(new InscribeCommand(potion, ""));
        Assert.Null(potion.Note);

        // Objects underfoot can be inscribed too, not ones elsewhere.
        var here = game.Objects.Create("dagger");
        var away = game.Objects.Create("dagger");
        game.Level.Objects.Add(game.Player.Position, here);
        game.Level.Objects.Add(game.Player.Position + new Loc(2, 0), away);
        game.Execute(new InscribeCommand(here, "=g"));
        game.Execute(new InscribeCommand(away, "=g"));
        Assert.Equal("=g", here.Note);
        Assert.Null(away.Note);

        game.Execute(new UninscribeCommand(here));
        Assert.Null(here.Note);
    }

    [Theory]
    [InlineData(null, false, false, 0)] // nothing asks for it
    [InlineData("=g", false, false, 5)] // always
    [InlineData("!g", true, true, 5)]   // pickup_always overrides !g
    [InlineData("!g", false, true, 0)]  // never
    [InlineData("=g3", false, false, 3)] // up to three
    public void AutoPickup_FollowsInscriptionsAndOptions(string? note, bool always, bool inven, int expected)
    {
        var game = Arena.Create(4);
        var arrows = game.Objects.Create("arrow", 5);
        arrows.Note = note;
        Assert.Equal(expected, Inscription.AutoPickupCount(arrows, 5, [], always, inven));
    }

    [Fact]
    public void AutoPickup_CountsWhatThePackAlreadyHolds()
    {
        var game = Arena.Create(5);
        var carried = game.Objects.Create("arrow", 7);
        carried.Note = "=g10";
        var floor = game.Objects.Create("arrow", 20);
        Assert.Equal(3, Inscription.AutoPickupCount(floor, 20, [carried], pickupAlways: false, pickupInven: true));
        carried.Note = "!g";
        Assert.Equal(0, Inscription.AutoPickupCount(floor, 20, [carried], pickupAlways: false, pickupInven: true));
    }

    [Fact]
    public void WalkingOntoAPartlyWantedStack_PicksUpOnlyPartOfIt()
    {
        var game = Arena.Create(6);
        var flasks = game.Objects.Create("flask_of_oil", 10);
        flasks.Note = "=g4";
        var east = game.Player.Position + new Loc(1, 0);
        foreach (var f in game.Player.Inventory.Pack.Where(i => i.Kind.Id == "flask_of_oil").ToList())
            game.Player.Inventory.Remove(f, f.Number, () => game.Objects.NextSerial++);
        game.Level.Objects.Add(east, flasks);
        game.Execute(new WalkCommand(Direction.East));
        Assert.Equal(4, game.Player.Inventory.Pack.Single(i => i.Kind.Id == "flask_of_oil").Number);
        Assert.Equal(6, game.Level.Objects.At(east).Single().Number);
    }

    [Fact]
    public void TheQuiver_IsOrderedByFireInscriptions()
    {
        var game = Arena.Create(7);
        var inv = game.Player.Inventory;
        foreach (var q in inv.Quiver.ToList()) inv.Remove(q, q.Number, () => game.Objects.NextSerial++);
        var plain = Carry(game, "iron_shot", 10);
        var arrows = Carry(game, "arrow", 10);
        Assert.Same(plain, inv.Quiver[0]);
        game.Execute(new InscribeCommand(arrows, "@f0"));
        Assert.Same(arrows, inv.Quiver[0]);
        Assert.Equal(0, Inscription.QuiverSlot(arrows));
    }

    [Fact]
    public void AutoInscriptions_MarkEveryObjectOfAKind_AndAreSaved()
    {
        var game = Arena.Create(8);
        var clw = TestData.Game.Object("cure_light_wounds")!;
        game.SetAutoInscription(clw, "@q1");
        Assert.All(game.Player.Inventory.Pack.Where(i => i.Kind == clw), i => Assert.Equal("@q1", i.Note));

        var found = game.Objects.Create(clw);
        game.Level.Objects.Add(game.Player.Position + new Loc(1, 0), found);
        game.UpdateView();
        Assert.Equal("@q1", found.Note);

        // An object's own inscription wins.
        var mine = game.Objects.Create(clw);
        mine.Note = "!*";
        game.Knowledge.See(mine);
        Assert.Equal("!*", mine.Note);

        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal("@q1", loaded.Knowledge.KindNote(clw));
        Assert.Contains(loaded.Player.Inventory.Pack, i => i.Note == "@q1");
    }

    [Fact]
    public void Tags_AndConfirmations_ReadTheInscription()
    {
        var game = Arena.Create(9);
        var potion = game.Objects.Create("speed");
        potion.Note = "@q3 !d";
        Assert.True(Inscription.HasTag(potion, 'q', '3'));
        Assert.False(Inscription.HasTag(potion, 'r', '3'));
        Assert.True(Inscription.AsksFirst(potion, 'd'));
        Assert.False(Inscription.AsksFirst(potion, 'q'));
        potion.Note = "@2 !*";
        Assert.True(Inscription.HasTag(potion, 'r', '2'));
        Assert.True(Inscription.AsksFirst(potion, 'q'));
    }
}
