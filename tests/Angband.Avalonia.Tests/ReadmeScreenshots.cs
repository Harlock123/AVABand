using Avalonia.VisualTree;
using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Definitions;
using Angband.Core.Geometry;
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
/// to <c>screenshots/</c> and runs these. Fixed seeds, and a fixed date on the character dump, keep
/// the pictures the same from run to run.
/// </summary>
public class ReadmeScreenshots
{
    private static readonly string? Folder = Environment.GetEnvironmentVariable("AVABAND_README_SHOTS") is { Length: > 0 } dir ? dir : null;
    private static readonly DateTime ShotTime = new(2025, 1, 1, 12, 0, 0);
    private static readonly IReadOnlyList<TilesetManifest> Tilesets = TilesetCatalog.Discover([TilesetCatalog.DefaultDirectory]);

    private static (MainWindow Window, MainWindowViewModel Vm) Open(string classId, bool tiles, string tileset = "gervais", ulong seed = 42,
        bool scenes = false, double tileScale = 1.5, int width = 1440, int height = 900)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings { UseTiles = tiles, TilesetId = tileset, TileScale = tileScale, Muted = true };
        settings.Options[DisplayOptions.Hints] = false; // no tips over the pictures
        settings.Options[DisplayOptions.Scenes] = scenes; // (only the stair shots are of scenes)
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), Tilesets, settings, save: null)
        {
            Clock = () => ShotTime, // the character dump's date, fixed so the sheet looks the same every run
        };
        vm.StartGame(seed, classId);
        var window = new MainWindow { DataContext = vm, Width = width, Height = height };
        window.Show();
        return (window, vm);
    }

    private static void Shoot(Window window, string name)
    {
        if (Folder is null) return;
        Directory.CreateDirectory(Folder);
        (window.DataContext as MainWindowViewModel)?.Effects.Clear(); // no half-played bolt in the picture
        Dispatcher();
        // Let transitions (the expander's chevron turning) finish: they run on the clock.
        Thread.Sleep(500);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(30);
        Dispatcher();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
        frame.Save(Path.Combine(Folder, name + ".png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static void Dispatcher() => global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    /// <summary>Down into the dungeon, with a few monsters and objects in view of the start.</summary>
    private static void Descend(MainWindowViewModel vm, int depth = 5) =>
        Descend(vm, depth, ["cave_spider", "bullroarer", "kobold_archer", "cave_spider"], ["cure_light_wounds", "dagger", "phase_door"], awake: 1);

    internal static void Descend(MainWindowViewModel vm, int depth, string[] monsters, string[] objects, int awake, int minRoom = 0)
    {
        var game = vm.Game;
        game.MarkDebugUsed(); // the jumps below are debug commands; don't stop to ask
        for (var d = 0; d < depth; d += 5) vm.HandleAction(InputAction.JumpDeeper); // debug: 5 levels at a time
        // A level where the player starts in a lit room well inside the map, so the view is centred on it.
        // (For a busy picture, minRoom asks for a big room round the start; it's lit up below.)
        bool Good() => game.Player.Position is var at && at.X > 35 && at.X < game.Level.Width - 35 && at.Y > 12
                       && at.Y < game.Level.Height - 12 && game.Level[at].Has(SquareFlags.Room)
                       && (minRoom > 0 ? game.Level.AllLocs().Count(l => l.DistanceTo(at) < 9 && game.Level.IsEmptyFloor(l)
                                                                         && game.Level[l].Has(SquareFlags.Room)) >= minRoom
                                       : game.Level[at].Has(SquareFlags.Glow));
        if (minRoom == 0)
            for (var tries = 0; tries < 40 && !Good(); tries++) vm.HandleAction(InputAction.RegenerateLevel);
        if (minRoom > 0)
        {
            // Find the roomiest spot on the level, away from the edges, and put the player there.
            int Roominess(Loc at) => game.Level.AllLocs().Count(l => l.DistanceTo(at) < 9 && game.Level.IsEmptyFloor(l)
                                                                   && game.Level[l].Has(SquareFlags.Room));
            for (var tries = 0; tries < 20; tries++)
            {
                var best = game.Level.AllLocs()
                    .Where(l => l.X > 35 && l.X < game.Level.Width - 35 && l.Y > 12 && l.Y < game.Level.Height - 12
                                && game.Level.IsEmptyFloor(l) && game.Level[l].Has(SquareFlags.Room) && (l.X + l.Y) % 3 == 0)
                    .Select(l => (Loc: l, Score: Roominess(l)))
                    .OrderByDescending(t => t.Score).FirstOrDefault();
                if (best.Score >= minRoom)
                {
                    game.Player.Position = best.Loc;
                    break;
                }
                vm.HandleAction(InputAction.RegenerateLevel);
            }
            // Map the surroundings, as magic mapping would, so the corridors and rooms round about show too.
            foreach (var l in game.Level.AllLocs().Where(l => l.ChebyshevTo(game.Player.Position) <= 30))
            {
                var f = game.Level.FeatureAt(l);
                if (f.Has(TerrainFlags.Passable) || f.HasAny(TerrainFlags.DoorAny | TerrainFlags.Stair | TerrainFlags.Rubble)
                    || game.Level.Neighbors(l).Any(game.Level.IsPassable))
                    game.Known.Remember(game.Level, l);
            }

            // Light the whole room, as a Scroll of Light would, so all of it (and everyone in it) shows.
            var start = game.Player.Position;
            var queue = new Queue<Loc>([start]);
            var seen = new HashSet<Loc> { start };
            while (queue.Count > 0)
            {
                var q = queue.Dequeue();
                game.Level[q].Flags |= SquareFlags.Glow;
                if (!game.Level.IsPassable(q) && q != start) continue;
                foreach (var n in game.Level.Neighbors(q))
                    if (game.Level[n].Has(SquareFlags.Room) && seen.Add(n)) queue.Enqueue(n);
            }
            game.UpdateView();
        }
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        var p = game.Player.Position;
        var spots = game.Level.AllLocs()
            .Where(l => l.DistanceTo(p) is > 2 && l.DistanceTo(p) < (minRoom > 0 ? 9 : 7) && game.Level.IsEmptyFloor(l) && game.Level[l].Has(SquareFlags.Seen))
            .OrderBy(l => (l.X * 7919 + l.Y * 104729) % 97).ToList();
        var spawner = new Angband.Core.Monsters.MonsterSpawner(game.Data);
        for (var i = 0; i < monsters.Length && i < spots.Count; i++)
            if (game.Data.Monster(monsters[i]) is { } race) spawner.Place(game.Level, game.Rng, race, spots[i], asleep: i >= awake);
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

    /// <summary>
    /// The picture for the portfolio site's card: 16:9, so the site has nothing to crop, with bigger tiles
    /// and a busier room: an orc war party and its shaman, a cave bear and a pair of wolves, loot on the
    /// floor, and the monster and object lists alongside.
    /// </summary>
    [AvaloniaFact]
    public void Portfolio_Card()
    {
        var (window, vm) = Open("warrior", tiles: true, seed: 11, tileScale: 2.25, width: 1600, height: 900);
        Descend(vm, 10,
            ["grishnakh_the_hill_orc", "cave_orc", "orc_shaman", "cave_orc", "snaga", "cave_bear", "wolf", "wolf", "giant_spider"],
            ["cure_light_wounds", "long_sword", "phase_door", "wand_of_magic_missile", "flask_of_oil"], awake: 5, minRoom: 70);
        vm.ShowMonsterPanel = true;
        vm.ShowObjectPanel = true;
        Shoot(window, "portfolio-card");
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
        // Bracers with a ruby set, to show the arms slot and its sockets.
        var game = vm.Game;
        var bracers = game.Objects.Create("iron_bracers");
        game.Knowledge.LearnKind(bracers.Kind);
        game.Player.Inventory.Add(bracers);
        game.Execute(new WieldCommand(bracers));
        var ruby = game.Objects.Create("ruby");
        game.Knowledge.LearnKind(ruby.Kind);
        game.Execute(new SetGemCommand(game.Player.Inventory.Equipped.First(i => i.Kind.Id == "iron_bracers"), game.Player.Inventory.Add(ruby)!));
        vm.HandleAction(InputAction.CharacterSheet);
        Shoot(window.OwnedWindows.Last(), "character-sheet");
    }

    /// <summary>The quest satchel holding the Alchemist's Bandolier, and the potion belt it gives above it.</summary>
    [AvaloniaFact]
    public void QuestSatchel()
    {
        var (window, vm) = Open("warrior", tiles: true);
        var game = vm.Game;
        var inv = game.Player.Inventory;
        void Carry(string kind, int number = 1)
        {
            var item = game.Objects.Create(kind, number);
            game.Knowledge.LearnKind(item.Kind);
            inv.Add(item);
        }
        Carry(Angband.Core.Items.Inventory.BeltKind); // (the Frozen Cellar's reward)
        Carry("cure_serious_wounds", 3);
        Carry("speed");
        Carry("potion_of_heroism", 2);
        Carry("potion_of_resist_poison");
        vm.HandleAction(InputAction.Hold); // a turn passes and the panel catches up
        Shoot(window, "quest-satchel");
    }

    /// <summary>
    /// The Load dialog importing a save from another computer: three characters here, and a newer copy of
    /// one of them chosen, so it asks before replacing. (A temporary folder; the save times are fixed.)
    /// </summary>
    [AvaloniaFact]
    public void ImportSave()
    {
        var (window, vm) = Open("warrior", tiles: true);
        var data = vm.Game.Data;
        var root = Directory.CreateTempSubdirectory("avaband-readme-import-").FullName;
        try
        {
            var here = new SaveStore(Path.Combine(root, "saves"));
            var elsewhere = new SaveStore(Path.Combine(root, "elsewhere"));
            string Save(SaveStore store, string name, string cls, ulong seed, int level, int depth, int maxDepth, long turn, DateTime savedUtc)
            {
                var game = GameSession.NewGame(data, seed, cls);
                game.Player.Name = name;
                if (level > 1) game.GainExperience(game.ExperienceForLevel(level - 1));
                (game.Player.Depth, game.Player.MaxDepth) = (depth, maxDepth);
                game.Scheduler.SetGameTurn(turn);
                var path = store.Save(game);
                SetSavedAt(path, savedUtc);
                return path;
            }
            Save(here, "Brego", "warrior", 11, 18, 0, 14, 412_000, new DateTime(2025, 1, 1, 21, 15, 0, DateTimeKind.Utc));
            Save(here, "Elwing", "mage", 12, 9, 6, 7, 151_000, new DateTime(2024, 12, 30, 19, 40, 0, DateTimeKind.Utc));
            Save(here, "Haldor", "ranger", 13, 24, 0, 22, 690_000, new DateTime(2024, 12, 28, 17, 5, 0, DateTimeKind.Utc));
            // Brego, played on at the other computer: deeper, and newer.
            var copy = Save(elsewhere, "Brego", "warrior", 11, 21, 0, 18, 508_000, new DateTime(2025, 1, 3, 18, 30, 0, DateTimeKind.Utc));

            var dialog = new LoadGameViewModel(here, data);
            var load = new LoadGameWindow { DataContext = dialog };
            load.Show(window);
            dialog.BeginImport(copy);
            Shoot(load, "import-save");
            load.Close();
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>Restamps a save's "saved at" (it's written with the real time), so the picture is the same every run.</summary>
    private static void SetSavedAt(string path, DateTime savedUtc)
    {
        System.Text.Json.Nodes.JsonNode file;
        using (var read = File.OpenRead(path))
        using (var unzip = new System.IO.Compression.GZipStream(read, System.IO.Compression.CompressionMode.Decompress))
            file = System.Text.Json.Nodes.JsonNode.Parse(unzip)!;
        file["Summary"]!["SavedAtUtc"] = savedUtc;
        using var write = File.Create(path);
        using var zip = new System.IO.Compression.GZipStream(write, System.IO.Compression.CompressionLevel.Fastest);
        using var writer = new System.Text.Json.Utf8JsonWriter(zip);
        file.WriteTo(writer);
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

    /// <summary>The town in tiles, by night: the shopfronts' colours, wares, awnings, lit windows and lanterns.</summary>
    [AvaloniaFact]
    public void TownAtNight()
    {
        var (window, vm) = Open("ranger", tiles: true);
        vm.Game.Scheduler.SetGameTurn(vm.Game.Data.Constants.DayLength * 3 / 4);
        vm.Execute(new HoldCommand()); // (a turn, so the status line says it's night)
        Shoot(window, "town-night-tiles");
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

    /// <summary>A stair scene, caught at a fixed moment (its own clock stopped, so every run matches).</summary>
    private static void StairShot(bool down, int fromDepth, string name)
    {
        var (window, vm) = Open("warrior", tiles: true, scenes: true);
        var game = vm.Game;
        game.MarkDebugUsed();
        if (fromDepth > 0) vm.Execute(new Angband.Core.Game.DebugJumpCommand(fromDepth));
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        game.Player.Position = game.Level.FindFeature(down ? Angband.Core.Definitions.TerrainFlags.DownStair
            : Angband.Core.Definitions.TerrainFlags.UpStair).First();
        vm.SkipScenes(); // (whatever arriving there brought up)
        vm.Execute(new Angband.Core.Game.TakeStairsCommand(Down: down));
        var scene = window.GetVisualDescendants().OfType<Angband.Avalonia.Controls.SceneView>().Single();
        scene.Advance(700);
        if (Folder is null) return;
        Directory.CreateDirectory(Folder);
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
        frame.Save(Path.Combine(Folder, name + ".png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    [AvaloniaFact]
    public void StairsDown() => StairShot(down: true, fromDepth: 4, "stairs-down");

    [AvaloniaFact]
    public void StairsUp() => StairShot(down: false, fromDepth: 6, "stairs-up");

    [AvaloniaFact]
    public void StairsUpToTown() => StairShot(down: false, fromDepth: 1, "stairs-up-town");

    [AvaloniaFact]
    public void Journey()
    {
        var (window, vm) = Open("warrior", tiles: true, seed: 11);
        var game = vm.Game;
        game.MarkDebugUsed();
        foreach (var depth in new[] { 1, 2, 4, 3, 5, 7, 6, 9, 12, 10, 14 })
        {
            vm.Execute(new Angband.Core.Game.DebugJumpCommand(depth));
            for (var i = 0; i < 60; i++) vm.Execute(new Angband.Core.Game.HoldCommand());
        }
        vm.ShowJourney();
        var journey = window.OwnedWindows.OfType<JourneyWindow>().Last();
        Shoot(journey, "journey");
        journey.Close();
    }
}
