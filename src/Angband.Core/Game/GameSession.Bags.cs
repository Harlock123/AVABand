using Angband.Core.Definitions;
using Angband.Core.Items;

namespace Angband.Core.Game;

// AVABand's bags of holding (ava_objects.json, base "bag"): carried in the pack, the best one lets you
// carry more before you're slowed (Player.WeightLimit) and, for the larger ones, holds more things
// (Inventory.PackSize). Lose it — burnt, stolen, sold, dropped — and whatever no longer fits spills
// onto the floor. A Bag of Devouring passes for a Bag of Holding until its curse shows: now and then
// it swallows something in the pack for good.
public sealed partial class GameSession
{
    /// <summary>One player turn in this many (on average), a Bag of Devouring eats something.</summary>
    public const int DevouringChance = 800;

    /// <summary>The best bag the bonuses were last worked out with (to notice one arriving or going).</summary>
    private string? _bagCounted;

    /// <summary>After each command: a bag that came or went changes what you can carry, and a pack too full spills.</summary>
    private void AfterCommandBags()
    {
        if (IsGameOver) return;
        var bag = Player.Inventory.BestBag?.Kind.Id;
        if (bag != _bagCounted)
        {
            _bagCounted = bag;
            RecalculateBonuses();
        }
        PackOverflow();
    }

    /// <summary>Angband pack_overflow: what no longer fits in the pack falls to the floor (the last things first).</summary>
    private void PackOverflow()
    {
        var inv = Player.Inventory;
        if (inv.SlotsUsed <= inv.PackSize) return;
        for (var guard = 0; inv.SlotsUsed > inv.PackSize && inv.Pack.Count > 0 && guard < 100; guard++)
        {
            // (the pack's own things before gems: dropping one kind of gem seldom frees the pouch's slot)
            if ((inv.Pack.LastOrDefault(i => !i.IsQuestItem && !Inventory.InPouch(i) && !Inventory.InBookBag(i) && inv.BestBag != i)
                 ?? inv.Pack.LastOrDefault(i => !i.IsQuestItem && inv.BestBag != i)
                 ?? inv.Pack.LastOrDefault(i => !i.IsQuestItem)) is not { } spill) break;
            inv.Remove(spill, spill.Number, () => Objects.NextSerial++);
            Publish(new MessageEvent($"Your pack overflows! You drop {Describe(spill)}."));
            DropNear(spill, Player.Position);
        }
        RecalculateBonuses();
    }

    /// <summary>Each player turn: a Bag of Devouring, carried, now and then swallows something (and so shows its curse).</summary>
    private void BagUpkeep()
    {
        var bag = Player.Inventory.Pack.FirstOrDefault(i => i.Curses.Contains("devouring"));
        if (bag is null || !Rng.OneIn(DevouringChance)) return;
        var food = Player.Inventory.Pack.Where(i => i != bag && !i.IsQuestItem).ToList();
        if (food.Count == 0) return;
        var eaten = Rng.Pick(food);
        var one = Player.Inventory.Remove(eaten, 1, () => Objects.NextSerial++);
        Publish(new MessageEvent($"Your {Describe(bag, withArticle: false)} makes a small, satisfied noise. {Capital(Describe(one))} is gone!"));
        LearnRune(RuneIds.Curse("devouring"));
        RecalculateBonuses();
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
