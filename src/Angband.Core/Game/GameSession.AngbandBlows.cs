using Angband.Core.Definitions;
using Angband.Core.Items;
using Angband.Core.Magic;

namespace Angband.Core.Game;

// Angband 4.2.5's melee blows and armour weight (player-calcs.c), with the birth option "Angband 4.2's
// blows" (birth_angband_blows, on by default; off keeps each class's fixed blows):
//  - calc_blows: Strength for the weapon's weight (adj_str_blow × the class's strength-multiplier ÷
//    the weight, a class minimum weight) and Dexterity (adj_dex_blow) pick the energy one blow costs
//    from blows_table; the blows are 100 ÷ that, up to the class's max-attacks — so a light weapon
//    and a strong, quick hand give more blows. Extra blows from gear add on.
//  - A weapon heavier than Strength can hold (adj_str_hold) is wielded with trouble: -2 to hit for each
//    pound over, and one blow, whatever else.
//  - A caster's armour beyond the class's spell weight costs a point of mana for each pound over.
// The figures are class.txt's (ava_classes.json, beside classes.json).
public sealed partial class GameSession
{
    /// <summary>Whether Angband's blow rules are in use (the option, and a class that has the figures).</summary>
    public bool AngbandBlowsOn => Options[OptionIds.AngbandBlows] && Player.Class is { MaxAttacks: > 0 };

    private int StatIndex(string stat) => StatTables.Index(Player.Stats.GetValueOrDefault(stat, 15));

    /// <summary>Angband adj_str_hold: whether a weapon is too heavy to wield well, and by how many pounds.</summary>
    public int HeavyBy(Item? weapon) => weapon is null ? 0 : Math.Max(0, weapon.Weight / 10 - StatTables.StrHold[StatIndex("str")]);

    /// <summary>Angband calc_blows, without extra blows: blows a turn (x100) with this weapon (or bare hands).</summary>
    public int CalcBlows(Item? weapon)
    {
        var cls = Player.Class!;
        var div = Math.Max(weapon?.Weight ?? 0, cls.MinWeight);
        var strIndex = Math.Min(11, StatTables.StrBlow[StatIndex("str")] * cls.StrengthMultiplier / Math.Max(1, div));
        var dexIndex = Math.Min(StatTables.DexBlow[StatIndex("dex")], 11);
        return Math.Min(10000 / StatTables.BlowsTable[strIndex, dexIndex], 100 * cls.MaxAttacks);
    }

    /// <summary>
    /// Blows a turn (x100) the player would have with this weapon in hand — Angband's calculation (or
    /// the class's fixed blows without it), with the weapon's and the rest of the gear's extra blows.
    /// </summary>
    public int BlowsWith(Item? weapon)
    {
        var current = Player.Inventory.Weapon;
        var extra = Player.Inventory.Equipped.Where(i => i != current && !(i.Base.IsWeapon && i != current)).Sum(i => i.Modifier(ItemModifiers.Blows))
                    + (weapon?.Modifier(ItemModifiers.Blows) ?? 0);
        if (!AngbandBlowsOn) return Player.BaseBlows + 100 * extra;
        if (HeavyBy(weapon) > 0) return 100;
        return Math.Max(CalcBlows(weapon) + 100 * extra, PercentDamage ? 200 : 100);
    }

    /// <summary>What <see cref="RecalculateBonuses"/> starts the blows from (the gear's extra blows added after).</summary>
    private int BaseBlowsNow(Item? weapon) => AngbandBlowsOn ? CalcBlows(weapon) : Player.BaseBlows;

    /// <summary>The last heavy-wield state said, so it's said only when it changes (Angband "You have trouble wielding...").</summary>
    private bool? _heavySaid;

    /// <summary>After the bonuses: a too-heavy weapon's penalty (Angband calc_bonuses' heavy_wield), and said when it changes.</summary>
    private void ApplyHeavyWield(Item? weapon)
    {
        var over = AngbandBlowsOn ? HeavyBy(weapon) : 0;
        if (over > 0)
        {
            Player.ToHit -= 2 * over;
            Player.Blows = 100; // (calc_blows isn't used for a heavy weapon, nor extra blows)
        }
        var heavy = over > 0;
        if (_heavySaid is { } was && was != heavy)
            Publish(new MessageEvent(heavy ? "You have trouble wielding such a heavy weapon." : "You have no trouble wielding your weapon."));
        _heavySaid = heavy;
    }

    /// <summary>The last armour-encumbrance state said (Angband "The weight of your armor encumbers your movement.").</summary>
    private bool? _cumberSaid;

    /// <summary>Angband calc_mana's armour weight: a caster loses a point of mana per pound of armour beyond the class's allowance.</summary>
    private int ArmourManaPenalty()
    {
        if (!AngbandBlowsOn || Player.Class is not { SpellWeight: > 0 } cls) return 0;
        var worn = Player.Inventory.Equipped
            .Where(i => !i.Base.IsWeapon && i.Base.Slot is not (EquipSlot.Bow or EquipSlot.Ring or EquipSlot.Amulet or EquipSlot.Light))
            .Sum(i => i.Weight);
        var penalty = Math.Max(0, (worn - cls.SpellWeight) / 10);
        var cumbered = penalty > 0;
        if (_cumberSaid is { } was && was != cumbered)
            Publish(new MessageEvent(cumbered ? "The weight of your armor encumbers your movement." : "You feel able to move more freely."));
        _cumberSaid = cumbered;
        return penalty;
    }
}
