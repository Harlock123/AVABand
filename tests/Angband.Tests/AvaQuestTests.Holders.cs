using System.IO.Compression;
using System.Text.Json.Nodes;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>The Frozen Cellar and the Burning Library: the quests that earn the potion belt and the scroll case.</summary>
public partial class AvaQuestTests
{
    [Fact]
    public void A_new_character_has_no_belt_or_case_and_its_potions_take_pack_slots()
    {
        var q = Start(level: 1);
        var inv = q.Game.Player.Inventory;
        Assert.False(inv.HasBelt);
        Assert.False(inv.HasCase);
        Assert.Empty(inv.Belt);
        var before = inv.SlotsUsed;
        foreach (var kind in KindsOf(q, "potion", 3)) inv.Add(q.Game.Objects.Create(kind));
        Assert.True(inv.SlotsUsed >= before + 2);                              // (one may have joined a stack carried)
    }

    [Fact]
    public void The_Frozen_Cellar_earns_the_Alchemists_bandolier()
    {
        var q = Start(level: 5);
        var game = q.Game;
        q.TakeQuest("cellar");
        var state = game.AvaQuests.Get("cellar")!;
        Assert.Equal("find", state.Stage);

        q.Jump(state.N("depth"));
        var crate = Assert.Single(game.Level.Objects.All, o => o.Item.Kind.Id == "alchemists_crate");
        game.Player.Position = crate.Loc;
        game.Execute(new PickupCommand());
        Assert.Equal("found", state.Stage);

        var gold = game.Player.Gold;
        q.EnterShop("alchemist");
        q.Choose("cellar:give");
        Assert.Equal("done", state.Stage);
        Assert.True(state.IsDone);
        Assert.Null(q.Carried("alchemists_crate"));
        Assert.True(game.Player.Inventory.HasBelt);
        Assert.True(game.Player.Gold > gold);
        var bandolier = q.Carried(Inventory.BeltKind)!;
        Assert.True(bandolier.IsQuestItem);                                     // (in the satchel: no slot, and never parted with)

        // Potions ride in it now.
        var cure = game.Player.Inventory.Pack.First(i => i.Base.Id == "potion");
        Assert.Contains(cure, game.Player.Inventory.Belt);
        Assert.True(game.Sheltered(cure));
    }

    [Fact]
    public void The_Burning_Library_earns_the_scribes_case_for_three_folios()
    {
        var q = Start(level: 8);
        var game = q.Game;
        q.TakeQuest("library");
        var state = game.AvaQuests.Get("library")!;
        q.Jump(state.N("depth"));
        var folios = game.Level.Objects.All.Where(o => o.Item.Kind.Id == "scorched_folio").ToList();
        Assert.Equal(3, folios.Count);

        // Two, then away: the shelves keep only the one still wanted.
        foreach (var f in folios.Take(2))
        {
            game.Player.Position = f.Loc;
            game.Execute(new PickupCommand());
        }
        Assert.Equal(2, state.N("folios"));
        Assert.Equal("find", state.Stage);
        q.Jump(state.N("depth"));
        var last = Assert.Single(game.Level.Objects.All, o => o.Item.Kind.Id == "scorched_folio");
        game.Player.Position = last.Loc;
        game.Execute(new PickupCommand());
        Assert.Equal("deliver", state.Stage);

        q.EnterShop("bookseller");
        q.Choose("library:give");
        Assert.Equal("done", state.Stage);
        Assert.Null(q.Carried("scorched_folio"));
        Assert.True(game.Player.Inventory.HasCase);
        Assert.Contains(game.Player.Inventory.Pack, i => game.Player.Inventory.InCase(i));
    }

    [Fact]
    public void Folios_picked_up_by_walking_onto_them_still_count()
    {
        var q = Start(level: 8);
        var game = q.Game;
        q.TakeQuest("library");
        var state = game.AvaQuests.Get("library")!;
        q.Jump(state.N("depth"));
        var folios = game.Level.Objects.All.Where(o => o.Item.Kind.Id == "scorched_folio").Select(o => o.Loc).ToList();
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m); // (the guards are another test's)
        bool Open(Loc at) => game.Level.AllLocs().Any(l => l.ChebyshevTo(at) == 1 && game.Level.IsPassable(l));
        folios = [.. folios.OrderBy(Open)];                                     // (the closet's, behind its door, by hand)
        game.Player.Position = folios[0];
        game.Execute(new PickupCommand());
        foreach (var at in folios.Skip(1))                                     // (joining the stack carried: picked up by walking on)
        {
            var from = game.Level.AllLocs().First(l => l.ChebyshevTo(at) == 1 && game.Level.IsPassable(l) && game.Level.Monsters.At(l) is null);
            game.Player.Position = from;
            game.Execute(new WalkCommand(DirectionExtensions.FromOffset(at.X - from.X, at.Y - from.Y)));
            Assert.Equal(at, game.Player.Position);
        }
        Assert.Equal(3, q.Carried("scorched_folio")!.Number);
        Assert.Equal("deliver", state.Stage);
    }

    [Theory]
    [InlineData("cellar", "alchemists_crate")]
    [InlineData("library", "scorched_folio")]
    public void Taken_long_past_their_level_the_quests_are_deeper_and_more_heavily_guarded(string quest, string item)
    {
        int Guards(Quester q, out int depth, out string offer)
        {
            q.TakeQuest(quest);
            offer = q.Prompts.Last(p => p.Choices.Any(c => c.Id == $"accept:{quest}")).Text;
            var s = q.Game.AvaQuests.Get(quest)!;
            depth = s.N("depth");
            q.Jump(depth);
            Assert.Contains(q.Game.Level.Objects.All, o => o.Item.Kind.Id == item);
            return s.N("guards");
        }

        var green = Start(level: quest == "cellar" ? 5 : 8);
        var fresh = Guards(green, out var greenDepth, out var greenOffer);
        Assert.DoesNotContain("You've been deeper", greenOffer);

        var veteran = Start(level: 35);
        veteran.Game.Player.MaxDepth = 30;
        var many = Guards(veteran, out var veteranDepth, out var veteranOffer);
        Assert.Contains("You've been deeper", veteranOffer);
        Assert.True(veteranDepth > greenDepth);                                // near your deepest
        Assert.True(veteran.Game.AvaQuests.Get(quest)!.N("lead") >= 27);
        Assert.Equal(green.Game.Level.QuestSpots.Count(p => p.Symbol == ')'), fresh);  // one on each post
        var lead = veteran.Game.AvaQuests.Get(quest)!.N("lead");
        Assert.Equal(fresh + Math.Min(6, lead / 5), many);                      // and one more for every five levels past it (six at most)
    }

    [Fact]
    public void An_older_saves_overfull_pack_sends_the_rest_home_on_loading()
    {
        // A save from when every character had the belt: more kinds of potion than the pack holds without it.
        var q = Start();
        var game = q.Game;
        var inv = game.Player.Inventory;
        inv.Add(game.Objects.Create(Inventory.BeltKind));
        foreach (var kind in game.Data.Objects.Where(k => k.Base == "potion" && !k.Flags.Contains("QUEST_ITEM")).Take(30))
            inv.Add(game.Objects.Create(kind.Id));
        foreach (var kind in game.Data.Objects.Where(k => k.Base is "food" or "mushroom").Take(30))
            if (inv.SlotsUsed < inv.PackSize) inv.Add(game.Objects.Create(kind.Id));
        Assert.Equal(inv.PackSize, inv.SlotsUsed);
        var carried = inv.Pack.Where(i => !i.IsQuestItem).Sum(i => i.Number);

        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        JsonNode file;
        using (var unzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true)) file = JsonNode.Parse(unzip)!;
        var pack = FindArray(file, "Pack")!;
        pack.Remove(pack.First(n => n!["Kind"]?.GetValue<string>() == Inventory.BeltKind));
        var old = new MemoryStream();
        using (var zip = new GZipStream(old, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new System.Text.Json.Utf8JsonWriter(zip)) file.WriteTo(writer);
        old.Position = 0;

        var loaded = SaveGame.Load(TestData.Game, old);
        var after = loaded.Player.Inventory;
        Assert.False(after.HasBelt);
        Assert.True(after.SlotsUsed <= after.PackSize);
        var home = loaded.Stores.Values.Single(s => s.IsHome);
        var sent = home.Stock.Where(i => i.Base.Id == "potion").Sum(i => i.Number);
        Assert.True(sent > 0);
        Assert.Equal(carried, after.Pack.Where(i => !i.IsQuestItem).Sum(i => i.Number) + home.Stock.Sum(i => i.Number)); // nothing lost
    }
}
