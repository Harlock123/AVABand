using Angband.Core.Definitions;
using Angband.Core.Items;

namespace Angband.Core.Game;

/// <summary>What a hotbar slot holds: a spell, or a kind of object (whichever of them you carry).</summary>
public sealed record HotbarEntry(string? SpellId, string? KindId)
{
    public static HotbarEntry ForSpell(string spellId) => new(spellId, null);
    public static HotbarEntry ForKind(string kindId) => new(null, kindId);

    /// <summary>As saved: "spell:id" or "item:id".</summary>
    public string Code => SpellId is not null ? $"spell:{SpellId}" : $"item:{KindId}";

    public static HotbarEntry? Parse(string? code) => code switch
    {
        { } c when c.StartsWith("spell:", StringComparison.Ordinal) => ForSpell(c[6..]),
        { } c when c.StartsWith("item:", StringComparison.Ordinal) => ForKind(c[5..]),
        _ => null,
    };
}

// The hotbar (AVABand's own, not Angband's): ten slots of spells and items, used with Alt+1..Alt+0
// or a click. It belongs to the character and is saved with it.
public sealed partial class GameSession
{
    public const int HotbarSize = 10;

    private readonly HotbarEntry?[] _hotbar = new HotbarEntry?[HotbarSize];

    public IReadOnlyList<HotbarEntry?> Hotbar => _hotbar;

    public void SetHotbar(int slot, HotbarEntry? entry)
    {
        if (slot is >= 0 and < HotbarSize) _hotbar[slot] = entry;
    }

    internal void RestoreHotbar(IReadOnlyList<string?> codes)
    {
        for (var i = 0; i < HotbarSize; i++) _hotbar[i] = HotbarEntry.Parse(codes.ElementAtOrDefault(i));
    }

    /// <summary>The item a slot would use: the first of its kind in the pack, quiver or equipment.</summary>
    public Item? HotbarItem(HotbarEntry entry) => entry.KindId is not { } kind ? null
        : Player.Inventory.Pack.Concat(Player.Inventory.Quiver).Concat(Player.Inventory.Equipped)
            .FirstOrDefault(i => i.Kind.Id == kind);

    /// <summary>How many of a slot's kind you carry.</summary>
    public int HotbarCount(HotbarEntry entry) => entry.KindId is not { } kind ? 0
        : Player.Inventory.Pack.Concat(Player.Inventory.Quiver).Concat(Player.Inventory.Equipped)
            .Where(i => i.Kind.Id == kind).Sum(i => i.Number);

    /// <summary>A slot's spell, if it is one you know and have the book for.</summary>
    public SpellDef? HotbarSpell(HotbarEntry entry) =>
        entry.SpellId is { } id && Player.LearnedSpells.Contains(id) && Data.Spell(id) is { } spell && HasBookFor(spell) ? spell : null;
}
