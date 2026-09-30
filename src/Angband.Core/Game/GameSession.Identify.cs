using Angband.Core.Items;

namespace Angband.Core.Game;

// AVABand's identify service at the Alchemist (5), offered from the shop screen (Services, or !) when
// you carry or wear anything you don't fully know (an untried flavour, or runes you haven't learned):
// the alchemist tells you all about it,
// for gold — the item's kind and every rune on it, curses included, as selling it to a shop would
// teach you (Angband 4.2 has no *Identify*; this is a paid shortcut to what use would show).
public sealed partial class GameSession
{
    /// <summary>What the alchemist charges for one item: 50 gold, and 50 more for each unknown rune and for an unknown kind.</summary>
    public int IdentifyCost(Item item) =>
        50 + 50 * Knowledge.UnknownRunes(item).Count() + (Knowledge.KnowsKind(item) ? 0 : 50);

    /// <summary>Everything carried or worn that isn't fully known (one of each kind of untried flavour).</summary>
    public IReadOnlyList<Item> Unidentified() =>
        [.. Player.Inventory.Equipped.Concat(Player.Inventory.Pack).Concat(Player.Inventory.Quiver)
            .Where(i => !i.IsGold && !Knowledge.IsFullyKnown(i))
            .GroupBy(i => Knowledge.KnowsKind(i) ? $"item:{i.Serial}" : $"kind:{i.Kind.Id}").Select(g => g.First())];

    private void OfferIdentify()
    {
        var items = Unidentified();
        if (items.Count == 0)
        {
            OpenStoreAfterAsking("alchemist", greet: false);
            return;
        }
        var choices = items.Select(i => ($"ident:item:{i.Serial}", $"{Capital(Describe(i))} ({IdentifyCost(i)} gold)")).ToList();
        if (items.Count > 1) choices.Add(("ident:all", $"Everything ({items.Sum(IdentifyCost)} gold)"));
        choices.Add(("back:alchemist", "Back to the shop"));
        AskQuest("The Alchemy shop", $"\"Something you can't put a name to? I can tell you what it is, and all it does — for a price. Which? You have {Player.Gold} gold.\"", [.. choices]);
    }

    private void IdentifyChoice(string[] parts)
    {
        switch (parts.ElementAtOrDefault(1))
        {
            case "list":
                OfferIdentify();
                return;
            case "all":
                var all = Unidentified();
                var total = all.Sum(IdentifyCost);
                if (Player.Gold < total) Publish(new MessageEvent($"\"That'll be {total} gold, and you haven't got it.\""));
                else
                {
                    Player.Gold -= total;
                    foreach (var item in all) Identify(item);
                }
                break;
            case "item" when long.TryParse(parts.ElementAtOrDefault(2), out var serial):
                if (Unidentified().FirstOrDefault(i => i.Serial == serial) is not { } one) break;
                var cost = IdentifyCost(one);
                if (Player.Gold < cost) Publish(new MessageEvent($"\"That'll be {cost} gold, and you haven't got it.\""));
                else
                {
                    Player.Gold -= cost;
                    Identify(one);
                }
                break;
        }
        OfferIdentify(); // anything else? (or on into the shop)
    }

    /// <summary>Everything about an item made known: its kind, and every rune on it.</summary>
    private void Identify(Item item)
    {
        var before = Describe(item);
        Knowledge.LearnKind(item.Kind);
        foreach (var rune in item.Runes().ToList()) LearnRune(rune);
        Publish(new MessageEvent($"The alchemist turns {before} over in the light, and tells you: it is {Describe(item)}."));
        RecalculateBonuses();
    }
}
