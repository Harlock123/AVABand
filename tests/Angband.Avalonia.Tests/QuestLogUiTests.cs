using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

/// <summary>The quest log (Ctrl+J): a card for each quest and job, with where it stands.</summary>
public class QuestLogUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings();
        settings.Options[DisplayOptions.Hints] = false;
        settings.Options[DisplayOptions.Scenes] = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(3, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    private static void AtTheInn(MainWindowViewModel vm, params string[] choices)
    {
        var game = vm.Game;
        if (game.Player.Depth != 0) vm.Execute(new DebugJumpCommand(0));
        game.Player.Position = game.Level.AllLocs().First(l => game.Level.FeatureAt(l).Shop == "inn");
        vm.Execute(new EnterStoreCommand());
        foreach (var c in choices) vm.Execute(new QuestChoiceCommand(c));
    }

    [AvaloniaFact]
    public void CtrlJ_OpensTheQuestLog_WithACardForEachQuestAndJob()
    {
        var (window, vm) = Open();
        var game = vm.Game;
        game.MarkDebugUsed();
        game.GainExperience(game.ExperienceForLevel(24));
        game.Player.Hp = game.Player.MaxHp = 100_000;
        vm.Execute(new DebugJumpCommand(5));
        AtTheInn(vm, "inn:board");
        Assert.Equal(5, game.AvaQuests.Board.Count);
        foreach (var job in game.AvaQuests.Board.Take(3).ToList()) vm.Execute(new QuestChoiceCommand($"board:take:{job.Id}"));
        Assert.Equal(3, game.AvaQuests.Board.Count(j => j.Taken));
        AtTheInn(vm, "inn:work", "offer:sealed_door", "accept:sealed_door");
        AtTheInn(vm, "inn:work", "offer:cartographer", "accept:cartographer");
        game.AvaQuests.Board.First(j => j.Taken).Progress = 2;
        vm.SkipScenes();
        while (vm.IsPrompting) window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); // (out of the inn's menu)

        window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control);
        var log = Assert.IsType<QuestLogWindow>(window.OwnedWindows.Last());
        var model = Assert.IsType<QuestLogViewModel>(log.DataContext);
        Assert.Equal(5, model.Cards.Count);
        Assert.StartsWith("5 under way (3 of 5 notice-board jobs), 0 done", model.Summary);
        Assert.Contains(model.Cards, c => c.Name == "The Sealed Door" && c.Status == "Step 1 of 3" && c.Source == "The Prancing Pony");
        Assert.Equal(3, model.Cards.Count(c => c.HasCount && c.HasReward));
        TileRenderingTests.Save(log, "quest-log");
        log.Close();
    }

    [AvaloniaFact]
    public void AnEmptyLog_SaysWhereToFindWork()
    {
        var (_, vm) = Open();
        var model = vm.CreateQuestLog();
        Assert.True(model.IsEmpty);
        Assert.StartsWith("No quests yet.", model.Summary);
    }
}
