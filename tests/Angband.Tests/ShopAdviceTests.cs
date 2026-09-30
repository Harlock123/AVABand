using Angband.Core.Game;

namespace Angband.Tests;

/// <summary>"Will this suit me?": how a thing compares with what it would replace, as AVABand's rules count it.</summary>
public class ShopAdviceTests
{
    private static GameSession Warrior() => GameSession.NewGame(TestData.Game, 42, "warrior"); // dagger, sling, soft leather armour

    [Fact]
    public void Armour_compares_with_what_it_replaces()
    {
        var game = Warrior();
        var chain = game.Objects.Create("chain_mail");
        var advice = game.AdviceFor(chain)!;
        Assert.StartsWith("vs your Soft Leather Armour: ", advice.Text);
        Assert.Contains($"+{chain.Armour + chain.ToAc - 8} armour", advice.Text);
        Assert.Contains("-2 to hit", advice.Text); // chain mail's clumsiness counts for every attack
        Assert.Equal(0, advice.Tone);              // better armour, worse aim: mixed

        var cap = game.AdviceFor(game.Objects.Create("metal_cap"))!;
        Assert.StartsWith("for your empty helm slot: +", cap.Text);
        Assert.Equal(1, cap.Tone);
    }

    [Fact]
    public void A_weapon_says_its_damage_a_turn()
    {
        var game = Warrior();
        var axe = game.AdviceFor(game.Objects.Create("battle_axe"))!;
        Assert.StartsWith("vs your Dagger: +", axe.Text);
        Assert.Contains("damage a turn (about ", axe.Text);
        Assert.Equal(1, axe.Tone);
        Assert.True(game.AdviceFor(game.Objects.Create("dagger"))!.Tone <= 0); // a dagger for a dagger: no better
    }

    [Fact]
    public void Missiles_say_whether_they_fit_your_launcher()
    {
        var game = Warrior();
        Assert.Equal("you have no launcher for these", game.AdviceFor(game.Objects.Create("iron_shot"))!.Text);
        var sling = game.Objects.Create("sling");
        game.Player.Inventory.Add(sling);
        game.Execute(new WieldCommand(sling));
        Assert.Equal(("fits your Sling", 1), (game.AdviceFor(game.Objects.Create("iron_shot"))!.Text, game.AdviceFor(game.Objects.Create("iron_shot"))!.Tone));
        var arrows = game.AdviceFor(game.Objects.Create("arrow"))!;
        Assert.Equal(("not for your Sling", -1), (arrows.Text, arrows.Tone));
        var bow = game.AdviceFor(game.Objects.Create("long_bow"))!;
        Assert.Contains("x3 shots (now x2)", bow.Text);
        Assert.Contains("fires arrows, not your shots", bow.Text);
    }

    [Fact]
    public void Resistances_brought_and_lost_are_named()
    {
        var game = Warrior();
        var ring = game.Objects.Create("ring_of_resist_fire_and_cold");
        var advice = game.AdviceFor(ring)!;
        Assert.StartsWith("for your empty ring slot: ", advice.Text);
        Assert.Contains("resist fire", advice.Text);
        Assert.Contains("resist cold", advice.Text);

        // Both rings on: another replaces one, and says what would go.
        game.Player.Inventory.Add(ring);
        game.Execute(new WieldCommand(ring));
        var second = game.Objects.Create("ring_of_free_action");
        game.Player.Inventory.Add(second);
        game.Execute(new WieldCommand(second));
        var third = game.AdviceFor(game.Objects.Create("ring_of_see_invisible"))!;
        Assert.StartsWith("vs your ", third.Text); // (by its flavour until you know the kind)
        Assert.Contains("see invisible", third.Text);
        Assert.Contains("loses ", third.Text);
        Assert.Equal(0, third.Tone);
    }

    [Fact]
    public void Too_heavy_to_carry_is_said_and_unknown_things_are_not_judged()
    {
        var game = Warrior();
        game.Player.Inventory.Add(game.Objects.Create("iron_shot", 40));
        game.Player.Inventory.Add(game.Objects.Create("chain_mail"));
        game.Player.Inventory.Add(game.Objects.Create("chain_mail"));
        game.Player.Inventory.Add(game.Objects.Create("chain_mail"));
        Assert.Contains("too heavy: you'd be slowed", game.AdviceFor(game.Objects.Create("chain_mail"))!.Text);
        Assert.Equal("not fully known yet", game.AdviceFor(game.Objects.Create("chain_mail"), known: false)!.Text);
        Assert.Null(game.AdviceFor(game.Objects.Create("ration_of_food")));
    }
}
