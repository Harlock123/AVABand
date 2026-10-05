using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
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
        Assert.Equal(((char)('a' + vm.PackRows.Count)).ToString(), vm.SatchelRows[0].Letter); // lettered on from the pack
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
        Assert.Equal(((char)('a' + vm.PackRows.Count)).ToString(), vm.PouchRows[0].Letter);
        Assert.Equal(((char)('a' + vm.PackRows.Count + 3)).ToString(), vm.SatchelRows[0].Letter);
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
        Assert.Equal(((char)('a' + vm.PackRows.Count)).ToString(), vm.BookRows[0].Letter);
        Assert.Equal(((char)('a' + vm.PackRows.Count + vm.BookRows.Count)).ToString(), vm.PouchRows[0].Letter);
        TileRenderingTests.Save(window, "book-bag");
    }
}
