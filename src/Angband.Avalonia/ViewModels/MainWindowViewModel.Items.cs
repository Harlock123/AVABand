using System.Collections.ObjectModel;
using System.Globalization;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Records;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Angband.Avalonia.ViewModels;

/// <summary>What an item prompt is for (Angband's command keys).</summary>
public enum ItemPromptKind
{
    Wield,
    TakeOff,
    Quaff,
    Read,
    Eat,
    Drop,
    Throw,
    Pickup,
    Refuel,
    Inspect,
    Aim,
    UseStaff,
    Zap,
    Activate,
    Inscribe,
    Uninscribe,
    Ignore,
    /// <summary>Any item that can be used (Angband 'U'): each goes to its own command.</summary>
    UseAny,
}

/// <summary>One line in the inventory panel or an item prompt.</summary>
public sealed record ItemRow(string Letter, string Glyph, uint GlyphColor, string Name, string Weight, Item Item)
{
    public IBrush GlyphBrush { get; } = new ImmutableSolidColorBrush(Color.FromUInt32(GlyphColor));
}

/// <summary>Inventory panel and Angband-style "which item?" prompts.</summary>
public sealed partial class MainWindowViewModel
{
    private ItemPromptKind _promptKind;

    public ObservableCollection<ItemRow> EquipmentRows { get; } = [];
    public ObservableCollection<ItemRow> PackRows { get; } = [];
    public ObservableCollection<ItemRow> QuiverRows { get; } = [];
    public ObservableCollection<ItemRow> FloorRows { get; } = [];
    public ObservableCollection<ItemRow> PromptRows { get; } = [];

    [ObservableProperty] private string _goldText = "";
    [ObservableProperty] private bool _hasFloorItems;
    [ObservableProperty] private string _burdenText = "";
    [ObservableProperty] private bool _isPrompting;
    [ObservableProperty] private string _promptTitle = "";

    /// <summary>Starts an item prompt; if nothing fits, says so instead.</summary>
    public void BeginItemPrompt(ItemPromptKind kind)
    {
        var candidates = Candidates(kind).ToList();
        if (candidates.Count == 0)
        {
            AddMessage(kind switch
            {
                ItemPromptKind.Wield => "You have nothing to wield or wear.",
                ItemPromptKind.TakeOff => "You are not wearing anything.",
                ItemPromptKind.Quaff => "You have no potions to quaff.",
                ItemPromptKind.Read => "You have no scrolls to read.",
                ItemPromptKind.Eat => "You have nothing to eat.",
                ItemPromptKind.Refuel => "You have no fuel.",
                ItemPromptKind.Pickup => "There is nothing here to pick up.",
                ItemPromptKind.Aim => "You have no wand to aim.",
                ItemPromptKind.UseStaff => "You have no staff to use.",
                ItemPromptKind.Zap => "You have no rod to zap.",
                ItemPromptKind.Activate => "You are wearing nothing you can activate.",
                ItemPromptKind.Inscribe => "You have nothing to inscribe.",
                ItemPromptKind.Uninscribe => "You have nothing with an inscription.",
                ItemPromptKind.Ignore => "You have nothing to ignore.",
                ItemPromptKind.UseAny => "You have nothing to use.",
                _ => "You have nothing to choose.",
            });
            return;
        }

        // Picking up a lone item needs no prompt.
        if (kind == ItemPromptKind.Pickup && candidates.Count == 1)
        {
            var lone = candidates[0];
            if (Inscription.AsksFirst(lone, 'g')) AskFirst($"Really pick up {_game.Describe(lone)}?", () => Execute(new PickupCommand(lone)));
            else Execute(new PickupCommand(lone));
            return;
        }

        _promptKind = kind;
        SpellPromptRows.Clear();
        PromptTitle = kind switch
        {
            ItemPromptKind.Wield => "Wear or wield which item?",
            ItemPromptKind.TakeOff => "Take off which item?",
            ItemPromptKind.Quaff => "Quaff which potion?",
            ItemPromptKind.Read => "Read which scroll?",
            ItemPromptKind.Eat => "Eat which food?",
            ItemPromptKind.Drop => "Drop which item?",
            ItemPromptKind.Throw => "Throw which item?",
            ItemPromptKind.Pickup => "Pick up which item?",
            ItemPromptKind.Refuel => "Refuel with which fuel?",
            ItemPromptKind.Aim => "Aim which wand?",
            ItemPromptKind.UseStaff => "Use which staff?",
            ItemPromptKind.Zap => "Zap which rod?",
            ItemPromptKind.Activate => "Activate which item?",
            ItemPromptKind.Inscribe => "Inscribe which item?",
            ItemPromptKind.Uninscribe => "Un-inscribe which item?",
            ItemPromptKind.Ignore => "Ignore which item?",
            ItemPromptKind.UseAny => "Use which item?",
            _ => "Inspect which item?",
        };
        PromptRows.Clear();
        for (var i = 0; i < candidates.Count && i < 26; i++)
            PromptRows.Add(Row(((char)('a' + i)).ToString(), candidates[i]));
        IsPrompting = true;
    }

    /// <summary>Handles a key while prompting: a letter picks an item, anything else cancels. Returns true if handled.</summary>
    public bool PromptKey(char key)
    {
        if (!IsPrompting) return false;
        IsPrompting = false;
        if (ChoiceRows.Count > 0)
        {
            if (_menuActions is not null) ChooseMenu(key);
            else if (_studyBooks is not null) ChooseStudyBook(key);
            else if (_spellBooks is not null) ChooseSpellBook(key);
            else ChooseIgnore(key);
            return true;
        }
        if (SpellPromptRows.Count > 0)
        {
            var spellRow = SpellPromptRows.FirstOrDefault(r => r.Letter[0] == key);
            SpellPromptRows.Clear();
            if (spellRow is null) LastMessage = "Cancelled.";
            else ChooseSpell(spellRow);
            return true;
        }
        // A digit picks the item inscribed for it (Angband @q1, or @1 for any command).
        var item = char.IsAsciiDigit(key)
            ? PromptRows.Select(r => r.Item).FirstOrDefault(i => Inscription.HasTag(i, CommandKey(_promptKind), key))
            : PromptRows.FirstOrDefault(r => r.Letter[0] == key)?.Item;
        if (item is null)
        {
            LastMessage = "Cancelled.";
            return true;
        }

        // Angband !d, !*: ask before a command uses an inscribed item.
        var kind = _promptKind;
        if (Inscription.AsksFirst(item, CommandKey(kind)))
        {
            AskFirst($"Really {CommandVerb(kind)} {_game.Describe(item)}?", () => UsePromptItem(kind, item));
            return true;
        }
        UsePromptItem(kind, item);
        return true;
    }

    /// <summary>Whether a digit picks an item in the open prompt (by its inscription).</summary>
    public bool PromptHasTag(char digit) =>
        IsPrompting && SpellPromptRows.Count == 0 && PromptRows.Any(r => Inscription.HasTag(r.Item, CommandKey(_promptKind), digit));

    /// <summary>The Angband command key an item prompt belongs to, as inscriptions name it (@q1, !d).</summary>
    public static char CommandKey(ItemPromptKind kind) => kind switch
    {
        ItemPromptKind.Wield => 'w', ItemPromptKind.TakeOff => 't', ItemPromptKind.Quaff => 'q', ItemPromptKind.Read => 'r',
        ItemPromptKind.Eat => 'E', ItemPromptKind.Drop => 'd', ItemPromptKind.Throw => 'v', ItemPromptKind.Pickup => 'g',
        ItemPromptKind.Refuel => 'F', ItemPromptKind.Inspect => 'I', ItemPromptKind.Aim => 'a', ItemPromptKind.UseStaff => 'u',
        ItemPromptKind.Zap => 'z', ItemPromptKind.Activate => 'A', ItemPromptKind.Inscribe => '{', ItemPromptKind.Ignore => 'k',
        ItemPromptKind.UseAny => 'U', _ => '}',
    };

    private static string CommandVerb(ItemPromptKind kind) => kind switch
    {
        ItemPromptKind.Wield => "wield", ItemPromptKind.TakeOff => "take off", ItemPromptKind.Quaff => "quaff",
        ItemPromptKind.Read => "read", ItemPromptKind.Eat => "eat", ItemPromptKind.Drop => "drop", ItemPromptKind.Throw => "throw",
        ItemPromptKind.Pickup => "pick up", ItemPromptKind.Refuel => "refuel with", ItemPromptKind.Inspect => "inspect",
        ItemPromptKind.Aim => "aim", ItemPromptKind.UseStaff => "use", ItemPromptKind.Zap => "zap", ItemPromptKind.Activate => "activate",
        ItemPromptKind.Inscribe => "inscribe", ItemPromptKind.Ignore => "ignore", ItemPromptKind.UseAny => "use", _ => "un-inscribe",
    };

    /// <summary>The command an item is used with, for 'U' (none if it can't be used).</summary>
    private ItemPromptKind? UseKind(Item item) => item.Base.Id switch
    {
        "potion" => ItemPromptKind.Quaff,
        "scroll" => ItemPromptKind.Read,
        "food" or "mushroom" => ItemPromptKind.Eat,
        "wand" => ItemPromptKind.Aim,
        "staff" => ItemPromptKind.UseStaff,
        "rod" => ItemPromptKind.Zap,
        _ => _game.Player.Inventory.Equipped.Contains(item) && item.CanActivate ? ItemPromptKind.Activate : null,
    };

    private void UsePromptItem(ItemPromptKind kind, Item item)
    {
        if (kind == ItemPromptKind.UseAny)
        {
            if (UseKind(item) is not { } use) return;
            if (use == ItemPromptKind.Read && _game.CannotRead() is { } why)
            {
                AddMessage(why);
                return;
            }
            kind = use;
        }
        switch (kind)
        {
            case ItemPromptKind.Inscribe:
                BeginInscription(item);
                break;
            case ItemPromptKind.Uninscribe:
                Execute(new UninscribeCommand(item));
                break;
            case ItemPromptKind.Ignore:
                BeginIgnoreMenu(item);
                break;
            case ItemPromptKind.Inspect:
                AddMessage(Inspect(item));
                break;
            case ItemPromptKind.Aim or ItemPromptKind.Zap when GameSession.NeedsDirection(item.Kind):
                AskDeviceDirection(item);
                break;
            case ItemPromptKind.Activate when GameSession.NeedsDirection(item.Activation):
                AskDeviceDirection(item, activate: true);
                break;
            case ItemPromptKind.Activate when GameSession.NeedsGlyph(item.Activation):
                BeginGlyphChoice(item, activate: true);
                break;
            case ItemPromptKind.Activate:
                Execute(new ActivateCommand(item));
                break;
            case ItemPromptKind.Quaff or ItemPromptKind.Read or ItemPromptKind.Eat or ItemPromptKind.Aim
                or ItemPromptKind.UseStaff or ItemPromptKind.Zap when GameSession.NeedsGlyph(item.Kind.Effect):
                BeginGlyphChoice(item, activate: false);
                break;
            default:
                Execute(kind switch
                {
                    ItemPromptKind.Wield => new WieldCommand(item),
                    ItemPromptKind.TakeOff => new TakeOffCommand(item),
                    ItemPromptKind.Drop => new DropCommand(item, item.Number),
                    ItemPromptKind.Throw => new ThrowCommand(item),
                    ItemPromptKind.Pickup => new PickupCommand(item),
                    ItemPromptKind.Refuel => new RefuelCommand(item),
                    _ => new UseCommand(item),
                });
                break;
        }
    }

    public void CancelPrompt()
    {
        if (!IsPrompting) return;
        IsPrompting = false;
        SpellPromptRows.Clear();
        ChoiceRows.Clear();
        _ignoring = null;
        _menuActions = null;
        _game.AimAtTargetNext = false;
        _studyBooks = null;
        _spellBooks = null;
        LastMessage = "Cancelled.";
    }

    private IEnumerable<Item> Candidates(ItemPromptKind kind)
    {
        var inv = _game.Player.Inventory;
        var floor = _game.Level.Objects.At(_game.Player.Position).Where(i => !i.IsGold && !Hidden(i));
        var carried = inv.Pack.Concat(inv.Quiver);
        return kind switch
        {
            ItemPromptKind.Wield => carried.Concat(floor).Where(i => i.IsWearable),
            ItemPromptKind.TakeOff => inv.Equipped,
            ItemPromptKind.Quaff => carried.Concat(floor).Where(i => i.Base.Id == "potion"),
            ItemPromptKind.Read => carried.Concat(floor).Where(i => i.Base.Id == "scroll"),
            ItemPromptKind.Eat => carried.Concat(floor).Where(i => i.Base.Id is "food" or "mushroom"),
            ItemPromptKind.Aim => inv.Pack.Where(i => i.Base.Id == "wand"),
            ItemPromptKind.UseStaff => inv.Pack.Where(i => i.Base.Id == "staff"),
            ItemPromptKind.Zap => inv.Pack.Where(i => i.Base.Id == "rod"),
            ItemPromptKind.Activate => inv.Equipped.Where(i => i.CanActivate),
            ItemPromptKind.Drop => carried,
            // Throwing weapons first (Angband SHOW_THROWING) — those in the quiver, then the rest — then
            // everything else, then the wielded weapon.
            ItemPromptKind.Throw => carried.Concat(floor).OrderBy(i => !i.IsThrowing ? 2 : inv.Quiver.Contains(i) ? 0 : 1)
                .Concat(inv.Weapon is { } weapon ? [weapon] : []),
            ItemPromptKind.Pickup => floor,
            ItemPromptKind.Refuel => inv.Pack.Where(i => i.Kind.Has("FUEL")),
            ItemPromptKind.Inscribe => inv.Equipped.Concat(carried).Concat(floor),
            ItemPromptKind.Uninscribe => inv.Equipped.Concat(carried).Concat(floor).Where(i => i.Note is not null),
            ItemPromptKind.Ignore => inv.Equipped.Concat(carried).Concat(floor),
            ItemPromptKind.UseAny => carried.Concat(floor).Concat(inv.Equipped).Where(i => UseKind(i) is not null)
                .Where(i => i.Base.Id is not ("wand" or "staff" or "rod") || inv.Pack.Contains(i)),
            _ => inv.Equipped.Concat(carried).Concat(floor),
        };
    }

    /// <summary>What the player knows about an item (the object info text, on one line).</summary>
    private string Inspect(Item item)
    {
        var lines = ObjectInfo.DescribeItem(_game, item).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines[0] += ".";
        return string.Join(" ", lines);
    }

    private void RefreshInventory()
    {
        var p = _game.Player;
        var inv = p.Inventory;

        EquipmentRows.Clear();
        for (var i = 0; i < Inventory.Slots.Count; i++)
            if (inv.Equipment[i] is { } item) EquipmentRows.Add(Row(Inventory.Slots[i].Name, item));

        PackRows.Clear();
        for (var i = 0; i < inv.Pack.Count; i++) PackRows.Add(Row(((char)('a' + i)).ToString(), inv.Pack[i]));

        QuiverRows.Clear();
        for (var i = 0; i < inv.Quiver.Count; i++) QuiverRows.Add(Row(i.ToString(CultureInfo.InvariantCulture), inv.Quiver[i]));

        FloorRows.Clear();
        foreach (var item in _game.Level.Objects.At(p.Position).Where(i => !Hidden(i))) FloorRows.Add(Row("-", item));
        HasFloorItems = FloorRows.Count > 0;

        GoldText = $"Gold {p.Gold}";
        var slow = p.Inventory.TotalWeight > p.WeightLimit / 2 ? " (burdened)" : "";
        BurdenText = string.Format(CultureInfo.InvariantCulture, "Weight {0:0.0} / {1:0} lb{2}  |  Slots {3}/{4}",
            inv.TotalWeight / 10.0, p.WeightLimit / 20.0, slow, inv.SlotsUsed, inv.PackSize);
    }

    private ItemRow Row(string letter, Item item)
    {
        var flavor = item.IsFlavored ? _game.Knowledge.Flavor(item.Kind) : null;
        var weight = item.IsGold ? "" : string.Format(CultureInfo.InvariantCulture, "{0:0.0} lb", item.TotalWeight / 10.0);
        return new ItemRow(letter, item.Base.Glyph.ToString(), _cells.Color(flavor?.Color ?? item.Base.Color),
            _game.Describe(item), weight, item);
    }
}
