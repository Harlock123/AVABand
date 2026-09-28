using Angband.Core.Items;
using Angband.Core.World;

namespace Angband.Core.Game;

/// <summary>The choices Angband's ignore menu offers for an item (ui-object.c textui_cmd_ignore_menu).</summary>
public enum IgnoreChoice { ThisItem, UnignoreItem, Kind, UnignoreKind, Ego, UnignoreEgo, Quality }

/// <summary>One line of the ignore menu.</summary>
public sealed record IgnoreOption(IgnoreChoice Choice, string Label);

/// <summary>Ignore an item, its kind, its ego or its quality (Angband 'k' / Ctrl+D). Takes no time.</summary>
public sealed record IgnoreCommand(Item Item, IgnoreChoice Choice) : GameCommand;

/// <summary>Show or hide ignored items (Angband 'K'). Takes no time.</summary>
public sealed record ToggleUnignoreCommand : GameCommand;

// Ignoring (Angband obj-ignore.c): ignored objects vanish from the map and menus, aren't picked up,
// and those carried are dropped (unless worn or inscribed !d). 'K' shows them again for a while.
public sealed partial class GameSession
{
    /// <summary>This character's ignore settings.</summary>
    public IgnoreSettings Ignore { get; private set; } = new();

    /// <summary>Ignored objects are being shown (Angband player->unignoring).</summary>
    public bool Unignoring { get; private set; }

    /// <summary>Whether an object is hidden by ignoring right now (Angband ignore_item_ok).</summary>
    public bool IsIgnored(Item item) => !Unignoring && Ignore.IsIgnored(item, Knowledge);

    /// <summary>Whether ignoring applies to it at all (shown as {ignore} while unignoring).</summary>
    public bool IsMarkedIgnored(Item item) => Ignore.IsIgnored(item, Knowledge);

    internal void RestoreIgnore(IgnoreSettings settings) => Ignore = settings;

    /// <summary>The ignore menu for an item, as Angband builds it.</summary>
    public IReadOnlyList<IgnoreOption> IgnoreOptions(Item item)
    {
        var options = new List<IgnoreOption>
        {
            item.Ignored ? new(IgnoreChoice.UnignoreItem, "Unignore this item") : new(IgnoreChoice.ThisItem, "This item only"),
        };

        var aware = Knowledge.KnowsKind(item);
        if (Ignoring.KindBases.Contains(item.Base.Id) && (!item.IsArtifact || !aware))
        {
            var sample = item.Clone(0, 2);
            sample.Note = null;
            sample.Ego = null;
            var kinds = ItemNaming.Describe(sample, Knowledge, withArticle: false, full: false).Replace("2 ", "");
            var ignored = Ignore.KindsAware.Contains(item.Kind.Id) || Ignore.KindsUnaware.Contains(item.Kind.Id);
            options.Add(ignored ? new(IgnoreChoice.UnignoreKind, $"Unignore all {kinds}") : new(IgnoreChoice.Kind, $"All {kinds}"));
        }

        var type = Ignoring.TypeOf(item.Kind);
        if (item.Ego is { } ego && Knowledge.IsFullyKnown(item) && type is not null)
        {
            var name = $"{type.Name} {ego.Name}";
            options.Add(Ignore.Egos.Contains(IgnoreSettings.EgoKey(ego.Id, type.Id))
                ? new(IgnoreChoice.UnignoreEgo, $"Unignore all {name}")
                : new(IgnoreChoice.Ego, $"All {name}"));
        }

        var level = Ignoring.LevelOf(item, Knowledge);
        if (item.Base.Id is "ring" or "amulet" && level != IgnoreLevel.Bad) level = IgnoreLevel.Unknown;
        if (level != IgnoreLevel.Unknown && type is not null)
            options.Add(new(IgnoreChoice.Quality, $"All {Ignoring.LevelNames[level]} {type.Name}"));
        return options;
    }

    private int IgnoreItem(Item item, IgnoreChoice choice)
    {
        var type = Ignoring.TypeOf(item.Kind);
        switch (choice)
        {
            case IgnoreChoice.ThisItem:
                item.Ignored = true;
                break;
            case IgnoreChoice.UnignoreItem:
                item.Ignored = false;
                break;
            case IgnoreChoice.Kind:
                (Knowledge.KnowsKind(item) ? Ignore.KindsAware : Ignore.KindsUnaware).Add(item.Kind.Id);
                break;
            case IgnoreChoice.UnignoreKind:
                Ignore.KindsAware.Remove(item.Kind.Id);
                Ignore.KindsUnaware.Remove(item.Kind.Id);
                break;
            case IgnoreChoice.Ego when item.Ego is { } ego && type is not null:
                Ignore.Egos.Add(IgnoreSettings.EgoKey(ego.Id, type.Id));
                break;
            case IgnoreChoice.UnignoreEgo when item.Ego is { } ego && type is not null:
                Ignore.Egos.Remove(IgnoreSettings.EgoKey(ego.Id, type.Id));
                break;
            case IgnoreChoice.Quality when type is not null:
                Ignore.Quality[type.Id] = Ignoring.LevelOf(item, Knowledge);
                break;
            default:
                return 0;
        }
        Publish(new MessageEvent(IsMarkedIgnored(item) ? $"You ignore {Describe(item)}." : $"You no longer ignore {Describe(item)}."));
        IgnoreDrop();
        return 0;
    }

    /// <summary>Sets the quality ignored for an item type (the options menu).</summary>
    public void SetIgnoreQuality(string typeId, IgnoreLevel level)
    {
        using var _ = Recorded("ignore-quality", typeId, level);
        if (level is IgnoreLevel.None or IgnoreLevel.Unknown) Ignore.Quality.Remove(typeId);
        else Ignore.Quality[typeId] = level;
        IgnoreDrop();
    }

    /// <summary>Ignores (or stops ignoring) a kind, as known or unknown (the knowledge menu).</summary>
    public void SetKindIgnored(Definitions.ObjectKindDef kind, bool ignored)
    {
        using var _ = Recorded("ignore-kind", kind.Id, ignored);
        var set = Knowledge.IsAware(kind) || Knowledge.Flavor(kind) is null ? Ignore.KindsAware : Ignore.KindsUnaware;
        if (ignored) set.Add(kind.Id);
        else
        {
            Ignore.KindsAware.Remove(kind.Id);
            Ignore.KindsUnaware.Remove(kind.Id);
        }
        IgnoreDrop();
    }

    public bool IsKindIgnored(Definitions.ObjectKindDef kind) =>
        (Knowledge.IsAware(kind) || Knowledge.Flavor(kind) is null ? Ignore.KindsAware : Ignore.KindsUnaware).Contains(kind.Id);

    /// <summary>Ignores (or stops ignoring) an ego for every item type it comes on (the knowledge menu).</summary>
    public void SetEgoIgnored(Definitions.EgoItemDef ego, bool ignored)
    {
        using var _ = Recorded("ignore-ego", ego.Id, ignored);
        foreach (var type in EgoTypes(ego))
            if (ignored) Ignore.Egos.Add(IgnoreSettings.EgoKey(ego.Id, type.Id));
            else Ignore.Egos.Remove(IgnoreSettings.EgoKey(ego.Id, type.Id));
        IgnoreDrop();
    }

    public bool IsEgoIgnored(Definitions.EgoItemDef ego) =>
        EgoTypes(ego).Any() && EgoTypes(ego).All(t => Ignore.Egos.Contains(IgnoreSettings.EgoKey(ego.Id, t.Id)));

    /// <summary>Angband ego_has_ignore_type: the item types an ego can appear on.</summary>
    private IEnumerable<IgnoreType> EgoTypes(Definitions.EgoItemDef ego) =>
        Data.Objects.Where(k => ego.Bases.Contains(k.Base)).Select(Ignoring.TypeOf).OfType<IgnoreType>().Distinct();

    private int ToggleUnignore()
    {
        Unignoring = !Unignoring;
        Publish(new MessageEvent(Unignoring ? "Ignored items are shown." : "Ignored items are hidden."));
        IgnoreDrop();
        return 0;
    }

    /// <summary>
    /// Angband ignore_drop: carried objects that are now ignored are dropped — not worn ones (Angband
    /// asks first; AVABand leaves them), not in a shop, and not those inscribed !d or !*.
    /// </summary>
    private void IgnoreDrop()
    {
        if (Unignoring || Level.FeatureAt(Player.Position).Shop is not null) return;
        var inv = Player.Inventory;
        var dropped = false;
        foreach (var item in inv.Pack.Concat(inv.Quiver).Where(IsIgnored).ToList())
        {
            if (Inscription.AsksFirst(item, 'd')) continue;
            inv.Remove(item, item.Number, () => Objects.NextSerial++);
            DropUnderfoot(item);
            Publish(new MessageEvent($"You drop {Describe(item)}."));
            dropped = true;
        }
        if (dropped) RecalculateBonuses();
    }
}
