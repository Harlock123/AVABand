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
    bool Available, string Tip)
{
    public double Opacity => Available ? 1.0 : 0.45;
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
        for (var i = 0; i < GameSession.HotbarSize; i++) rows.Add(SlotRow(i));
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
