using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Data;
using Angband.Data.Tiles;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

/// <summary>
/// AVABand's messages from the fallen in the app: a death leaves its last words on the headstone from the
/// game-over window, the graveyard shows them, and the next character finds them scratched into the floor.
/// </summary>
public sealed class FallenNoteUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "avaband-notes-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private (MainWindow Window, MainWindowViewModel Vm, RecordStore Records) Open(bool tiles = false)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings { UseTiles = tiles, TilesetId = "dcss", TileScale = 1 };
        settings.Options[DisplayOptions.Hints] = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory),
            TilesetCatalog.Discover([TilesetCatalog.DefaultDirectory]), settings, save: null);
        vm.StartGame(42, "warrior");
        var records = new RecordStore(Path.Combine(_dir, "records"));
        vm.UseRecords(records);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm, records);
    }

    private static (GameOverWindow Window, GameOverMenuViewModel Menu) MenuOf(MainWindow window)
    {
        var dialog = window.OwnedWindows.OfType<GameOverWindow>().Last();
        return (dialog, (GameOverMenuViewModel)dialog.DataContext!);
    }

    /// <summary>The character dies at 150 ft (a debug jump there, its mark taken off again).</summary>
    private static void DieAt150Ft(MainWindowViewModel vm, string name, string killer)
    {
        vm.Game.Player.Name = name;
        vm.Game.MarkDebugUsed();
        vm.Execute(new DebugJumpCommand(3));
        vm.Game.ToggleDebugMark();
        vm.SkipScenes();
        vm.Game.TakeHit(100_000, killer);
    }

    [AvaloniaFact]
    public void Dying_OffersALastMessage_ReadyMadeOrOwn_CarvedOnTheHeadstone_AndShownInTheGraveyard()
    {
        var (window, vm, records) = Open();
        DieAt150Ft(vm, "Arwen", "a cave orc");
        var (dialog, menu) = MenuOf(window);
        var prompt = Assert.IsType<LastWordsPrompt>(menu.LastWords);
        Assert.True(dialog.FindControl<Border>("LastWordsPanel")!.IsVisible);
        Assert.Equal("Beware the cave orc.", prompt.Phrases[0]);
        Assert.False(prompt.CanCarve);                                       // (nothing written yet)

        // A ready-made phrase goes in the box, to change or leave.
        var phrases = dialog.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "LastWordsPhrases");
        phrases.SelectedItem = "Turn back. The way is shut.";
        var box = dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "LastWordsBox");
        Assert.Equal("Turn back. The way is shut.", box.Text);
        Assert.Equal(80, box.MaxLength);

        // Letters typed into the box are words, not the menu's choices.
        box.Text = "";
        box.Focus();
        dialog.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        dialog.KeyTextInput("Beware the cave orc. It hides behind the door.");
        Assert.True(dialog.IsVisible);
        Assert.Equal("Beware the cave orc. It hides behind the door.", prompt.Text);
        Assert.True(prompt.CanCarve);
        dialog.UpdateLayout();
        TileRenderingTests.Save(dialog, "last-words-prompt");

        var carve = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "LastWordsCarve");
        carve.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(prompt.IsCarved);
        Assert.False(prompt.CanCarve);                                       // (once)
        Assert.Equal("Carved on the headstone: 'Beware the cave orc. It hides behind the door.'", prompt.Carved);
        Assert.True(dialog.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "LastWordsCarved").IsVisible);
        dialog.UpdateLayout();
        TileRenderingTests.Save(dialog, "last-words-carved");

        // On the headstone, the dump and the sheet.
        var stone = Assert.Single(records.LoadGraveyard().Fallen);
        Assert.Equal("Beware the cave orc. It hides behind the door.", stone.LastWords);
        Assert.Contains(" Last words: \"Beware the cave orc. It hides behind the door.\"", File.ReadAllText(stone.DumpPath!));
        Assert.Contains("Last words:", vm.BuildDump());

        // The graveyard shows them on the stone and in its story.
        var yard = vm.CreateGraveyard();
        var row = Assert.Single(yard.Stones);
        Assert.Equal("'Beware the cave orc. It hides behind the door.'", row.LastWords);
        Assert.Contains("Their last words: 'Beware the cave orc. It hides behind the door.'", yard.Story);
        var graveyard = new GraveyardWindow { DataContext = yard };
        DialogFit.Show(graveyard, window);
        graveyard.UpdateLayout();
        var carved = graveyard.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "StoneLastWords");
        Assert.True(carved.IsVisible);
        Assert.Equal(row.LastWords, carved.Text);
        TileRenderingTests.Save(graveyard, "graveyard-last-words");
    }

    [AvaloniaFact]
    public void NoLastMessage_ForAWinner_OrInTheDailyDungeon()
    {
        var (window, vm, _) = Open();
        vm.Game.Player.IsWinner = true;
        vm.Game.TakeHit(100_000, "a cave orc");
        var (dialog, menu) = MenuOf(window);
        Assert.Null(menu.LastWords);
        Assert.False(dialog.FindControl<Border>("LastWordsPanel")!.IsVisible);

        var (window2, vm2, _) = Open();
        vm2.Game.Player.DailyDate = "2026-10-26";
        vm2.Game.TakeHit(100_000, "a cave orc");
        Assert.Null(MenuOf(window2).Menu.LastWords);
    }

    [AvaloniaFact]
    public void TheNextCharacter_FindsTheWords_ScratchedIntoTheFloor_AndReadsThemByLooking()
    {
        var (window, vm, records) = Open(tiles: true);
        DieAt150Ft(vm, "Arwen", "Grip, Farmer Maggot's Dog");
        var (dialog, menu) = MenuOf(window);
        menu.LastWords!.Text = "Beware the Grip.";
        Assert.True(menu.LastWords.Carve());
        dialog.Close();

        // The next character is handed the words; a level at 150 ft may have them in a room.
        vm.StartGame(78, "warrior");
        var game = vm.Game;
        var note = Assert.Single(game.FloorNotes.Fallen);
        Assert.Equal(("Arwen", 3, "Beware the Grip."), (note.Name, note.Depth, note.Words));
        game.MarkDebugUsed();
        Loc? at = null;
        for (var i = 0; i < 60 && at is null; i++)
        {
            vm.Execute(new DebugJumpCommand(3));
            vm.SkipScenes();
            at = game.Level.FallenNotes.Keys.Cast<Loc?>().FirstOrDefault();
        }
        game.ToggleDebugMark();
        Assert.NotNull(at);
        var spot = at.Value;

        // Beside it (nothing else in sight), it is drawn as its own terrain, and looking reads it.
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        game.Player.Position = DirectionExtensions.Compass.Select(d => spot.Step(d)).First(p => game.Level.IsPassable(p) && game.Level[p].Trap == 0);
        game.UpdateView();
        vm.Execute(new HoldCommand());
        Assert.Equal('¶', vm.GetCell(spot.X, spot.Y).Glyph);
        Assert.Equal("terrain:fallen_note", vm.GetCell(spot.X, spot.Y).TileKey);
        vm.Look();
        for (var i = 0; i < 20 && !vm.LastMessage.StartsWith("Scratched into the floor", StringComparison.Ordinal); i++) vm.CursorKey(" ");
        const string text = "Scratched into the floor: 'Beware the Grip.' — Arwen the Human Warrior, killed here by Grip, Farmer Maggot's Dog";
        Assert.StartsWith(text, vm.LastMessage);
        Assert.Contains(vm.Messages, m => m.StartsWith(text, StringComparison.Ordinal));  // (the message log keeps it)
        Assert.Equal([note.Key], game.FloorNotes.Read);
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "fallen-note-tiles");
        vm.CursorKey("t");

        vm.ToggleTiles();
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "fallen-note-ascii");
        _ = records;
    }
}
