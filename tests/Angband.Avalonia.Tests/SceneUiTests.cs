using Angband.Avalonia.Controls;
using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

/// <summary>Scenes at moments of note: stairs, recall, level types, danger, a first unique, death.</summary>
public sealed class SceneUiTests : IDisposable
{
    private readonly string _art = Path.Combine(Path.GetTempPath(), "avaband-art-" + Guid.NewGuid());
    private readonly string _bundled = Path.Combine(Path.GetTempPath(), "avaband-bundled-" + Guid.NewGuid());

    public void Dispose()
    {
        foreach (var dir in new[] { _art, _bundled })
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    private (MainWindow Window, MainWindowViewModel Vm, SceneView Scene) Open(bool scenes = true, bool pictures = true, bool wait = false)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings();
        settings.Options[DisplayOptions.Hints] = false;
        if (!scenes) settings.Options[DisplayOptions.Scenes] = false;
        if (!pictures) settings.Options[DisplayOptions.ScenePictures] = false;
        if (wait) settings.Options[DisplayOptions.ScenesWait] = true;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null)
        {
            ArtDirectory = _art,
            BundledArtDirectory = _bundled, // (empty: the painted scenes, whatever ships with the game)
        };
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var scene = window.GetVisualDescendants().OfType<SceneView>().Single();
        // Held still: the tests move scenes on themselves (Advance), and on a slow machine a scene's own
        // clock could end it between two lines of a test.
        scene.RunsByItself = false;
        return (window, vm, scene);
    }

    private static void TakeStairs(MainWindowViewModel vm, bool down)
    {
        var game = vm.Game;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        game.Player.Position = game.Level.FindFeature(down ? TerrainFlags.DownStair : TerrainFlags.UpStair).First();
        vm.Execute(new TakeStairsCommand(Down: down));
    }

    private static void Shot(MainWindow window, SceneView scene, string name)
    {
        scene.Advance(scene.Scene is { } s ? SceneView.DurationFor(s.Kind) * 0.45 : 700);
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, name);
    }

    [AvaloniaFact]
    public void GoingDown_ThenUp_ShowsEachItsScene_ForAMoment()
    {
        var (window, vm, scene) = Open();
        TakeStairs(vm, down: true);
        var down = Assert.IsType<AmbientScene>(vm.Scene);
        Assert.Equal(SceneKind.StairsDown, down.Kind);
        Assert.Equal("Descending… 50 ft (level 1)", down.Caption);
        Assert.Null(down.Picture);
        Shot(window, scene, "scene-stairs-down");
        vm.SkipScenes();

        vm.Game.MarkDebugUsed();
        vm.Execute(new DebugJumpCommand(3)); // a jump is not the stairs
        Assert.False(vm.Scene?.Kind is SceneKind.StairsDown or SceneKind.StairsUp);
        vm.SkipScenes();
        TakeStairs(vm, down: false);
        Assert.Equal(SceneKind.StairsUp, vm.Scene!.Kind);
        Assert.Equal("Climbing… 100 ft (level 2)", vm.Scene.Caption);
        Shot(window, scene, "scene-stairs-up");
        scene.Advance(SceneView.DurationMs * 2); // played through
        Assert.True(vm.Scene is null || vm.Scene.Kind != SceneKind.StairsUp);
    }

    [AvaloniaFact]
    public void UpIntoTheTown_SaysSo_AndAKeyEndsTheScenes_AndDoesNothingMore()
    {
        var (window, vm, _) = Open();
        long now = 1000;
        vm.SceneClock = () => now;
        TakeStairs(vm, down: true);
        vm.SkipScenes();
        TakeStairs(vm, down: false);
        Assert.StartsWith("Up into the town, by ", vm.Scene!.Caption);
        var turn = vm.Game.GameTurn;

        // Keys typed ahead (arriving with the scene, while the turn was worked out) are dropped: the scene stays.
        window.KeyPressQwerty(PhysicalKey.Digit5, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Digit5, RawInputModifiers.None);
        Assert.NotNull(vm.Scene);
        Assert.Equal(turn, vm.Game.GameTurn);

        // A key once it's up ends it, as Escape would — and goes no further.
        now += MainWindowViewModel.SceneTypeAheadMs;
        window.KeyPressQwerty(PhysicalKey.Digit5, RawInputModifiers.None);
        Assert.Null(vm.Scene);
        Assert.Equal(turn, vm.Game.GameTurn);
        // The keys right behind it are dropped too, and the key that ended it, held, until it's let go.
        window.KeyPressQwerty(PhysicalKey.Digit4, RawInputModifiers.None);
        now += MainWindowViewModel.SceneTypeAheadMs;
        window.KeyPress(Key.D5, RawInputModifiers.None, PhysicalKey.Digit5, "5"); // (auto-repeat: down, no up)
        Assert.Equal(turn, vm.Game.GameTurn);
        window.KeyRelease(Key.D5, RawInputModifiers.None, PhysicalKey.Digit5, "5");
        window.KeyPressQwerty(PhysicalKey.Digit5, RawInputModifiers.None); // hold a turn
        Assert.True(vm.Game.GameTurn > turn);

        // A controller button likewise.
        TakeStairs(vm, down: true);
        now += MainWindowViewModel.SceneTypeAheadMs;
        turn = vm.Game.GameTurn;
        window.HandleGamepadAction(InputAction.Hold);
        Assert.Null(vm.Scene);
        Assert.Equal(turn, vm.Game.GameTurn);
    }

    [AvaloniaFact]
    public void Recall_ACavern_ADeadlyFeeling_AFirstUnique_AndDeath_EachHaveAScene()
    {
        var (window, vm, scene) = Open();
        var game = vm.Game;

        // Word of Recall, up to the town.
        game.MarkDebugUsed();
        vm.Execute(new DebugJumpCommand(5));
        vm.SkipScenes();
        game.Player.RecallTimer = 1;
        vm.Execute(new HoldCommand());
        Assert.Equal(0, game.Player.Depth);
        Assert.Equal(SceneKind.RecallUp, vm.Scene!.Kind);
        Shot(window, scene, "scene-recall-up");
        vm.SkipScenes();

        // A first unique: Grip, seen for the first time ever.
        var at = game.Level.AllLocs().First(l => l.DistanceTo(game.Player.Position) == 3 && game.Level.IsEmptyFloor(l)
            && Angband.Core.Combat.ProjectionPath.Projectable(game.Level, game.Player.Position, l, 20));
        new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster("grip")!, at, asleep: true);
        vm.Execute(new HoldCommand());
        Assert.Equal(SceneKind.Unique, vm.Scene!.Kind);
        Assert.StartsWith("Grip", vm.Scene.Caption);
        Assert.Equal("C", vm.Scene.Glyph);
        Shot(window, scene, "scene-unique");
        vm.SkipScenes();
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        vm.Execute(new HoldCommand());
        Assert.Null(vm.Scene); // only the first time

        // Arriving in a cavern (and, maybe, a deadly feeling), in order.
        for (var tries = 0; tries < 200 && game.Level.ProfileId != "cavern"; tries++)
        {
            vm.SkipScenes();
            vm.Execute(new DebugJumpCommand(15));
        }
        Assert.Equal("cavern", game.Level.ProfileId);
        Assert.Equal(SceneKind.Cavern, vm.Scene!.Kind); // (a deadly feeling, if any, comes after it)
        Shot(window, scene, "scene-cavern");
        vm.SkipScenes();

        // Death: the tombstone, behind the game-over menu.
        game.Player.Hp = 1;
        game.TakeHit(1000, "a test");
        vm.Execute(new HoldCommand());
        Assert.True(game.Player.IsDead);
        Assert.Equal(SceneKind.Death, vm.Scene!.Kind);
        Assert.StartsWith("Killed by a test", vm.Scene.Subtitle);
        Shot(window, scene, "scene-death");
    }

    [AvaloniaFact]
    public void TheOption_TurnsScenesOff_AndPicturesComeFromTheArtFolders()
    {
        var (_, off, _) = Open(scenes: false);
        TakeStairs(off, down: true);
        Assert.Null(off.Scene);

        // A bundled picture is used; the player's own replaces it; "no bundled pictures" keeps only the player's.
        var png = Directory.GetFiles(AppContext.BaseDirectory, "*.png", SearchOption.AllDirectories).First();
        Directory.CreateDirectory(_bundled);
        File.Copy(png, Path.Combine(_bundled, "stairs-down.png"));
        var (window, vm, scene) = Open();
        TakeStairs(vm, down: true);
        Assert.Equal(Path.Combine(_bundled, "stairs-down.png"), vm.Scene!.Picture);
        scene.Advance(700);
        window.CaptureRenderedFrame(); // draws the picture
        vm.SkipScenes();

        Directory.CreateDirectory(_art);
        File.Copy(png, Path.Combine(_art, "stairs-up.jpg"));
        TakeStairs(vm, down: false);
        Assert.Equal(Path.Combine(_art, "stairs-up.jpg"), vm.Scene!.Picture); // .jpg too, and the player's own first

        var (_, own, _) = Open(pictures: false);
        TakeStairs(own, down: true);
        Assert.Null(own.Scene!.Picture); // the bundled one is not used: painted
    }

    [AvaloniaFact]
    public void EveryScene_HasItsBundledPicture_AndEachOneDraws()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null)
        {
            ArtDirectory = _art, // (no pictures of the player's own)
        };
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var view = window.GetVisualDescendants().OfType<SceneView>().Single();
        string[] names = ["stairs-down", "stairs-up", "stairs-up-town", "unique", "danger", "death", "deep-descent", "trapdoor", "quest-complete", "boss-slain",
            "level-cavern", "level-labyrinth", "level-fortress", "level-moria", "level-lair", "level-gauntlet"];
        foreach (var name in names)
        {
            var picture = vm.PictureFor(name);
            Assert.NotNull(picture);
            Assert.StartsWith(Path.Combine(AppContext.BaseDirectory, "art"), picture);
            using (var bitmap = new global::Avalonia.Media.Imaging.Bitmap(picture)) Assert.True(bitmap.PixelSize.Width >= 1000);
            vm.Scene = new AmbientScene(SceneKind.StairsDown, name, 1, true, picture);
            Assert.True(view.IsEffectivelyVisible);
            view.Advance(600);
            window.CaptureRenderedFrame();
            if (name == "unique") TileRenderingTests.Save(window, "scene-picture-unique");
        }
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "art", "CREDITS.md")));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "ambience", "CREDITS.md")));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "ambience", "ambient-dungeon-shallow.ogg")));
    }

    /// <summary>
    /// "Scenes stay until you press Space": the scene fades in and holds, other keys wait (and do
    /// nothing), Space moves on; the controller's A does too, and B ends them all. Off by default.
    /// </summary>
    [AvaloniaFact]
    public void WithTheOption_AScene_HoldsUntilSpace()
    {
        Assert.False(DisplayOptions.All.Single(o => o.Id == DisplayOptions.ScenesWait).Default);
        var (window, vm, scene) = Open(wait: true);
        long now = 1000;
        vm.SceneClock = () => now;
        TakeStairs(vm, down: true);
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None); // typed ahead: dropped, the scene holds
        Assert.NotNull(vm.Scene);
        now += MainWindowViewModel.SceneTypeAheadMs;
        Assert.True(vm.Scene!.WaitForKey);
        scene.Advance(SceneView.DurationMs * 3); // long past its usual moment
        Assert.NotNull(vm.Scene);
        Assert.True(scene.IsHolding);
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "scene-holding");

        var turn = vm.Game.GameTurn;
        window.KeyPressQwerty(PhysicalKey.Digit5, RawInputModifiers.None); // not Space: nothing happens
        Assert.NotNull(vm.Scene);
        Assert.Equal(turn, vm.Game.GameTurn);
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Assert.Null(vm.Scene);

        // The controller: A moves on, B ends them all.
        TakeStairs(vm, down: false);
        Assert.NotNull(vm.Scene);
        vm.HandleAction(InputAction.Confirm);
        Assert.Null(vm.Scene);
        TakeStairs(vm, down: true);
        vm.HandleAction(InputAction.Cancel);
        Assert.Null(vm.Scene);

        // A scene still holding when a new game starts goes with the old one.
        TakeStairs(vm, down: false);
        Assert.NotNull(vm.Scene);
        vm.StartGame(43, "warrior");
        Assert.Null(vm.Scene);
    }

    [AvaloniaFact]
    public void WithoutTheOption_AScene_StillPassesByItself()
    {
        var (_, vm, scene) = Open();
        TakeStairs(vm, down: true);
        Assert.False(vm.Scene!.WaitForKey);
        scene.Advance(SceneView.DurationMs * 2);
        Assert.Null(vm.Scene);
    }

    /// <summary>
    /// Word of Recall's cutscene: read in the dungeon, a rent torn in a dungeon room; in town, in the
    /// town square (by day or night). A hand reaches out of it, grows and closes; the scene holds,
    /// with the option, before it closes. The player's own picture replaces it.
    /// </summary>
    [AvaloniaFact]
    public void WordOfRecall_TearsARent_AndAHandReachesOut()
    {
        var (window, vm, scene) = Open();
        vm.BundledArtDirectory = Path.Combine(AppContext.BaseDirectory, "art");
        var game = vm.Game;
        game.MarkDebugUsed();
        vm.Execute(new DebugJumpCommand(5));
        vm.SkipScenes();

        game.Player.RecallTimer = 1;
        vm.Execute(new HoldCommand());
        Assert.Equal(0, game.Player.Depth);
        var up = vm.Scene!;
        Assert.Equal(SceneKind.RecallUp, up.Kind);
        Assert.EndsWith("recall-portal-room.jpg", up.Picture);
        Assert.Equal(vm.BundledArtDirectory, up.PortalArt);
        foreach (var name in new[] { "recall-portal-rim.png", "recall-hand-open.png", "recall-hand-grasp.png" })
            Assert.True(File.Exists(Path.Combine(vm.BundledArtDirectory, name)), name);
        var duration = SceneView.DurationFor(SceneKind.RecallUp);
        foreach (var (at, name) in new[] { (0.12, "tear"), (0.45, "reach"), (0.6, "hand"), (0.72, "grasp") })
        {
            scene.Advance(duration * at - scene.Elapsed);
            window.CaptureRenderedFrame();
            TileRenderingTests.Save(window, "scene-recall-" + name);
        }
        vm.SkipScenes();

        // Read in town: the town square, by day or by night.
        game.Player.RecallDepth = 5;
        game.Player.RecallTimer = 1;
        for (var i = 0; i < 20 && game.Player.Depth == 0; i++) vm.Execute(new HoldCommand());
        Assert.True(game.Player.Depth > 0);
        var down = vm.Scene!;
        Assert.Equal(SceneKind.RecallDown, down.Kind);
        Assert.EndsWith(down.Day ? "recall-portal-square-day.jpg" : "recall-portal-square-night.jpg", down.Picture);
        scene.Advance(SceneView.DurationFor(SceneKind.RecallDown) * 0.5);
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "scene-recall-town-square");
        vm.SkipScenes();

        // The player's own picture wins, and is shown plainly.
        Directory.CreateDirectory(_art);
        var png = Directory.GetFiles(AppContext.BaseDirectory, "*.png", SearchOption.AllDirectories).First();
        File.Copy(png, Path.Combine(_art, "recall-town.png"));
        vm.Execute(new DebugJumpCommand(5));
        vm.SkipScenes();
        game.Player.RecallTimer = 1;
        for (var i = 0; i < 20 && game.Player.Depth > 0; i++) vm.Execute(new HoldCommand());
        Assert.Equal(Path.Combine(_art, "recall-town.png"), vm.Scene!.Picture);
        Assert.Null(vm.Scene.PortalArt);
    }

    [AvaloniaFact]
    public void TheRecallCutscene_HoldsBeforeTheHandCloses()
    {
        var (_, vm, scene) = Open(wait: true);
        vm.BundledArtDirectory = Path.Combine(AppContext.BaseDirectory, "art");
        vm.Game.MarkDebugUsed();
        vm.Execute(new DebugJumpCommand(5));
        vm.SkipScenes();
        vm.Game.Player.RecallTimer = 1;
        vm.Execute(new HoldCommand());
        scene.Advance(SceneView.DurationFor(SceneKind.RecallUp) * 3);
        Assert.True(scene.IsHolding);
        Assert.Equal(SceneView.DurationFor(SceneKind.RecallUp) * SceneView.RecallHold, scene.Elapsed, 3);
    }

    private sealed class RecordingEngine : Angband.Audio.NullAudioEngine
    {
        public List<string> Played { get; } = [];
        public override bool IsAvailable => true;
        public override void PlayEffect(string path, float gain = 1f) => Played.Add(path);
    }

    private static List<string> SceneSounds(RecordingEngine engine) =>
        engine.Played.Where(p => p.Contains("scene-sounds")).Select(Path.GetFileNameWithoutExtension).ToList()!;

    /// <summary>Scenes have sounds: footsteps on the stairs; the recall cutscene's tear and reach, then its grasp and thunderclap.</summary>
    [AvaloniaFact]
    public void Scenes_PlayTheirSounds_AtTheirMoments()
    {
        var (_, vm, scene) = Open();
        vm.BundledArtDirectory = Path.Combine(AppContext.BaseDirectory, "art");
        var engine = new RecordingEngine();
        vm.UseAudio(new AudioServices(engine, new Angband.Audio.SoundDirector(engine), []));
        Assert.All(new[] { "recall-reach", "recall-take", "stairs-down", "stairs-up", "death", "unique", "danger" },
            name => Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "scene-sounds", name + ".ogg")), name));

        TakeStairs(vm, down: true);
        Assert.Equal(["stairs-down"], SceneSounds(engine));
        scene.Advance(100);
        Assert.Equal(["stairs-down"], SceneSounds(engine)); // once, at the start
        vm.SkipScenes();

        engine.Played.Clear();
        vm.Game.MarkDebugUsed();
        vm.Execute(new DebugJumpCommand(5));
        vm.SkipScenes();
        engine.Played.Clear();
        vm.Game.Player.RecallTimer = 1;
        for (var i = 0; i < 20 && vm.Game.Player.Depth > 0; i++) vm.Execute(new HoldCommand());
        Assert.Equal(["recall-reach"], SceneSounds(engine));
        var duration = SceneView.DurationFor(SceneKind.RecallUp);
        scene.Advance(duration * 0.5);
        Assert.Equal(["recall-reach"], SceneSounds(engine));
        scene.Advance(duration * 0.2);
        Assert.Equal(["recall-reach", "recall-take"], SceneSounds(engine));

        // Muted, or effects off: silence.
        vm.SkipScenes();
        engine.Played.Clear();
        vm.Muted = true;
        TakeStairs(vm, down: true);
        Assert.Empty(SceneSounds(engine));
    }

    /// <summary>Holding for Space, the recall scene never reaches its grasp: no thunderclap until it is let go.</summary>
    [AvaloniaFact]
    public void AHeldRecallScene_KeepsItsThunderclap()
    {
        var (_, vm, scene) = Open(wait: true);
        vm.BundledArtDirectory = Path.Combine(AppContext.BaseDirectory, "art");
        var engine = new RecordingEngine();
        vm.UseAudio(new AudioServices(engine, new Angband.Audio.SoundDirector(engine), []));
        vm.Game.MarkDebugUsed();
        vm.Execute(new DebugJumpCommand(5));
        vm.SkipScenes();
        engine.Played.Clear();
        vm.Game.Player.RecallTimer = 1;
        for (var i = 0; i < 20 && vm.Game.Player.Depth > 0; i++) vm.Execute(new HoldCommand());
        scene.Advance(SceneView.DurationFor(SceneKind.RecallUp) * 3);
        Assert.True(scene.IsHolding);
        Assert.Equal(["recall-reach"], SceneSounds(engine));
    }

    /// <summary>
    /// The newer scenes, each with its picture and sound: a trap door, Deep Descent, a quest done, a
    /// great foe slain (not a small one) — and every picture draws with its motion over it.
    /// </summary>
    [AvaloniaFact]
    public void Falls_AQuestDone_AndAGreatFoeSlain_HaveTheirScenes()
    {
        var (window, vm, scene) = Open();
        vm.BundledArtDirectory = Path.Combine(AppContext.BaseDirectory, "art");
        var engine = new RecordingEngine();
        vm.UseAudio(new AudioServices(engine, new Angband.Audio.SoundDirector(engine), []));
        var game = vm.Game;
        game.MarkDebugUsed();
        game.Player.Hp = game.Player.MaxHp = 100_000;

        // A trap door, a level down.
        vm.Execute(new DebugJumpCommand(3));
        vm.SkipScenes();
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        var next = game.Level.Neighbors(game.Player.Position).First(p => game.Level.IsEmptyFloor(p));
        game.Level[next].Trap = game.Data.Traps.Single(t => t.Id == "trap_door").Index;
        game.Level[next].Flags |= Angband.Core.World.SquareFlags.TrapVisible;
        engine.Played.Clear();
        vm.Execute(new JumpCommand(Angband.Core.Geometry.DirectionExtensions.FromOffset(next.X - game.Player.Position.X, next.Y - game.Player.Position.Y)));
        Assert.Equal(4, game.Player.Depth);
        Assert.Equal(SceneKind.Trapdoor, vm.Scene!.Kind);
        Assert.Equal("You fall through a trap door… 200 ft (level 4)", vm.Scene.Caption);
        Assert.EndsWith("trapdoor.jpg", vm.Scene.Picture);
        Assert.Contains("fall", SceneSounds(engine));
        Shot(window, scene, "scene-trapdoor");
        vm.SkipScenes();

        // Deep Descent, five levels down.
        game.Player.DeepDescentTimer = 1;
        for (var i = 0; i < 10 && game.Player.Depth == 4; i++) vm.Execute(new HoldCommand());
        Assert.Equal(SceneKind.DeepDescent, vm.Scene!.Kind);
        Assert.Equal($"The floor opens beneath you… down to {game.Player.Depth * 50} ft", vm.Scene.Caption);
        Shot(window, scene, "scene-deep-descent");
        vm.SkipScenes();

        // A great foe: Golfimbul (600 ft) has his scene; Grip and Bullroarer, shallower, don't.
        foreach (var (id, scened) in new[] { ("grip", false), ("bullroarer", false), ("golfimbul_the_hill_orc_chief", true) })
        {
            var race = game.Data.Monster(id)!;
            Assert.True(race.IsUnique);
            var spot = game.Level.AllLocs().First(l => game.Level.IsEmptyFloor(l) && l.DistanceTo(game.Player.Position) > 3);
            var foe = new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, race, spot);
            vm.Execute(new HoldCommand());
            vm.SkipScenes();
            game.DamageMonster(foe, 1_000_000);
            vm.Execute(new HoldCommand());
            Assert.Equal(scened, vm.Scene?.Kind == SceneKind.BossSlain);
            if (scened)
            {
                Assert.Equal("Golfimbul, the Hill Orc Chief is slain", vm.Scene!.Caption);
                Shot(window, scene, "scene-boss-slain");
            }
            vm.SkipScenes();
        }

        // A quest done.
        vm.Scene = new AmbientScene(SceneKind.QuestComplete, "Quest complete: The Letter", 0, true, vm.PictureFor("quest-complete"),
            Subtitle: "Your journal is in Knowledge (~).");
        Shot(window, scene, "scene-quest-complete");
    }

    [Fact]
    public void AQuestDone_IsAScene_ButALostOneIsNot()
    {
        var events = new List<Angband.Core.Game.AvaQuestCompletedEvent>();
        var game = Angband.Core.Game.GameSession.NewGame(DataLoader.Load(DataLoader.DefaultDataDirectory), 3, "warrior");
        game.Events.Subscribe<Angband.Core.Game.AvaQuestCompletedEvent>(events.Add);
        game.MarkDebugUsed();
        game.GainExperience(game.ExperienceForLevel(9));
        game.Player.Position = game.Level.AllLocs().First(l => game.Level.FeatureAt(l).Shop == "inn");
        game.Execute(new EnterStoreCommand());
        game.Execute(new QuestChoiceCommand("inn:work"));
        game.Execute(new QuestChoiceCommand("offer:letter"));
        game.Execute(new QuestChoiceCommand("accept:letter"));
        var letter = game.Player.Inventory.Pack.First(i => i.Kind.Id == "sealed_letter");
        game.Execute(new UseCommand(letter));
        game.Execute(new QuestChoiceCommand("letter:burn"));
        var done = Assert.Single(events);
        Assert.Equal(("letter", "The Letter"), (done.QuestId, done.Name));
    }

    /// <summary>The boss music: on while a great foe is in view, lingering a little once it slips from sight, off when it dies.</summary>
    [AvaloniaFact]
    public void AGreatFoe_InView_BringsTheBossMusic()
    {
        var (_, vm, _) = Open();
        var engine = new RecordingEngine();
        var director = new Angband.Audio.SoundDirector(engine) { MusicPack = Angband.Audio.SoundPack.Load(Path.Combine(AppContext.BaseDirectory, "soundpacks", "cc0-dungeon-music")) };
        vm.UseAudio(new AudioServices(engine, director, []));
        var game = vm.Game;
        game.MarkDebugUsed();
        game.Player.Hp = game.Player.MaxHp = 100_000;
        vm.Execute(new DebugJumpCommand(12));
        vm.SkipScenes();
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        vm.Execute(new HoldCommand());
        Assert.False(director.Boss);

        var race = game.Data.Monster("golfimbul_the_hill_orc_chief")!;
        var near = game.Level.AllLocs().First(l => game.Level.IsEmptyFloor(l) && l.ChebyshevTo(game.Player.Position) == 2
            && Angband.Core.Combat.ProjectionPath.Projectable(game.Level, game.Player.Position, l, 20));
        var foe = new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, race, near);
        vm.Execute(new HoldCommand());
        Assert.True(foe.IsVisible);
        Assert.True(director.Boss);
        Assert.Equal("boss", director.MusicMood);

        game.DamageMonster(foe, 1_000_000);
        vm.Execute(new HoldCommand());
        Assert.False(director.Boss);

        // A small unique (Grip, 100 ft) brings no boss music.
        var grip = new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster("grip")!, near);
        vm.Execute(new HoldCommand());
        Assert.Contains(game.Level.Monsters.All, m => m == grip);
        Assert.False(director.Boss);
    }
}
