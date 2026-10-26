using System.Collections.ObjectModel;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>One pickup rule, as Settings → Options lists it.</summary>
public sealed record PickupRuleRow(ObjectKindDef Kind, string Name, PickupRule Rule)
{
    public string RuleText => Rule == PickupRule.Always ? "always picked up" : "never picked up";
}

// AVABand's pickup rules in the UI: set from an item's menu ("Always pick these up"), listed (and
// removed) in Settings → Options. The rules themselves are the game's (GameSession.Pickup.cs).
public sealed partial class MainWindowViewModel
{
    public ObservableCollection<PickupRuleRow> PickupRuleRows { get; } = [];

    public bool HasPickupRules => PickupRuleRows.Count > 0;

    /// <summary>The kind's name as you know it: its flavour ("Blue Potions") until you've learned what it is.</summary>
    private string PickupKindName(ObjectKindDef kind)
    {
        var flavor = _game.Knowledge.Flavor(kind);
        var name = flavor is not null && !_game.Knowledge.IsAware(kind)
            ? $"{flavor.Name} {ItemNaming.Plain(_data.ObjectBase(kind.Base)?.Name ?? kind.Base, true)}"
            : ItemNaming.Plain(kind.Name, true);
        return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
    }

    private void SetPickupRule(Item item, PickupRule? rule)
    {
        _game.SetPickupRule(item.Kind, rule);
        var name = PickupKindName(item.Kind);
        AddMessage(rule switch
        {
            PickupRule.Always => $"You will pick up {name} whenever you can.",
            PickupRule.Never => $"You will leave {name} where they lie (g still picks them up).",
            _ => $"{name} are picked up as usual again.",
        });
        RefreshPickupRules();
    }

    [RelayCommand]
    private void RemovePickupRule(PickupRuleRow row)
    {
        _game.SetPickupRule(row.Kind, null);
        RefreshPickupRules();
    }

    public void RefreshPickupRules()
    {
        PickupRuleRows.Clear();
        foreach (var (id, rule) in _game.PickupRules.OrderBy(r => r.Key, StringComparer.Ordinal))
            if (_data.Object(id) is { } kind)
                PickupRuleRows.Add(new PickupRuleRow(kind, PickupKindName(kind), rule));
        OnPropertyChanged(nameof(HasPickupRules));
    }
}
