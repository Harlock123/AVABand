using System.Collections.ObjectModel;
using System.Globalization;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Angband.Avalonia.ViewModels;

/// <summary>A line in the store screen: something to buy, or something of yours to sell.</summary>
public sealed record StoreRow(string Letter, string Glyph, uint GlyphColor, string Name, string Price, string Weight, Item Item,
    string Equipped = "", string BookNote = "", string Advice = "", uint AdviceColor = 0xFFD8C07A)
{
    /// <summary>The shop note: how it compares with what you'd replace (or whether missiles fit your launcher).</summary>
    public bool HasAdvice => Advice.Length > 0;
    public IBrush AdviceBrush { get; } = new ImmutableSolidColorBrush(Color.FromUInt32(AdviceColor));

    /// <summary>Whether the row is something the player has on (shown, and asked about before it goes).</summary>
    public bool IsEquipped => Equipped.Length > 0;

    /// <summary>A book's note: whether it's one the player's class can read.</summary>
    public bool HasBookNote => BookNote.Length > 0;
    public bool IsBookForYou => BookNote == "for you";
    public bool IsBookNotForYou => HasBookNote && !IsBookForYou;

    public IBrush GlyphBrush { get; } = new ImmutableSolidColorBrush(Color.FromUInt32(GlyphColor));
}

/// <summary>The store screen: stock and prices, the player's goods, buying and selling.</summary>
public sealed partial class MainWindowViewModel
{
    public ObservableCollection<StoreRow> StoreRows { get; } = [];

    [ObservableProperty] private bool _isInStore;
    [ObservableProperty] private bool _storeSellMode;
    [ObservableProperty] private string _storeTitle = "";
    [ObservableProperty] private string _storeSubtitle = "";
    [ObservableProperty] private int _storeSelectedIndex;

    /// <summary>
    /// The pane heading. Under birth_no_selling (on by default, as in Angband 4.2) the shops pay
    /// nothing, so — like 4.2's "Give which item?" — selling is called giving, and says why.
    /// </summary>
    public string StoreModeText => _game.StoreHere?.IsHome == true
        ? (StoreSellMode ? "Your belongings (letter to store, Tab for the home)" : "In your home (letter to take, Tab to store)")
        : _game.NoSelling
            ? (StoreSellMode
                ? "Give (letter to give; the shop identifies it, and recharges some wands and staves; Tab to buy)"
                : "For sale (letter to buy, Tab to give items)")
            : (StoreSellMode ? "Sell (letter to sell, Tab to buy)" : "For sale (letter to buy, Tab to sell)");

    private void OnShopEntered(ShopEnteredEvent e)
    {
        IsInStore = true;
        StoreSellMode = false;
        StoreSelectedIndex = 0;
        RefreshStore();
    }

    partial void OnStoreSellModeChanged(bool value)
    {
        StoreSelectedIndex = 0;
        RefreshStore();
    }

    public void LeaveStore()
    {
        if (!IsInStore) return;
        IsInStore = false;
        Execute(new LeaveStoreCommand());
        LastMessage = "You leave the store.";
    }

    /// <summary>Buys or sells the item on a row: one of it, or the whole stack.</summary>
    public void StoreTransact(char letter, bool all)
    {
        var row = StoreRows.FirstOrDefault(r => r.Letter[0] == char.ToLowerInvariant(letter));
        if (row is null) return;
        Transact(row, all);
    }

    private void Transact(StoreRow row, bool all)
    {
        if (_game.StoreHere is not { } store) return;
        var item = row.Item;
        if (StoreSellMode && row.IsEquipped)
        {
            // What you have on never goes at a keypress: ask first.
            var verb = store.IsHome ? "Put" : _game.NoSelling ? "Give away" : "Sell";
            AskFirst($"{verb} {_game.Describe(item)}, which you are {(item.Base.Slot is EquipSlot.Weapon or EquipSlot.Bow ? "wielding" : "wearing")}?", () =>
            {
                Execute(new SellCommand(item, item.Number));
                RefreshStore();
            });
            return;
        }
        // The most that can change hands: the whole stack sold; bought, as many as are there and you can pay for.
        int most;
        if (StoreSellMode) most = item.Number;
        else
        {
            var price = _game.BuyPrice(store, item);
            var affordable = price == 0 ? item.Number : (int)Math.Min(int.MaxValue, _game.Player.Gold / price);
            most = Math.Max(1, Math.Min(store.IsAlways(item) ? item.Base.MaxStack : item.Number, affordable));
        }
        if (all || most <= 1)
        {
            Trade(item, all ? most : 1);
            return;
        }
        // Angband store_purchase / store_sell: "Quantity (1-N):", one unless you say otherwise.
        BeginNumberPrompt($"Quantity (1-{most})?", 1, n =>
        {
            if (n <= 0)
            {
                LastMessage = "Cancelled.";
                return;
            }
            Trade(item, Math.Min(n, most));
        }, most);
    }

    private void Trade(Angband.Core.Items.Item item, int count)
    {
        if (StoreSellMode) Execute(new SellCommand(item, count));
        else Execute(new BuyCommand(item, count));
        RefreshStore();
    }

    /// <summary>Store-screen meaning of input actions (selection, buy/sell, switch, examine, leave).</summary>
    private bool HandleStoreAction(InputAction action)
    {
        if (!IsInStore) return false;
        switch (action)
        {
            case InputAction.MoveNorth:
                if (StoreRows.Count > 0) StoreSelectedIndex = (StoreSelectedIndex - 1 + StoreRows.Count) % StoreRows.Count;
                break;
            case InputAction.MoveSouth:
                if (StoreRows.Count > 0) StoreSelectedIndex = (StoreSelectedIndex + 1) % StoreRows.Count;
                break;
            case InputAction.Confirm:
                if (StoreRows.ElementAtOrDefault(StoreSelectedIndex) is { } row) Transact(row, all: false);
                break;
            case InputAction.SwitchPane or InputAction.Fire:
                StoreSellMode = !StoreSellMode;
                break;
            case InputAction.Inspect:
                if (StoreRows.ElementAtOrDefault(StoreSelectedIndex) is { } seen) AddMessage(Inspect(seen.Item));
                break;
            case InputAction.Cancel:
                LeaveStore();
                break;
        }
        return true;
    }

    private void RefreshStore()
    {
        StoreRows.Clear();
        if (!IsInStore || _game.StoreHere is not { } store)
        {
            IsInStore = false;
            return;
        }

        StoreTitle = store.Name;
        StoreSubtitle = store.IsHome ? $"Your home ({store.Stock.Count}/{store.Def.Capacity} slots)"
            : store.Owner is { } owner && !_game.NoSelling
                ? $"{owner.Name}, who pays up to {owner.Purse} gold  ·  You have {_game.Player.Gold} gold" // Angband: "Store (max_cost)"
                : $"{store.Owner?.Name ?? "The shopkeeper"}  ·  You have {_game.Player.Gold} gold"
                  + (_game.NoSelling ? "  ·  Shops pay nothing (birth option \"no selling\"): gold in the dungeon is increased instead" : "");

        var items = StoreSellMode
            ? _game.Player.Inventory.Pack.Concat(_game.Player.Inventory.Quiver).Concat(_game.Player.Inventory.Equipped)
                .Where(i => _game.StoreWillBuy(store, i)).ToList()
            : store.Stock;
        bool equippedNow(Item it) => _game.Player.Inventory.Equipped.Contains(it);
        for (var i = 0; i < items.Count && i < 26; i++)
        {
            var item = items[i];
            var flavor = _game.Knowledge.Flavor(item.Kind);
            var price = store.IsHome ? ""
                : StoreSellMode ? (_game.SellPrice(store, item) is var p and > 0 ? $"{p} gold" : _game.NoSelling ? "" : "no gold")
                : $"{_game.BuyPrice(store, item)} gold";
            var name = StoreSellMode ? _game.Describe(item) : DescribeStock(store, item);
            // "Will this suit me?": stock is known in full; your own things only once all their runes are.
            var advice = equippedNow(item) ? null
                : StoreSellMode ? _game.AdviceFor(item, _game.Knowledge.IsFullyKnown(item), buying: false) : _game.AdviceFor(item);
            var equipped = StoreSellMode && _game.Player.Inventory.Equipped.Contains(item)
                ? item.Base.Slot is EquipSlot.Weapon or EquipSlot.Bow ? "wielded" : "worn"
                : "";
            StoreRows.Add(new StoreRow(((char)('a' + i)).ToString(), item.Base.Glyph.ToString(),
                _cells.Color(flavor?.Color ?? item.Kind.Color ?? item.Base.Color), name, price,
                string.Format(CultureInfo.InvariantCulture, "{0:0.0} lb", item.Weight / 10.0), item, equipped, _game.BookNote(item) ?? "",
                advice?.Text ?? "",
                // (Through the map's palette, so the colour-blind option applies.)
                _cells.Color(advice?.Tone switch { > 0 => "LightGreen", < 0 => "LightRed", _ => "Yellow" })));
        }
        StoreSelectedIndex = Math.Clamp(StoreSelectedIndex, 0, Math.Max(0, StoreRows.Count - 1));
        OnPropertyChanged(nameof(StoreModeText));
    }

    /// <summary>Stock is shown fully identified, as Angband stores do.</summary>
    private string DescribeStock(Store store, Item item)
    {
        var known = new PlayerKnowledge(_data, 0);
        known.LearnKind(item.Kind);
        foreach (var rune in item.Runes()) known.LearnRune(rune);
        var text = ItemNaming.Describe(item, known);
        return store.IsAlways(item) ? System.Text.RegularExpressions.Regex.Replace(text, @"^\d+ ", "") + " (plenty)" : text;
    }
}
