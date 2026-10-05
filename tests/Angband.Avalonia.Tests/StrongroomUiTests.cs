using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>Butterbur's strongroom through the inn's menus, its lockers in a file shared by every character.</summary>
public sealed class StrongroomUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "avaband-strongroom-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static void Choose(MainWindowViewModel vm, string label)
    {
        var index = vm.MenuLabels.ToList().FindIndex(l => l.StartsWith(label, StringComparison.Ordinal));
        Assert.True(index >= 0, $"no '{label}' in: {string.Join(" | ", vm.MenuLabels)}");
        vm.PromptKey((char)('a' + index));
    }

    private static void AtTheInn(MainWindowViewModel vm)
    {
        var game = vm.Game;
        game.Player.Position = game.Level.AllLocs().First(p => game.Level.FeatureAt(p).Shop == "inn");
        vm.Execute(new EnterStoreCommand());
    }

    [AvaloniaFact]
    public void ASwordLeftByOneCharacter_IsTakenByTheNext_ThroughTheFile()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var store = new StrongroomStore(_dir, () => new DateTime(2026, 10, 5));
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.UseStrongroom(store);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var sword = vm.Game.Player.Inventory.Add(vm.Game.Objects.Create("long_sword"))!;
        sword.ToHit = 6;
        vm.Game.Player.Gold = 10_000;
        AtTheInn(vm);
        Choose(vm, "The strongroom");
        Assert.Equal("The strongroom", vm.PromptTitle);
        Choose(vm, "Leave a Long Sword");
        var locker = Assert.Single(store.Lockers);
        Assert.Equal("2026-10-05", locker.Date);
        Assert.True(File.Exists(store.FilePath));
        Assert.DoesNotContain(vm.Game.Player.Inventory.Pack, i => i.Kind.Id == "long_sword");

        // A new character, the same lockers.
        vm.StartGame(7, "mage");
        vm.Game.GainExperience(vm.Game.ExperienceForLevel(4));            // (level 5: a long sword is within reach)
        vm.Game.Player.Gold = 10_000;
        AtTheInn(vm);
        Choose(vm, "The strongroom");
        TileRenderingTests.Save(window, "strongroom");
        Choose(vm, "Take a Long Sword");
        Assert.Contains(vm.Game.Player.Inventory.Pack, i => i.Kind.Id == "long_sword" && i.ToHit == 6);
        Assert.Empty(store.Lockers);
        Assert.Equal(10_000 - GameSession.StrongroomWithdrawFee(locker.Value), vm.Game.Player.Gold);
    }
}
