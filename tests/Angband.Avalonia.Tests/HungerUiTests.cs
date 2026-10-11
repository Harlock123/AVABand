using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

/// <summary>AVABand's hunger safeguards: the food meter, being asked to eat, auto-eating, the gates and the one-key eat.</summary>
public class HungerUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, GameSession Game) Open(bool autoEat = false)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings();
        settings.Options[DisplayOptions.Hints] = false;
        settings.Options[DisplayOptions.Scenes] = false;
        settings.Options[DisplayOptions.AutoEat] = autoEat;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var game = vm.Game;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        return (window, vm, game);
    }

    private static void RemoveFood(GameSession game)
    {
        foreach (var food in game.Player.Inventory.Pack.Where(game.IsProvision).ToList())
            game.Player.Inventory.Remove(food, food.Number, () => game.Objects.NextSerial++);
    }

    private static int Rations(GameSession game) => game.Player.Inventory.Pack.Where(i => i.Kind.Id == "ration_of_food").Sum(i => i.Number);

    [AvaloniaFact]
    public void TheFoodMeter_ShowsHowFedYouAre_AndPulsesWhenWeak()
    {
        var (window, vm, game) = Open();
        vm.Execute(new HoldCommand());
        Assert.StartsWith("Food: Fed", vm.FoodText);
        Assert.Contains($"{game.Provisions} meals", vm.FoodText);
        Assert.False(vm.FoodUrgent);

        game.SetFood(game.Data.Constants.FoodHungry - 1);
        vm.Refresh();
        Assert.StartsWith("Food: Hungry", vm.FoodText);
        game.SetFood(game.Data.Constants.FoodWeak - 1);
        vm.Refresh();
        Assert.StartsWith("Food: Weak from hunger", vm.FoodText);
        Assert.True(vm.FoodUrgent);
        RemoveFood(game);
        vm.Refresh();
        Assert.EndsWith("no food!", vm.FoodText);
        TileRenderingTests.Save(window, "food-meter-weak");
    }

    [AvaloniaFact]
    public void GrowingWeak_AsksToEat_AndYesEats()
    {
        var (window, vm, game) = Open();
        var rations = Rations(game);
        Assert.True(rations > 0);
        game.SetFood(game.Data.Constants.FoodWeak - 1);                       // (as digestion would)
        vm.Execute(new HoldCommand());
        Assert.True(vm.IsConfirming);
        Assert.Equal("You are weak from hunger. Eat a Ration of Food? (y/n)", vm.LastMessage);
        window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
        Assert.Equal(rations - 1, Rations(game));
        Assert.True(game.HungerLevel > HungerLevel.Weak);
    }

    [AvaloniaFact]
    public void WithAutoEat_YouEatByYourself()
    {
        var (_, vm, game) = Open(autoEat: true);
        var rations = Rations(game);
        game.SetFood(game.Data.Constants.FoodFaint - 1);
        vm.Execute(new HoldCommand());
        Assert.False(vm.IsConfirming);
        Assert.Equal(rations - 1, Rations(game));
        Assert.Contains(vm.Messages, m => m.Contains("You are faint from hunger, so you eat a Ration of Food."));
    }

    [AvaloniaFact]
    public void RestingWhileWeak_AsksToEatFirst_ThenRests()
    {
        var (window, vm, game) = Open();
        game.SetFood(game.Data.Constants.FoodWeak - 1);
        vm.Execute(new HoldCommand());
        window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);          // (not now)
        Assert.Contains("Eat soon", vm.LastMessage);
        var rations = Rations(game);

        vm.Execute(new RestCommand());
        Assert.Equal("You are weak from hunger. Eat a Ration of Food first? (y/n)", vm.LastMessage);
        window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);
        Assert.StartsWith("Not while you're weak from hunger", vm.LastMessage);
        Assert.Equal(rations, Rations(game));

        vm.Execute(new ExploreCommand());
        window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
        Assert.Equal(rations - 1, Rations(game));                               // ate, then explored

        // With nothing to eat, it asks whether to go on regardless.
        RemoveFood(game);
        game.SetFood(game.Data.Constants.FoodWeak - 1);
        vm.Execute(new RestCommand());
        Assert.Equal("You are weak from hunger and have nothing to eat. Rest anyway? (y/n)", vm.LastMessage);
        game.Player.Hp = game.Player.MaxHp - 3;                                // (something to rest for)
        var turn = game.GameTurn;
        window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
        Assert.False(vm.IsConfirming);
        Assert.True(game.GameTurn > turn || game.Player.IsDead);
    }

    [AvaloniaFact]
    public void TheLastMeal_AndGoingDownFromTownWithNoFood_AreWarnedOf()
    {
        var (window, vm, game) = Open();
        RemoveFood(game);
        var ration = game.Objects.Create("ration_of_food", 2);
        game.Knowledge.LearnKind(ration.Kind);
        game.Player.Inventory.Add(ration);
        game.SetFood(game.Data.Constants.FoodHungry - 1);
        vm.Execute(new UseCommand(game.Player.Inventory.Pack.First(i => i.Kind.Id == "ration_of_food")));
        Assert.Contains(vm.Messages, m => m == "You have one meal left.");
        game.SetFood(game.Data.Constants.FoodHungry - 1);
        vm.Execute(new UseCommand(game.Player.Inventory.Pack.First(i => i.Kind.Id == "ration_of_food")));
        Assert.Contains(vm.Messages, m => m.StartsWith("You have no food left.", StringComparison.Ordinal));

        // Down the stairs from town with no food: asked first; no stays.
        game.Player.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();
        vm.Execute(new TakeStairsCommand(Down: true));
        Assert.Equal("You're carrying no food (the General Store, 1, sells rations). Go down anyway? (y/n)", vm.LastMessage);
        window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);
        Assert.Equal(0, game.Player.Depth);
        vm.Execute(new TakeStairsCommand(Down: true));
        window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
        Assert.Equal(1, game.Player.Depth);
    }

    [AvaloniaFact]
    public void RecallingFromTownWithNoFood_AsksFirst()
    {
        var (window, vm, game) = Open();
        RemoveFood(game);
        var scroll = game.Player.Inventory.Pack.First(i => i.Kind.Id == "scroll_of_word_of_recall");
        game.Player.MaxDepth = 5;
        vm.Execute(new UseCommand(scroll));
        Assert.Equal("You're carrying no food (the General Store, 1, sells rations). Recall to the dungeon anyway? (y/n)", vm.LastMessage);
        window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);
        Assert.Equal(0, game.Player.RecallTimer);
        vm.Execute(new UseCommand(scroll));
        window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
        Assert.False(vm.IsConfirming);
        Assert.True(game.Player.RecallTimer > 0);

        // With food, no question.
        var (_, vm2, game2) = Open();
        game2.Player.MaxDepth = 5;
        vm2.Execute(new UseCommand(game2.Player.Inventory.Pack.First(i => i.Kind.Id == "scroll_of_word_of_recall")));
        Assert.False(vm2.IsConfirming);
        Assert.True(game2.Player.RecallTimer > 0);
    }

    [AvaloniaFact]
    public void TheEatKey_EatsYourBestFood_WithoutAMenu()
    {
        var (_, vm, game) = Open();
        var rations = Rations(game);
        game.SetFood(game.Data.Constants.FoodHungry - 1);
        vm.HandleAction(InputAction.EatBest);
        Assert.False(vm.IsPrompting);
        Assert.Equal(rations - 1, Rations(game));
        game.SetFood(game.Data.Constants.FoodMax - 100);
        vm.HandleAction(InputAction.EatBest);
        Assert.Equal("You are too full to eat any more.", vm.LastMessage);
        Assert.Equal(InputAction.EatBest, InputBindings.Defaults().Keys["Ctrl+Shift+E"]);
        Assert.Equal(InputAction.EatBest, InputBindings.Preset(InputBindings.Keyset.Roguelike).Keys["Ctrl+Shift+E"]);
    }
}
