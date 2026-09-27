using Angband.Core.Definitions;

namespace Angband.Core.Items;

/// <summary>
/// What an object is worth (Angband 4.2 object_value_real and object_value, obj-power.c).
/// Wearables and ammunition are priced by their power — power × (power + 5) gold, a twentieth of
/// that for ammunition and ordinary torches — and nothing if their power is negative. Everything
/// else costs its kind's price, wands and staffs a little more for each charge.
/// </summary>
public static class ItemValue
{
    /// <summary>Angband AMMO_RESCALER: ammunition and torches are expendable.</summary>
    public const int AmmoRescaler = 20;

    /// <summary>The true value of one (Angband object_value_real(obj, 1)).</summary>
    public static long Of(Item item, GameData data) => Real(item, data, 1);

    /// <summary>Angband tval_has_variable_power: wearables and ammunition.</summary>
    public static bool HasVariablePower(Item item) => item.Base.IsWearable || item.Base.IsAmmo;

    /// <summary>Angband tval_can_have_charges: wands and staffs.</summary>
    public static bool HasCharges(Item item) => item.Base.Id is "wand" or "staff";

    /// <summary>Angband object_value_real: the true value of <paramref name="qty"/> of them.</summary>
    public static long Real(Item item, GameData data, int qty) => ValueOf(item, data, qty, known: null);

    /// <summary>
    /// Angband object_value: what they are worth as far as the player knows — unlearned runes
    /// (curses too) don't count, and an unfamiliar flavour gets a guess by type.
    /// </summary>
    public static long Known(Item item, GameData data, PlayerKnowledge knowledge, int qty = 1)
    {
        if (HasVariablePower(item)) return ValueOf(item, data, qty, knowledge);
        if (!item.IsFlavored || knowledge.IsAware(item.Kind)) return Real(item, data, qty);
        return BaseGuess(item, knowledge) * qty;
    }

    /// <summary>Angband object_value_base: a guess at an unfamiliar flavour's worth.</summary>
    private static long BaseGuess(Item item, PlayerKnowledge knowledge)
    {
        if (knowledge.IsAware(item.Kind)) return item.Kind.Cost;
        return item.Base.Id switch
        {
            "food" or "mushroom" => 5,
            "potion" or "scroll" => 20,
            "ring" or "amulet" => 45,
            "wand" => 50,
            "staff" => 70,
            "rod" => 90,
            _ => 0,
        };
    }

    private static long ValueOf(Item item, GameData data, int qty, PlayerKnowledge? known)
    {
        if (item.IsGold) return item.GoldValue;
        if (HasVariablePower(item))
        {
            long power = ObjectPower.Of(item, data, known);
            // a = 1, b = 5: power × (power + 5), or −power × (power − 5) (negative) below zero.
            var value = power > 0 ? power * (power + 5) : power < 0 ? -power * (power - 5) : 0;
            if (item.Base.Slot == EquipSlot.Light && item.Kind.Has("BURNS_OUT") && item.Ego is null || item.IsAmmo)
                value /= AmmoRescaler;
            if (value == 0) value = 1; // so cloaks and the like aren't worthless
            return Math.Max(0, value * qty);
        }

        if (item.Kind.Cost == 0) return 0;
        long total = (long)item.Kind.Cost * qty;
        if (HasCharges(item))
        {
            // Pay extra for charges: the stack's charges shared out over the quantity, rounded up.
            var number = Math.Max(1, item.Number);
            var charges = item.Charges * qty / number + (item.Charges * qty % number != 0 ? 1 : 0);
            total += (long)item.Kind.Cost * charges / 20;
        }
        return Math.Max(0, total);
    }
}
