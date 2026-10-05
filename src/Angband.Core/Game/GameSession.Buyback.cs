using Angband.Core.Items;

namespace Angband.Core.Game;

/// <summary>Something sold on this visit to a shop, which can be bought back (AVABand's own).</summary>
/// <param name="Item">The item as it was sold (before the shop recharged or topped it up).</param>
/// <param name="Price">What the shop paid for it — and what buying it back costs.</param>
/// <param name="Pile">The stack in the shop's stock it went into.</param>
/// <param name="AddedNumber">How many it added to that stack (none to a staple pile already full).</param>
/// <param name="AddedCharges">The charges it added to that stack (wands and staves).</param>
public sealed record BuybackEntry(Item Item, long Price, string StoreId, Item Pile, int AddedNumber, int AddedCharges);

/// <summary>Buys back something sold on this visit (AVABand's own; the index into <see cref="GameSession.Buyback"/>).</summary>
public sealed record BuybackCommand(int Index) : GameCommand;

// AVABand's own: a slip of the finger in a shop can be undone. What you sell is kept, as it was, in a
// buy-back list for as long as you stay in that shop; buying it back takes it out of the shop's stock
// again and costs what the shop paid (nothing, with "no selling" on). The list is cleared on leaving
// (or entering another shop), and isn't saved. The Home needs none: what you leave there you can take.
public sealed partial class GameSession
{
    private readonly List<BuybackEntry> _buyback = [];

    /// <summary>What you've sold to the shop you're in, on this visit (the latest last).</summary>
    public IReadOnlyList<BuybackEntry> Buyback => StoreHere is { } store ? [.. _buyback.Where(e => e.StoreId == store.Id)] : [];

    /// <summary>A visit to a shop begins: the last visit's sales can no longer be bought back.</summary>
    private void BeginShopVisit(Store store)
    {
        _buyback.Clear();
        if (AvaQuestsOn) DeliverParcels(store.Id); // (a board job's parcel for this shop)
        Publish(new ShopEnteredEvent(store.Id, store.IsHome));
    }

    private void RecordSale(Store store, Item asSold, long price, Item pile, int addedNumber, int addedCharges) =>
        _buyback.Add(new BuybackEntry(asSold, price, store.Id, pile, addedNumber, addedCharges));

    private int BuyBack(int index)
    {
        var list = Buyback;
        if (StoreHere is not { } store || index < 0 || index >= list.Count)
        {
            Publish(new MessageEvent("There is nothing to buy back."));
            return 0;
        }
        var entry = list[index];
        if (Player.Gold < entry.Price)
        {
            Publish(new MessageEvent($"You need {entry.Price} gold to buy back {Describe(entry.Item)}."));
            return 0;
        }
        if (!Player.Inventory.CanCarry(entry.Item))
        {
            Publish(new MessageEvent($"You have no room for {Describe(entry.Item)}."));
            return 0;
        }
        // Out of the shop's stock again (unless it's gone from there — bought back in the ordinary way).
        _buyback.Remove(entry);
        if (entry.AddedNumber > 0 && (!store.Stock.Contains(entry.Pile) || entry.Pile.Number < entry.AddedNumber))
        {
            Publish(new MessageEvent("That is no longer in the shop."));
            return 0;
        }
        entry.Pile.Number -= entry.AddedNumber;
        if (entry.Pile.Kind.Charges is not null) entry.Pile.Charges = Math.Max(0, entry.Pile.Charges - entry.AddedCharges);
        if (entry.Pile.Number <= 0) store.Stock.Remove(entry.Pile);
        Player.Gold -= entry.Price;
        // (a fresh serial: the shop's pile it came out of may still carry the one it was sold with)
        var stack = Player.Inventory.Add(entry.Item.Clone(Objects.NextSerial++, entry.Item.Number))!;
        Publish(new MessageEvent(entry.Price > 0 ? $"You buy back {Describe(stack)} for {entry.Price} gold." : $"You take back {Describe(stack)}."));
        RecalculateBonuses();
        return 0;
    }
}
