using Angband.Core.Definitions;
using Angband.Core.Items;

namespace Angband.Core.Game;

/// <summary>A store in town: its keeper and stock.</summary>
public sealed class Store(StoreDef def, ShopDef shop, StoreOwnerDef? owner)
{
    public StoreDef Def { get; } = def;
    public ShopDef Shop { get; } = shop;
    public StoreOwnerDef? Owner { get; internal set; } = owner;
    public List<Item> Stock { get; } = [];

    public string Id => Def.Id;
    public string Name => Shop.Name;
    public bool IsHome => Def.Home;

    /// <summary>Whether the store always stocks this kind (Angband store_is_staple).</summary>
    public bool IsStaple(ObjectKindDef kind) => Def.Staples.Contains(kind.Id);

    /// <summary>
    /// A plain staple the store never runs out of: buying it leaves the pile as it is (Angband's
    /// !store_sale_should_reduce_stock). Magical copies of a staple sell out like anything else.
    /// </summary>
    public bool IsAlways(Item item) =>
        IsStaple(item.Kind) && item.Artifact is null && item.Ego is null
        && !((item.Base.IsWeapon || item.Base.IsAmmo || item.Base.Slot == EquipSlot.Bow) && (item.ToHit != item.Kind.ToHit || item.ToDam != item.Kind.ToDam))
        && !(item.IsWearable && item.Base.Slot is not (EquipSlot.Weapon or EquipSlot.Bow or EquipSlot.Light or EquipSlot.Ring or EquipSlot.Amulet)
             && item.ToAc != item.Kind.ToAc);

    /// <summary>Whether the store stocks this kind at all (Angband store_can_carry).</summary>
    public bool CanCarry(ObjectKindDef kind) => Def.Stocked.Contains(kind.Id) || IsStaple(kind);
}

/// <summary>Stores (Angband 4.2 store.c): stock, maintenance, prices, buying and selling.</summary>
public sealed partial class GameSession
{
    private readonly Dictionary<string, Store> _stores = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, Store> Stores => _stores;

    /// <summary>
    /// Days that have passed in the dungeon since the stores were last maintained (Angband
    /// daycount): one every 10 × store_turns game turns spent below the town.
    /// </summary>
    public int StoreDays { get; internal set; }

    /// <summary>The store whose entrance the player is standing on, if any.</summary>
    public Store? StoreHere =>
        Level.FeatureAt(Player.Position).Shop is { } id ? _stores.GetValueOrDefault(id) : null;

    /// <summary>Game turns in a store "day" (Angband: 10 × store_turns).</summary>
    private long StoreDay => 10L * Math.Max(1, Data.Constants.StoreTurns);

    /// <summary>Angband store_reset: each store gets a keeper and ten rounds of maintenance.</summary>
    private void InitStores()
    {
        foreach (var def in Data.Stores)
        {
            if (Data.Shops.FirstOrDefault(s => s.Id == def.Id) is not { } shop) continue;
            var store = new Store(def, shop, null);
            _stores[def.Id] = store;
            ShuffleOwner(store);
            if (def.Home) continue;
            for (var i = 0; i < 10; i++) Maintain(store);
        }
    }

    /// <summary>Counts store days while in the dungeon (Angband process_world).</summary>
    private void CountStoreDay(long gameTurn)
    {
        if (Player.Depth > 0 && gameTurn % StoreDay == 0) StoreDays++;
    }

    /// <summary>
    /// Angband store_update, on arriving in town: each store is maintained once per day spent in the
    /// dungeon, and each day one keeper in <see cref="GameConstants.StoreShuffle"/> retires.
    /// </summary>
    internal void UpdateStores()
    {
        var shops = _stores.Values.Where(s => !s.IsHome).ToList();
        for (; StoreDays > 0; StoreDays--)
        {
            foreach (var store in shops) Maintain(store);
            if (shops.Count > 0 && Rng.OneIn(Data.Constants.StoreShuffle)) ShuffleOwner(Rng.Pick(shops));
        }
    }

    /// <summary>Angband store_shuffle: a different keeper takes over.</summary>
    private void ShuffleOwner(Store store)
    {
        var owners = store.Def.Owners;
        if (owners.Count == 0) return;
        if (owners.Count == 1) { store.Owner = owners[0]; return; }
        var old = store.Owner;
        while (store.Owner == old) store.Owner = Rng.Pick(owners);
    }

    /// <summary>
    /// Angband store_maint: some stock is sold off (the black market first clears anything no longer
    /// worth its while), missing staples are made and piled high, then new items come in until the
    /// store's range is met.
    /// </summary>
    internal void Maintain(Store store)
    {
        var def = store.Def;
        if (def.Home) return;

        if (def.BlackMarket)
            foreach (var item in store.Stock.Where(i => !BlackMarketOk(i)).ToList()) RemoveStock(store, item);

        if (def.Turnover > 0)
        {
            var stock = Math.Clamp(store.Stock.Count - Rng.RandInt1(def.Turnover), 0, def.MaxItems);
            while (store.Stock.Count > stock) DeleteRandom(store);
        }
        else if (def.Staples.Count > 0 && store.Stock.Count > 0)
        {
            // The bookseller occasionally sells a book or two.
            for (var sales = Rng.RandInt1(store.Stock.Count); sales > 0 && store.Stock.Count > 0; sales--) DeleteRandom(store);
        }

        StockStaples(store);

        if (def.Turnover > 0)
        {
            var stock = Math.Clamp(store.Stock.Count + Rng.RandInt1(def.Turnover),
                def.MinItems + def.Staples.Count, def.MaxItems + def.Staples.Count);
            for (var attempts = 0; store.Stock.Count < stock && attempts < 10_000; attempts++) CreateRandom(store);
        }
        SortStock(store);
    }

    /// <summary>Missing staples are made and every staple piled high (store_maint).</summary>
    private void StockStaples(Store store)
    {
        foreach (var id in store.Def.Staples)
        {
            if (Data.Object(id) is not { } kind) continue;
            var staple = store.Stock.FirstOrDefault(i => i.Kind == kind && store.IsAlways(i)) ?? CreateStaple(store, kind);
            if (staple is not null) staple.Number = staple.Base.MaxStack;
        }
    }

    /// <summary>
    /// Angband store_delete_random: someone else buys some or all of a pile. Ammunition goes whole
    /// or down to a multiple of five; other piles lose one, half, or everything.
    /// </summary>
    private void DeleteRandom(Store store)
    {
        var item = store.Stock[Rng.RandInt0(store.Stock.Count)];
        var num = item.Number;
        if (num > 1)
        {
            if (item.IsAmmo)
            {
                if (Rng.RandInt0(100) >= 50 && num >= 10) num = Rng.RandInt1(num / 5) * 5 + num % 5;
            }
            else
            {
                if (Rng.RandInt0(100) < 50) num = 1;
                else if (Rng.RandInt0(100) < 50) num = (num + 1) / 2;
                if (item.Kind.Charges is not null) item.Charges -= num * item.Charges / item.Number;
            }
        }
        if (num >= item.Number) RemoveStock(store, item);
        else item.Number -= num;
    }

    /// <summary>A shop gets rid of a pile; an artifact among them is lost for good (Angband store_delete).</summary>
    private void RemoveStock(Store store, Item item)
    {
        store.Stock.Remove(item);
        if (item.Artifact is { } art) LoseArtifact(art);
    }

    /// <summary>Angband store_create_item: a plain staple (no magic, no stack yet).</summary>
    private Item? CreateStaple(Store store, ObjectKindDef kind)
    {
        var item = Objects.Create(kind);
        ObjectFactory.ApplyKindRolls(Rng, item, 0);
        return StoreCarry(store, item);
    }

    /// <summary>
    /// Angband store_create_random: up to six tries at an item for the shelves. Ordinary stores pick
    /// from their list with a little magic (level 1 to store_magic_level, more once the player has
    /// been below dlvl 20); the black market takes anything from well below the player's deepest
    /// level, as long as it is worth its prices. Damaged, cursed or worthless items are refused.
    /// </summary>
    private bool CreateRandom(Store store)
    {
        var def = store.Def;
        int minLevel, maxLevel;
        if (def.BlackMarket)
            (minLevel, maxLevel) = (Player.MaxDepth + 5, Player.MaxDepth + 20);
        else
            (minLevel, maxLevel) = (1, Data.Constants.StoreMagicLevel + Math.Max(Player.MaxDepth - 20, 0));
        minLevel = Math.Min(minLevel, 55);
        maxLevel = Math.Min(maxLevel, 70);

        for (var tries = 0; tries < 6; tries++)
        {
            var level = Rng.RandRange(minLevel, maxLevel);
            var kind = def.BlackMarket ? Objects.PickKind(Rng, level)
                : def.Stocked.Count > 0 ? Data.Object(def.Stocked[Rng.RandInt0(def.Stocked.Count)]) : null;
            if (kind is null || kind.Base == "chest") continue;

            var item = Objects.Create(kind);
            Objects.ApplyMagic(Rng, item, level, artifacts: false);
            if ((item.Base.IsWeapon || item.Base.IsAmmo || item.Base.Slot == EquipSlot.Bow) && (item.ToHit < 0 || item.ToDam < 0)) continue;
            if (item.Base.IsWearable && item.Base.Slot is EquipSlot.Body or EquipSlot.Cloak or EquipSlot.Shield or EquipSlot.Head
                    or EquipSlot.Hands or EquipSlot.Feet && item.ToAc < 0) continue; // Angband tval_is_armor
            if (item.IsCursed) continue;
            if (def.BlackMarket && !BlackMarketOk(item)) continue;
            if (ItemValue.Of(item, Data) < 1) continue;

            item.OriginDepth = 0;
            item.Assessed = true;
            MassProduce(item);
            if (StoreCarry(store, item) is not null) return true;
        }
        return false;
    }

    /// <summary>
    /// Angband black_market_ok: egos and well-enchanted things are welcome; anything else must be
    /// worth at least 10 gold and not be sold in any other store.
    /// </summary>
    private bool BlackMarketOk(Item item)
    {
        if (item.Ego is not null) return true;
        if (item.ToAc > 2 || item.ToHit > 1 || item.ToDam > 2) return true;
        if (ItemValue.Of(item, Data) < 10) return false;
        return !_stores.Values.Any(s => !s.IsHome && !s.Def.BlackMarket && s.Stock.Any(i => i.Kind == item.Kind));
    }

    /// <summary>Angband mass_roll: the sum of <paramref name="times"/> rolls of 0..max-1.</summary>
    private int MassRoll(int times, int max)
    {
        var total = 0;
        for (var i = 0; i < times; i++) total += Rng.RandInt0(max);
        return total;
    }

    /// <summary>Angband mass_produce: cheap things come in piles.</summary>
    private void MassProduce(Item item)
    {
        var size = 1;
        var cost = ItemValue.Of(item, Data);
        switch (item.Base.Id)
        {
            case "food" or "mushroom" or "flask" or "light":
                if (cost <= 5) size += MassRoll(3, 5);
                if (cost <= 20) size += MassRoll(3, 5);
                break;
            case "potion" or "scroll":
                if (cost <= 60) size += MassRoll(3, 5);
                if (cost <= 240) size += MassRoll(1, 5);
                break;
            case "magic_book" or "prayer_book" or "nature_book" or "shadow_book":
                if (cost <= 50) size += MassRoll(2, 3);
                if (cost <= 500) size += MassRoll(1, 3);
                break;
            case "soft_armour" or "hard_armour" or "shield" or "gloves" or "boots" or "cloak" or "helm" or "crown"
                or "sword" or "polearm" or "hafted" or "digger" or "sling" or "bow" or "crossbow":
                if (item.Ego is not null) break;
                if (cost <= 10) size += MassRoll(3, 5);
                if (cost <= 100) size += MassRoll(3, 5);
                break;
            case "shot" or "arrow" or "bolt":
                size = cost <= 5 ? Rng.RandInt1(2) * 20
                    : cost <= 50 ? Rng.RandInt1(4) * 10
                    : cost <= 500 ? Rng.RandInt1(4) * 5
                    : 1;
                break;
        }
        item.Number = Math.Min(size, item.Base.MaxStack);
    }

    /// <summary>
    /// Angband store_carry: an item joins the stock, merging with a matching pile (up to a full
    /// stack) or taking a free slot. Worthless items vanish; inscriptions are rubbed off; lights are
    /// refuelled, rods recharged, and wands and staves the store deals in are recharged.
    /// Returns the pile it joined, or null if it was lost.
    /// </summary>
    private Item? StoreCarry(Store store, Item item)
    {
        if (ItemValue.Of(item, Data) <= 0) return null;
        item.Note = null;
        // Angband store_create_item: a light is sold as object_prep makes it (a torch full, a lamp half).
        if (item.Base.Slot == EquipSlot.Light && item.Kind.Fuel > 0)
            item.Fuel = Math.Max(item.Fuel, item.Kind.Has("REFUELABLE") ? Math.Min(item.Kind.Fuel, Data.Constants.DefaultLampFuel) : item.Kind.Fuel);
        else if (item.Base.Id == "rod") item.Timeout = 0;
        else if (item.Kind.Charges is { } charges && store.CanCarry(item.Kind))
        {
            var full = 0;
            for (var i = 0; i < item.Number; i++) full += Math.Max(0, RandomValue.Parse(charges).Roll(Rng, 0));
            if (full > item.Charges) item.Charges = full;
        }

        if (store.Stock.FirstOrDefault(s => s.CanStackWith(item)) is { } pile)
        {
            pile.Number = Math.Min(pile.Number + item.Number, pile.Base.MaxStack);
            if (item.Kind.Charges is not null) pile.Charges += item.Charges;
            return pile;
        }
        if (store.Stock.Count >= Data.Constants.StoreInvenMax) return null;
        store.Stock.Add(item);
        return item;
    }

    private void SortStock(Store store) =>
        store.Stock.Sort((a, b) =>
        {
            var c = string.CompareOrdinal(a.Base.Id, b.Base.Id);
            if (c != 0) return c;
            c = a.Kind.Level.CompareTo(b.Kind.Level);
            return c != 0 ? c : ItemValue.Of(a, Data).CompareTo(ItemValue.Of(b, Data));
        });

    /// <summary>
    /// Angband price_item with the store selling: the object's true value, doubled and then half as
    /// much again at the black market (three times the value in all).
    /// </summary>
    public long BuyPrice(Store store, Item item)
    {
        if (store.IsHome) return 0;
        if (Math.Max(ItemValue.Of(item, Data), ItemValue.Known(item, Data, Knowledge)) <= 0) return 1;
        var value = ItemValue.Of(item, Data);
        var adjust = store.Def.BlackMarket ? 150 : 100;
        if (store.Def.BlackMarket) value *= 2;
        return RacePrice(QuestPrice(store, Math.Max(1, (value * adjust + 50) / 100)), buying: true);
    }

    /// <summary>
    /// Angband price_item with the store buying: two thirds of the value — the lower of what it is
    /// worth and what you know it to be worth — a sixth at the black market, no more than the keeper's
    /// purse, and nothing at all under birth_no_selling.
    /// </summary>
    public long SellPrice(Store store, Item item)
    {
        if (store.IsHome || NoSelling) return 0;
        var price = Math.Min(ItemValue.Of(item, Data), ItemValue.Known(item, Data, Knowledge));
        if (price <= 0) return 0;
        var adjust = 100;
        price = price * 2 / 3;
        if (store.Def.BlackMarket)
        {
            price /= 2;
            adjust = 50;
        }
        price = RacePrice((price * adjust + 50) / 100, buying: false);
        return Math.Clamp(price, 1, store.Owner?.Purse ?? long.MaxValue);
    }

    /// <summary>Whether the store deals in this kind of item.</summary>
    public bool StoreWillBuy(Store store, Item item) =>
        !item.IsQuestItem && (store.IsHome || store.Def.BlackMarket || store.Def.Buys.Contains(item.Base.Id) || store.Def.AvabandBuys.Contains(item.Base.Id));

    private int EnterStore()
    {
        if (StoreHere is { } browsing)
            foreach (var item in browsing.Stock) Knowledge.See(item);
        if (StoreHere is not { } store)
        {
            if (Level.FeatureAt(Player.Position).Shop == "inn") EnterInn();
            else if (Level.FeatureAt(Player.Position).Shop == "artificer") EnterArtificer();
            else Publish(new MessageEvent("There is no store here."));
            return 0;
        }
        if (QuestAtShop(store.Id)) return 0;
        // A staple the store lacks (one added to the game since the save was made) is in by now.
        if (!store.IsHome && store.Def.Staples.Any(id => !store.Stock.Any(i => i.Kind.Id == id && store.IsAlways(i))))
        {
            StockStaples(store);
            SortStock(store);
        }
        Publish(new ShopEnteredEvent(store.Id, store.IsHome));
        GreetInShop(store);
        return 0;
    }

    private int LeaveStore()
    {
        if (StoreHere is { } store) Publish(new ShopLeftEvent(store.Id));
        return 0;
    }

    /// <summary>Buys (or, at home, takes back) items from the store. Takes no game time.</summary>
    private int Buy(Item stockItem, int count)
    {
        if (StoreHere is not { } store || !store.Stock.Contains(stockItem))
        {
            Publish(new MessageEvent("That is not for sale here."));
            return 0;
        }
        var always = store.IsAlways(stockItem);
        count = Math.Clamp(count, 1, stockItem.Number);
        var total = BuyPrice(store, stockItem) * count;
        if (total > Player.Gold)
        {
            Publish(new MessageEvent("You do not have enough gold for this item."));
            return 0;
        }

        var bought = always || count < stockItem.Number ? stockItem.Clone(Objects.NextSerial++, count) : stockItem;
        if (!Player.Inventory.CanCarry(bought))
        {
            Publish(new MessageEvent("You cannot carry that many items."));
            return 0;
        }

        var soldOut = false;
        if (!always)
        {
            if (bought == stockItem) store.Stock.Remove(stockItem);
            else stockItem.Number -= count;
            soldOut = !store.IsHome && store.Stock.Count == 0;
        }
        Player.Gold -= total;
        // Items from a store are fully known.
        Knowledge.LearnKind(bought.Kind);
        foreach (var rune in bought.Runes()) Knowledge.LearnRune(rune);
        var stack = Player.Inventory.Add(bought)!;

        Publish(new MessageEvent(store.IsHome
            ? $"You take {Describe(bought)}. You have {Describe(stack)}."
            : $"You bought {Describe(bought)} for {total} gold."));
        Publish(new ItemBoughtEvent(store.Id, bought.Kind.Id, total));
        if (soldOut)
        {
            // Angband do_cmd_buy: an emptied store restocks at once, sometimes under a new keeper.
            if (Rng.OneIn(Data.Constants.StoreShuffle))
            {
                Publish(new MessageEvent("The shopkeeper retires."));
                ShuffleOwner(store);
            }
            else Publish(new MessageEvent("The shopkeeper brings out some new stock."));
            for (var i = 0; i < 10; i++) Maintain(store);
        }
        RecalculateBonuses();
        return 0;
    }

    /// <summary>Sells (or, at home, stores) items. Takes no game time.</summary>
    private int Sell(Item item, int count)
    {
        if (StoreHere is not { } store || !Player.Inventory.Contains(item))
        {
            Publish(new MessageEvent("You have nothing like that to sell here."));
            return 0;
        }
        if (!StoreWillBuy(store, item))
        {
            Publish(new MessageEvent("I don't deal in that kind of item."));
            return 0;
        }
        if (store.IsHome && store.Stock.Count >= store.Def.Capacity && !store.Stock.Any(s => s.CanStackWith(item)))
        {
            Publish(new MessageEvent("Your home is full."));
            return 0;
        }
        // Angband store_will_buy: nothing apparently worthless — though under no-selling a store
        // takes wearables whose runes you haven't all learned, to identify them for you.
        if (!store.IsHome && ItemValue.Known(item, Data, Knowledge) <= 0
            && !(NoSelling && ItemValue.HasVariablePower(item) && !Knowledge.IsFullyKnown(item)))
        {
            Publish(new MessageEvent("I have no interest in that."));
            return 0;
        }

        count = Math.Clamp(count, 1, item.Number);
        var price = SellPrice(store, item) * count;
        var sold = Player.Inventory.Remove(item, count, () => Objects.NextSerial++);
        if (store.IsHome)
        {
            var merge = store.Stock.FirstOrDefault(s => s.CanStackWith(sold));
            if (merge is not null) merge.Absorb(sold);
            else store.Stock.Add(sold);
        }
        else
        {
            // Everything about it becomes known to the store (and the player: Angband learns the runes on a sale).
            Knowledge.LearnKind(sold.Kind);
            foreach (var rune in sold.Runes()) Knowledge.LearnRune(rune);
            if (StoreCarry(store, sold) is null && sold.Artifact is { } art) LoseArtifact(art); // the store threw it away
        }
        SortStock(store);
        Player.Gold += price;

        Publish(new MessageEvent(store.IsHome ? $"You drop off {Describe(sold)}."
            : price > 0 ? $"You sold {Describe(sold)} for {price} gold."
            : NoSelling ? $"You give {Describe(sold)} to the store (shops pay nothing: \"no selling\" is on)."
            : $"You give {Describe(sold)} to the store."));
        Publish(new ItemSoldEvent(store.Id, sold.Kind.Id, price));
        RecalculateBonuses();
        return 0;
    }

    /// <summary>
    /// Gold found in the dungeon. Under no-selling it is multiplied by the depth (up to five times)
    /// to make up for stores paying nothing, as Angband does.
    /// </summary>
    private Item MakeLevelGold(int level)
    {
        var gold = Objects.MakeGold(Rng, level);
        if (NoSelling && Level.Depth > 0) gold.GoldValue *= Math.Min(5, Level.Depth);
        return gold;
    }

    /// <summary>Angband comment_welcome: how a shopkeeper greets you, warmer the higher your level.</summary>
    private static readonly string[] ShopWelcomes =
    [
        "", "{0} nods to you.", "{0} says hello.", "{0}: \"See anything you like, adventurer?\"",
        "{0}: \"How may I help you, {1}?\"", "{0}: \"Welcome back, {1}.\"", "{0}: \"A pleasure to see you again, {1}.\"",
        "{0}: \"How may I be of assistance, good {1}?\"", "{0}: \"You do honour to my humble store, noble {1}.\"",
        "{0}: \"I and my family are entirely at your service, {1}.\"",
    ];

    /// <summary>
    /// Angband prt_welcome: half the time the keeper says something as you come in — one time in
    /// three a hint (hints.txt), otherwise, once you're past level 5, a welcome by your level, calling
    /// you by your title, your name or "valued customer".
    /// </summary>
    private void GreetInShop(Store store)
    {
        if (IsTutorial) TutorialDone.Add("shop"); // (the tutorial's Armoury lesson)
        if (store.IsHome || store.Owner is not { } owner || Rng.OneIn(2)) return;
        var shortName = owner.Name.Split(' ')[0];
        if (Rng.OneIn(3))
        {
            // Angband random_hint: each hint in turn, kept one time in n.
            string? hint = null;
            for (var n = 1; n <= Data.Hints.Count; n++)
                if (Rng.OneIn(n)) hint = Data.Hints[n - 1];
            if (hint is not null) Publish(new MessageEvent($"\"{hint}\""));
        }
        else if (Player.Level > 5)
        {
            var i = Math.Min((Player.Level - 1) / 5, ShopWelcomes.Length - 1);
            var titles = Player.Class?.Titles ?? [];
            string name;
            if (i % 2 == 1 && Rng.RandInt0(2) != 0 && titles.Count > 0) name = titles[Math.Min(titles.Count - 1, (Player.Level - 1) / 5)];
            else if (Rng.RandInt0(2) != 0) name = Player.Name;
            else name = "valued customer";
            Publish(new MessageEvent(string.Format(System.Globalization.CultureInfo.InvariantCulture, ShopWelcomes[i], shortName, name)));
        }
    }
}
