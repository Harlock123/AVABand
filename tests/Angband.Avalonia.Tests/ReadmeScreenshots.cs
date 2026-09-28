using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.World;
using Angband.Data;
using Angband.Data.Tiles;
using Angband.Input;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

/// <summary>
/// The README's screenshots, rendered headlessly from the real windows (no desktop is captured).
/// Nothing is written unless AVABAND_README_SHOTS names a folder; <c>tools/screenshots.sh</c> sets it
/// to <c>screenshots/</c> and runs these. Fixed seeds keep the pictures the same from run to run.
/// </summary>
public class ReadmeScreenshots
{
    private static readonly string? Folder = Environment.GetEnvironmentVariable("AVABAND_README_SHOTS") is { Length: > 0 } dir ? dir : null;
    private static readonly IReadOnlyList<TilesetManifest> Tilesets = TilesetCatalog.Discover([TilesetCatalog.DefaultDirectory]);

    private static (MainWindow Window, MainWindowViewModel Vm) Open(string classId, bool tiles, string tileset = "gervais", ulong seed = 42)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings { UseTiles = tiles, TilesetId = tileset, TileScale = 1.5, Muted = true };
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), Tilesets, settings, save: null);
        vm.StartGame(seed, classId);
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        return (window, vm);
    }

    private static void Shoot(Window window, string name)
    {
        if (Folder is null) return;
        Directory.CreateDirectory(Folder);
        Dispatcher();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
        frame.Save(Path.Combine(Folder, name + ".png"));
    }

    private static void Dispatcher() => global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    /// <summary>Down into the dungeon, with a few monsters and objects in view of the start.</summary>
    private static void Descend(MainWindowViewModel vm, int depth = 5)
    {
        var game = vm.Game;
        game.MarkDebugUsed(); // the jumps below are debug commands; don't stop to ask
        for (var d = 0; d < depth; d += 5) vm.HandleAction(InputAction.JumpDeeper); // debug: 5 levels at a time
        // A level where the player starts in a lit room well inside the map, so the view is centred on it.
        bool Good() => game.Player.Position is var at && at.X > 35 && at.X < game.Level.Width - 35 && at.Y > 12
                       && at.Y < game.Level.Height - 12 && game.Level[at].Has(SquareFlags.Room);
        for (var tries = 0; tries < 40 && !Good(); tries++) vm.HandleAction(InputAction.RegenerateLevel);
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        var p = game.Player.Position;
        var spots = game.Level.AllLocs()
            .Where(l => l.DistanceTo(p) is > 2 and < 7 && game.Level.IsEmptyFloor(l) && game.Level[l].Has(SquareFlags.Seen))
            .OrderBy(l => (l.X * 7919 + l.Y * 104729) % 97).ToList();
        var spawner = new Angband.Core.Monsters.MonsterSpawner(game.Data);
        string[] monsters = ["cave_spider", "bullroarer", "kobold_archer", "cave_spider"];
        for (var i = 0; i < monsters.Length && i < spots.Count; i++)
            if (game.Data.Monster(monsters[i]) is { } race) spawner.Place(game.Level, game.Rng, race, spots[i], asleep: i != 1);
        string[] objects = ["cure_light_wounds", "dagger", "phase_door"];
        for (var i = 0; i < objects.Length && monsters.Length + i < spots.Count; i++)
            game.Level.Objects.Add(spots[monsters.Length + i], game.Objects.Create(objects[i]));
        game.UpdateView();
        vm.HandleAction(InputAction.Hold); // a turn passes and the view catches up
    }

    [AvaloniaFact]
    public void Dungeon_Tiles()
    {
        var (window, vm) = Open("warrior", tiles: true);
        Descend(vm);
        vm.ShowMonsterPanel = true;
        vm.ShowObjectPanel = true;
        Shoot(window, "dungeon-tiles");
    }

    [AvaloniaFact]
    public void Dungeon_Ascii()
    {
        var (window, vm) = Open("mage", tiles: false, seed: 7);
        Descend(vm);
        Shoot(window, "dungeon-ascii");
    }

    [AvaloniaFact]
    public void CharacterSheet()
    {
        var (window, vm) = Open("warrior", tiles: true);
        vm.HandleAction(InputAction.CharacterSheet);
        Shoot(window.OwnedWindows.Last(), "character-sheet");
    }

    [AvaloniaFact]
    public void SpellMenu()
    {
        var (window, vm) = Open("mage", tiles: true);
        Descend(vm);
        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Shift); // study...
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);  // ...Magic Missile
        window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.None);  // and cast
        Shoot(window, "cast-a-spell");
    }

    [AvaloniaFact]
    public void Town()
    {
        var (window, _) = Open("ranger", tiles: false);
        Shoot(window, "town-ascii");
        window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Shift); // inspect: pick an item
        Shoot(window, "item-menu");
    }

    [AvaloniaFact]
    public void Store()
    {
        var (window, vm) = Open("warrior", tiles: true, tileset: "adam-bolt");
        var game = vm.Game;
        game.Player.Position = game.Level.AllLocs().First(p => game.Level.FeatureAt(p).Shop == "alchemist");
        game.Player.Gold = 800;
        vm.HandleAction(InputAction.EnterStore);
        Shoot(window, "store");
    }

    [AvaloniaFact]
    public void MonsterList()
    {
        var (window, vm) = Open("warrior", tiles: true);
        Descend(vm);
        vm.HandleAction(InputAction.MonsterList);
        Shoot(window, "monster-list");
    }

    [AvaloniaFact]
    public void Knowledge()
    {
        var (window, vm) = Open("warrior", tiles: true);
        Descend(vm); // meet some monsters first
        vm.ShowKnowledge();
        Shoot(window.OwnedWindows.Last(), "knowledge");
    }

    [AvaloniaFact]
    public void KeyCommands()
    {
        var (window, vm) = Open("warrior", tiles: false);
        vm.HandleAction(InputAction.ShowCommands);
        Shoot(window.OwnedWindows.Last(), "keyboard-commands");
    }

    [AvaloniaFact]
    public void Settings()
    {
        var (window, _) = Open("warrior", tiles: true);
        window.OpenSettings();
        Shoot(window.OwnedWindows.Last(), "settings");
    }

    [AvaloniaFact]
    public void CharacterCreation()
    {
        var (window, _) = Open("warrior", tiles: true);
        window.OpenCharacterCreation();
        Shoot(window.OwnedWindows.Last(), "character-creation");
    }
}
