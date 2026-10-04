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
}
