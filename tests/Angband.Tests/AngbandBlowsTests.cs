using Angband.Core.Game;
using Angband.Core.Items;

namespace Angband.Tests;

/// <summary>Angband 4.2.5's blows (calc_blows), heavy weapons and casters' armour weight (player-calcs.c).</summary>
public class AngbandBlowsTests
{
    private static GameSession Make(string cls, bool angband = true) =>
        GameSession.NewGame(TestData.Game, 6, CharacterSpec.Default("human", cls)
            with { Options = new Dictionary<string, bool> { [OptionIds.AngbandBlows] = angband } });

    /// <summary>Sets the final Strength and Dexterity (natural stats adjusted for race and class).</summary>
    private static void Stats(GameSession game, int str, int dex)
    {
        game.RecalculateBonuses();
        game.Player.NaturalStats["str"] += str - game.Player.Stats["str"];
        game.Player.NaturalStats["dex"] += dex - game.Player.Stats["dex"];
        game.RecalculateBonuses();
        Assert.Equal((str, dex), (game.Player.Stats["str"], game.Player.Stats["dex"]));
    }

    private static Item Wield(GameSession game, string kind)
    {
        var item = game.Objects.Create(kind);
        game.Player.Inventory.Add(item);
        game.Execute(new WieldCommand(item));
        return game.Player.Inventory.Weapon!;
    }

    [Fact]
    public void A_warriors_blows_come_from_the_blows_table()
    {
        var game = Make("warrior");
        Wield(game, "dagger"); // 1.2 lb: the class's 3 lb minimum counts
        Stats(game, str: 18, dex: 18);
        // adj_str_blow[18] = 20, x5 / 30 = 3; adj_dex_blow[18] = 2; blows_table[3][2] = 60 energy: 100/60 blows.
        Assert.Equal(10000 / 60, game.Player.Blows);
        Stats(game, str: 28, dex: 28); // 18/100 both
        // adj_str_blow = 120, x5 / 30 = 20 -> 11; adj_dex_blow = 7; blows_table[11][7] = 19.
        Assert.Equal(10000 / 19, game.Player.Blows);
    }

    [Fact]
    public void Classes_have_their_most_blows()
    {
        var mage = Make("mage");
        Wield(mage, "dagger");
        Stats(mage, str: 28, dex: 28);
        // 120 x2 / 40 = 6; blows_table[6][7] = 23 -> 434, but a mage's max-attacks is 4.
        Assert.Equal(400, mage.Player.Blows);
    }

    [Fact]
    public void A_weapon_too_heavy_to_hold_costs_to_hit_and_blows()
    {
        var game = Make("warrior");
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        Stats(game, str: 8, dex: 15); // adj_str_hold[8] = 10 lb
        var light = game.Player.ToHit;
        Wield(game, "battle_axe"); // 17 lb: 7 over
        Assert.Equal(7, game.HeavyBy(game.Player.Inventory.Weapon));
        Assert.Equal(light - 14, game.Player.ToHit);
        Assert.Equal(100, game.Player.Blows);
        Assert.Contains("You have trouble wielding such a heavy weapon.", said);
    }

    [Fact]
    public void Heavy_armour_costs_a_caster_mana()
    {
        var mage = Make("mage");
        mage.GainExperience(mage.ExperienceForLevel(19));
        mage.RecalculateMana();
        var said = new List<string>();
        mage.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        var before = mage.Player.MaxMana;
        foreach (var kind in new[] { "augmented_chain_mail", "small_metal_shield", "iron_helm" }) // 27 + 6 + 5 lb
        {
            var piece = mage.Objects.Create(kind);
            mage.Player.Inventory.Add(piece);
            mage.Execute(new WieldCommand(piece));
        }
        var armour = mage.Player.Inventory.Equipped.Where(i => !i.Base.IsWeapon && i.Base.Slot is not (Angband.Core.Definitions.EquipSlot.Light
            or Angband.Core.Definitions.EquipSlot.Ring or Angband.Core.Definitions.EquipSlot.Amulet or Angband.Core.Definitions.EquipSlot.Bow)).Sum(i => i.Weight);
        Assert.Equal(Math.Max(0, before - (armour - 300) / 10), mage.Player.MaxMana); // a point a pound past the mage's 30 lb
        Assert.Contains("The weight of your armor encumbers your movement.", said);
    }

    [Fact]
    public void Without_the_option_each_class_has_its_fixed_blows()
    {
        var game = Make("warrior", angband: false);
        Wield(game, "battle_axe");
        Stats(game, str: 8, dex: 15);
        Assert.Equal(game.Player.BaseBlows, game.Player.Blows);
        Assert.False(game.AngbandBlowsOn);
    }
}
