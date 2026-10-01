using Angband.Core.Definitions;
using Angband.Core.Items;

namespace Angband.Core.Game;

/// <summary>AVABand: sets a gem into socketed bracers (from the pack, into bracers worn or carried). Takes a turn.</summary>
public sealed record SetGemCommand(Item Host, Item Gem) : GameCommand;

// AVABand's socketed bracers (the arms slot) and gems (ava_objects.json, bases "bracers" and "gem").
// Setting a gem merges its properties into the bracers — to-hit, to-dam, armour, modifiers, and the
// resistances, flags and curses they didn't have (recorded on the gem, so taking it out takes only
// those) — so everything that reads worn gear sees them. You can set a gem anywhere; only the
// Armoury's armourer can prise one out, for gold, and a stone sometimes cracks coming free. A
// cursed gem's curse must be broken first.
public sealed partial class GameSession
{
    /// <summary>One gem in this many cracks as the armourer prises it out.</summary>
    public const int GemBreakChance = 8;

    /// <summary>What the armourer charges to take a gem out.</summary>
    public static int GemRemovalCost(Item gem) => 50 + gem.Kind.Cost / 5;

    public static bool IsGem(Item item) => item.Base.Id == "gem";

    /// <summary>Bracers (worn first, then carried) with a free socket.</summary>
    public IEnumerable<Item> FreeSockets() =>
        Player.Inventory.Equipped.Concat(Player.Inventory.Pack).Where(i => i.Sockets > i.Gems.Count);

    private int SetGem(Item host, Item gem)
    {
        if (!IsGem(gem) || !Player.Inventory.Pack.Contains(gem) || !Player.Inventory.Contains(host) || host.Sockets <= host.Gems.Count)
        {
            Publish(new MessageEvent(host.Sockets <= host.Gems.Count ? "There is no empty socket for it." : "You can't set that there."));
            return 0;
        }
        var one = Player.Inventory.Remove(gem, 1, () => Objects.NextSerial++);
        var setsBefore = GemSets(host).Count;
        host.Gems.Add(one);
        Merge(host, one, +1);
        foreach (var rune in one.Runes().Where(r => !r.StartsWith("curse", StringComparison.Ordinal))) LearnRune(rune);
        var worn = Player.Inventory.Equipped.Contains(host);
        // A passive curse shows itself at once on something worn, as when it's put on (the rest when they act).
        if (worn)
            foreach (var curse in one.AddedCurses)
                if (Data.Curse(curse) is { Effect: null }) LearnRune(RuneIds.Curse(curse));
        Publish(new MessageEvent($"You set {ItemNaming.Describe(one, Knowledge, full: false)} into your {ItemNaming.Describe(host, Knowledge, withArticle: false, full: false)}."));
        // A set completed (AVABand's gem sets): the stones answer one another.
        if (GemSets(host) is { } sets && sets.Count > setsBefore)
            Publish(new MessageEvent($"The stones answer one another — {sets[^1].Text}{(worn ? "" : ", once you wear them")}."));
        RecalculateBonuses();
        return Time.EnergyTable.MoveEnergy;
    }

    /// <summary>Adds a gem's properties to its host (+1), or takes away what it added (-1).</summary>
    private static void Merge(Item host, Item gem, int sign)
    {
        host.ToHit += sign * gem.ToHit;
        host.ToDam += sign * gem.ToDam;
        host.ToAc += sign * gem.ToAc;
        foreach (var (mod, value) in gem.Modifiers)
        {
            var now = host.Modifier(mod) + sign * value;
            if (now == 0) host.Modifiers.Remove(mod);
            else host.Modifiers[mod] = now;
        }
        if (sign > 0)
        {
            foreach (var r in gem.Resists) if (host.Resists.Add(r)) gem.AddedResists.Add(r);
            foreach (var f in gem.Flags.Where(f => f != "EASY_KNOW")) if (host.Flags.Add(f)) gem.AddedFlags.Add(f);
            foreach (var c in gem.Curses.Where(c => !host.Curses.Contains(c)))
            {
                host.Curses.Add(c);
                host.CursePowers[c] = gem.CursePower(c);
                gem.AddedCurses.Add(c);
            }
        }
        else
        {
            foreach (var r in gem.AddedResists) host.Resists.Remove(r);
            foreach (var f in gem.AddedFlags) host.Flags.Remove(f);
            foreach (var c in gem.AddedCurses)
            {
                host.Curses.Remove(c);
                host.CursePowers.Remove(c);
                host.CurseTimeouts.Remove(c);
            }
            gem.AddedResists.Clear();
            gem.AddedFlags.Clear();
            gem.AddedCurses.Clear();
        }
    }

    /// <summary>Bracers carried or worn with gems set in them.</summary>
    private List<Item> GemHosts() => [.. Player.Inventory.Equipped.Concat(Player.Inventory.Pack).Where(i => i.Gems.Count > 0)];

    /// <summary>The Armoury's service (from the shop screen): the armourer offers to take a gem out.</summary>
    private void OfferGemRemoval()
    {
        var hosts = GemHosts();
        var choices = new List<(string, string)>();
        foreach (var host in hosts)
            for (var i = 0; i < host.Gems.Count; i++)
                choices.Add(($"gem:out:{host.Serial}:{i}",
                    $"Take {Describe(host.Gems[i])} out of your {ItemNaming.Describe(host, Knowledge, withArticle: false, full: false)} ({GemRemovalCost(host.Gems[i])} gold)"));
        choices.Add(("back:armoury", "Back to the shop"));
        AskQuest("The Armoury", "The armourer eyes your settings. \"I can prise a stone out of those, if you like. For a price — and I don't promise the stone.\"",
            [.. choices]);
    }

    private void GemChoice(string[] parts)
    {
        if (parts.Length < 4 || parts[1] != "out" || !long.TryParse(parts[2], out var serial) || !int.TryParse(parts[3], out var index)) return;
        if (Player.Inventory.Equipped.Concat(Player.Inventory.Pack).FirstOrDefault(i => i.Serial == serial) is not { } host
            || index < 0 || index >= host.Gems.Count)
            return;
        var gem = host.Gems[index];
        var cost = GemRemovalCost(gem);
        if (gem.AddedCurses.Any(host.Curses.Contains))
            Publish(new MessageEvent("The armourer sets down his tools. \"Something in that stone doesn't want to leave. Break its curse first.\""));
        else if (Player.Gold < cost)
            Publish(new MessageEvent($"\"That'll be {cost} gold, and you haven't got it.\""));
        else
        {
            Player.Gold -= cost;
            host.Gems.RemoveAt(index);
            Merge(host, gem, -1);
            if (Rng.OneIn(GemBreakChance))
                Publish(new MessageEvent($"The {Describe(gem, withArticle: false)} cracks as it comes free, and falls to dust. \"Sorry. They do that.\""));
            else
            {
                Publish(new MessageEvent($"The armourer prises out {Describe(gem)} and hands it to you."));
                if (Player.Inventory.Add(gem) is null) DropNear(gem, Player.Position);
            }
            RecalculateBonuses();
        }
        if (GemHosts().Count > 0) OfferGemRemoval(); // another? (or back to the shop)
        else OpenStoreAfterAsking("armoury", greet: false);
    }
}
