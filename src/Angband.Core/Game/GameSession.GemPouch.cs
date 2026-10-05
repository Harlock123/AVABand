using Angband.Core.Definitions;
using Angband.Core.Items;

namespace Angband.Core.Game;

// AVABand's gem pouch (Inventory.Pouch: every gem carried, one pack slot for them all) and the Arcane
// Artificer's gem cutting: three gems of a grade cut into one of the next — three chipped rubies into a
// flawed ruby, three flawed into a ruby — for a fifth of the finer stone's worth. Bloodstone, black
// onyx and the star sapphire come one way only.
public sealed partial class GameSession
{
    /// <summary>What a gem does in a socket, in words ("resist fire, +1 strength").</summary>
    public string GemEffectText(Item gem)
    {
        var parts = new List<string>();
        if (gem.ToHit != 0) parts.Add($"{gem.ToHit:+#;-#} to-hit");
        if (gem.ToDam != 0) parts.Add($"{gem.ToDam:+#;-#} to-dam");
        if (gem.ToAc != 0) parts.Add($"{gem.ToAc:+#;-#} armour");
        foreach (var (mod, value) in gem.Modifiers) parts.Add($"{value:+#;-#} {mod.Replace('_', ' ')}");
        foreach (var r in gem.Resists) parts.Add(r.StartsWith("sust_", StringComparison.Ordinal) ? $"sustain {r[5..]}" : $"resist {Data.Element(r)?.Name ?? r}");
        foreach (var f in gem.Flags.Where(f => f != "EASY_KNOW")) parts.Add(ItemFlags.Name(f));
        foreach (var c in gem.Curses) parts.Add($"cursed: {Data.Curse(c)?.Name ?? c}");
        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    /// <summary>The next grade of a cuttable gem (chipped → flawed → whole), if it has one.</summary>
    public string? NextGemGrade(string kindId) =>
        kindId.StartsWith("chipped_", StringComparison.Ordinal) ? "flawed_" + kindId["chipped_".Length..]
        : kindId.StartsWith("flawed_", StringComparison.Ordinal) ? kindId["flawed_".Length..]
        : null;

    /// <summary>What the artificer asks to cut three gems into one of the next grade.</summary>
    public long GemCuttingFee(string nextKindId) => Math.Max(1, (Data.Object(nextKindId)?.Cost ?? 0) / 5);

    /// <summary>The kinds of gem you carry three or more of, that could be cut finer.</summary>
    public IReadOnlyList<string> CuttableGems() =>
        [.. Player.Inventory.Pouch.GroupBy(i => i.Kind.Id)
            .Where(g => g.Sum(i => i.Number) >= 3 && NextGemGrade(g.Key) is { } next && Data.Object(next) is not null)
            .Select(g => g.Key)];

    /// <summary>The artificer's gem cutting: each kind you could cut, at its fee.</summary>
    private void GemCuttingMenu()
    {
        var choices = new List<(string, string)>();
        foreach (var kind in CuttableGems())
        {
            var next = Data.Object(NextGemGrade(kind)!)!;
            var from = Objects.Create(kind, 3);
            choices.Add(($"artificer:cutgem:{kind}",
                $"{ItemNaming.Describe(from, Knowledge, withArticle: false)} into {ItemNaming.Describe(Objects.Create(next.Id), Knowledge)} — {GemCuttingFee(next.Id)} gold"));
        }
        choices.Add(("artificer:menu", "Back"));
        AskQuest("The Arcane Artificer", choices.Count == 1
                ? $"{ArtificerName} tips your pouch out onto the bench. \"Nothing here I can cut finer — bring me three of a kind, chipped or flawed.\""
                : $"{ArtificerName} tips your pouch out onto the bench and sorts the stones with a fingertip. \"Three of a grade, and I'll cut you one of "
                  + $"the next. You have {Player.Gold} gold.\"",
            [.. choices]);
    }

    /// <summary>Cuts three of a gem into one of the next grade, for the fee.</summary>
    private void CutGems(string kind)
    {
        if (NextGemGrade(kind) is not { } nextId || Data.Object(nextId) is null || !CuttableGems().Contains(kind))
        {
            GemCuttingMenu();
            return;
        }
        var fee = GemCuttingFee(nextId);
        if (Player.Gold < fee)
        {
            Publish(new MessageEvent($"{ArtificerName}: \"That's {fee} gold for the cutting. Come back when you have it.\""));
            GemCuttingMenu();
            return;
        }
        var left = 3;
        foreach (var stack in Player.Inventory.Pouch.Where(i => i.Kind.Id == kind).ToList())
        {
            var take = Math.Min(left, stack.Number);
            Player.Inventory.Remove(stack, take, () => Objects.NextSerial++);
            left -= take;
            if (left == 0) break;
        }
        Player.Gold -= fee;
        var cut = Objects.Create(nextId);
        Knowledge.LearnKind(cut.Kind);
        var held = Player.Inventory.Add(cut) ?? cut;
        if (!Player.Inventory.Contains(held)) DropNear(cut, Player.Position);
        Publish(new MessageEvent($"{ArtificerName} works the three stones into one, and holds it to the light: {ItemNaming.Describe(cut, Knowledge)}."));
        GemCuttingMenu();
    }
}
