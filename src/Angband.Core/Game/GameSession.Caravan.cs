using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Persistence;
using Angband.Core.Quests;
using Angband.Core.Randomness;

namespace Angband.Core.Game;

// AVABand's own: merchant caravans. Now and then, coming back up to town, you find a caravan camped in the
// town square — a painted wagon (the shopfronts draw it; '&' in letters) — with five rare things made
// well below where you've been (15 levels deeper, and never shallower than 1250 ft): weapons, armour and
// ammunition that are always egos, and now and then a ring, an amulet or a magic device of real worth —
// never cursed, never an artifact — at three times what they're worth. It stays for three of your
// returns, then moves on. Its arrival and its wares come from dice of their own (seeded from the town and
// the time), so they don't shift the game's other chances.
public sealed partial class GameSession
{
    /// <summary>One return in this many finds a caravan (once you've been 250 ft down).</summary>
    public const int CaravanChance = 4;

    /// <summary>How many of your returns a caravan stays.</summary>
    public const int CaravanStays = 3;

    /// <summary>How many things a caravan brings.</summary>
    public const int CaravanWares = 5;

    /// <summary>How much deeper than your deepest level the caravan's wares are made, and the shallowest they're made.</summary>
    public const int CaravanDepthBonus = 15, CaravanMinLevel = 25;

    /// <summary>A caravan's price: this many times what the thing is worth.</summary>
    public const int CaravanMarkup = 3;

    /// <summary>One of the caravan's wares in this many is tried as a ring, an amulet or a device rather than gear.</summary>
    public const int CaravanTrinketChance = 3;

    /// <summary>The least a ring, amulet or device must be worth for the caravan to carry it.</summary>
    public const int CaravanTrinketValue = 1000;

    private static readonly string[] CaravanTrinkets = ["ring", "amulet", "wand", "staff", "rod"];

    /// <summary>Back in town from the dungeon: a caravan moves on, stays, or arrives.</summary>
    private void CaravanOnReturn()
    {
        if (!AvaQuestsOn) return;
        var dice = new GameRandom(TownSeed ^ (ulong)GameTurn * 0x9E3779B97F4A7C15UL);
        if (AvaQuests.Caravan is { } caravan)
        {
            if (--caravan.VisitsLeft <= 0 || caravan.Stock.Count == 0)
            {
                AvaQuests.Caravan = null;
                Publish(new MessageEvent("The merchant caravan has moved on."));
                return;
            }
            PlaceCaravan(caravan);
            return;
        }
        if (Player.MaxDepth < 5 || !dice.OneIn(CaravanChance)) return;
        if (MakeCaravan(dice) is not { } made) return;
        AvaQuests.Caravan = made;
        PlaceCaravan(made, fresh: true);
        Publish(new MessageEvent("A merchant caravan has come to town, and camped in the square."));
    }

    /// <summary>A caravan and its wares, from its own dice; null if it found nothing to bring.</summary>
    internal Caravan? MakeCaravan(GameRandom dice)
    {
        var made = new Caravan { VisitsLeft = CaravanStays };
        var level = CaravanLevel;
        // (made aside: the serials and any artifact it rolls are put back, so nothing else in the game moves)
        var serial = Objects.NextSerial;
        var artifacts = Objects.CreatedArtifacts.ToHashSet(StringComparer.Ordinal);
        for (var tries = 0; made.Stock.Count < CaravanWares && tries < 400; tries++)
        {
            var item = dice.OneIn(CaravanTrinketChance)
                ? Objects.Make(dice, level, bases: CaravanTrinkets)
                : Objects.Make(dice, level, good: true, great: true);
            if (item is null || !CaravanWants(item)) continue;
            made.Stock.Add(SaveGame.ItemToJson(item));
            made.Prices.Add(CaravanPrice(item));
        }
        Objects.NextSerial = serial;
        Objects.CreatedArtifacts.RemoveWhere(a => !artifacts.Contains(a));
        return made.Stock.Count == 0 ? null : made;
    }

    /// <summary>The level the caravan's wares are made at: well below your deepest.</summary>
    public int CaravanLevel => Math.Max(CaravanMinLevel, Player.MaxDepth + CaravanDepthBonus);

    /// <summary>
    /// What the caravan carries: never gold, an artifact or anything cursed; gear (weapons, armour,
    /// lights, ammunition) only as an ego; rings, amulets and devices only if they're worth
    /// <see cref="CaravanTrinketValue"/>; nothing else.
    /// </summary>
    internal bool CaravanWants(Item item)
    {
        if (item.IsGold || item.IsArtifact || item.IsCursed) return false;
        if (item.Base.Slot is EquipSlot.Ring or EquipSlot.Amulet || item.Base.Id is "wand" or "staff" or "rod")
            return ItemValue.Of(item, Data) >= CaravanTrinketValue;
        return (item.IsWearable || item.IsAmmo) && item.Ego is not null;
    }

    /// <summary>The caravan's price: <see cref="CaravanMarkup"/> times the worth (no haggling, no discounts).</summary>
    internal long CaravanPrice(Item item) => Math.Max(10, ItemValue.Of(item, Data) * item.Number * CaravanMarkup);

    /// <summary>The wagon on its square of the town (a spot found the first time: open ground near the middle).</summary>
    private void PlaceCaravan(Caravan caravan, bool fresh = false)
    {
        var at = new Loc(caravan.X, caravan.Y);
        if (fresh || !Level.InBounds(at) || !Level.IsEmptyFloor(at))
        {
            var middle = new Loc(Level.Width / 2, Level.Height / 2);
            if (Level.AllLocs().Where(p => Level.IsEmptyFloor(p) && p != Player.Position
                                           && Level.Neighbors(p).All(n => Level.IsPassable(n) && Level.FeatureAt(n).Shop is null))
                    .OrderBy(p => p.DistanceTo(middle)).ThenBy(p => p.Y).ThenBy(p => p.X).Select(p => (Loc?)p).FirstOrDefault() is not { } spot)
                return;
            at = spot;
            (caravan.X, caravan.Y) = (at.X, at.Y);
        }
        Level[at].Feature = Data.Terrain["shop_caravan"].Index;
        Known.Remember(Level, at);
    }

    /// <summary>Walking into the caravan: its wares, each at its price.</summary>
    private void EnterCaravan()
    {
        if (AvaQuests.Caravan is not { } caravan)
        {
            Publish(new MessageEvent("The wagon stands empty."));
            return;
        }
        var choices = new List<(string, string)>();
        for (var i = 0; i < caravan.Stock.Count; i++)
            if (SaveGame.ItemFromJson(this, caravan.Stock[i]) is { } item)
                choices.Add(($"caravan:buy:{i}", $"{Describe(item)} — {caravan.Prices[i]} gold"));
        choices.Add(("none", "Leave"));
        AskQuest("A merchant caravan",
            $"A merchant in travel-stained silks spreads a cloth over the tailboard. \"Come far, these have, and they'll go farther. "
            + $"Nothing like them in the shops here — and priced for it.\" You have {Player.Gold} gold."
            + (caravan.VisitsLeft <= 1 ? " The wagon is half packed: it leaves before you're next back." : ""),
            [.. choices]);
    }

    private void CaravanChoice(string[] parts)
    {
        if (AvaQuests.Caravan is not { } caravan || parts.ElementAtOrDefault(1) != "buy" || !int.TryParse(parts.ElementAtOrDefault(2), out var i)
            || i < 0 || i >= caravan.Stock.Count)
            return;
        if (SaveGame.ItemFromJson(this, caravan.Stock[i]) is not { } item) return;
        var price = caravan.Prices[i];
        if (Player.Gold < price)
            Publish(new MessageEvent($"The merchant smiles thinly. \"{price} gold, friend. I don't haggle.\""));
        else if (!Player.Inventory.CanCarry(item))
            Publish(new MessageEvent($"You have no room for {Describe(item)}."));
        else
        {
            caravan.Stock.RemoveAt(i);
            caravan.Prices.RemoveAt(i);
            Player.Gold -= price;
            Knowledge.LearnKind(item.Kind);
            foreach (var rune in item.Runes()) Knowledge.LearnRune(rune);
            var held = Player.Inventory.Add(item)!;
            Publish(new MessageEvent($"You buy {Describe(held)} for {price} gold."));
            Publish(new ItemBoughtEvent("caravan", item.Kind.Id, price));
            RecalculateBonuses();
        }
        EnterCaravan();
    }
}
