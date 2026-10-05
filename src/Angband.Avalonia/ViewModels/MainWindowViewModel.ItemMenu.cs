using Angband.Core.Game;
using Angband.Core.Items;

namespace Angband.Avalonia.ViewModels;

// The item menu (Angband ui-context.c context_menu_object): right-click an item in the sidebar, or
// pick one with 'i' (the pack) or 'e' (what you wear) as Angband's own inventory commands do.
// Also AVABand's "Clear out junk": a checklist of carried things to ignore in one go.
public sealed partial class MainWindowViewModel
{
    /// <summary>Right-click on an item row, or an item chosen with 'i' / 'e': what can be done with it.</summary>
    public void OpenItemMenu(Item item)
    {
        if (IsPrompting || IsConfirming || IsInStore || IsLooking) return;
        ShowMenu(Capitalize(_game.Describe(item)) + ":", ItemMenu(item));
    }

    /// <summary>Angband context_menu_object, entry by entry.</summary>
    private List<(string, Action)> ItemMenu(Item item)
    {
        var inv = _game.Player.Inventory;
        var equipped = inv.Equipped.Contains(item);
        var carried = equipped || inv.Pack.Contains(item) || inv.Quiver.Contains(item);
        var menu = new List<(string, Action)> { ("Inspect", () => UseItemAsked(ItemPromptKind.Inspect, item)) };

        var inBook = _game.ClassSpells.Where(s => s.Book == item.Kind.Id).ToList();
        if (inBook.Count > 0 && inv.Pack.Contains(item))
        {
            if (CanCastSpells && inBook.Any(s => _game.Player.LearnedSpells.Contains(s.Id)))
                menu.Add(("Cast", () => SpellsFromBook(SpellPromptKind.Cast, inBook)));
            if (_game.StudyableBooks().Contains(item.Kind.Id))
                menu.Add(("Study", () => SpellsFromBook(SpellPromptKind.Study, inBook)));
            menu.Add(("Browse", () => SpellsFromBook(SpellPromptKind.Browse, inBook)));
        }
        else if (UseKind(item) is { } use && (use == ItemPromptKind.Activate || item.Base.Id is not ("wand" or "staff" or "rod") || inv.Pack.Contains(item)))
        {
            var label = use switch
            {
                ItemPromptKind.Aim => "Aim", ItemPromptKind.Zap => "Zap", ItemPromptKind.UseStaff => "Use",
                ItemPromptKind.Read => "Read", ItemPromptKind.Quaff => "Quaff", ItemPromptKind.Eat => "Eat",
                ItemPromptKind.QuestUse => "Use", ItemPromptKind.Apply => "Apply", _ => "Activate",
            };
            menu.Add((label, () => UseItemAsked(ItemPromptKind.UseAny, item)));
        }
        else if (inv.Quiver.Contains(item) && inv.Bow is { } bow && item.Base.AmmoClass == bow.Base.AmmoClass)
            menu.Add(("Fire", () => FireChosen(item)));

        if (_game.CanRefillFrom(item))
            menu.Add(("Refill", () => UseItemAsked(ItemPromptKind.Refuel, item)));
        // AVABand's socketed bracers: a gem goes into bracers with room, from either's menu.
        if (GameSession.IsGem(item) && inv.Pack.Contains(item))
            foreach (var host in _game.FreeSockets())
                menu.Add(($"Set into your {ItemNaming.Describe(host, _game.Knowledge, withArticle: false, full: false)} ({_game.GemEffectText(item)})", () => Execute(new SetGemCommand(host, item))));
        if (item.Sockets > item.Gems.Count && carried)
            foreach (var gem in inv.Pack.Where(GameSession.IsGem))
                menu.Add(($"Set {ItemNaming.Describe(gem, _game.Knowledge, withArticle: false, full: false)} here ({_game.GemEffectText(gem)})", () => Execute(new SetGemCommand(item, gem))));

        if (_game.CanWieldOffHand(item)) menu.Add(("Wield in off hand", () => Execute(new WieldOffHandCommand(item)))); // AVABand's Humans

        if (equipped && !item.IsSticky) menu.Add(("Take off", () => UseItemAsked(ItemPromptKind.TakeOff, item)));
        else if (!equipped && item.IsWearable) menu.Add(("Equip", () => UseItemAsked(ItemPromptKind.Wield, item)));

        if (carried)
        {
            menu.Add(("Drop", () => DropChosen(item, 1)));
            if (item.Number > 1) menu.Add(("Drop all", () => DropChosen(item, item.Number)));
        }
        else menu.Add(("Pick up", () => UseItemAsked(ItemPromptKind.Pickup, item)));

        if (!equipped || (item.Base.IsWeapon && !item.IsSticky)) menu.Add(("Throw", () => UseItemAsked(ItemPromptKind.Throw, item)));
        menu.Add(("Inscribe", () => UseItemAsked(ItemPromptKind.Inscribe, item)));
        if (item.Note is not null) menu.Add(("Uninscribe", () => UseItemAsked(ItemPromptKind.Uninscribe, item)));
        menu.Add((_game.IsMarkedIgnored(item) ? "Unignore" : "Ignore", () => UseItemAsked(ItemPromptKind.Ignore, item)));
        return menu;
    }

    /// <summary>Cast, study or browse from one book: its spells, those on offer choosable.</summary>
    private void SpellsFromBook(SpellPromptKind kind, List<Angband.Core.Definitions.SpellDef> inBook)
    {
        if (kind == SpellPromptKind.Study && !_game.ChoosesSpells)
        {
            Execute(new StudyCommand(Book: inBook[0].Book));
            return;
        }
        var offered = kind switch
        {
            SpellPromptKind.Cast => inBook.Where(s => _game.Player.LearnedSpells.Contains(s.Id)),
            SpellPromptKind.Study => _game.StudyableSpells().Where(s => s.Book == inBook[0].Book),
            _ => inBook,
        };
        _spellPromptKind = kind;
        ShowSpellRows(kind, inBook, [.. offered.Select(s => s.Id)]);
    }

    private void FireChosen(Item ammo)
    {
        if (Inscription.AsksFirst(ammo, 'f')) AskFirst($"Really fire {_game.Describe(ammo)}?", () => Execute(new FireCommand(Ammo: ammo)));
        else Execute(new FireCommand(Ammo: ammo));
    }

    private void DropChosen(Item item, int count)
    {
        if (Inscription.AsksFirst(item, 'd')) AskFirst($"Really drop {_game.Describe(item)}?", () => Execute(new DropCommand(item, count)));
        else Execute(new DropCommand(item, count));
    }

    // --- Clear out junk -------------------------------------------------------------------------

    private List<JunkItem>? _junk;
    private HashSet<Item> _junkTicked = [];

    /// <summary>Game → Tidy pack: stacks that can go together merged, and how full the pack is.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void TidyPack()
    {
        Execute(new TidyPackCommand());
        Refresh();
    }

    /// <summary>Game → Clear out junk.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ClearJunk() => BeginClearJunk();

    /// <summary>AVABand's "Clear out junk": the carried things worth ignoring, ticked or not, to ignore at once.</summary>
    public void BeginClearJunk()
    {
        if (IsPrompting || IsConfirming || IsInStore) return;
        var junk = _game.JunkCandidates();
        if (junk.Count == 0)
        {
            AddMessage("You carry nothing that looks like junk.");
            return;
        }
        _junk = [.. junk];
        _junkTicked = [.. junk.Where(j => j.Ticked).Select(j => j.Item)];
        ShowJunkMenu(0);
    }

    private void ShowJunkMenu(int selected)
    {
        var junk = _junk!;
        var ticked = junk.Count(j => _junkTicked.Contains(j.Item));
        var entries = new List<(string, Action)>
        {
            (ticked == 0 ? "Ignore nothing (tick something first)" : $"Ignore the {ticked} ticked", IgnoreTickedJunk),
        };
        for (var i = 0; i < junk.Count; i++)
        {
            var j = junk[i];
            var row = i + 1;
            entries.Add(($"[{(_junkTicked.Contains(j.Item) ? "x" : " ")}] {_game.Describe(j.Item)} — {j.Why}", () =>
            {
                if (!_junkTicked.Remove(j.Item)) _junkTicked.Add(j.Item);
                ShowJunkMenu(row);
            }));
        }
        ShowMenu("Clear out junk — tick what to ignore (it's dropped and hidden; K shows it again):", entries);
        PromptSelectedIndex = selected;
    }

    private void IgnoreTickedJunk()
    {
        var chosen = _junk?.Where(j => _junkTicked.Contains(j.Item)).Select(j => j.Item).ToList() ?? [];
        _junk = null;
        if (chosen.Count == 0)
        {
            LastMessage = "Nothing ignored.";
            return;
        }
        foreach (var item in chosen) Execute(new IgnoreCommand(item, IgnoreChoice.ThisItem));
        AddMessage(chosen.Count == 1 ? "You ignore 1 item." : $"You ignore {chosen.Count} items.");
    }
}
