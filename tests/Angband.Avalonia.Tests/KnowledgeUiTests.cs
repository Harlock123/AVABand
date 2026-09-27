using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Definitions;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

public sealed class KnowledgeUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    [AvaloniaFact]
    public void Tilde_OpensKnowledge_WithWhatTheCharacterHasSeen()
    {
        var (window, vm) = Open();
        var game = vm.Game;

        // The starting kit is seen, and the ration is a known kind from the start.
        vm.HandleAction(InputAction.MonsterKnowledge);
        var knowledgeWindow = Assert.IsType<KnowledgeWindow>(window.OwnedWindows.Last());
        var knowledge = (KnowledgeViewModel)knowledgeWindow.DataContext!;
        var ration = Assert.Single(knowledge.Objects.Rows, r => r.Name == "Ration of Food");
        knowledge.Objects.Selected = ration;
        Assert.Contains("When eaten", knowledge.Objects.Text);
        Assert.Empty(knowledge.Artifacts.Rows);
        knowledge.SelectedTab = 1;
        TileRenderingTests.Save(knowledgeWindow, "knowledge-objects");
        knowledgeWindow.Close();

        // Finding (and learning) an artifact adds it, its runes, and its kind.
        var phial = game.Objects.CreateArtifact(game.Data.Artifacts.Single(a => a.Id == "galadriel"));
        game.Player.Inventory.Add(phial);
        foreach (var rune in phial.Runes()) game.Knowledge.LearnRune(rune);
        game.RecalculateBonuses();
        var after = vm.CreateKnowledge();
        var row = Assert.Single(after.Artifacts.Rows);
        Assert.Equal("Phial of Galadriel", row.Name);
        after.Artifacts.Selected = row;
        Assert.Contains("When activated, it lights up", after.Artifacts.Text);
        Assert.Contains(after.Runes.Rows, r => r.Name.Contains("light", StringComparison.OrdinalIgnoreCase));
        Assert.Matches(@"^\d+ of \d+ runes learned$", after.Runes.Summary);
    }

    [AvaloniaFact]
    public void UnknownFlavours_AreListedByAppearance()
    {
        var (_, vm) = Open();
        var game = vm.Game;
        var speed = game.Objects.Create("speed");
        game.Player.Inventory.Add(speed);
        game.RecalculateBonuses();

        var objects = vm.CreateObjectKnowledge();
        var row = Assert.Single(objects.Rows, r => r.Name == ((string)game.Describe(speed, withArticle: false)).Split(" {")[0]);
        Assert.DoesNotContain("Speed", row.Name);
        objects.Selected = row;
        Assert.Contains("You don't know what it does.", objects.Text);

        game.Knowledge.LearnKind(speed.Kind);
        Assert.Contains(vm.CreateObjectKnowledge().Rows, r => r.Name == "Potion of Speed");
    }

    [AvaloniaFact]
    public void Inspect_UsesTheObjectInfoText()
    {
        var (window, vm) = Open();
        var torch = vm.Game.Player.Inventory.Equipped.First(i => i.Base.Slot == EquipSlot.Light);
        vm.HandleAction(InputAction.Inspect);
        vm.PromptKey(vm.PromptRows.Single(r => r.Item == torch).Letter[0]);
        Assert.Contains("It burns for up to", vm.LastMessage);
        _ = window;
    }
}

public sealed class RandartUiTests
{
    [AvaloniaFact]
    public void ARandomArtifactGame_ShowsItsOwnArtifactsInKnowledge()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        var creation = vm.CreateCharacterCreation();
        creation.BirthOptionRows.Single(r => r.Id == Angband.Core.Game.OptionIds.Randarts).IsChecked = true;
        vm.StartCharacter(creation.Spec()!);
        var game = vm.Game;
        Assert.NotNull(game.RandartSeed);

        var art = game.Artifacts.First(a => a.Id.StartsWith("randart_"));
        var item = game.Objects.CreateArtifact(art);
        foreach (var rune in item.Runes()) game.Knowledge.LearnRune(rune);
        game.Player.Inventory.Add(item);
        game.RecalculateBonuses();

        var row = Assert.Single(vm.CreateArtifactKnowledge().Rows);
        Assert.EndsWith(art.Name, row.Name);
        Assert.StartsWith("Random ", Angband.Core.Records.ObjectInfo.DescribeArtifact(game, art).Split("\n\n").Last());
    }
}
