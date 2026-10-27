using Angband.Avalonia.Controls;
using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

/// <summary>AVABand's cursed levels in the interface: the scene that holds for Space, the question, and the status bar.</summary>
public sealed class CursedLevelUiTests : IDisposable
{
    private readonly string _art = Path.Combine(Path.GetTempPath(), "avaband-art-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_art)) Directory.Delete(_art, true);
    }

    private (MainWindow Window, MainWindowViewModel Vm, SceneView Scene, Func<long> Tick) Open()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings();
        settings.Options[DisplayOptions.Hints] = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null)
        {
            ArtDirectory = _art,
            BundledArtDirectory = _art, // (empty: the painted scene)
        };
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.AfterQueuedInput = a => a();
        long now = 1000;
        vm.SceneClock = () => now;
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var scene = window.GetVisualDescendants().OfType<SceneView>().Single();
        scene.RunsByItself = false;
        vm.Game.MarkDebugUsed();
        vm.Game.Player.Hp = vm.Game.Player.MaxHp = 100_000;
        return (window, vm, scene, () => now += MainWindowViewModel.SceneTypeAheadMs + MainWindowViewModel.SceneMinimumMs);
    }

    /// <summary>Down to a cursed level, its scenes queued (the cursed one among them, after any other).</summary>
    private static void ArriveCursed(MainWindowViewModel vm, LevelCurse curse, int depth = 8)
    {
        vm.Game.CurseNextLevel = curse;
        vm.Execute(new DebugJumpCommand(depth));
        Assert.Equal(curse.Id, vm.Game.Level.Curse);
    }

    /// <summary>Space past every scene before the cursed one.</summary>
    private static void UpToTheCurse(MainWindow window, MainWindowViewModel vm, Func<long> tick)
    {
        for (var guard = 0; vm.Scene is { Kind: not SceneKind.Cursed } && guard < 10; guard++)
        {
            tick();
            vm.EndScene();
        }
    }

    [AvaloniaFact]
    public void A_cursed_level_shows_its_scene_until_Space_then_asks_and_the_status_bar_names_the_curse()
    {
        var (window, vm, scene, tick) = Open();
        ArriveCursed(vm, LevelCurses.BloodMoon);
        UpToTheCurse(window, vm, tick);
        var shown = Assert.IsType<AmbientScene>(vm.Scene);
        Assert.Equal(SceneKind.Cursed, shown.Kind);
        Assert.Equal("A cursed level: Blood moon", shown.Caption);
        Assert.Contains("+5 speed", shown.Subtitle);
        Assert.Contains("In return:", shown.Subtitle);
        Assert.True(shown.WaitForKey); // (it always holds: its terms are to be read)
        Assert.True(SceneKinds.HoldsForKey(SceneKind.Cursed));
        Assert.Null(shown.Picture);
        scene.Advance(SceneView.DurationFor(SceneKind.Cursed) * 3);
        Assert.True(scene.IsHolding);
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "scene-cursed-level");

        // Space: the question, behind it.
        tick();
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Assert.Null(vm.Scene);
        Assert.True(vm.IsPrompting);
        Assert.Equal("A cursed level: Blood moon", vm.PromptTitle);
        Assert.Contains("+5 speed", vm.PromptText);
        Assert.Equal(["Stay, on these terms", "Take the stairs up and leave it"], vm.ChoiceRows.Select(r => r.Text));
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "cursed-level-question");

        // Stay: the status bar says so.
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        Assert.False(vm.IsPrompting);
        Assert.Equal(8, vm.Game.Player.Depth);
        Assert.Contains("Cursed: Blood moon", vm.StatusText);
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "cursed-level-status");
    }

    [AvaloniaFact]
    public void Choosing_to_leave_takes_the_stairs_and_the_curse_is_gone_from_the_status_bar()
    {
        var (window, vm, _, tick) = Open();
        ArriveCursed(vm, LevelCurses.Teeming);
        Assert.Contains("Cursed: Teeming", vm.StatusText);
        vm.SkipScenes();
        tick();
        Assert.True(vm.IsPrompting);
        window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.None);
        Assert.Equal(7, vm.Game.Player.Depth);
        Assert.DoesNotContain("Cursed", vm.StatusText);
        // The stairs' scene, as for any stairs taken.
        Assert.Equal(SceneKind.StairsUp, vm.Scene?.Kind);
    }
}
