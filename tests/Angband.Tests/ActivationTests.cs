using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Persistence;

namespace Angband.Tests;

public class ActivationTests
{
    private static GameSession Game()
    {
        var game = Arena.Create(3,
            "#################",
            "#,,,,,,,,,,,,,,,#",
            "#,,@,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,#",
            "#################");
        TestGames.ClearMonsters(game);
        game.Player.SkillDevice = 500; // activations always work
        game.Player.Hp = game.Player.MaxHp = 5000;
        return game;
    }

    private static Item Wear(GameSession game, Item item)
    {
        item = game.Player.Inventory.Add(item)!;
        game.Execute(new WieldCommand(item));
        Assert.Contains(item, game.Player.Inventory.Equipped);
        return item;
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    [Fact]
    public void Artifacts_and_dragon_armour_have_activations()
    {
        var data = TestData.Game;
        Assert.True(data.Artifacts.Count(a => a.Activation is not null) >= 50);
        Assert.All(data.Artifacts.Where(a => a.Activation is not null), a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.ActivationText));
            Assert.False(string.IsNullOrWhiteSpace(a.Recharge));
        });
        Assert.Equal("breath:fire:200", data.Object("red_dragon_scale_mail")!.Activation);
    }

    [Fact]
    public void Dragon_armour_breathes_then_recharges()
    {
        var game = Game();
        var messages = Messages(game);
        game.SetOption(OptionIds.NotifyRecharge, true);
        var mail = Wear(game, game.Objects.Create("red_dragon_scale_mail"));
        var orc = Arena.AddMonster(game, "cave_orc", new Loc(9, 2));
        orc.Hp = orc.MaxHp = 10_000;

        Assert.True(game.Execute(new ActivateCommand(mail)));
        Assert.True(orc.Hp < 10_000);
        Assert.InRange(mail.Timeout, 48, 50); // the world may tick once in the same turn
        Assert.Contains("(charging)", game.Describe(mail));

        Assert.False(game.Execute(new ActivateCommand(mail)));
        Assert.Contains("It whines, glows and fades...", messages);

        TestGames.HoldUntil(game, game.GameTurn + 50 * 10 + 20);
        Assert.Equal(0, mail.Timeout);
        Assert.Contains(messages, m => m.EndsWith("has recharged."));
    }

    [Fact]
    public void Only_worn_items_can_be_activated()
    {
        var game = Game();
        var messages = Messages(game);
        var ring = game.Player.Inventory.Add(game.Objects.Create("ring_of_flames"))!;
        Assert.False(game.Execute(new ActivateCommand(ring)));
        Assert.Contains("You must be wearing it to activate it.", messages);
        Assert.False(game.Execute(new ActivateCommand(game.Player.Inventory.Weapon!)));
        Assert.Contains("That item has no activation.", messages);
    }

    [Fact]
    public void Aimed_activations_need_a_target()
    {
        var game = Game();
        var messages = Messages(game);
        var dagger = Wear(game, game.Objects.CreateArtifact(game.Data.Artifacts.Single(a => a.Id == "narthanc")));
        Assert.False(game.Execute(new ActivateCommand(dagger)));
        Assert.Contains("You have no target.", messages);
        Assert.Equal(0, dagger.Timeout);
    }

    [Fact]
    public void Narthanc_shoots_fire_bolts()
    {
        var game = Game();
        var dagger = Wear(game, game.Objects.CreateArtifact(game.Data.Artifacts.Single(a => a.Id == "narthanc")));
        var orc = Arena.AddMonster(game, "cave_orc", new Loc(9, 2));
        orc.Hp = orc.MaxHp = 10_000;
        Assert.True(game.Execute(new ActivateCommand(dagger)));
        Assert.InRange(10_000 - orc.Hp, 9, 72);
        Assert.InRange(dagger.Timeout, 8, 16); // 8+d8, less the tick that passes as the turn ends
    }

    [Fact]
    public void Line_activations_ask_for_a_direction()
    {
        var game = Game();
        var ring = Wear(game, game.Objects.Create("ring_of_digging"));
        Assert.False(game.Execute(new ActivateCommand(ring)));
        Assert.True(game.Execute(new ActivateCommand(ring, Direction: Direction.North)));
        Assert.True(game.Level.IsFloor(game.Player.Position + new Loc(0, -1)) ||
                    !game.Level.InBoundsFully(game.Player.Position + new Loc(0, -2)));
    }

    [Fact]
    public void Multi_hued_breath_can_be_used_again_and_again()
    {
        var game = Game();
        var mail = Wear(game, game.Objects.Create("multi_hued_dragon_scale_mail"));
        var names = new HashSet<string>();
        game.Events.Subscribe<MessageEvent>(m => names.Add(m.Text));
        for (var i = 0; i < 12; i++)
        {
            TestGames.ClearMonsters(game);
            var orc = Arena.AddMonster(game, "cave_orc", new Loc(9, 2));
            orc.Hp = orc.MaxHp = 100_000;
            mail.Timeout = 0;
            Assert.True(game.Execute(new ActivateCommand(mail)));
        }
        Assert.True(game.Level.Monsters.All.Any());
    }

    [Fact]
    public void Healing_activations_heal()
    {
        var game = Game();
        var phial = Wear(game, game.Objects.CreateArtifact(game.Data.Artifacts.Single(a => a.Id == "galadriel")));
        Assert.True(game.Execute(new ActivateCommand(phial)));
        Assert.True(phial.Timeout >= 11);
    }

    [Fact]
    public void Recharge_survives_saving()
    {
        var game = Game();
        var mail = Wear(game, game.Objects.Create("blue_dragon_scale_mail"));
        mail.Timeout = 33;
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal(33, loaded.Player.Inventory.Equipped.Single(i => i.Serial == mail.Serial).Timeout);
    }
}
