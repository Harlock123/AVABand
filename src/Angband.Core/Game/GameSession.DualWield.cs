using Angband.Core.Definitions;
using Angband.Core.Items;
using Angband.Core.Time;

namespace Angband.Core.Game;

/// <summary>AVABand, Humans: wields a light weapon in the off hand (where a shield would go). Takes a turn.</summary>
public sealed record WieldOffHandCommand(Item Item) : GameCommand;

// AVABand's Human ability, DUAL_WIELD (GameSession.RaceAbilities.cs): a weapon of up to 15 lb in the
// off hand — the shield's place, so it's one or the other — gives an extra blow at the end of each
// round of melee, at -15 to hit. Its own to-hit, to-dam, blows, slays and brands count for that blow
// only, never for the main hand's.
public sealed partial class GameSession
{
    /// <summary>The heaviest weapon that can go in the off hand, in tenths of a pound.</summary>
    public const int OffHandMaxWeight = 150;

    /// <summary>The off hand's blow is this much harder to land.</summary>
    public const int OffHandToHitPenalty = 15;

    /// <summary>The off hand's penalty now: less with bracers of the Duelist (AVABand's ego), never below none.</summary>
    public int OffHandPenalty => Math.Max(0, OffHandToHitPenalty - Player.Inventory.Equipped.Sum(i => i.Ego?.OffHandBonus ?? 0));

    private static readonly int ShieldSlot = Enumerable.Range(0, Inventory.Slots.Count).First(i => Inventory.Slots[i].Type == EquipSlot.Shield);

    /// <summary>The weapon in the off hand, if any.</summary>
    public Item? OffHand => Player.Inventory.Equipment[ShieldSlot] is { Base.IsWeapon: true } w ? w : null;

    /// <summary>Whether this could go in the off hand now.</summary>
    public bool CanWieldOffHand(Item item) =>
        HasRaceAbility("DUAL_WIELD") && item.Base.IsWeapon && item.Base.Slot == EquipSlot.Weapon && item.Weight <= OffHandMaxWeight
        && Player.Inventory.Pack.Contains(item);

    private int WieldOffHand(Item item)
    {
        if (!CanWieldOffHand(item))
        {
            Publish(new MessageEvent(!HasRaceAbility("DUAL_WIELD") ? "You have no skill with two weapons."
                : item.Weight > OffHandMaxWeight ? "That is too heavy for your off hand." : "You cannot wield that in your off hand."));
            return 0;
        }
        if (Player.Inventory.Equipment[ShieldSlot] is { IsSticky: true } stuck)
        {
            Publish(new MessageEvent($"You cannot remove the {Describe(stuck, withArticle: false)} you are wearing."));
            return 0;
        }
        var previous = Player.Inventory.WieldInto(ShieldSlot, item, () => Objects.NextSerial++);
        if (previous is not null)
        {
            if (Player.Inventory.Add(previous) is null)
            {
                DropNear(previous, Player.Position);
                Publish(new MessageEvent($"You have no room for {Describe(previous)}, so it drops to the floor."));
            }
            else Publish(new MessageEvent($"You were {(previous.Base.IsWeapon ? "wielding" : "wearing")} {Describe(previous)}."));
        }
        var wielded = OffHand!;
        foreach (var rune in wielded.Runes().Where(r => r.StartsWith("mod:") || r.StartsWith("flag:"))) LearnRune(rune);
        KeenEye(wielded);
        Publish(new MessageEvent($"You are wielding {Describe(wielded)} in your off hand."));
        RecalculateBonuses();
        return EnergyTable.MoveEnergy;
    }
}
