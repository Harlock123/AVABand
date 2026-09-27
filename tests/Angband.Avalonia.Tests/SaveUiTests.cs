using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

public sealed class SaveUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "avaband-saves-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private (MainWindow Window, MainWindowViewModel Vm, SaveStore Saves) Open(bool resume = false, ulong seed = 42)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(seed);
        var saves = new SaveStore(_dir);
        vm.UseSaves(saves, resume);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm, saves);
    }

    [AvaloniaFact]
    public void CtrlS_SavesTheGame()
    {
        var (window, vm, saves) = Open();
        window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);

        var entry = Assert.Single(saves.List());
        Assert.Equal(vm.Game.Player.Name, entry.Summary.Name);
        Assert.Equal("Game saved.", vm.LastMessage);
    }

    [AvaloniaFact]
    public void Startup_ResumesTheMostRecentCharacter()
    {
        var (_, first, _) = Open(seed: 7);
        first.Execute(new DebugJumpCommand(2)); // autosaves on the level change
        var position = first.Game.Player.Position;

        var (_, second, _) = Open(resume: true, seed: 99);
        Assert.Equal(7ul, second.Game.Seed);
        Assert.Equal(2, second.Game.Player.Depth);
        Assert.Equal(position, second.Game.Player.Position);
        Assert.StartsWith("Welcome back", second.LastMessage);
        Assert.False(second.IsFirstRun);
    }

    [AvaloniaFact]
    public void LoadDialog_ListsAndLoadsSaves()
    {
        var (window, vm, _) = Open(seed: 5);
        vm.SaveGame();
        vm.StartGame(6); // keeps the first character's save, starts another
        Assert.Equal(6ul, vm.Game.Seed);

        window.KeyPressQwerty(PhysicalKey.O, RawInputModifiers.Control);
        var dialogWindow = Assert.IsType<LoadGameWindow>(window.OwnedWindows.Last());
        var dialog = (LoadGameViewModel)dialogWindow.DataContext!;
        Assert.Single(dialog.Saves);
        TileRenderingTests.Save(dialogWindow, "load-game");

        dialog.LoadCommand.Execute(null);
        Assert.Equal(5ul, vm.Game.Seed);
        Assert.DoesNotContain(dialogWindow, window.OwnedWindows);
        Assert.Equal(2, new SaveStore(_dir).List().Count); // the seed-6 character was saved on switching
    }

    [AvaloniaFact]
    public void LoadDialog_DeletesSaves()
    {
        var (_, vm, saves) = Open();
        vm.SaveGame();
        var dialog = vm.CreateLoadGame();
        dialog.DeleteCommand.Execute(null); // the first press only asks
        Assert.Equal("Really delete?", dialog.DeleteText);
        Assert.Single(dialog.Saves);
        dialog.DeleteCommand.Execute(null);
        Assert.Empty(dialog.Saves);
        Assert.True(dialog.IsEmpty);
        Assert.Empty(saves.List());
    }

    [AvaloniaFact]
    public void Death_DeletesTheSave()
    {
        var (_, vm, saves) = Open();
        vm.SaveGame();
        Assert.Single(saves.List());

        vm.Game.TakeHit(vm.Game.Player.Hp + 100, "a test");
        Assert.True(vm.Game.Player.IsDead);
        Assert.Empty(saves.List());
        vm.HandleAction(InputAction.SaveGame);
        Assert.Empty(saves.List());
    }

    [AvaloniaFact]
    public void DamagedSave_IsSkippedAndReported()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "broken.avasave"), "not a save");
        var (_, vm, saves) = Open(resume: true, seed: 3);
        Assert.Empty(saves.List());
        Assert.Equal(3ul, vm.Game.Seed);
        Assert.False(vm.TryLoad(Path.Combine(_dir, "broken.avasave"), out var error));
        Assert.Contains("damaged", error);
    }

    [AvaloniaFact]
    public void Hunger_ShowsInTheStatusBar_AndSurvivesSaving()
    {
        var (window, vm, saves) = Open();
        Assert.DoesNotContain("Hungry", vm.StatusText); // fed is the normal state and isn't shown
        vm.Game.SetFood(vm.Game.Data.Constants.FoodHungry - 1);
        window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.None); // hold a turn
        Assert.Contains("Hungry", vm.StatusText);

        vm.SaveGame();
        Assert.True(vm.TryLoad(saves.List()[0].Path, out _));
        Assert.Equal(Angband.Core.Game.HungerLevel.Hungry, vm.Game.HungerLevel);
    }
}
