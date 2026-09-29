using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>AVABand's quests in the window: the Prancing Pony's menus, and the journal.</summary>
public class QuestUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        vm.Game.GainExperience(vm.Game.ExperienceForLevel(11));
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    private static void Pick(MainWindowViewModel vm, string label) =>
        vm.PromptKey(vm.ChoiceRows.First(r => r.Text.StartsWith(label)).Letter[0]);

    [AvaloniaFact]
    public void Walking_into_the_inn_offers_work_and_a_quest_shows_its_words()
    {
        var (window, vm) = Open();
        var game = vm.Game;
        var door = game.Level.AllLocs().First(l => game.Level.FeatureAt(l).Shop == "inn");
        var from = game.Level.AllLocs().First(l => l.ChebyshevTo(door) == 1 && game.Level.IsEmptyFloor(l));
        game.Player.Position = from;
        vm.Execute(new WalkCommand(Angband.Core.Geometry.DirectionExtensions.FromOffset(door.X - from.X, door.Y - from.Y)));

        Assert.True(vm.IsPrompting);
        Assert.Equal("The Prancing Pony", vm.PromptTitle);
        Assert.Contains("Butterbur", vm.PromptText);
        Pick(vm, "Ask who has work");
        Pick(vm, "Ask about: The Broken Blade");
        Assert.Contains("hilt, the blade and the point", vm.PromptText);
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "quest-offer");
        Pick(vm, "Take it on");
        Assert.False(vm.IsPrompting);
        Assert.NotNull(game.AvaQuests.Get("broken_blade"));

        var journal = vm.CreateKnowledge().Quests!;
        var row = Assert.Single(journal.Rows);
        Assert.Equal("The Broken Blade", row.Name);
        Assert.Contains("You have 0 of 3", row.Describe());
    }
}
