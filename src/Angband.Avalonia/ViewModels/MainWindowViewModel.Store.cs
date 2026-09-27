using System.Collections.ObjectModel;
using System.Globalization;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Angband.Avalonia.ViewModels;

/// <summary>A line in the store screen: something to buy, or something of yours to sell.</summary>
public sealed record StoreRow(string Letter, string Glyph, uint GlyphColor, string Name, string Price, string Weight, Item Item)
{
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

    public string StoreModeText => StoreSellMode
        ? (_game.StoreHere?.IsHome == true ? "Your belongings (letter to store, Tab for the home)" : "Sell (letter to sell, Tab to buy)")
        : (_game.StoreHere?.IsHome == true ? "In your home (letter to take, Tab to store)" : "For sale (letter to buy, Tab to sell)");

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
        if (StoreSellMode) Execute(new SellCommand(item, all ? item.Number : 1));
        else
        {
            var count = 1;
            if (all)
            {
                var price = _game.BuyPrice(store, item);
                var affordable = price == 0 ? item.Number : (int)Math.Min(int.MaxValue, _game.Player.Gold / price);
                count = Math.Max(1, Math.Min(store.IsAlways(item) ? item.Base.MaxStack : item.Number, affordable));
            }
            Execute(new BuyCommand(item, count));
        }
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
                : $"{store.Owner?.Name ?? "The shopkeeper"}  ·  You have {_game.Player.Gold} gold";

        var items = StoreSellMode
            ? _game.Player.Inventory.Pack.Concat(_game.Player.Inventory.Quiver).Concat(_game.Player.Inventory.Equipped)
                .Where(i => _game.StoreWillBuy(store, i)).ToList()
            : store.Stock;
        for (var i = 0; i < items.Count && i < 26; i++)
        {
            var item = items[i];
            var flavor = item.IsFlavored ? _game.Knowledge.Flavor(item.Kind) : null;
            var price = store.IsHome ? ""
                : StoreSellMode ? (_game.SellPrice(store, item) is var p and > 0 ? $"{p} gold" : "no gold")
                : $"{_game.BuyPrice(store, item)} gold";
            var name = StoreSellMode ? _game.Describe(item) : DescribeStock(store, item);
            StoreRows.Add(new StoreRow(((char)('a' + i)).ToString(), item.Base.Glyph.ToString(),
                _cells.Color(flavor?.Color ?? item.Base.Color), name, price,
                string.Format(CultureInfo.InvariantCulture, "{0:0.0} lb", item.Weight / 10.0), item));
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
