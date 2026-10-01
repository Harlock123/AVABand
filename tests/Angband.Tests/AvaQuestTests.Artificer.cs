using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>The Arcane Artificer: SlatriBartSlow's sockets, for gold, and his quest, the Artificer's Chisel.</summary>
public partial class AvaQuestTests
{
    private static Item Wearing(GameSession game, EquipSlot slot) => game.Player.Inventory.InSlot(slot)!;

    private static Item CarryKnown(GameSession game, string kind)
    {
        var item = game.Objects.Create(kind);
        game.Knowledge.LearnKind(item.Kind);
        return game.Player.Inventory.Add(item)!;
    }

    [Fact]
    public void The_Arcane_Artificer_stands_in_town()
    {
        var game = GameSession.NewGame(TestData.Game, 5, "warrior");
        var door = Assert.Single(game.Level.AllLocs(), l => game.Level.FeatureAt(l).Shop == "artificer");
        Assert.Equal('0', game.Level.FeatureAt(door).Glyph);
    }

    [Fact]
    public void How_many_sockets_he_cuts_depends_on_the_piece()
    {
        var game = GameSession.NewGame(TestData.Game, 5, "warrior");
        Assert.Equal(2, GameSession.ArtificerSocketLimit(Wearing(game, EquipSlot.Body)));
        Assert.Equal(1, GameSession.ArtificerSocketLimit(Wearing(game, EquipSlot.Weapon)));
        Assert.Equal(1, GameSession.ArtificerSocketLimit(CarryKnown(game, "metal_cap")));
        Assert.Equal(1, GameSession.ArtificerSocketLimit(CarryKnown(game, "leather_bracers")));
        Assert.Equal(0, GameSession.ArtificerSocketLimit(CarryKnown(game, "ring_of_digging")));
        Assert.Equal(0, GameSession.ArtificerSocketLimit(Wearing(game, EquipSlot.Light)));
        var artifact = game.Objects.CreateArtifact(game.Data.Artifacts.Single(a => a.Id == "boots_of_strider"));
        Assert.False(game.CanTakeSocket(artifact));
        Assert.True(game.CanTakeSocket(artifact, byChisel: true));
    }

    [Fact]
    public void A_socket_costs_a_great_deal_and_a_second_three_times_as_much()
    {
        var game = GameSession.NewGame(TestData.Game, 5, "warrior");
        var armour = Wearing(game, EquipSlot.Body);
        var first = game.SocketCost(armour);
        Assert.Equal(GameSession.SocketBaseCost + ItemValue.Of(armour, game.Data) / 4, first);
        armour.AddedSockets = 1;
        Assert.Equal(3 * (GameSession.SocketBaseCost + ItemValue.Of(armour, game.Data) / 4), game.SocketCost(armour));
    }

    [Fact]
    public void He_cuts_a_socket_for_gold_and_now_and_then_his_hand_slips()
    {
        int cut = 0, slipped = 0;
        for (var seed = 1UL; seed <= 40 && (cut == 0 || slipped == 0); seed++)
        {
            var q = Start(level: 10, seed: seed);
            var game = q.Game;
            var armour = Wearing(game, EquipSlot.Body);
            var toAc = armour.ToAc;
            var cost = game.SocketCost(armour);
            game.Player.Gold = cost;
            q.EnterShop("artificer");
            Assert.Equal("The Arcane Artificer", q.Last.Title);
            q.Choose(q.Last.Choices.First(c => c.Id == $"artificer:cut:{armour.Serial}").Id);
            if (armour.AddedSockets == 1)
            {
                cut++;
                Assert.Equal(0, game.Player.Gold);
                Assert.Equal(toAc, armour.ToAc);
                Assert.Equal(1, armour.Sockets);
                // A gem goes in, as into bracers, and the Armoury can take it out again.
                var ruby = q.Carried("ruby") ?? CarryKnown(game, "ruby");
                Assert.True(game.Execute(new SetGemCommand(armour, ruby)));
                Assert.Contains("fire", armour.Resists);
            }
            else
            {
                slipped++;
                Assert.Equal(toAc - 1, armour.ToAc);
                Assert.Equal(cost / 2, game.Player.Gold);
                Assert.Contains(q.Said, s => s.Contains("The chisel skids"));
            }
        }
        Assert.True(cut > 0 && slipped > 0, $"cut {cut}, slipped {slipped}");
    }

    [Fact]
    public void Without_the_gold_he_cuts_nothing()
    {
        var q = Start(level: 10);
        var armour = Wearing(q.Game, EquipSlot.Body);
        q.Game.Player.Gold = 100;
        q.EnterShop("artificer");
        q.Choose($"artificer:cut:{armour.Serial}");
        Assert.Equal(0, armour.AddedSockets);
        Assert.Equal(100, q.Game.Player.Gold);
        Assert.Contains(q.Said, s => s.Contains("Come back when you have it"));
    }

    /// <summary>His quest: offered by him (from level 20), the chisel in his fallen workshop; given back, a free socket, half price, no slips.</summary>
    [Fact]
    public void The_Artificers_Chisel_brought_back()
    {
        var q = Start(level: 25);
        var game = q.Game;
        q.EnterShop("artificer");
        q.Choose("artificer:trouble");
        Assert.Equal("The Artificer's Chisel", q.Last.Title);
        q.Choose("accept:chisel");
        var state = game.AvaQuests.Get("chisel")!;
        Assert.Equal("find", state.Stage);

        q.Jump(state.N("depth"));
        var chisel = Assert.Single(game.Level.Objects.All, o => o.Item.Kind.Id == "star_forged_chisel");
        game.Player.Position = chisel.Loc;
        game.Execute(new PickupCommand());
        Assert.Equal("found", state.Stage);

        q.EnterShop("artificer");
        q.Choose("chisel:return");
        Assert.Equal("returned", state.Stage);
        Assert.True(state.IsDone);
        Assert.Null(q.Carried("star_forged_chisel"));

        // The first is free; the rest half price, and his hand never slips.
        var armour = Wearing(game, EquipSlot.Body);
        Assert.Equal(0, game.SocketCost(armour));
        game.Player.Gold = 0;
        q.Choose($"artificer:cut:{armour.Serial}");
        Assert.Equal(1, armour.AddedSockets);
        var full = 3 * (GameSession.SocketBaseCost + ItemValue.Of(armour, game.Data) / 4);
        Assert.Equal(full / 2, game.SocketCost(armour));
        for (var i = 0; i < 20; i++)
        {
            var helm = CarryKnown(game, "metal_cap");
            game.Player.Gold = game.SocketCost(helm);
            q.EnterShop("artificer");
            q.Choose($"artificer:cut:{helm.Serial}");
            Assert.Equal(1, helm.AddedSockets);
        }
    }

    /// <summary>Kept, the chisel cuts once for you — even into an artifact — and is spent.</summary>
    [Fact]
    public void The_Artificers_Chisel_kept_cuts_once_even_into_an_artifact()
    {
        var q = Start(level: 25);
        var game = q.Game;
        q.EnterShop("artificer");
        q.Choose("artificer:trouble");
        q.Choose("accept:chisel");
        var state = game.AvaQuests.Get("chisel")!;
        q.Jump(state.N("depth"));
        var at = game.Level.Objects.All.Single(o => o.Item.Kind.Id == "star_forged_chisel").Loc;
        game.Player.Position = at;
        game.Execute(new PickupCommand());
        q.EnterShop("artificer");
        q.Choose("chisel:keep");
        Assert.Equal("kept", state.Stage);

        var boots = game.Objects.CreateArtifact(game.Data.Artifacts.Single(a => a.Id == "boots_of_strider"));
        boots = game.Player.Inventory.Add(boots)!;
        game.Execute(new UseCommand(q.Carried("star_forged_chisel")!));
        Assert.Equal("The star-forged chisel", q.Last.Title);
        q.Choose($"chisel:cut:{boots.Serial}");
        Assert.Equal(1, boots.AddedSockets);
        Assert.Null(q.Carried("star_forged_chisel"));
        Assert.Contains("1 socket", ObjectInfo.DescribeItem(game, boots).Replace("a socket", "1 socket"));
    }

    [Fact]
    public void A_cut_socket_is_kept_in_the_save()
    {
        var game = GameSession.NewGame(TestData.Game, 5, "warrior");
        Wearing(game, EquipSlot.Body).AddedSockets = 2;
        using var stream = new MemoryStream();
        Angband.Core.Persistence.SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = Angband.Core.Persistence.SaveGame.Load(TestData.Game, stream);
        Assert.Equal(2, loaded.Player.Inventory.InSlot(EquipSlot.Body)!.Sockets);
    }
}
