using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;

namespace Angband.Avalonia.Tests;

/// <summary>The Debug menu's jumps: to the next level (F8) and straight back to town (F9).</summary>
public class DebugUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    /// <summary>A plain open square: walkable floor, not a staircase or a shop door.</summary>
    private static void AssertOnAPlainSpot(GameSession game)
    {
        var feature = game.Level.FeatureAt(game.Player.Position);
        Assert.True(feature.Has(TerrainFlags.Passable));
        Assert.False(feature.Has(TerrainFlags.Stair));
        Assert.Null(feature.Shop);
        Assert.Null(game.Level.Monsters.At(game.Player.Position));
    }

    [AvaloniaFact]
    public void F8_GoesDownOneLevel_ToARandomSpot()
    {
        var (window, vm) = Open();
        var game = vm.Game;

        window.KeyPressQwerty(PhysicalKey.F8, RawInputModifiers.None);
        Assert.Equal(1, game.Player.Depth);
        AssertOnAPlainSpot(game);

        var starts = new HashSet<Angband.Core.Geometry.Loc> { game.Player.Position };
        for (var depth = 2; depth <= 4; depth++)
        {
            window.KeyPressQwerty(PhysicalKey.F8, RawInputModifiers.None);
            Assert.Equal(depth, game.Player.Depth);
            AssertOnAPlainSpot(game);
            starts.Add(game.Player.Position);
        }
        Assert.True(starts.Count > 1); // somewhere new each time, not a fixed square
        Assert.Equal(4, game.Player.MaxDepth);
    }

    [AvaloniaFact]
    public void F9_GoesStraightBackToTown_WithoutRecallsMessage()
    {
        var (window, vm) = Open();
        var game = vm.Game;
        vm.JumpDeeperCommand.Execute(null);
        vm.JumpDeeperCommand.Execute(null);
        Assert.Equal(10, game.Player.Depth);
        var messages = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));

        window.KeyPressQwerty(PhysicalKey.F9, RawInputModifiers.None);

        Assert.Equal(0, game.Player.Depth);
        AssertOnAPlainSpot(game);
        Assert.DoesNotContain(messages, m => m.Contains("yanked"));
        Assert.Equal(10, game.Player.MaxDepth); // recall depth is kept

        window.KeyPressQwerty(PhysicalKey.F9, RawInputModifiers.None);
        Assert.Equal("You are already in town.", vm.LastMessage);
    }

    [AvaloniaFact]
    public void NextLevel_StopsAtTheBottom()
    {
        var (_, vm) = Open();
        vm.Execute(new DebugJumpCommand(vm.Game.Data.Constants.MaxDepth));
        vm.JumpNextLevelCommand.Execute(null);
        Assert.Equal(vm.Game.Data.Constants.MaxDepth, vm.Game.Player.Depth);
        Assert.Equal("You are as deep as the dungeon goes.", vm.LastMessage);
    }

    [AvaloniaFact]
    public void TheDebugMenu_HasBothJumps()
    {
        var (window, _) = Open();
        var headers = window.GetLogicalDescendants().OfType<global::Avalonia.Controls.MenuItem>()
            .Select(m => m.Header as string).ToList();
        Assert.Contains("Jump to _next level", headers);
        Assert.Contains("Jump back to _town", headers);
        Assert.Contains("Shops pay _gold when you sell", headers);
    }
}
