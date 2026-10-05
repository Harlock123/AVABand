using Angband.Core.Game;
using Angband.Core.Items;

namespace Angband.Tests;

/// <summary>An item laid beside what you wear in its slot (AVABand's own).</summary>
public class CompareTests
{
    private static GameSession Warrior()
    {
        var game = GameSession.NewGame(TestData.Game, 3, "warrior");
        return game;
    }

    [Fact]
    public void A_better_sword_is_better_line_by_line()
    {
        var game = Warrior();
        var sword = game.Objects.Create("long_sword");
        sword.ToHit = 5;
        sword.ToDam = 4;
        var lines = game.Compare(sword)!;
        var perTurn = lines.Single(l => l.What == "Damage a turn");
        Assert.Equal(1, perTurn.Tone);
        Assert.Equal(1, lines.Single(l => l.What == "To dam").Tone);
        Assert.Contains(lines, l => l.What == "Damage dice" && l.This == "2d5");
        var text = game.CompareText(sword);
        Assert.StartsWith("Against your ", text);
        Assert.Contains("+ Damage a turn:", text);
    }

    [Fact]
    public void Armour_and_resistances_and_weight()
    {
        var game = Warrior();
        var mail = game.Objects.Create("chain_mail");
        mail.Resists.Add("fire");
        var lines = game.Compare(mail)!;
        Assert.Equal(1, lines.Single(l => l.What == "Armour").Tone);
        var fire = lines.Single(l => l.What.Contains("fire", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(("no", "yes", 1), (fire.Yours, fire.This, fire.Tone));
        Assert.Equal(-1, lines.Single(l => l.What == "Weight (lb)").Tone);  // heavier
    }

    [Fact]
    public void Nothing_to_compare_for_a_potion_or_what_you_wear()
    {
        var game = Warrior();
        Assert.Null(game.Compare(game.Objects.Create("cure_light_wounds")));
        Assert.Null(game.Compare(game.Player.Inventory.Weapon!));
    }

    [Fact]
    public void Against_an_empty_slot()
    {
        var game = Warrior();
        var cap = game.Objects.Create("hard_leather_cap");
        Assert.StartsWith("Against your empty helm slot:", game.CompareText(cap));
        Assert.Contains(game.Compare(cap)!, l => l.What == "Armour" && l.Yours == "—" && l.Tone == 1);
    }
}
