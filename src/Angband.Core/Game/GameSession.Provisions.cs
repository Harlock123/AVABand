using Angband.Core.Definitions;
using Angband.Core.Items;

namespace Angband.Core.Game;

// AVABand's own: what the player carries to eat, for the hunger safeguards (the sidebar's food meter,
// "eat something?" on growing weak, the one-key eat, eating by itself when weak, and the warnings as the
// food runs out). Only what you know and what does nothing but feed you counts: rations, slime molds,
// waybread and the like, then scrolls of Remove Hunger — never whisky, nor a mushroom with a sting in it.
public sealed partial class GameSession
{
    /// <summary>How much a kind feeds, if feeding is all it does (else 0): nourish:N, nourish_to:P% of full, or satisfy.</summary>
    public int PlainNourishment(ObjectKindDef kind)
    {
        if (kind.Effect is not { Length: > 0 } effect) return 0;
        var total = 0;
        foreach (var part in effect.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = part.Split(':');
            if (bits[0] == "nourish" && bits.Length > 1 && int.TryParse(bits[1], out var n)) total += n;
            else if (bits[0] == "nourish_to" && bits.Length > 1 && int.TryParse(bits[1], out var p)) total += Data.Constants.FoodFull * p / 100;
            else if (bits[0] == "satisfy" && bits.Length == 1) total += Data.Constants.FoodMax;
            else return 0;
        }
        return total;
    }

    /// <summary>Something the player can eat (or read) to stave off hunger: known, and only feeding.</summary>
    public bool IsProvision(Item item) =>
        Knowledge.KnowsKind(item) && item.Base.Id is "food" or "mushroom" or "scroll" && PlainNourishment(item.Kind) > 0;

    /// <summary>How many meals the pack holds (each food or scroll of Remove Hunger counts one).</summary>
    public int Provisions => Player.Inventory.Pack.Where(IsProvision).Sum(i => i.Number);

    /// <summary>
    /// The best thing to eat now: food before scrolls (and no scroll if you can't read), the most filling
    /// that won't gorge you (a ration before a biscuit), the cheaper of two alike — null when you're too full.
    /// </summary>
    public Item? BestFood()
    {
        var room = Data.Constants.FoodMax - 1 - Player.Food;
        return Player.Inventory.Pack
            .Where(i => IsProvision(i) && (i.Base.Id != "scroll" || CannotRead() is null))
            .Where(i => i.Kind.Effect!.Trim() == "satisfy" || PlainNourishment(i.Kind) <= room)
            .OrderBy(i => i.Base.Id == "scroll" ? 1 : 0)
            .ThenByDescending(i => PlainNourishment(i.Kind))
            .ThenBy(i => i.Kind.Cost)
            .FirstOrDefault();
    }
}
