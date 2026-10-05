using CommunityToolkit.Mvvm.Input;
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

    /// <summary>The row in words (its accessible name, and what a screen reader hears as it's chosen): the note last.</summary>
    public string Spoken => string.Join(", ", new[] { Letter, Name, Price, Weight, Equipped, BookNote }.Where(s => s.Length > 0))
                            + (HasAdvice ? ". " + Advice : "");
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
    /// <summary>The buy-back list (AVABand's own): what you've sold on this visit, to buy back.</summary>
    [ObservableProperty] private bool _storeBuybackMode;

    /// <summary>Whether there's anything to buy back here (Tab then shows the list after selling).</summary>
    public bool CanBuyBack => IsInStore && _game.Buyback.Count > 0;
    [ObservableProperty] private string _storeTitle = "";
    [ObservableProperty] private string _storeSubtitle = "";
    [ObservableProperty] private int _storeSelectedIndex;

    /// <summary>
    /// The pane heading. Under birth_no_selling (on by default, as in Angband 4.2) the shops pay
    /// nothing, so — like 4.2's "Give which item?" — selling is called giving, and says why.
    /// </summary>
    public string StoreModeText => StoreBuybackMode
        ? "Buy back what you sold here (letter to buy it back, at the price you were paid; Ctrl+Z the last; Tab to buy)"
        : PaneText + (StoreSellMode && CanBuyBack ? $" · {_game.Buyback.Count} to buy back: Tab, or Ctrl+Z for the last" : "");

    private string PaneText => _game.StoreHere?.IsHome == true
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
        StoreBuybackMode = false;
        StoreSelectedIndex = 0;
        RefreshStore();
        if (ScreenReaderOn)
            Announce($"{StoreTitle}. {StoreSubtitle}. {StoreModeText}. {StoreRows.Count} {(StoreRows.Count == 1 ? "thing" : "things")}."
                     + (HasStoreService ? $" Service on offer: {StoreServiceLabel}, press !." : "")
                     + (StoreRows.FirstOrDefault() is { } first ? " " + first.Spoken : ""));
    }

    partial void OnStoreBuybackModeChanged(bool value)
    {
        StoreSelectedIndex = 0;
        RefreshStore();
    }

    /// <summary>Ctrl+Z in a shop: buys back the last thing sold here, if there is one.</summary>
    public void UndoLastSale()
    {
        if (!IsInStore) return;
        if (_game.Buyback.Count == 0)
        {
            LastMessage = "You haven't sold anything here to buy back.";
            return;
        }
        Execute(new BuybackCommand(_game.Buyback.Count - 1));
        RefreshStore();
    }

    partial void OnStoreSellModeChanged(bool value)
    {
        StoreSelectedIndex = 0;
        RefreshStore();
        if (IsInStore && ScreenReaderOn)
            Announce($"{StoreModeText}. {StoreRows.Count} {(StoreRows.Count == 1 ? "thing" : "things")}."
                     + (StoreRows.FirstOrDefault() is { } first ? " " + first.Spoken : ""));
    }

    /// <summary>A screen reader hears the row the arrows move to (the list itself never takes focus).</summary>
    private void AnnounceStoreRow()
    {
        if (ScreenReaderOn && StoreRows.ElementAtOrDefault(StoreSelectedIndex) is { } row) Announce(row.Spoken);
    }

    /// <summary>The shop's service on offer now (the Armoury's gem removal, the Alchemist's identifying), for the button.</summary>
    public string? StoreServiceLabel => IsInStore ? _game.StoreServiceLabel : null;
    public bool HasStoreService => StoreServiceLabel is not null;

    /// <summary>Services (or !) in the shop screen: the shop's service, asked for from inside.</summary>
    [RelayCommand]
    public void OpenStoreServices()
    {
        if (!HasStoreService) return;
        IsInStore = false; // (the question takes the screen; its "Back to the shop" brings the shop back)
        Execute(new StoreServicesCommand());
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
        if (StoreBuybackMode)
        {
            // A thing sold here goes back whole, as it was sold.
            Execute(new BuybackCommand(StoreRows.IndexOf(row)));
            RefreshStore();
            return;
        }
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
        // Angband store_purchase / store_sell: "Quantity (1-N):", one unless you say otherwise
        // (and AVABand's own: A for all of them).
        BeginNumberPrompt($"Quantity (1-{most}, a for all)?", 1, n =>
        {
            if (n <= 0)
            {
                LastMessage = "Cancelled.";
                return;
            }
            Trade(item, Math.Min(n, most));
        }, most, allowAll: true);
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
                AnnounceStoreRow();
                break;
            case InputAction.MoveSouth:
                if (StoreRows.Count > 0) StoreSelectedIndex = (StoreSelectedIndex + 1) % StoreRows.Count;
                AnnounceStoreRow();
                break;
            case InputAction.Confirm:
                if (StoreRows.ElementAtOrDefault(StoreSelectedIndex) is { } row) Transact(row, all: false);
                break;
            case InputAction.SwitchPane or InputAction.Fire:
                // Buy → sell → (buy back, if there's anything to) → buy.
                if (StoreBuybackMode) StoreBuybackMode = false;
                else if (StoreSellMode && CanBuyBack)
                {
                    StoreSellMode = false;
                    StoreBuybackMode = true;
                }
                else StoreSellMode = !StoreSellMode;
                break;
            case InputAction.Inspect:
                // (something you could wear: laid beside what you wear, line by line; anything else, described)
                if (StoreRows.ElementAtOrDefault(StoreSelectedIndex) is { } seen)
                {
                    if (_game.Compare(seen.Item) is not null) ShowComparison(seen.Item, stock: !StoreSellMode && !StoreBuybackMode);
                    else AddMessage(Inspect(seen.Item));
                }
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

        if (StoreBuybackMode && _game.Buyback.Count == 0)
        {
            // All bought back: back to selling.
            StoreBuybackMode = false;
            StoreSellMode = true;
            return;
        }
        if (StoreBuybackMode)
        {
            var back = _game.Buyback;
            for (var i = 0; i < back.Count && i < 26; i++)
            {
                var entry = back[i];
                var item = entry.Item;
                var flavor = _game.Knowledge.Flavor(item.Kind);
                StoreRows.Add(new StoreRow(((char)('a' + i)).ToString(), item.Base.Glyph.ToString(),
                    _cells.Color(flavor?.Color ?? item.Kind.Color ?? item.Base.Color), _game.Describe(item),
                    entry.Price > 0 ? $"{entry.Price} gold" : "free",
                    string.Format(CultureInfo.InvariantCulture, "{0:0.0} lb", item.Weight / 10.0), item));
            }
            StoreSelectedIndex = Math.Clamp(StoreSelectedIndex, 0, Math.Max(0, StoreRows.Count - 1));
            OnPropertyChanged(nameof(StoreModeText));
            OnPropertyChanged(nameof(CanBuyBack));
            return;
        }
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
        OnPropertyChanged(nameof(CanBuyBack));
        OnPropertyChanged(nameof(StoreServiceLabel));
        OnPropertyChanged(nameof(HasStoreService));
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
