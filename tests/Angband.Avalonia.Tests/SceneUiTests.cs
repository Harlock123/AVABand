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
    public void UpIntoTheTown_SaysSo_AndAKeyEndsTheScenes_AndStillCounts()
    {
        var (window, vm, _) = Open();
        TakeStairs(vm, down: true);
        vm.SkipScenes();
        TakeStairs(vm, down: false);
        Assert.StartsWith("Up into the town, by ", vm.Scene!.Caption);
        var turn = vm.Game.GameTurn;
        window.KeyPressQwerty(PhysicalKey.Digit5, RawInputModifiers.None); // hold a turn
        Assert.Null(vm.Scene);
        Assert.True(vm.Game.GameTurn > turn);
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
        string[] names = ["stairs-down", "stairs-up", "stairs-up-town", "recall-town", "recall-dungeon", "unique", "danger", "death",
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
        TakeStairs(vm, down: true);
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
}
