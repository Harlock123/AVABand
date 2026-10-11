using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Data;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>The quest satchel in the sidebar: quest items under their own heading, lettered after the pack's.</summary>
public class SatchelUiTests
{
    [AvaloniaFact]
    public void QuestItems_ShowInTheSatchel_NotTheInventory()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        Assert.False(vm.HasSatchelItems);

        var slots = vm.Game.Player.Inventory.SlotsUsed;
        vm.Game.Player.Inventory.Add(vm.Game.Objects.Create("shard_of_the_hilt"));
        vm.Game.Player.Inventory.Add(vm.Game.Objects.Create("palantir"));
        vm.Execute(new HoldCommand());
        Assert.True(vm.HasSatchelItems);
        Assert.Equal(2, vm.SatchelRows.Count);
        Assert.DoesNotContain(vm.PackRows, r => r.Item.IsQuestItem);
        Assert.Equal(((char)('a' + vm.PackRows.Count + vm.BeltRows.Count + vm.CaseRows.Count)).ToString(), vm.SatchelRows[0].Letter); // lettered on from the pack (and its belt and case)
        Assert.Equal(slots, vm.Game.Player.Inventory.SlotsUsed);
        TileRenderingTests.Save(window, "quest-satchel");
    }

    /// <summary>The gem pouch: gems under their own heading, between the pack and the satchel, each with what it does in a socket.</summary>
    [AvaloniaFact]
    public void Gems_ShowInTheGemPouch_WithTheirSocketEffects()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        Assert.False(vm.HasPouchItems);
        var inv = vm.Game.Player.Inventory;
        var slots = inv.SlotsUsed;
        foreach (var kind in new[] { "ruby", "chipped_sapphire", "diamond" })
        {
            var gem = vm.Game.Objects.Create(kind);
            vm.Game.Knowledge.LearnKind(gem.Kind);
            inv.Add(gem);
        }
        inv.Add(vm.Game.Objects.Create("palantir"));
        vm.Execute(new HoldCommand());
        Assert.True(vm.HasPouchItems);
        Assert.Equal(3, vm.PouchRows.Count);
        Assert.Equal(slots + 1, inv.SlotsUsed);
        Assert.Contains(vm.PouchRows, r => r.Name.Contains("Ruby") && r.Advice == "in a socket: +3 to-dam, resist fire");
        // Lettered on from the pack, and the satchel after.
        Assert.Equal(((char)('a' + vm.PackRows.Count + vm.BeltRows.Count + vm.CaseRows.Count)).ToString(), vm.PouchRows[0].Letter);
        Assert.Equal(((char)('a' + vm.PackRows.Count + vm.BeltRows.Count + vm.CaseRows.Count + 3)).ToString(), vm.SatchelRows[0].Letter);
        TileRenderingTests.Save(window, "gem-pouch");
    }

    /// <summary>The book bag: spellbooks under their own heading, after the pack's things and before the gem pouch.</summary>
    [AvaloniaFact]
    public void Spellbooks_ShowInTheBookBag()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42, "mage");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var inv = vm.Game.Player.Inventory;
        Assert.True(vm.HasBookBagItems);                                        // (a mage starts with a book)
        var slots = inv.SlotsUsed;
        foreach (var kind in vm.Game.Data.Objects.Where(k => k.Base is "prayer_book" or "nature_book").Take(2))
            inv.Add(vm.Game.Objects.Create(kind.Id));
        inv.Add(vm.Game.Objects.Create("ruby"));
        vm.Execute(new HoldCommand());
        Assert.Equal(slots + 1, inv.SlotsUsed);                                 // (the books share the bag; the ruby's the pouch)
        Assert.Equal(inv.BookBag.Count(), vm.BookRows.Count);
        Assert.DoesNotContain(vm.PackRows, r => vm.BookRows.Any(b => b.Name == r.Name));
        var before = vm.PackRows.Count + vm.BeltRows.Count + vm.CaseRows.Count; // (the pack, its potion belt and scroll case)
        Assert.Equal(((char)('a' + before)).ToString(), vm.BookRows[0].Letter);
        Assert.Equal(((char)('a' + before + vm.BookRows.Count)).ToString(), vm.PouchRows[0].Letter);
        TileRenderingTests.Save(window, "book-bag");
    }

    /// <summary>The potion belt and scroll case: their own headings, before the book bag; overfull, the heading says what spills.</summary>
    [AvaloniaFact]
    public void PotionsAndScrolls_ShowInTheBeltAndCase()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42, "mage");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var inv = vm.Game.Player.Inventory;
        inv.Add(vm.Game.Objects.Create(Inventory.BeltKind));                  // (both earned: the quests' rewards)
        inv.Add(vm.Game.Objects.Create(Inventory.CaseKind));
        foreach (var kind in vm.Game.Data.Objects.Where(k => k.Base == "potion" && !k.Flags.Contains("QUEST_ITEM")).Take(10))
            inv.Add(vm.Game.Objects.Create(kind.Id));
        inv.Add(vm.Game.Objects.Create(vm.Game.Data.Objects.First(k => k.Base == "scroll").Id));
        vm.Execute(new HoldCommand());
        Assert.True(vm.HasBeltItems);
        Assert.True(vm.HasCaseItems);
        Assert.Equal(inv.Belt.Count(), vm.BeltRows.Count);
        Assert.Equal(inv.ScrollCase.Count(), vm.CaseRows.Count);
        Assert.DoesNotContain(vm.PackRows, r => vm.BeltRows.Concat(vm.CaseRows).Any(b => b.Name == r.Name));
        Assert.Equal(((char)('a' + vm.PackRows.Count)).ToString(), vm.BeltRows[0].Letter);
        Assert.Equal(((char)('a' + vm.PackRows.Count + vm.BeltRows.Count)).ToString(), vm.CaseRows[0].Letter);
        Assert.Equal(((char)('a' + vm.PackRows.Count + vm.BeltRows.Count + vm.CaseRows.Count)).ToString(), vm.BookRows[0].Letter);
        Assert.Equal("Scroll case", vm.CaseHeader);
        var over = inv.Belt.Count() - Inventory.BeltSize;
        Assert.True(over > 0);
        Assert.Equal($"Potion belt ({inv.Belt.Count()} kinds; {over} take a pack slot each)", vm.BeltHeader);
        TileRenderingTests.Save(window, "potion-belt");
    }
}
