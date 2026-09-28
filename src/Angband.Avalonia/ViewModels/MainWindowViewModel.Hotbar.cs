using System.Collections.ObjectModel;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Records;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>One hotbar slot as shown: its key, what it holds and whether it can be used now.</summary>
public sealed record HotbarSlotRow(int Index, string Key, string Glyph, IBrush GlyphBrush, string Label, string Detail,
    bool Available, string Tip, bool Selected = false)
{
    public double Opacity => Available ? 1.0 : 0.45;
    public IBrush Outline => Selected ? SelectedOutline : PlainOutline;

    private static readonly IBrush SelectedOutline = new ImmutableSolidColorBrush(Color.FromRgb(0x8f, 0xc1, 0xff));
    private static readonly IBrush PlainOutline = new ImmutableSolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
}

// The hotbar (AVABand's own): ten slots under the map for spells and items, used with Alt+1..Alt+0
// or a click. An empty slot is filled with a click; right-click (or Alt+Shift+digit) offers to put
// a spell or an item there, or to clear it. Items go by kind, so a slot of Cure Light Wounds
// potions keeps working as you find more.
public sealed partial class MainWindowViewModel
{
    public ObservableCollection<HotbarSlotRow> HotbarSlots { get; } = [];

    [ObservableProperty] private bool _showHotbar;

    /// <summary>The slot being filled from an item or spell prompt.</summary>
    private int? _assignSlot;

    /// <summary>The slot a gamepad's "use" works on; highlighted once the gamepad has moved it.</summary>
    public int HotbarCursor { get; private set; }
    private bool _hotbarCursorShown;

    private void MoveHotbarCursor(int delta)
    {
        HotbarCursor = ((HotbarCursor + delta) % GameSession.HotbarSize + GameSession.HotbarSize) % GameSession.HotbarSize;
        _hotbarCursorShown = true;
        var slot = HotbarSlots.ElementAtOrDefault(HotbarCursor);
        LastMessage = slot is null || slot.Label.Length == 0 ? $"Hotbar slot {HotbarKey(HotbarCursor)}: empty." : $"Hotbar slot {HotbarKey(HotbarCursor)}: {slot.Label} ({slot.Detail}).";
        RefreshHotbar();
    }

    /// <summary>The key a slot answers to: Alt+1 .. Alt+9, then Alt+0.</summary>
    public static string HotbarKey(int slot) => ((slot + 1) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);

    [RelayCommand]
    private void ToggleHotbar() => ShowHotbar = !ShowHotbar;

    partial void OnShowHotbarChanged(bool value)
    {
        _settings.ShowHotbar = value;
        _saveSettings?.Invoke(_settings);
        RefreshHotbar();
    }

    /// <summary>Keeps the slots current (called with every refresh of the screen).</summary>
    private void RefreshHotbar()
    {
        var rows = new List<HotbarSlotRow>();
        for (var i = 0; i < GameSession.HotbarSize; i++) rows.Add(SlotRow(i) with { Selected = _hotbarCursorShown && i == HotbarCursor });
        if (HotbarSlots.SequenceEqual(rows)) return;
        HotbarSlots.Clear();
        foreach (var r in rows) HotbarSlots.Add(r);
    }

    private HotbarSlotRow SlotRow(int i)
    {
        var key = HotbarKey(i);
        var entry = _game.Hotbar[i];
        IBrush Tint(string colour) => new ImmutableSolidColorBrush(Color.FromUInt32(_cells.Color(colour)));
        if (entry is null) return new HotbarSlotRow(i, key, "", Tint("White"), "", "empty", false, $"Alt+{key}: click to fill this slot");
        if (entry.SpellId is { } id)
        {
            var def = _data.Spell(id);
            var spell = _game.HotbarSpell(entry);
            var info = spell is null ? null : _game.SpellInfo(spell);
            var name = def?.Name ?? id;
            return new HotbarSlotRow(i, key, "~", Tint("LightBlue"), name,
                info is null ? "not now" : $"{info.Mana} mana", spell is not null,
                $"Alt+{key}: cast {name}" + (info is null ? "" : $" ({info.Mana} mana, {_game.SpellFailChance(spell!)}% fail)"));
        }
        var kind = _data.Object(entry.KindId!);
        var count = _game.HotbarCount(entry);
        var item = _game.HotbarItem(entry);
        var label = kind is null ? entry.KindId! : ObjectInfo.KindName(_game, kind);
        var flavor = kind is null ? null : _game.Knowledge.Flavor(kind);
        return new HotbarSlotRow(i, key, (kind is null ? '?' : _data.ObjectBase(kind.Base)?.Glyph ?? '?').ToString(),
            Tint(flavor?.Color ?? (kind is null ? "White" : _data.ObjectBase(kind.Base)?.Color ?? "White")), label,
            count == 0 ? "none" : item is not null && _game.Player.Inventory.Equipped.Contains(item) ? "worn" : $"x{count}",
            item is not null, $"Alt+{key}: use {label}");
    }

    /// <summary>Alt+digit or a click: uses what the slot holds (an empty slot asks what to put there).</summary>
    public void UseHotbar(int slot)
    {
        if (IsPrompting || IsConfirming || IsInStore || _game.Player.IsDead) return;
        if (slot is < 0 or >= GameSession.HotbarSize) return;
        if (_game.Hotbar[slot] is not { } entry)
        {
            OpenHotbarMenu(slot);
            return;
        }
        if (entry.SpellId is not null)
        {
            if (_game.HotbarSpell(entry) is not { } spell)
            {
                AddMessage($"You can't {_game.PlayerRealm?.Verb ?? "cast"} {_data.Spell(entry.SpellId)?.Name ?? "that"} now (you need to know it and carry its book).");
                return;
            }
            _spellPromptKind = SpellPromptKind.Cast;
            _spellOffered = null;
            ChooseSpell(new SpellRow("", spell.Name, 0, 0, 0, "", "", spell));
            return;
        }
        if (_game.HotbarItem(entry) is not { } item)
        {
            AddMessage($"You have no {(_data.Object(entry.KindId!) is { } k ? ObjectInfo.KindName(_game, k) : "such item")} left.");
            return;
        }
        if (UseKind(item) is null)
        {
            AddMessage($"You can't use {_game.Describe(item)} like that.");
            return;
        }
        UseItemAsked(ItemPromptKind.UseAny, item);
    }

    /// <summary>What a drag carries to the hotbar: "item:serial", "spell:id" or "slot:n".</summary>
    public static string DragPayload(Item item) => $"item:{item.Serial}";
    public static string DragPayload(Angband.Core.Definitions.SpellDef spell) => $"spell:{spell.Id}";
    public static string DragPayload(int slot) => $"slot:{slot}";

    /// <summary>
    /// Something dropped on a hotbar slot: an item (by its kind, if it is one that can be used), a
    /// spell you know, or another slot (the two swap). True if the slot took it.
    /// </summary>
    public bool DropOnHotbar(int slot, string payload)
    {
        if (slot is < 0 or >= GameSession.HotbarSize || IsConfirming || _game.Player.IsDead) return false;
        if (IsPrompting) CancelPrompt(); // dragged out of an item or spell list
        var (kind, value) = payload.IndexOf(':') is var i and > 0 ? (payload[..i], payload[(i + 1)..]) : ("", "");
        switch (kind)
        {
            case "slot" when int.TryParse(value, out var from) && from is >= 0 and < GameSession.HotbarSize && from != slot:
                var moved = _game.Hotbar[from];
                _game.SetHotbar(from, _game.Hotbar[slot]);
                _game.SetHotbar(slot, moved);
                LastMessage = $"Hotbar slots {HotbarKey(from)} and {HotbarKey(slot)} swapped.";
                break;
            case "item" when long.TryParse(value, out var serial)
                             && _game.Player.Inventory.All.Concat(_game.Level.Objects.At(_game.Player.Position))
                                 .FirstOrDefault(it => it.Serial == serial) is { } item:
                if (UseKind(item) is null)
                {
                    AddMessage($"{Capitalize(_game.Describe(item))} can't be used from the hotbar.");
                    return false;
                }
                _game.SetHotbar(slot, HotbarEntry.ForKind(item.Kind.Id));
                LastMessage = $"Alt+{HotbarKey(slot)} now uses {ObjectInfo.KindName(_game, item.Kind)}.";
                break;
            case "spell" when _data.Spell(value) is { } spell && _game.Player.LearnedSpells.Contains(spell.Id):
                _game.SetHotbar(slot, HotbarEntry.ForSpell(spell.Id));
                LastMessage = $"Alt+{HotbarKey(slot)} now casts {spell.Name}.";
                break;
            default:
                return false;
        }
        Refresh();
        return true;
    }

    /// <summary>Right-click or Alt+Shift+digit: put a spell or an item in the slot, or clear it.</summary>
    public void OpenHotbarMenu(int slot)
    {
        if (IsPrompting || IsConfirming || IsInStore || slot is < 0 or >= GameSession.HotbarSize) return;
        var menu = new List<(string, Action)>();
        if (_game.PlayerRealm is { } realm) menu.Add(($"Put a {realm.SpellNoun} here", () => AssignFrom(slot, spells: true)));
        menu.Add(("Put an item here", () => AssignFrom(slot, spells: false)));
        if (_game.Hotbar[slot] is not null) menu.Add(("Clear this slot", () =>
        {
            _game.SetHotbar(slot, null);
            Refresh();
        }));
        ShowMenu($"Hotbar slot {HotbarKey(slot)}:", menu);
    }

    private void AssignFrom(int slot, bool spells)
    {
        _assignSlot = slot;
        if (spells) BeginSpellPrompt(SpellPromptKind.Cast);
        else BeginItemPrompt(ItemPromptKind.UseAny);
        if (!IsPrompting) _assignSlot = null; // nothing to offer
        else PromptTitle = spells ? "Put which spell in the hotbar?" : "Put which item in the hotbar?";
    }

    /// <summary>Fills the slot being assigned, if one is (from the item or spell prompt).</summary>
    private bool AssignChosen(Item? item, Angband.Core.Definitions.SpellDef? spell)
    {
        if (_assignSlot is not { } slot) return false;
        _assignSlot = null;
        _game.SetHotbar(slot, spell is not null ? HotbarEntry.ForSpell(spell.Id) : HotbarEntry.ForKind(item!.Kind.Id));
        LastMessage = $"Alt+{HotbarKey(slot)} now {(spell is not null ? $"casts {spell.Name}" : $"uses {ObjectInfo.KindName(_game, item!.Kind)}")}.";
        Refresh();
        return true;
    }
}
