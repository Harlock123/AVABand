using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;

namespace Angband.Avalonia.Tests;

/// <summary>Repeat the last command (Angband 'n'; here Ctrl+V, the roguelike key).</summary>
public class RepeatUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open(string classId)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.StartGame(42, classId);
        TestKit.Give(vm);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        return (window, vm);
    }

    private static void CtrlV(MainWindow window) => window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);

    [AvaloniaFact]
    public void WithNothingDone_ThereIsNothingToRepeat()
    {
        var (window, vm) = Open("warrior");
        CtrlV(window);
        Assert.Equal("There is no command to repeat.", vm.LastMessage);
    }

    [AvaloniaFact]
    public void Quaffing_Repeats_UntilTheStackRunsOut_AndWalkingDoesntReplaceIt()
    {
        var (window, vm) = Open("warrior");
        var game = vm.Game;
        var potions = game.Player.Inventory.Pack.First(i => i.Kind.Id == "cure_light_wounds");
        Assert.Equal(2, potions.Number);

        vm.Execute(new UseCommand(potions));
        Assert.Equal(1, potions.Number);
        vm.Execute(new WalkCommand(Angband.Core.Geometry.Direction.East)); // walking isn't remembered
        vm.Execute(new WalkCommand(Angband.Core.Geometry.Direction.West));
        Assert.IsType<UseCommand>(vm.LastCommand);

        var turn = game.GameTurn;
        CtrlV(window);
        Assert.True(game.GameTurn > turn);
        Assert.DoesNotContain(game.Player.Inventory.Pack, i => i.Kind.Id == "cure_light_wounds");

        CtrlV(window);
        Assert.StartsWith("You have no more ", vm.LastMessage);
        Assert.Contains("Cure Light Wounds", vm.LastMessage);
    }

    [AvaloniaFact]
    public void AMage_CastsTheSameSpellAgain()
    {
        var (window, vm) = Open("mage");
        var game = vm.Game;
        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Shift); // study
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);  // Magic Missile
        game.Player.MaxMana = game.Player.Mana = 20;
        TestKit.OpenGround(vm);
        // Something tough to shoot at, in the open east of the player.
        var at = game.Level.AllLocs().First(p => p.Y == game.Player.Position.Y && p.X > game.Player.Position.X + 1
                                                 && p.X < game.Player.Position.X + 5 && game.Level.IsEmptyFloor(p));
        new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster("bullroarer")!, at, asleep: true);
        game.UpdateView();

        vm.Execute(new CastCommand("magic_missile"));
        vm.SkipScenes(); // (a unique's first sight has its scene, which would take the next key)
        var mana = game.Player.Mana;
        Assert.True(mana < 20, $"{vm.LastMessage} mana {mana}/{game.Player.MaxMana}");

        CtrlV(window);
        Assert.True(game.Player.Mana < mana, vm.LastMessage);
        Assert.Equal(new CastCommand("magic_missile"), vm.LastCommand);
    }

    [AvaloniaFact]
    public void Zero_TypesACount_ForTheNextCommand()
    {
        var (window, vm) = Open("warrior");
        var game = vm.Game;
        var turn = game.GameTurn;
        window.KeyPressQwerty(PhysicalKey.Comma, RawInputModifiers.None); // hold once, to measure a turn
        var oneTurn = game.GameTurn - turn;

        window.KeyPressQwerty(PhysicalKey.Digit0, RawInputModifiers.None);
        Assert.True(vm.IsEnteringCount);
        window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Digit4, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Digit3, RawInputModifiers.None);
        Assert.Equal("Repeat: 13", vm.LastMessage);
        Assert.Equal("Repeat 13_", vm.CountBadge);
        var badge = window.GetLogicalDescendants().OfType<global::Avalonia.Controls.Border>().Single(b => b.Name == "CountBadge");
        Assert.True(badge.IsVisible);
        turn = game.GameTurn;
        window.KeyPressQwerty(PhysicalKey.Comma, RawInputModifiers.None); // the command: hold 13 times
        Assert.False(vm.IsEnteringCount);
        Assert.Equal(13 * oneTurn, game.GameTurn - turn);
        Assert.Null(vm.CountBadge); // used up
        Assert.False(badge.IsVisible);

        // The count is used up: the next hold is a single one.
        turn = game.GameTurn;
        window.KeyPressQwerty(PhysicalKey.Comma, RawInputModifiers.None);
        Assert.Equal(oneTurn, game.GameTurn - turn);
    }

    [AvaloniaFact]
    public void ACount_WaitsThroughTheDirectionPrompt_AndEscapeCancelsIt()
    {
        var (window, vm) = Open("warrior");
        window.KeyPressQwerty(PhysicalKey.Digit0, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Digit5, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Shift); // tunnel: asks which way
        Assert.Equal(5, vm.PendingCount);
        Assert.Equal("Repeat 5", vm.CountBadge); // waiting for the direction
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Equal(0, vm.PendingCount);

        window.KeyPressQwerty(PhysicalKey.Digit0, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Digit7, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(vm.IsEnteringCount);
        Assert.Equal(0, vm.PendingCount);
        Assert.Equal("Cancelled.", vm.LastMessage);
    }

    [AvaloniaFact]
    public void Recall_InAPersistentDungeon_AsksWhichLevel()
    {
        var (window, vm) = Open("warrior");
        var game = vm.Game;
        game.Options[OptionIds.LevelsPersist] = true;
        foreach (var down in new[] { true, true, false, false })
        {
            game.Player.Position = game.Level.FindFeature(down ? Angband.Core.Definitions.TerrainFlags.DownStair
                : Angband.Core.Definitions.TerrainFlags.UpStair).First();
            foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
            vm.Execute(new TakeStairsCommand(down));
        }
        Assert.Equal(0, game.Player.Depth);
        var scroll = game.Objects.Create("scroll_of_word_of_recall");
        game.Knowledge.LearnKind(scroll.Kind);
        scroll = game.Player.Inventory.Add(scroll)!;

        vm.SkipScenes(); // (the stairs' scenes would take the next key)
        vm.Execute(new UseCommand(scroll));
        Assert.True(vm.IsEnteringNumber);
        Assert.StartsWith("Which level do you wish to return to (0 to cancel)? 2", vm.LastMessage); // the deepest
        window.KeyPressQwerty(PhysicalKey.Digit5, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Contains("You must choose a level you have previously visited.", vm.Messages);
        Assert.True(vm.IsEnteringNumber);                        // asked again
        window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.False(vm.IsEnteringNumber);
        Assert.True(game.Player.RecallTimer > 0);
        Assert.Equal(1, game.Player.RecallDepth);
    }

    [AvaloniaFact]
    public void Recall_BelowTheDeepestLevel_AsksWhetherToSetTheDepth()
    {
        var (window, vm) = Open("warrior");
        var game = vm.Game;
        game.MarkDebugUsed();
        vm.Execute(new DebugJumpCommand(8));
        vm.Execute(new DebugJumpCommand(3));
        var scroll = game.Objects.Create("scroll_of_word_of_recall");
        game.Knowledge.LearnKind(scroll.Kind);
        scroll = game.Player.Inventory.Add(scroll)!;
        vm.Execute(new UseCommand(scroll));
        Assert.True(vm.IsConfirming);
        Assert.Equal("Set recall depth to current depth? (y/n)", vm.LastMessage);
        window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
        Assert.True(game.Player.RecallTimer > 0);
        Assert.Equal(3, game.Player.MaxDepth);
    }
}
