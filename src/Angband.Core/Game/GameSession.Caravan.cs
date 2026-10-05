using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Persistence;
using Angband.Core.Quests;
using Angband.Core.Randomness;

namespace Angband.Core.Game;

// AVABand's own: merchant caravans. Now and then, coming back up to town, you find a caravan camped in the
// town square — a painted wagon (the shopfronts draw it; '&' in letters) — with five rare, good things
// made deeper than you've been, at half as much again as they're worth. It stays for three of your
// returns, then moves on. Its arrival and its wares come from dice of their own (seeded from the town and
// the time), so they don't shift the game's other chances.
public sealed partial class GameSession
{
    /// <summary>One return in this many finds a caravan (once you've been 250 ft down).</summary>
    public const int CaravanChance = 4;

    /// <summary>How many of your returns a caravan stays.</summary>
    public const int CaravanStays = 3;

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
        var made = new Caravan { VisitsLeft = CaravanStays };
        var level = Math.Max(10, Player.MaxDepth + 5);
        // (made aside: the serials and any artifact it rolls are put back, so nothing else in the game moves)
        var serial = Objects.NextSerial;
        var artifacts = Objects.CreatedArtifacts.ToHashSet(StringComparer.Ordinal);
        for (var tries = 0; made.Stock.Count < 5 && tries < 40; tries++)
        {
            if (Objects.Make(dice, level, good: true) is not { } item || item.IsGold || item.Artifact is not null) continue;
            made.Stock.Add(SaveGame.ItemToJson(item));
            made.Prices.Add(Math.Max(10, ItemValue.Of(item, Data) * item.Number * 3 / 2));
        }
        Objects.NextSerial = serial;
        Objects.CreatedArtifacts.RemoveWhere(a => !artifacts.Contains(a));
        if (made.Stock.Count == 0) return;
        AvaQuests.Caravan = made;
        PlaceCaravan(made, fresh: true);
        Publish(new MessageEvent("A merchant caravan has come to town, and camped in the square."));
    }

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
