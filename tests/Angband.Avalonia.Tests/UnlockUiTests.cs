using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Records;
using Angband.Data;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

/// <summary>Unlocks earned through feats: said when earned, listed in Knowledge → Unlocks, the kits on the creation screen, card frames and title scenes.</summary>
public sealed class UnlockUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "avaband-unlocks-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private (MainWindow Window, MainWindowViewModel Vm, AppSettings Settings) Open()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        MainWindow.OpenSavedCards = false;
        var settings = new AppSettings();
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null)
        {
            ArtDirectory = Path.Combine(_dir, "art"), // (none of the player's own)
            BundledArtDirectory = Path.Combine(AppContext.BaseDirectory, "art"),
        };
        vm.StartGame(42, "warrior");
        vm.UseRecords(new RecordStore(Path.Combine(_dir, "records")));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm, settings);
    }

    private static void Earn(MainWindowViewModel vm, params string[] feats)
    {
        foreach (var id in feats) vm.FeatBook.Earned[id] = new FeatRecord { Character = "Wren the Human Warrior", WhenUtc = DateTime.UtcNow };
    }

    [AvaloniaFact]
    public void AFeat_ThatUnlocksSomething_SaysSo_AndTheUnlocksPageListsIt()
    {
        var (window, vm, _) = Open();
        var before = vm.CreateUnlocksPage();
        Assert.Equal($"0 of {Unlocks.All.Count} unlocked  ·  earned through feats, for every character", before.Summary);
        Assert.All(before.Rows, r => Assert.Equal("·", r.Glyph));

        vm.Game.Player.MaxDepth = 20;
        vm.Execute(new HoldCommand());
        var said = vm.LastMessage + " " + string.Join(" ", vm.EarlierMessages.Select(m => m.Text));
        Assert.Contains("Feat: Into the Deep!", said);
        Assert.Contains("Unlocked: The Long Fall (a title-screen scene: a shaft falling away to a red glow).", said);

        var page = vm.CreateUnlocksPage();
        Assert.StartsWith($"1 of {Unlocks.All.Count} unlocked", page.Summary);
        var row = page.Rows.Single(r => r.Name == "The Long Fall");
        Assert.Equal("*", row.Glyph);
        Assert.Equal("Title scene", row.Note);
        Assert.Contains("Earned by the feat “Into the Deep” (Reach 1000 ft).", row.Describe());
        Assert.Contains("Unlocked.", row.Describe());
        var kit = page.Rows.Single(r => r.Name == "Delver's kit");
        Assert.Contains("Not yet unlocked.", kit.Describe());
        Assert.Contains("ten flasks of oil", kit.Describe());

        // The page is the knowledge browser's Unlocks tab (16).
        vm.HandleAction(Angband.Input.InputAction.MonsterKnowledge);
        var knowledge = window.OwnedWindows.OfType<KnowledgeWindow>().Last();
        var shown = (KnowledgeViewModel)knowledge.DataContext!;
        Assert.NotNull(shown.Unlocks);
        shown.SelectedTab = 16;
        var tabs = knowledge.GetVisualDescendants().OfType<TabControl>().First();
        Assert.Equal("Unlocks", ((TabItem)tabs.Items[16]!).Header);
        Assert.Same(tabs.Items[16], tabs.SelectedItem);
        TileRenderingTests.Save(knowledge, "knowledge-unlocks");
        knowledge.Close();
    }

    [AvaloniaFact]
    public void TheCreationScreen_OffersTheKitsUnlocked_AndTheCharacterStartsWithOne()
    {
        var (window, vm, _) = Open();
        var plain = vm.CreateCharacterCreation();
        Assert.False(plain.HasKitChoice);
        Assert.Null(plain.Spec()!.Kit);

        Earn(vm, "depth_10", "depth_20", "depth_40", "level_10", "level_20");
        var creation = vm.CreateCharacterCreation();
        Assert.True(creation.HasKitChoice);
        Assert.Equal(["Standard kit", "Delver's kit", "Scholar's kit"], creation.Kits.Select(k => k.Label));
        var dialog = new CharacterCreationWindow { DataContext = creation };
        dialog.Show(window);
        Assert.True(dialog.FindControl<StackPanel>("KitPanel")!.IsVisible);
        var standardGold = int.Parse(creation.Preview.Split('\n').Last().Trim().Split(' ').Last());
        dialog.FindControl<ComboBox>("KitBox")!.SelectedIndex = 1;
        Assert.Equal("delver", creation.SelectedKit.Id);
        Assert.Equal("delver", creation.Spec()!.Kit);
        var delverGold = int.Parse(creation.Preview.Split('\n').Last().Trim().Split(' ').Last());
        Assert.True(delverGold > standardGold, $"{delverGold} <= {standardGold}"); // (the Word of Recall is worth a little more than the goods)
        dialog.FindControl<StackPanel>("KitPanel")!.BringIntoView();
        TileRenderingTests.Save(dialog, "creation-kit");

        creation.StartCommand.Execute(null);
        Assert.Contains(vm.Game.Player.Inventory.All, i => i.Kind.Id == "phase_door");
        Assert.DoesNotContain(vm.Game.Player.Inventory.All, i => i.Kind.Id == StartingKits.TradedKind);
        // The kit is remembered with the character, for "play again".
        Assert.Equal("delver", vm.LastCharacter()!.Kit);
        dialog.Close();
    }

    [AvaloniaFact]
    public void ACardFrame_IsChosenInTheSettings_AndDrawnOnTheCard()
    {
        var (window, vm, settings) = Open();
        Assert.Equal(["Plain"], vm.CardFrameChoices);
        Assert.Null(vm.ChosenCardFrame);

        // Every feat done: every frame to choose from.
        Earn(vm, [.. Feats.All(vm.Game.Data).Select(f => f.Id)]);
        Assert.Equal(["Plain", .. CardFrames.All.Select(f => f.Name)], vm.CardFrameChoices);
        var settingsWindow = window.OpenSettings();
        var box = settingsWindow.FindControl<ComboBox>("CardFrameBox")!;
        Assert.Equal(CardFrames.All.Count + 1, box.ItemCount);
        box.SelectedIndex = vm.CardFrameChoices.ToList().IndexOf("Gold frame");
        Assert.Equal("gold", settings.CardFrame);
        Assert.Equal("gold", vm.ChosenCardFrame?.Id);
        settingsWindow.Close();

        vm.Game.Player.Name = "Wren";
        var shots = Environment.GetEnvironmentVariable("AVABAND_SCREENSHOTS");
        foreach (var frame in CardFrames.All)
        {
            vm.CardFrameIndex = vm.CardFrameChoices.ToList().IndexOf(frame.Name);
            Assert.Equal(frame.Id, vm.ChosenCardFrame?.Id);
            vm.SaveCharacterCardCommand.Execute(null);
            Assert.StartsWith("Character card saved: ", vm.LastMessage);
            var path = Directory.GetFiles(vm.CardDirectory!, "*.png").Single(); // (one at a time: each is deleted below)
            using (var png = new global::Avalonia.Media.Imaging.Bitmap(path)) Assert.Equal(1200, png.PixelSize.Width);
            if (shots is not null) File.Copy(path, Path.Combine(shots, $"card-frame-{frame.Id}.png"), true);
            File.Delete(path);
        }

        // A frame drawn on the card itself: the band is the frame's colour, not the scene's.
        var host = window.FindControl<Canvas>("CardHost")!;
        var card = new CharacterCardViewModel("Wren", "the Human Warrior", ["Level 1"], null, null, false, "AVABand")
            { Frame = CardFrames.Find("iron") };
        var file = Path.Combine(_dir, "iron.png");
        CharacterCard.Save(card, file, host);
        using var saved = new global::Avalonia.Media.Imaging.Bitmap(file);
        Assert.Equal(675, saved.PixelSize.Height);
    }

    [AvaloniaFact]
    public void TitleScenes_Unlocked_TakeTurnsBehindTheTitle()
    {
        var (window, vm, settings) = Open();
        Assert.Equal(["title"], vm.TitleScenes);
        Assert.All(Unlocks.All.Where(u => u.Kind == UnlockKind.TitleScene), u => Assert.NotNull(vm.PictureFor(u.Key)));

        Earn(vm, "depth_10", "depth_20", "ava_quest");
        Assert.Equal(["title", "deep-descent", "quest-complete"], vm.TitleScenes);
        var seen = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            vm.ShowTitle();
            seen.Add(vm.TitleScene);
            Assert.NotNull(vm.TitleBackground);
            vm.CloseTitle();
        }
        Assert.Equal(3, seen.Distinct().Count());
        Assert.Equal(3, settings.TitleShows);

        vm.ShowTitle();
        while (vm.TitleScene != "deep-descent") { vm.CloseTitle(); vm.ShowTitle(); }
        TileRenderingTests.Save(window, "title-unlocked-scene");
        vm.CloseTitle();
    }
}
