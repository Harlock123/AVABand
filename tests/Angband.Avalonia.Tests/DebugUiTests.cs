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
        Assert.True(vm.IsConfirming); // the first debug command asks
        window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
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
        game.MarkDebugUsed(); // already agreed to
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
        vm.Game.MarkDebugUsed();
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

    /// <summary>
    /// Debug commands mark the character, as Angband's debug mode does: the first asks, "no" does
    /// nothing, "yes" marks the character for good (kept in the save), and it is never scored.
    /// </summary>
    [AvaloniaFact]
    public void TheFirstDebugCommand_Asks_ThenMarksTheCharacter()
    {
        var (window, vm) = Open();
        var game = vm.Game;

        window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.None);
        Assert.True(vm.IsConfirming);
        Assert.StartsWith("Debug commands mark this character", vm.LastMessage);
        window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);
        Assert.Equal(0, game.Player.Depth);
        Assert.False(game.IsCheater);

        window.KeyPressQwerty(PhysicalKey.F7, RawInputModifiers.None); // whole map: asks too
        window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
        Assert.True(vm.ShowWholeMap);
        Assert.True(game.UsedDebug);
        Assert.True(game.IsCheater);

        window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.None); // no more questions
        Assert.False(vm.IsConfirming);
        Assert.Equal(5, game.Player.Depth);

        using var stream = new MemoryStream();
        Angband.Core.Persistence.SaveGame.Save(game, stream);
        stream.Position = 0;
        Assert.True(Angband.Core.Persistence.SaveGame.Load(game.Data, stream).UsedDebug);
        Assert.Contains("Cheated (debug): not scored.", Angband.Core.Records.CharacterDump.Build(game, []));
    }
}
