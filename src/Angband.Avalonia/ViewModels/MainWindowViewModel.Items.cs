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
    /// <summary>An item from the pack (Angband 'i') or what you wear ('e'), for its menu.</summary>
    InventoryMenu, EquipmentMenu,
    /// <summary>Reading, looking at or using one of AVABand's quest items.</summary>
    QuestUse,
    /// <summary>AVABand's weapon oils, rubbed on a weapon ('U', or the item's menu).</summary>
    Apply,
}

/// <summary>One line in the inventory panel or an item prompt.</summary>
public sealed record ItemRow(string Letter, string Glyph, uint GlyphColor, string Name, string Weight, Item Item,
    string Advice = "", uint AdviceColor = 0xFFD8C07A)
{
    public IBrush GlyphBrush { get; } = new ImmutableSolidColorBrush(Color.FromUInt32(GlyphColor));

    /// <summary>The shops' note (the Wear/Wield prompt shows it): whether it would suit you better than what it replaces.</summary>
    public bool HasAdvice => Advice.Length > 0;
    public IBrush AdviceBrush { get; } = new ImmutableSolidColorBrush(Color.FromUInt32(AdviceColor));

    /// <summary>The row in words (its accessible name): the note after the name.</summary>
    public string Spoken => HasAdvice ? $"{Name}. {Advice}" : Name;

    /// <summary>The pickup prompt's first row (AVABand's own): everything here at once, not the one item behind it.</summary>
    public bool AllOfThem { get; init; }
}

/// <summary>Inventory panel and Angband-style "which item?" prompts.</summary>
public sealed partial class MainWindowViewModel
{
    private ItemPromptKind _promptKind;

    public ObservableCollection<ItemRow> EquipmentRows { get; } = [];
    public ObservableCollection<ItemRow> PackRows { get; } = [];
    /// <summary>The book bag (AVABand's own): every spellbook carried, one pack slot for them all.</summary>
    public ObservableCollection<ItemRow> BookRows { get; } = [];
    [ObservableProperty] private bool _hasBookBagItems;
    /// <summary>The gem pouch (AVABand's own): every gem carried, one pack slot for them all, each with what it does in a socket.</summary>
    public ObservableCollection<ItemRow> PouchRows { get; } = [];
    [ObservableProperty] private bool _hasPouchItems;
    /// <summary>The quest satchel (AVABand's own): quest items, lettered after the pack's, taking none of its slots.</summary>
    public ObservableCollection<ItemRow> SatchelRows { get; } = [];
    [ObservableProperty] private bool _hasSatchelItems;
    public ObservableCollection<ItemRow> QuiverRows { get; } = [];
    public ObservableCollection<ItemRow> FloorRows { get; } = [];
    public ObservableCollection<ItemRow> PromptRows { get; } = [];

    [ObservableProperty] private string _goldText = "";
    [ObservableProperty] private bool _hasFloorItems;
    [ObservableProperty] private string _burdenText = "";

    /// <summary>The weight and slots line in words (its accessible name).</summary>
    [ObservableProperty] private string _burdenSpoken = "";

    private bool _packWasFull;
    [ObservableProperty] private bool _isPrompting;
    [ObservableProperty] private string _promptTitle = "";

    /// <summary>Starts an item prompt; if nothing fits, says so instead.</summary>
    public void BeginItemPrompt(ItemPromptKind kind)
    {
        if (kind == ItemPromptKind.Refuel && _game.RefillProblem() is { } why)
        {
            AddMessage(why);
            return;
        }
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
                ItemPromptKind.Refuel => "You have nothing you can refuel with.",
                ItemPromptKind.Pickup => "There is nothing here to pick up.",
                ItemPromptKind.Aim => "You have no wand to aim.",
                ItemPromptKind.UseStaff => "You have no staff to use.",
                ItemPromptKind.Zap => "You have no rod to zap.",
                ItemPromptKind.Activate => "You are wearing nothing you can activate.",
                ItemPromptKind.Inscribe => "You have nothing to inscribe.",
                ItemPromptKind.Uninscribe => "You have nothing with an inscription.",
                ItemPromptKind.Ignore => "You have nothing to ignore.",
                ItemPromptKind.UseAny => "You have nothing to use.",
                ItemPromptKind.Apply => "You have no weapon oil.",
                ItemPromptKind.InventoryMenu => "You have nothing in your pack.",
                ItemPromptKind.EquipmentMenu => "You are not wearing anything.",
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
            ItemPromptKind.InventoryMenu => "Inventory — which item?",
            ItemPromptKind.EquipmentMenu => "Equipment — which item?",
            ItemPromptKind.TakeOff => "Take off which item?",
            ItemPromptKind.Quaff => "Quaff which potion?",
            ItemPromptKind.Read => "Read which scroll?",
            ItemPromptKind.Eat => "Eat which food?",
            ItemPromptKind.Drop => "Drop which item?",
            ItemPromptKind.Throw => "Throw which item?",
            ItemPromptKind.Pickup => "Pick up which item?",
            ItemPromptKind.Refuel => "Refuel with which fuel source?",
            ItemPromptKind.Aim => "Aim which wand?",
            ItemPromptKind.UseStaff => "Use which staff?",
            ItemPromptKind.Zap => "Zap which rod?",
            ItemPromptKind.Activate => "Activate which item?",
            ItemPromptKind.Inscribe => "Inscribe which item?",
            ItemPromptKind.Uninscribe => "Un-inscribe which item?",
            ItemPromptKind.Ignore => "Ignore which item?",
            ItemPromptKind.UseAny => "Use which item?",
            ItemPromptKind.Apply => "Apply which oil?",
            _ => "Inspect which item?",
        };
        PromptRows.Clear();
        // Picking up from a pile (AVABand's own): a) takes everything here, and the pile's own things start at b).
        var first = 0;
        if (kind == ItemPromptKind.Pickup && candidates.Count > 1)
        {
            var weight = candidates.Where(i => !i.IsGold).Sum(i => i.TotalWeight);
            PromptRows.Add(new ItemRow("a", "*", 0xFFFFE08A, $"Everything here ({candidates.Count} things)",
                string.Format(CultureInfo.InvariantCulture, "{0:0.0} lb", weight / 10.0), candidates[0]) { AllOfThem = true });
            first = 1;
        }
        for (var i = 0; i < candidates.Count && i + first < 26; i++)
        {
            var row = Row(((char)('a' + i + first)).ToString(), candidates[i]);
            // Wear or wield: each with the shops' note on how it compares with what it would replace.
            if (kind == ItemPromptKind.Wield && WieldAdvice(candidates[i]) is { } advice)
                row = row with
                {
                    Advice = advice.Text,
                    // (Through the map's palette, so the colour-blind option applies.)
                    AdviceColor = _cells.Color(advice.Tone switch { > 0 => "LightGreen", < 0 => "LightRed", _ => "Yellow" }),
                };
            PromptRows.Add(row);
        }
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
            if (spellRow is null)
            {
                _assignSlot = null;
                LastMessage = "Cancelled.";
            }
            else if (!AssignChosen(null, spellRow.Spell)) ChooseSpell(spellRow);
            return true;
        }
        // The pickup prompt's a): everything here.
        if (!char.IsAsciiDigit(key) && PromptRows.FirstOrDefault(r => r.Letter[0] == key) is { AllOfThem: true })
        {
            _assignSlot = null;
            PickUpEverything();
            return true;
        }
        // A digit picks the item inscribed for it (Angband @q1, or @1 for any command).
        var item = char.IsAsciiDigit(key)
            ? PromptRows.Select(r => r.Item).FirstOrDefault(i => Inscription.HasTag(i, CommandKey(_promptKind), key))
            : PromptRows.FirstOrDefault(r => r.Letter[0] == key)?.Item;
        if (item is null)
        {
            _assignSlot = null;
            LastMessage = "Cancelled.";
            return true;
        }
        if (AssignChosen(item, null)) return true; // filling a hotbar slot
        UseItemAsked(_promptKind, item);
        return true;
    }

    /// <summary>Picks up everything underfoot — asking first if anything there is inscribed to ask (Angband !g, !*).</summary>
    private void PickUpEverything()
    {
        var here = Candidates(ItemPromptKind.Pickup).ToList();
        if (here.Any(i => Inscription.AsksFirst(i, 'g'))) AskFirst("Really pick up everything here?", () => Execute(new PickupCommand()));
        else Execute(new PickupCommand());
    }

    /// <summary>Uses an item as the command says — asking first if its inscription says to (Angband !d, !*).</summary>
    private void UseItemAsked(ItemPromptKind kind, Item item)
    {
        if (Inscription.AsksFirst(item, CommandKey(kind)))
        {
            AskFirst($"Really {CommandVerb(kind)} {_game.Describe(item)}?", () => UsePromptItem(kind, item));
            return;
        }
        UsePromptItem(kind, item);
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
        ItemPromptKind.UseAny => 'U', ItemPromptKind.InventoryMenu => 'i', ItemPromptKind.EquipmentMenu => 'e', ItemPromptKind.QuestUse => 'U', ItemPromptKind.Apply => 'U', _ => '}',
    };

    private static string CommandVerb(ItemPromptKind kind) => kind switch
    {
        ItemPromptKind.Wield => "wield", ItemPromptKind.TakeOff => "take off", ItemPromptKind.Quaff => "quaff",
        ItemPromptKind.Read => "read", ItemPromptKind.Eat => "eat", ItemPromptKind.Drop => "drop", ItemPromptKind.Throw => "throw",
        ItemPromptKind.Pickup => "pick up", ItemPromptKind.Refuel => "refuel with", ItemPromptKind.Inspect => "inspect",
        ItemPromptKind.Aim => "aim", ItemPromptKind.UseStaff => "use", ItemPromptKind.Zap => "zap", ItemPromptKind.Activate => "activate",
        ItemPromptKind.Inscribe => "inscribe", ItemPromptKind.Ignore => "ignore", ItemPromptKind.UseAny => "use",
        ItemPromptKind.InventoryMenu or ItemPromptKind.EquipmentMenu => "choose", ItemPromptKind.QuestUse => "use", ItemPromptKind.Apply => "apply", _ => "un-inscribe",
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
        "quest" => ItemPromptKind.QuestUse,
        "oil" => ItemPromptKind.Apply,
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
            case ItemPromptKind.InventoryMenu or ItemPromptKind.EquipmentMenu:
                OpenItemMenu(item);
                break;
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
            case ItemPromptKind.Read or ItemPromptKind.UseStaff
                when GameSession.NeedsCurseChoice(item.Kind.Effect) && _game.Knowledge.KnowsKind(item):
                BeginUncurse(choice => new UseCommand(item, Uncurse: choice), _game.UncurseStrengthText(item.Kind.Effect));
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
        _assignSlot = null;
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
            ItemPromptKind.Apply => carried.Concat(floor).Where(i => i.Base.Id == "oil"),
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
            ItemPromptKind.Refuel => carried.Concat(floor).Where(_game.CanRefillFrom),
            ItemPromptKind.Inscribe => inv.Equipped.Concat(carried).Concat(floor),
            ItemPromptKind.Uninscribe => inv.Equipped.Concat(carried).Concat(floor).Where(i => i.Note is not null),
            ItemPromptKind.Ignore => inv.Equipped.Concat(carried).Concat(floor),
            ItemPromptKind.InventoryMenu => carried,
            ItemPromptKind.EquipmentMenu => inv.Equipped,
            ItemPromptKind.UseAny => carried.Concat(floor).Concat(inv.Equipped).Where(i => UseKind(i) is not null)
                .Where(i => i.Base.Id is not ("wand" or "staff" or "rod") || inv.Pack.Contains(i)),
            _ => inv.Equipped.Concat(carried).Concat(floor),
        };
    }

    /// <summary>What the player knows about an item (the object info text, on one line).</summary>
    /// <summary>
    /// An item beside what you wear in its slot, line by line (AVABand's own) — for shop stock, or your own
    /// things once you know all their runes (the comparison never tells what you haven't learned).
    /// </summary>
    public void ShowComparison(Item item, bool stock)
    {
        var known = stock || _game.Knowledge.IsFullyKnown(item);
        var title = $"{Capitalize(_game.Describe(item))}";
        var text = known ? _game.CompareText(item)
            : "You don't know all of its runes yet, so it can't be compared fairly — wear it a while, or have it identified.";
        ShowMenu(title, [("Close", () => { })], text);
    }

    private string Inspect(Item item)
    {
        var lines = ObjectInfo.DescribeItem(_game, item).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines[0] += ".";
        return string.Join(" ", lines) + (AdviceNote(item) is { } note ? " " + note : "");
    }

    /// <summary>
    /// The shops' "will this suit me?" note, anywhere (inspecting, looking): how something you could
    /// wear compares with what you have, a gem or bag likewise, missiles whether they fit — for things
    /// not already worn, as far as you know them.
    /// </summary>
    private string? AdviceNote(Item item) =>
        _game.Player.Inventory.Equipped.Contains(item) || _game.StoreHere is not null && !_game.Player.Inventory.Contains(item)
            ? null
            : _game.AdviceFor(item, _game.Knowledge.IsFullyKnown(item) || !item.IsWearable, buying: !_game.Player.Inventory.Contains(item)) is { } a
                ? a.Text.TrimEnd('.') + "."
                : null;

    /// <summary>
    /// The note for something you might put on: its properties as far as you know them (all its runes
    /// learned, or it says it's not fully known yet), and from the floor, whether its weight would slow you.
    /// </summary>
    private ItemAdvice? WieldAdvice(Item item) =>
        _game.Player.Inventory.Equipped.Contains(item) ? null
            : _game.AdviceFor(item, _game.Knowledge.IsFullyKnown(item), buying: !_game.Player.Inventory.Contains(item));

    private void RefreshInventory()
    {
        var p = _game.Player;
        var inv = p.Inventory;

        EquipmentRows.Clear();
        for (var i = 0; i < Inventory.Slots.Count; i++)
            if (inv.Equipment[i] is { } item)
                EquipmentRows.Add(Row(Inventory.Slots[i].Type == EquipSlot.Shield && item.Base.IsWeapon ? "off hand" : Inventory.Slots[i].Name, item));

        PackRows.Clear();
        BookRows.Clear();
        PouchRows.Clear();
        SatchelRows.Clear();
        for (var i = 0; i < inv.Pack.Count; i++)
        {
            var item = inv.Pack[i];
            var letter = ((char)('a' + i)).ToString();
            if (item.IsQuestItem) SatchelRows.Add(Row(letter, item));
            else if (Angband.Core.Items.Inventory.InPouch(item)) PouchRows.Add(GemRow(letter, item));
            else if (Angband.Core.Items.Inventory.InBookBag(item)) BookRows.Add(Row(letter, item));
            else PackRows.Add(Row(letter, item));
        }
        HasPouchItems = PouchRows.Count > 0;
        HasBookBagItems = BookRows.Count > 0;
        HasSatchelItems = SatchelRows.Count > 0;

        QuiverRows.Clear();
        for (var i = 0; i < inv.Quiver.Count; i++) QuiverRows.Add(Row(i.ToString(CultureInfo.InvariantCulture), inv.Quiver[i]));

        FloorRows.Clear();
        foreach (var item in _game.Level.Objects.At(p.Position).Where(i => !Hidden(i))) FloorRows.Add(Row("-", item));
        HasFloorItems = FloorRows.Count > 0;

        GoldText = $"Gold {p.Gold}";
        // The weight that counts (worn gear three quarters, with AVABand's rules) against what can be carried unhindered.
        var penalty = _game.BurdenPenalty;
        var slow = penalty > 0 ? $" · {_game.BurdenName} (-{penalty} speed)" : "";
        var free = inv.PackSize - inv.SlotsUsed;
        BurdenText = string.Format(CultureInfo.InvariantCulture, "Weight {0:0.0} / {1:0} lb{2}  |  Slots {3}/{4}{5}",
            _game.BurdenWeight / 10.0, p.WeightLimit / 20.0, slow, inv.SlotsUsed, inv.PackSize,
            free <= 0 ? " (full)" : free == 1 ? " (1 left)" : "");
        // In words, for a screen reader; and said once as the pack fills (not again until there's room).
        var room = free <= 0 ? "your pack is full" : free == 1 ? "one pack slot left" : $"{free} pack slots free";
        BurdenSpoken = string.Format(CultureInfo.InvariantCulture, "Carrying {0:0.0} of {1:0} pounds{2}; {3} of {4} pack slots used, {5}.",
            _game.BurdenWeight / 10.0, p.WeightLimit / 20.0, penalty > 0 ? $", {_game.BurdenName}, minus {penalty} speed" : "",
            inv.SlotsUsed, inv.PackSize, room);
        if (free <= 0 && !_packWasFull && ScreenReaderOn) Announce("Your pack is full.");
        _packWasFull = free <= 0;
    }

    /// <summary>A gem's row: what it does in a socket, under its name.</summary>
    private ItemRow GemRow(string letter, Item gem)
    {
        var row = Row(letter, gem); // (built afresh, not with 'with': the note's brush is made from its colour once)
        return new ItemRow(row.Letter, row.Glyph, row.GlyphColor, row.Name, row.Weight, gem, "in a socket: " + _game.GemEffectText(gem), 0xFF9FB8D0);
    }

    private ItemRow Row(string letter, Item item)
    {
        var flavor = _game.Knowledge.Flavor(item.Kind);
        var weight = item.IsGold ? "" : string.Format(CultureInfo.InvariantCulture, "{0:0.0} lb", item.TotalWeight / 10.0);
        return new ItemRow(letter, item.Base.Glyph.ToString(), _cells.Color(flavor?.Color ?? item.Kind.Color ?? item.Base.Color),
            _game.Describe(item), weight, item);
    }
}
