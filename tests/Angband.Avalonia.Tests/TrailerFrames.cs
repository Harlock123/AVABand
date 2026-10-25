using Angband.Avalonia.Controls;
using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Data;
using Angband.Data.Tiles;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

/// <summary>
/// The frames of the promotional trailer, rendered headlessly from the real windows (no desktop is
/// captured), as the README's screenshots are. Nothing is written unless AVABAND_TRAILER_FRAMES names a
/// folder; <c>tools/trailer.sh</c> sets it, runs these, and cuts the shots together with title cards.
/// Each shot is a folder of PNGs and an ffconcat list saying how long each is shown (at 30 frames a
/// second), so a held frame is written once. Fixed seeds; the map's effects and the stair scene are
/// stepped a frame at a time (their own clocks off), so a rerun gives the same frames.
/// </summary>
public class TrailerFrames
{
    private static readonly string? Folder = Environment.GetEnvironmentVariable("AVABAND_TRAILER_FRAMES") is { Length: > 0 } dir ? dir : null;
    private static readonly IReadOnlyList<TilesetManifest> Tilesets = TilesetCatalog.Discover([TilesetCatalog.DefaultDirectory]);
    private const int Fps = 30;
    private const double FrameMs = 1000.0 / Fps;

    /// <summary>One shot: frames written as they are rendered, each shown for a number of video frames.</summary>
    private sealed class Reel : IDisposable
    {
        private readonly string _dir;
        private readonly List<(string File, int Frames)> _frames = [];

        public Reel(string name, Window window)
        {
            _dir = Path.Combine(Folder!, name);
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            Directory.CreateDirectory(_dir);
            Window = window;
        }

        /// <summary>The window being filmed (a dialog's, for a while).</summary>
        public Window Window { get; set; }

        public double Seconds => _frames.Sum(f => f.Frames) / (double)Fps;

        /// <summary>Renders the window and shows it for <paramref name="frames"/> video frames.</summary>
        public void Shot(int frames = 1)
        {
            Pump();
            var frame = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
            var file = $"f{_frames.Count:D5}.png";
            frame.Save(Path.Combine(_dir, file), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            _frames.Add((file, frames));
        }

        /// <summary>Keeps the last frame up a while longer.</summary>
        public void Hold(double seconds)
        {
            var (file, frames) = _frames[^1];
            _frames[^1] = (file, frames + (int)Math.Round(seconds * Fps));
        }

        public void Dispose()
        {
            // The concat demuxer wants the last file named again (its duration would otherwise be ignored).
            var lines = new List<string> { "ffconcat version 1.0" };
            foreach (var (file, frames) in _frames)
            {
                lines.Add($"file '{file}'");
                lines.Add(FormattableString.Invariant($"duration {frames / (double)Fps:0.######}"));
            }
            lines.Add($"file '{_frames[^1].File}'");
            File.WriteAllLines(Path.Combine(_dir, "list.ffconcat"), lines);
        }
    }

    private static void Pump()
    {
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// A game in the real main window, muted, without tips, the hotbar or replays on disk. The game's
    /// own shots are rendered at 1440x810 and scaled up to 1080p by the script, so the text reads in a
    /// video; the title and the stair scene, being pictures, at 1080p.
    /// </summary>
    private static (MainWindow Window, MainWindowViewModel Vm) Open(string classId, ulong seed, string tileset = "gervais",
        bool tiles = true, double tileScale = 1.5, bool scenes = false, double interfaceScale = 1, int width = 1440, int height = 810,
        Action<MainWindowViewModel>? before = null)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings
        {
            UseTiles = tiles, TilesetId = tileset, TileScale = tileScale, Muted = true, ShowHotbar = false, FullScreen = false,
            InterfaceScale = interfaceScale,
        };
        settings.Options[DisplayOptions.Hints] = false;
        settings.Options[DisplayOptions.Scenes] = scenes;
        settings.Options[DisplayOptions.CenterPlayer] = true; // the action in the middle of the picture
        settings.Options[DisplayOptions.Autosave] = false;
        settings.Options[DisplayOptions.RecordReplays] = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), Tilesets, settings, save: null)
        {
            Clock = () => new DateTime(2025, 1, 1, 12, 0, 0),
        };
        before?.Invoke(vm);
        vm.StartGame(seed, classId);
        var window = new MainWindow { DataContext = vm, Width = width, Height = height };
        window.Show();
        Map(window).RunsByItself = false;
        return (window, vm);
    }

    private static MapView Map(Window window) => window.GetVisualDescendants().OfType<MapView>().First();

    /// <summary>Plays out whatever the map is animating, a video frame at a time (slowed by <paramref name="slow"/>).</summary>
    private static void Play(Reel reel, MainWindowViewModel vm, double slow = 1)
    {
        var map = Map(reel.Window);
        for (var i = 0; i < 900 && vm.Effects.IsActive; i++)
        {
            vm.Effects.Advance(FrameMs / slow);
            map.InvalidateVisual();
            reel.Shot();
        }
    }

    /// <summary>A command, its animations, and a beat to take it in.</summary>
    private static void Act(Reel reel, MainWindowViewModel vm, GameCommand command, int beat = 4, double slow = 1)
    {
        vm.Execute(command);
        Play(reel, vm, slow);
        reel.Shot(beat);
    }

    private static Angband.Core.Items.Item Give(GameSession game, string kind, int charges = 0)
    {
        var item = game.Objects.Create(kind);
        game.Knowledge.LearnKind(item.Kind);
        if (charges > 0) item.Charges = charges;
        return game.Player.Inventory.Add(item)!;
    }

    private static Monster Place(GameSession game, string race, Loc at, bool asleep = false)
    {
        var m = new MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster(race)!, at, asleep);
        game.Scheduler.Add(m);
        return m;
    }

    /// <summary>The empty floor square in view nearest to (dx, dy) from the player.</summary>
    private static Loc Near(GameSession game, int dx, int dy)
    {
        var want = game.Player.Position + new Loc(dx, dy);
        return game.Level.AllLocs().Where(l => game.Level.IsEmptyFloor(l) && game.Level[l].Has(Angband.Core.World.SquareFlags.View)
                                                && l != game.Player.Position)
            .OrderBy(l => l.DistanceTo(want)).ThenBy(l => l.Y).ThenBy(l => l.X).First();
    }

    /// <summary>Blank message lines (the welcome's seed, the debug jumps), as if the game had just begun here.</summary>
    private static void Quiet(MainWindowViewModel vm)
    {
        foreach (var blank in new[] { " ", "  ", "   " }) vm.AddMessage(blank);
    }

    private static Direction Toward(Loc from, Loc to) =>
        DirectionExtensions.FromOffset(Math.Sign(to.X - from.X), Math.Sign(to.Y - from.Y));

    private static void Level(GameSession game, int level)
    {
        game.GainExperience(game.ExperienceForLevel(level - 1));
        game.Player.Hp = game.Player.MaxHp;
        game.Player.Mana = game.Player.MaxMana;
    }

    // --- the shots -------------------------------------------------------------------------------

    /// <summary>The title screen: Thangorodrim over the gates of Angband.</summary>
    [AvaloniaFact]
    public void Shot01_Title()
    {
        if (Folder is null) return;
        var temp = Directory.CreateTempSubdirectory("avaband-trailer-").FullName;
        try
        {
            var (window, vm) = Open("warrior", 42, width: 1920, height: 1080, before: v =>
            {
                v.UseRecords(new RecordStore(Path.Combine(temp, "records")));
                v.UseSaves(new SaveStore(Path.Combine(temp, "saves")), resume: false);
            });
            vm.ShowTitle();
            using var reel = new Reel("title", window);
            reel.Shot(Fps * 4);
        }
        finally { Directory.Delete(temp, true); }
    }

    /// <summary>Character creation, trying on a few races and classes.</summary>
    [AvaloniaFact]
    public void Shot02_Creation()
    {
        if (Folder is null) return;
        var (window, _) = Open("warrior", 42, interfaceScale: 1.5);
        var dialog = window.OpenCharacterCreation();
        var create = (CharacterCreationViewModel)dialog.DataContext!;
        using var reel = new Reel("creation", dialog);
        foreach (var (race, cls, name) in new[] { ("dwarf", "warrior", "Borin"), ("high_elf", "mage", "Elwing"), ("hobbit", "rogue", "Bramble") })
        {
            create.SelectedRace = create.Races.FirstOrDefault(r => r.Id == race) ?? create.SelectedRace;
            create.SelectedClass = create.Classes.FirstOrDefault(c => c.Id == cls) ?? create.SelectedClass;
            create.Name = name;
            reel.Shot(Fps * 6 / 5);
        }
    }

    /// <summary>The town by day: a merchant caravan in the square, then its wares.</summary>
    [AvaloniaFact]
    public void Shot03_Caravan()
    {
        if (Folder is null) return;
        var (window, vm) = Open("warrior", 11);
        var game = vm.Game;
        game.MarkDebugUsed();
        Level(game, 15);
        for (var i = 0; i < 60 && game.AvaQuests.Caravan is null; i++)
        {
            vm.Execute(new DebugJumpCommand(5));
            vm.Execute(new DebugJumpCommand(0));
        }
        Assert.NotNull(game.AvaQuests.Caravan);
        game.Scheduler.SetGameTurn(game.Data.Constants.DayLength / 4); // morning
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        game.Player.Gold = 25_000;
        var wagon = new Loc(game.AvaQuests.Caravan!.X, game.AvaQuests.Caravan.Y);
        // Start a few steps off and walk up to the wagon.
        var start = game.Level.AllLocs().Where(l => game.Level.IsEmptyFloor(l) && l.ChebyshevTo(wagon) == 5)
            .OrderBy(l => Math.Abs(l.Y - wagon.Y)).ThenBy(l => l.X).First();
        game.Player.Position = start;
        game.UpdateView();
        vm.Execute(new HoldCommand());
        Quiet(vm);
        using var reel = new Reel("caravan", window);
        reel.Shot(Fps * 4 / 5);
        for (var i = 0; i < 8 && !vm.IsPrompting; i++)
        {
            var at = game.Player.Position;
            vm.Execute(new WalkCommand(Toward(at, wagon)));
            reel.Shot(6);
        }
        reel.Hold(2.2);
    }

    /// <summary>The Prancing Pony: swords for hire.</summary>
    [AvaloniaFact]
    public void Shot04_Inn()
    {
        if (Folder is null) return;
        var (window, vm) = Open("warrior", 42);
        var game = vm.Game;
        Level(game, 12);
        game.Player.Gold = 4000;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        var door = game.Level.AllLocs().First(l => game.Level.FeatureAt(l).Shop == "inn");
        var from = game.Level.AllLocs().First(l => l.ChebyshevTo(door) == 1 && game.Level.IsEmptyFloor(l));
        game.Player.Position = from;
        game.UpdateView();
        vm.Execute(new HoldCommand()); // (the level-ups' feat said)
        Quiet(vm);
        vm.Execute(new WalkCommand(Toward(from, door)));
        using var reel = new Reel("inn", window);
        reel.Shot(Fps / 2);
        vm.PromptKey(vm.ChoiceRows.First(r => r.Text.StartsWith("Hire a sword", StringComparison.Ordinal)).Letter[0]);
        reel.Shot(Fps * 2);
    }

    /// <summary>Down the stairs: the scene, played at half speed and held as it would wait for a key.</summary>
    [AvaloniaFact]
    public void Shot05_Stairs()
    {
        if (Folder is null) return;
        var (window, vm) = Open("warrior", 42, scenes: true, width: 1920, height: 1080);
        var game = vm.Game;
        game.MarkDebugUsed();
        vm.Execute(new DebugJumpCommand(4));
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        game.Player.Position = game.Level.FindFeature(Angband.Core.Definitions.TerrainFlags.DownStair).First();
        vm.SkipScenes();
        var scene = window.GetVisualDescendants().OfType<SceneView>().Single();
        scene.RunsByItself = false;
        vm.Execute(new TakeStairsCommand(Down: true));
        using var reel = new Reel("stairs", window);
        for (var t = 0.0; t < SceneView.DurationMs * 0.95 && vm.HasScene; t += FrameMs / 2)
        {
            scene.Advance(FrameMs / 2);
            reel.Shot();
        }
    }

    /// <summary>A sword for hire fighting at your side in a lit room.</summary>
    [AvaloniaFact]
    public void Shot06_Hireling()
    {
        if (Folder is null) return;
        var (window, vm) = Open("warrior", 11);
        var game = vm.Game;
        Level(game, 14);
        ReadmeScreenshots.Descend(vm, 10, [], [], 0, minRoom: 70);
        game.Join(new Hireling { Name = "Frerin", Look = "dwarf", Role = HirelingRole.Warrior, Level = 9, Share = 12, Fee = 700 });
        var foes = new[] { "cave_orc", "snaga", "cave_orc", "snaga" };
        foreach (var (race, i) in foes.Select((r, i) => (r, i))) Place(game, race, Near(game, 6 + i % 2, -2 + i));
        game.UpdateView();
        vm.Execute(new HoldCommand());
        Quiet(vm);
        using var reel = new Reel("hireling", window);
        reel.Shot(Fps / 2);
        for (var turn = 0; turn < 5; turn++)
        {
            var foe = game.Level.Monsters.All.Where(m => !m.IsAlly).OrderBy(m => m.Position.DistanceTo(game.Player.Position)).FirstOrDefault();
            if (foe is null) break;
            Act(reel, vm, new WalkCommand(Toward(game.Player.Position, foe.Position)), beat: 5);
        }
        reel.Hold(0.5);
    }

    /// <summary>Devices: a ball into the pack, a bolt at its chief (its health bar falls), then dragon's flame.</summary>
    [AvaloniaFact]
    public void Shot07_Magic()
    {
        if (Folder is null) return;
        var (window, vm) = Open("mage", 7);
        var game = vm.Game;
        Level(game, 25);
        ReadmeScreenshots.Descend(vm, 15, [], [], 0, minRoom: 70);
        var bolts = Give(game, "wand_of_lightning_bolts", 20);
        var balls = Give(game, "wand_of_fire_balls", 20);
        var flame = Give(game, "wand_of_dragons_flame", 20);
        var chief = Place(game, "lugdush_the_uruk", Near(game, 6, 0));
        chief.MaxHp = chief.Hp = 420; // (sturdier than his kind, so the bar has somewhere to fall)
        foreach (var (dx, dy) in new[] { (7, -2), (7, 2), (8, -1), (8, 1) }) Place(game, "cave_orc", Near(game, dx, dy));
        game.UpdateView();
        vm.Execute(new HoldCommand());
        Quiet(vm);
        game.SetTarget(chief);
        vm.Refresh();
        using var reel = new Reel("magic", window);
        reel.Shot(Fps / 2);
        Act(reel, vm, new UseCommand(balls, chief.Position + new Loc(1, 0)), beat: 8, slow: 1.3);
        if (game.Level.Monsters.All.Contains(chief)) Act(reel, vm, new UseCommand(bolts, chief.Position), beat: 8, slow: 1.3);
        if (game.Level.Monsters.All.Contains(chief)) Act(reel, vm, new UseCommand(flame, chief.Position), beat: 8, slow: 1.3);
        reel.Hold(0.5);
    }

    /// <summary>A fallen character's ghost: it wails of what killed it, and is laid to rest, letting fall what it wore.</summary>
    [AvaloniaFact]
    public void Shot08_Ghost()
    {
        if (Folder is null) return;
        var (window, vm) = Open("warrior", 11);
        var game = vm.Game;
        Level(game, 22);
        ReadmeScreenshots.Descend(vm, 10, [], [], 0, minRoom: 70);
        // A headstone (a warrior, so no spells to blink it away), keyed so its ghost says the line we want.
        var fallen = Enumerable.Range(0, 50).Select(i => new FallenGhost
        {
            Key = $"trailer-{i}", Name = "Bramble", Race = "Hobbit", Class = "Warrior", Level = 12, Depth = game.Level.Depth,
            KilledBy = "Grip, Farmer Maggot's Dog", DateUtc = new DateTime(2024, 12, 24),
        }).First(f => FallenGhosts.Wail(f).Contains("ended me here", StringComparison.Ordinal));
        game.UseGraveyard([fallen]);
        var race = game.MonsterRace(FallenGhosts.RacePrefix + fallen.Key)!;
        var wand = Give(game, "wand_of_lightning_bolts", 20);
        Quiet(vm);
        vm.AddMessage("A chill runs through you. Someone who fell here does not rest."); // (as the level it walks on is made)
        using var reel = new Reel("ghost", window);
        reel.Shot(Fps * 2 / 3);
        var ghost = new MonsterSpawner(game.Data).Place(game.Level, game.Rng, race, Near(game, 5, 0), asleep: false);
        game.Scheduler.Add(ghost);
        game.Ghosts.Met.Add(fallen.Key);
        game.UpdateView(); // seen: it wails
        game.SetTarget(ghost);
        vm.Refresh();
        reel.Shot(Fps * 9 / 5);
        ghost.Hp = ghost.MaxHp / 2 + 30; // (two bolts' worth)
        Act(reel, vm, new UseCommand(wand, ghost.Position), beat: 8, slow: 1.3);
        if (game.Level.Monsters.All.Contains(ghost))
        {
            ghost.Hp = Math.Min(ghost.Hp, 5);
            Act(reel, vm, new UseCommand(wand, ghost.Position), beat: 8, slow: 1.3);
        }
        reel.Hold(1.5);
    }

    /// <summary>The same room in each tile set, then in letters.</summary>
    [AvaloniaFact]
    public void Shot09_Tilesets()
    {
        if (Folder is null) return;
        var (window, vm) = Open("warrior", 11);
        var game = vm.Game;
        Level(game, 16);
        ReadmeScreenshots.Descend(vm, 10,
            ["grishnakh_the_hill_orc", "cave_orc", "orc_shaman", "cave_orc", "snaga", "cave_bear", "wolf", "wolf", "giant_spider"],
            ["cure_light_wounds", "long_sword", "phase_door", "wand_of_magic_missile", "flask_of_oil"], awake: 0, minRoom: 70);
        Quiet(vm);
        string[] order = ["gervais", "dcss", "tangaria", "rltiles", "adam-bolt", "dawnlike", "hexany", "angband-old", "angband-nomad"];
        var names = new List<string>();
        foreach (var id in order)
        {
            var set = Tilesets.First(t => t.Id == id);
            vm.UseTiles = true;
            vm.SelectedTileset = set;
            vm.TileScale = set.TileHeight >= 32 ? 1.5 : set.TileHeight >= 16 ? 3 : 4; // (about 48 pixels a square in each)
            using var reel = new Reel($"tiles-{id}", window);
            reel.Shot(Fps / 2);
            names.Add($"tiles-{id}\t{set.Name}");
        }
        vm.UseTiles = false;
        using (var reel = new Reel("tiles-ascii", window)) reel.Shot(Fps * 6 / 5);
        names.Add("tiles-ascii\tClassic letters");
        File.WriteAllLines(Path.Combine(Folder, "tilesets.tsv"), names);
    }
}
