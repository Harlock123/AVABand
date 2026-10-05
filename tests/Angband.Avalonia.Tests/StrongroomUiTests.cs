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

    // --- Kept in step with the save, through a crash ----------------------------------------------

    private static StrongroomLocker ASword(string id = "") => new(id, "{}", "a Long Sword", "Fallow the Paladin", "", 10, 500);

    /// <summary>A store whose current save is a file in the test folder (written when asked).</summary>
    private (StrongroomStore Store, string Save) WithSave()
    {
        Directory.CreateDirectory(_dir);
        var save = Path.Combine(_dir, "Fallow.avasave");
        File.WriteAllText(save, "old");
        File.SetLastWriteTimeUtc(save, DateTime.UtcNow.AddMinutes(-5));
        return (new StrongroomStore(_dir) { CurrentSave = () => save }, save);
    }

    private static void SaveWritten(string save)
    {
        File.WriteAllText(save, "new");
        File.SetLastWriteTimeUtc(save, DateTime.UtcNow.AddSeconds(5));
    }

    [AvaloniaFact]
    public void ALockerLeft_ButTheGameStoppedBeforeTheSave_IsUndone()
    {
        var (store, _) = WithSave();
        store.Deposit(ASword());
        Assert.Empty(store.Lockers);                                 // (not final yet)
        Assert.Equal(1, new StrongroomStore(_dir).Recover());       // the next start: the save never had it gone
        Assert.Empty(new StrongroomStore(_dir).Lockers);
        Assert.Equal(0, new StrongroomStore(_dir).Recover());
    }

    [AvaloniaFact]
    public void ALockerLeft_AndSaved_ButNotMadeFinal_IsKept()
    {
        var (store, save) = WithSave();
        store.Deposit(ASword());
        SaveWritten(save);                                           // saved without it, then the game stopped
        new StrongroomStore(_dir).Recover();
        Assert.Single(new StrongroomStore(_dir).Lockers);
    }

    [AvaloniaFact]
    public void ALockerTaken_ButNotSaved_IsBackInItsLocker_AndSaved_IsGone()
    {
        var (store, save) = WithSave();
        store.Deposit(ASword());
        store.Commit();
        var id = Assert.Single(store.Lockers).Id;
        Assert.True(store.Remove(id));
        Assert.Empty(store.Lockers);
        Assert.False(store.Remove(id));                              // (not twice)
        new StrongroomStore(_dir).Recover();                         // the game stopped before the save
        Assert.Single(new StrongroomStore(_dir).Lockers);

        var again = new StrongroomStore(_dir) { CurrentSave = () => save };
        Assert.True(again.Remove(id));
        SaveWritten(save);                                           // this time the save has it
        new StrongroomStore(_dir).Recover();
        Assert.Empty(new StrongroomStore(_dir).Lockers);
    }

    [AvaloniaFact]
    public void ALockersFile_FromBeforeTheTwoSteps_StillOpens()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "strongroom.json"),
            System.Text.Json.JsonSerializer.Serialize(new List<StrongroomLocker> { ASword("old1") }));
        Assert.Equal("old1", Assert.Single(new StrongroomStore(_dir).Lockers).Id);
    }
}
