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

/// <summary>A moment's scene on taking the stairs: down, and up (to the dungeon, and to the town).</summary>
public sealed class StairSceneUiTests : IDisposable
{
    private readonly string _art = Path.Combine(Path.GetTempPath(), "avaband-art-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_art)) Directory.Delete(_art, true);
    }

    private (MainWindow Window, MainWindowViewModel Vm, StairSceneView Scene) Open(bool scenes = true)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings();
        settings.Options[DisplayOptions.Hints] = false;
        if (!scenes) settings.Options[DisplayOptions.StairScenes] = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null)
        {
            ArtDirectory = _art,
        };
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var scene = window.GetVisualDescendants().OfType<StairSceneView>().Single();
        return (window, vm, scene);
    }

    private static void TakeStairs(MainWindowViewModel vm, bool down)
    {
        var game = vm.Game;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        game.Player.Position = game.Level.FindFeature(down ? TerrainFlags.DownStair : TerrainFlags.UpStair).First();
        vm.Execute(new TakeStairsCommand(Down: down));
    }

    [AvaloniaFact]
    public void GoingDown_ThenUp_ShowsEachItsScene_ForAMoment()
    {
        var (window, vm, scene) = Open();
        TakeStairs(vm, down: true);
        Assert.Equal(1, vm.Game.Player.Depth);
        var down = Assert.IsType<StairScene>(vm.StairScene);
        Assert.True(down.Down);
        Assert.Equal("Descending… 50 ft (level 1)", down.Caption);
        Assert.Null(down.Picture);
        scene.Advance(700);
        window.CaptureRenderedFrame();
        Assert.True(scene.IsEffectivelyVisible);
        TileRenderingTests.Save(window, "stairs-down");
        scene.Advance(StairSceneView.DurationMs); // played through: gone
        Assert.Null(vm.StairScene);

        vm.Game.MarkDebugUsed();
        vm.Execute(new DebugJumpCommand(3)); // a jump is not the stairs: no scene
        Assert.Null(vm.StairScene);
        TakeStairs(vm, down: false);
        var up = Assert.IsType<StairScene>(vm.StairScene);
        Assert.False(up.Down);
        Assert.Equal("Climbing… 100 ft (level 2)", up.Caption);
        scene.Advance(700);
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "stairs-up");
    }

    [AvaloniaFact]
    public void UpIntoTheTown_SaysSo_AndAKeyEndsTheScene_AndStillCounts()
    {
        var (window, vm, scene) = Open();
        TakeStairs(vm, down: true);
        vm.EndStairScene();
        TakeStairs(vm, down: false);
        Assert.Equal(0, vm.Game.Player.Depth);
        Assert.StartsWith("Up into the town, by ", vm.StairScene!.Caption);
        scene.Advance(700);
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "stairs-up-town");

        var turn = vm.Game.GameTurn;
        window.KeyPressQwerty(PhysicalKey.Digit5, RawInputModifiers.None); // hold a turn
        Assert.Null(vm.StairScene);
        Assert.True(vm.Game.GameTurn > turn); // the key was not swallowed
    }

    [AvaloniaFact]
    public void TheOption_TurnsScenesOff_AndOwnPicturesReplaceThePaintedOnes()
    {
        var (_, off, _) = Open(scenes: false);
        TakeStairs(off, down: true);
        Assert.Null(off.StairScene);

        Directory.CreateDirectory(_art);
        var picture = Path.Combine(_art, "stairs-down.png");
        File.Copy(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory), "*.png", SearchOption.AllDirectories).First(), picture);
        var (window, vm, scene) = Open();
        TakeStairs(vm, down: true);
        Assert.Equal(picture, vm.StairScene!.Picture);
        scene.Advance(700);
        window.CaptureRenderedFrame(); // draws the picture (no failure on a real file)
    }
}
