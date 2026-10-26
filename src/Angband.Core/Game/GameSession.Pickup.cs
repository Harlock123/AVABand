using Angband.Core.Definitions;
using Angband.Core.Items;

namespace Angband.Core.Game;

/// <summary>A pickup rule for a kind of object (AVABand's own).</summary>
public enum PickupRule { Always, Never }

// AVABand's smarter pickup: rules by kind ("always pick up flasks of oil", "never pick up rations"),
// set from an item's menu and listed in Settings → Options, kept with the character; and two
// interface options, pickup_ammo (ammunition your launcher fires, whatever its enchantment) and
// pickup_unknown (flavoured things you haven't learned). An item's own =g / !g inscription still
// comes first; ignored items are never picked up. The rest is Angband's (GameSession.Options).
public sealed partial class GameSession
{
    private readonly Dictionary<string, PickupRule> _pickupRules = new(StringComparer.Ordinal);

    /// <summary>The pickup rules, by object kind.</summary>
    public IReadOnlyDictionary<string, PickupRule> PickupRules => _pickupRules;

    public PickupRule? PickupRuleFor(ObjectKindDef kind) => _pickupRules.TryGetValue(kind.Id, out var r) ? r : null;

    /// <summary>Sets (or with null, clears) the rule for a kind. Takes no game time.</summary>
    public void SetPickupRule(ObjectKindDef kind, PickupRule? rule)
    {
        using var _ = Recorded("pickup-rule", kind.Id, rule switch { PickupRule.Always => "always", PickupRule.Never => "never", _ => null });
        if (rule is { } r) _pickupRules[kind.Id] = r;
        else _pickupRules.Remove(kind.Id);
    }

    internal void RestorePickupRules(IReadOnlyDictionary<string, string>? rules)
    {
        _pickupRules.Clear();
        foreach (var (id, rule) in rules ?? new Dictionary<string, string>())
            if (Data.Object(id) is not null && rule is "always" or "never")
                _pickupRules[id] = rule == "always" ? PickupRule.Always : PickupRule.Never;
    }

    /// <summary>Ammunition the launcher you wield fires (pickup_ammo).</summary>
    private bool FitsLauncher(Item item) =>
        item.IsAmmo && Player.Inventory.Bow is { } bow && item.Base.AmmoClass is { } ammo && ammo == bow.Base.AmmoClass;

    /// <summary>A flavoured thing whose kind you haven't learned yet (pickup_unknown).</summary>
    private bool IsUnlearned(Item item) => item.IsFlavored && !Knowledge.IsAware(item.Kind);
}
