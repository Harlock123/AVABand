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
        TestKit.Give(vm);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    [AvaloniaFact]
    public void The_Curses_page_lists_the_curses_you_know()
    {
        var (window, vm) = Open();
        var game = vm.Game;
        Assert.Empty(vm.CreateKnowledge().Curses!.Rows);

        var dagger = game.Objects.Create("dagger");
        Assert.True(game.Objects.AddCurse(new Angband.Core.Randomness.GameRandom(1), dagger, game.Data.Curse("siren")!, 12));
        game.Player.Inventory.Add(dagger);
        game.Knowledge.LearnRune(RuneIds.Curse("siren"));
        var knowledge = vm.CreateKnowledge();
        var row = Assert.Single(knowledge.Curses!.Rows);
        Assert.Equal("Siren", row.Name);
        Assert.Equal("carried", row.Note);
        knowledge.Curses.Selected = row;
        Assert.Contains("(strength 12)", knowledge.Curses.Text);

        var knowledgeWindow = new KnowledgeWindow { DataContext = knowledge };
        knowledgeWindow.Show();
        knowledge.SelectedTab = 3;
        TileRenderingTests.Save(knowledgeWindow, "knowledge-curses");
        knowledgeWindow.Close();
        window.Close();
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

    [AvaloniaFact]
    public void Knowledge_HasFeatures_Traps_TheHome_AndTheHistory()
    {
        var (window, vm) = Open();
        var game = vm.Game;
        var home = game.Stores.Values.Single(s => s.IsHome);
        var torch = game.Objects.Create("wooden_torch");
        home.Stock.Add(torch.Clone(game.Objects.NextSerial++, 1));
        game.AddNote("left a torch at home");

        var k = vm.CreateKnowledge();
        var features = k.Features!;
        var granite = Assert.Single(features.Rows, r => r.Name == "Granite wall");
        Assert.Equal("wall", granite.Note);
        Assert.DoesNotContain(features.Rows, r => r.Name == "Secret door"); // it only looks like granite
        Assert.Contains(features.Rows, r => r.Name == "Up staircase" && r.Note == "stairs");
        features.Selected = granite;
        Assert.StartsWith("Granite wall\n\n", features.Text);
        Assert.True(features.Text.Length > "Granite wall\n\n".Length); // with its description

        var traps = k.Traps!;
        Assert.Equal(game.Data.Traps.Count, traps.Rows.Count);
        var trapDoor = Assert.Single(traps.Rows, r => r.Name == "Trap door");
        traps.Selected = trapDoor;
        Assert.Contains(game.Data.Traps.First(t => t.IsTrapDoor).Description, traps.Text);

        var stored = Assert.Single(k.Home!.Rows);
        Assert.Contains("Torch", stored.Name);

        Assert.StartsWith("      Turn   Depth  Note", k.History);
        Assert.Contains("Began the quest to destroy Morgoth.", k.History);
        Assert.Contains("-- Note: left a torch at home", k.History);

        vm.HandleAction(InputAction.MonsterKnowledge);
        var knowledgeWindow = Assert.IsType<KnowledgeWindow>(window.OwnedWindows.Last());
        ((KnowledgeViewModel)knowledgeWindow.DataContext!).SelectedTab = 11;
        TileRenderingTests.Save(knowledgeWindow, "knowledge-history");
        knowledgeWindow.Close();
    }

    [AvaloniaFact]
    public void Knowledge_HasShapes_AndTheEquipmentComparison()
    {
        var (window, vm) = Open();
        var game = vm.Game;
        var k = vm.CreateKnowledge();

        var shapes = k.Shapes!;
        Assert.Equal(game.Data.Shapes.Count(s => s.Id != "normal"), shapes.Rows.Count);
        shapes.Selected = shapes.Rows.Single(r => r.Name == "Bear");
        Assert.Contains("Adds +5 to AC, +15 to hit and +15 to damage.", shapes.Text);
        Assert.Contains("Gives protection from fear.", shapes.Text);
        Assert.Contains("triggers the shapechange.", shapes.Text);

        var equipment = k.Equipment!;
        Assert.Contains(equipment.Lines, l => l.Text.StartsWith("worn ", StringComparison.Ordinal) && l.Text.Contains("Dagger"));
        Assert.Contains("Where", equipment.Headings);
        Assert.Contains("resistances", equipment.Groups);
        equipment.Slot = "Weapon";
        Assert.All(equipment.Lines, l => Assert.Equal(Angband.Core.Definitions.EquipSlot.Weapon, l.Item.Base.Slot));
        var ownWeapons = equipment.Lines.Count;
        equipment.IncludeShops = true;
        Assert.True(equipment.Lines.Count > ownWeapons); // the weaponsmith's stock
        Assert.Contains(equipment.Lines, l => l.Text.StartsWith("Weapon", StringComparison.Ordinal));
        equipment.Selected = equipment.Lines.First();
        Assert.NotEmpty(equipment.Text);

        vm.HandleAction(InputAction.MonsterKnowledge);
        var knowledgeWindow = Assert.IsType<KnowledgeWindow>(window.OwnedWindows.Last());
        var shown = (KnowledgeViewModel)knowledgeWindow.DataContext!;
        shown.SelectedTab = 9;
        shown.Equipment!.IncludeShops = true;
        TileRenderingTests.Save(knowledgeWindow, "knowledge-equipment");
        knowledgeWindow.Close();
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
