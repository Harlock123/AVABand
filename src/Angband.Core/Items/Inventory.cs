using Angband.Core.Definitions;

namespace Angband.Core.Items;

/// <summary>An equipment slot on the body.</summary>
public sealed record EquipmentSlot(string Name, EquipSlot Type);

/// <summary>
/// The player's pack, quiver and equipment (Angband gear). The pack holds <c>PackSize</c> distinct
/// stacks; every <c>QuiverSlotSize</c> missiles in the quiver use up one of those slots. AVABand's
/// quest satchel: quest items (QUEST_ITEM) ride in the pack's list, last, but take none of its slots
/// (their weight still counts). And AVABand's gem pouch: gems ride just before them, and all of them
/// together take a single slot, however many kinds.
/// </summary>
public sealed class Inventory(int packSize = 23, int quiverSlotSize = 40, int quiverSize = 8)
{
    public static readonly IReadOnlyList<EquipmentSlot> Slots =
    [
        new("weapon", EquipSlot.Weapon), new("bow", EquipSlot.Bow), new("ring (left)", EquipSlot.Ring),
        new("ring (right)", EquipSlot.Ring), new("amulet", EquipSlot.Amulet), new("light", EquipSlot.Light),
        new("body", EquipSlot.Body), new("cloak", EquipSlot.Cloak), new("shield", EquipSlot.Shield),
        new("head", EquipSlot.Head), new("hands", EquipSlot.Hands), new("feet", EquipSlot.Feet),
        new("arms", EquipSlot.Arms), // AVABand's bracers (last: older saves' equipment lines up)
    ];

    private readonly List<Item> _pack = [];
    private readonly List<Item> _quiver = [];
    private readonly Item?[] _equipment = new Item?[Slots.Count];

    /// <summary>How many distinct things the pack holds: Angband's 23, and more with AVABand's best bag of holding in it.</summary>
    public int PackSize => BasePackSize + BagSlots;

    /// <summary>The pack's own size, without a bag.</summary>
    public int BasePackSize { get; } = packSize;

    /// <summary>The best bag of holding carried (only the best counts), or null.</summary>
    public Item? BestBag => _pack.Where(i => i.Kind.CarryPercent > 0 || i.Kind.PackSlots > 0)
        .OrderByDescending(i => i.Kind.PackSlots).ThenByDescending(i => i.Kind.CarryPercent).FirstOrDefault();

    /// <summary>The extra room the best bag gives.</summary>
    public int BagSlots => BestBag?.Kind.PackSlots ?? 0;
    public int QuiverSlotSize { get; } = quiverSlotSize;
    public int QuiverSize { get; } = quiverSize;

    /// <summary>A throwing weapon in the quiver counts as this many missiles (Angband thrown-quiver-mult).</summary>
    public const int ThrownQuiverMultiplier = 5;

    /// <summary>Missiles in the quiver, throwing weapons counting five each.</summary>
    public int QuiverCount => _quiver.Sum(q => q.QuiverWeight);

    /// <summary>
    /// Whether an item goes in the quiver: ammunition always; a throwing weapon only when inscribed
    /// with the quiver slot it wants (<c>@v1</c>, <c>@f1</c>), as in Angband 4.2.
    /// </summary>
    public static bool BelongsInQuiver(Item item) => item.IsAmmo || item.IsThrowing && Inscription.QuiverSlot(item) is not null;

    private int PackSlotsFor(int quiverCount) => (quiverCount + QuiverSlotSize - 1) / QuiverSlotSize;

    /// <summary>
    /// The quiver stack an item would join or become, if it fits there (Angband quiver_absorb_num):
    /// the pack has room for the extra missiles, and a throwing weapon's stack stays within one slot
    /// (eight) and doesn't take a slot another throwing weapon wants.
    /// </summary>
    private (bool Fits, Item? Match) QuiverPlace(Item item)
    {
        if (!BelongsInQuiver(item)) return (false, null);
        if (PackStacks + PackSlotsFor(QuiverCount + item.QuiverWeight) > PackSize) return (false, null);
        var match = _quiver.FirstOrDefault(q => q.CanStackWith(item));
        if (!item.IsAmmo)
        {
            var total = (match?.Number ?? 0) + item.Number;
            if (total * ThrownQuiverMultiplier > QuiverSlotSize) return (false, null);
            var slot = Inscription.QuiverSlot(item);
            if (match is null && _quiver.Any(q => !q.IsAmmo && Inscription.QuiverSlot(q) == slot)) return (false, null);
        }
        if (match is not null) return (true, match);
        return (_quiver.Count < QuiverSize, null);
    }

    public IReadOnlyList<Item> Pack => _pack;
    public IReadOnlyList<Item> Quiver => _quiver;
    public IReadOnlyList<Item?> Equipment => _equipment;

    public IEnumerable<Item> Equipped => _equipment.OfType<Item>();
    public IEnumerable<Item> All => _pack.Concat(_quiver).Concat(Equipped);

    public Item? InSlot(EquipSlot type) => _equipment
        .Select((item, i) => (item, i)).Where(t => Slots[t.i].Type == type).Select(t => t.item).FirstOrDefault(i => i is not null);

    public Item? Weapon => InSlot(EquipSlot.Weapon);
    public Item? Bow => InSlot(EquipSlot.Bow);
    public Item? Light => InSlot(EquipSlot.Light);

    /// <summary>Pack slots in use, counting the quiver (not the quest satchel).</summary>
    public int SlotsUsed => PackStacks + PackSlotsFor(QuiverCount);

    /// <summary>The pack's stacks that take a slot: all but quest items (the satchel), with the gem pouch one slot for all its gems.</summary>
    private int PackStacks => _pack.Count(i => !i.IsQuestItem && !InPouch(i)) + (_pack.Any(InPouch) ? 1 : 0);

    /// <summary>A gem rides in the gem pouch.</summary>
    public static bool InPouch(Item item) => item.Base.Id == "gem" && !item.IsQuestItem;

    /// <summary>The gem pouch: the gems carried (between the pack's own things and the satchel).</summary>
    public IEnumerable<Item> Pouch => _pack.Where(InPouch);

    /// <summary>The quest satchel: quest items carried (the last of the pack's list).</summary>
    public IEnumerable<Item> Satchel => _pack.Where(i => i.IsQuestItem);

    /// <summary>Total carried weight including equipment, in tenths of a pound.</summary>
    public int TotalWeight => All.Sum(i => i.TotalWeight);

    public bool Contains(Item item) => _pack.Contains(item) || _quiver.Contains(item) || _equipment.Contains(item);

    /// <summary>Whether the whole stack could be added (merging or into a free slot).</summary>
    public bool CanCarry(Item item)
    {
        if (item.IsQuestItem) return true; // (the satchel always has room)
        if (InPouch(item) && _pack.Any(InPouch)) return true; // (and the pouch, once it's carried, for any gem)
        if (QuiverPlace(item).Fits) return true;
        if (_pack.Any(p => p.CanStackWith(item) && p.Number + item.Number <= p.Base.MaxStack)) return true;
        return SlotsUsed < PackSize;
    }

    /// <summary>
    /// Adds an item, merging with a matching stack when possible. Missiles go to the quiver.
    /// Returns the stack now holding it, or null if there is no room.
    /// </summary>
    public Item? Add(Item item)
    {
        if (!CanCarry(item)) return null;

        if (QuiverPlace(item) is (true, var match))
        {
            if (match is not null) { Absorb(match, item); return match; }
            _quiver.Add(item);
            SortQuiver();
            return item;
        }

        var stack = _pack.FirstOrDefault(p => p.CanStackWith(item) && p.Number + item.Number <= p.Base.MaxStack);
        if (stack is not null)
        {
            Absorb(stack, item);
            return stack;
        }
        _pack.Add(item);
        SortPack();
        return item;
    }

    /// <summary>Merges an item into a stack; the stack takes its inscription if it had none (Angband object_absorb).</summary>
    private static void Absorb(Item stack, Item item) => stack.Absorb(item);

    /// <summary>
    /// Orders the quiver: ammunition inscribed <c>@f#</c> or <c>@v#</c> by that number, the rest after it
    /// (Angband preferred_quiver_slot). The first stack is what 'f' fires by default.
    /// </summary>
    public void SortQuiver()
    {
        // Inscriptions may have moved throwing weapons in or out of the quiver.
        foreach (var item in _pack.Where(BelongsInQuiver).ToList())
        {
            _pack.Remove(item);
            if (QuiverPlace(item) is (true, var match))
            {
                if (match is not null) Absorb(match, item);
                else _quiver.Add(item);
            }
            else _pack.Add(item);
        }
        foreach (var item in _quiver.Where(q => !BelongsInQuiver(q)).ToList())
        {
            _quiver.Remove(item);
            var stack = _pack.FirstOrDefault(p => p.CanStackWith(item) && p.Number + item.Number <= p.Base.MaxStack);
            if (stack is not null) Absorb(stack, item);
            else _pack.Add(item);
        }
        SortPack();

        var ordered = _quiver.Select((q, i) => (q, i)).OrderBy(t => Inscription.QuiverSlot(t.q) ?? int.MaxValue).ThenBy(t => t.i)
            .Select(t => t.q).ToList();
        _quiver.Clear();
        _quiver.AddRange(ordered);
    }

    /// <summary>Removes <paramref name="count"/> from a carried stack; returns the removed part.</summary>
    public Item Remove(Item item, int count, Func<long> nextSerial)
    {
        if (count >= item.Number)
        {
            if (!_pack.Remove(item) && !_quiver.Remove(item))
            {
                var slot = Array.IndexOf(_equipment, item);
                if (slot < 0) throw new InvalidOperationException($"{item} is not carried.");
                _equipment[slot] = null;
            }
            return item;
        }
        return item.Split(nextSerial(), count);
    }

    /// <summary>The slot an item would be wielded into (a free one of its type, else the first).</summary>
    public int SlotFor(Item item)
    {
        var candidates = Enumerable.Range(0, Slots.Count).Where(i => Slots[i].Type == item.Base.Slot).ToList();
        if (candidates.Count == 0) return -1;
        // An empty slot first, else one whose occupant can come off (not a STICKY ring).
        return candidates.FirstOrDefault(i => _equipment[i] is null,
            candidates.FirstOrDefault(i => _equipment[i] is { IsSticky: false }, candidates[0]));
    }

    /// <summary>Wields one of <paramref name="item"/> (from the pack, quiver or floor); returns what it replaced.</summary>
    public Item? Wield(Item item, Func<long> nextSerial)
    {
        var slot = SlotFor(item);
        if (slot < 0) throw new InvalidOperationException($"{item} cannot be wielded.");
        var one = Contains(item) ? Remove(item, 1, nextSerial) : item.Number > 1 ? item.Split(nextSerial(), 1) : item;
        var previous = _equipment[slot];
        _equipment[slot] = one;
        return previous;
    }

    /// <summary>Wields one of <paramref name="item"/> into a given slot (AVABand's off hand); returns what it replaced.</summary>
    public Item? WieldInto(int slot, Item item, Func<long> nextSerial)
    {
        var one = Contains(item) ? Remove(item, 1, nextSerial) : item.Number > 1 ? item.Split(nextSerial(), 1) : item;
        var previous = _equipment[slot];
        _equipment[slot] = one;
        return previous;
    }

    /// <summary>Takes off an equipped item into the pack; false if there is no room.</summary>
    public bool TakeOff(Item item)
    {
        var slot = Array.IndexOf(_equipment, item);
        if (slot < 0 || SlotsUsed >= PackSize) return false;
        _equipment[slot] = null;
        Add(item);
        return true;
    }

    /// <summary>Puts an item straight into an equipment slot (loading a save).</summary>
    public void RestoreEquipment(int slot, Item item) => _equipment[slot] = item;

    /// <summary>Removes items whose count dropped to zero (after use or firing).</summary>
    public void Prune()
    {
        _pack.RemoveAll(i => i.Number <= 0);
        _quiver.RemoveAll(i => i.Number <= 0);
        for (var i = 0; i < _equipment.Length; i++)
            if (_equipment[i] is { Number: <= 0 }) _equipment[i] = null;
    }

    /// <summary>
    /// Angband combine_pack: stacks that can now go together (a kind learned, an inscription
    /// gone) are merged, as far as a stack may grow; the pack is put back in order. Returns how many
    /// merged.
    /// </summary>
    public int CombinePack()
    {
        var merged = 0;
        for (var i = _pack.Count - 1; i > 0; i--)
            for (var j = 0; j < i; j++)
                if (_pack[j].CanStackWith(_pack[i]) && _pack[j].Number + _pack[i].Number <= _pack[j].Base.MaxStack)
                {
                    _pack[j].Absorb(_pack[i]);
                    _pack.RemoveAt(i);
                    merged++;
                    break;
                }
        SortPack();
        return merged;
    }

    /// <summary>The pack's own things first, then the gem pouch, then the quest satchel.</summary>
    private static int Rank(Item item) => item.IsQuestItem ? 2 : InPouch(item) ? 1 : 0;

    /// <summary>Angband pack order: by base (in data order), then kind level, then name; the gem pouch and the quest satchel last.</summary>
    private void SortPack() => _pack.Sort((a, b) =>
    {
        var c = Rank(a).CompareTo(Rank(b));
        if (c != 0) return c;
        c = string.CompareOrdinal(a.Base.Id, b.Base.Id);
        if (c != 0) return c;
        c = a.Kind.Level.CompareTo(b.Kind.Level);
        return c != 0 ? c : string.CompareOrdinal(a.Kind.Id, b.Kind.Id);
    });
}
