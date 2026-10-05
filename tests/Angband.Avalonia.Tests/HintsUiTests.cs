using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Angband.Input;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

/// <summary>Hints for new players: shown once each, with the keys you actually have.</summary>
public class HintsUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, AppSettings Settings) Open(InputBindings? keys = null, AppSettings? settings = null)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        settings ??= new AppSettings();
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null);
        vm.UseInput(keys ?? InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        vm.DismissHint(); // whatever the town start brought up
        return (window, vm, settings);
    }

    [AvaloniaFact]
    public void ABadWound_BringsUpAHint_WithTheQuaffKey_Once()
    {
        var (window, vm, settings) = Open();
        vm.Game.Player.Hp = 2;
        vm.Execute(new HoldCommand());
        Assert.True(vm.HasHint);
        Assert.StartsWith("You are badly hurt! Quaff a potion of Cure Light Wounds (q)", vm.HintText);
        Assert.Contains("hurt", settings.SeenHints);
        window.CaptureRenderedFrame();
        Assert.True(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "HintBanner").IsEffectivelyVisible);
        TileRenderingTests.Save(window, "hint");

        // It stays for a few commands, then goes; and never comes back.
        for (var i = 0; i < MainWindowViewModel.HintCommands - 1; i++)
        {
            vm.Game.Player.Hp = vm.Game.Player.MaxHp;
            vm.Execute(new HoldCommand());
            Assert.StartsWith("You are badly hurt!", vm.HintText); // still up
        }
        vm.Execute(new HoldCommand());
        Assert.DoesNotContain("badly hurt", vm.HintText);
        vm.DismissHint();
        vm.Game.Player.Hp = 2;
        vm.Execute(new HoldCommand());
        Assert.DoesNotContain("badly hurt", vm.HintText);
    }

    [AvaloniaFact]
    public void Hints_NameTheKeysOfTheKeysetInUse()
    {
        var (_, vm, settings) = Open(InputBindings.Preset(InputBindings.Keyset.Original));
        vm.Game.MarkDebugUsed();
        vm.Execute(new DebugJumpCommand(1));
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m); // the jackal is the first seen
        vm.DismissHint();
        vm.Execute(new HoldCommand()); // whatever else a first dungeon level has to say, said
        vm.DismissHint();
        settings.SeenHints.Remove("monster"); // (a monster in view on arrival would have had it already)
        var game = vm.Game;
        var at = game.Level.AllLocs().First(l => l.DistanceTo(game.Player.Position) == 2 && game.Level.IsEmptyFloor(l)
            && Angband.Core.Combat.ProjectionPath.Projectable(game.Level, game.Player.Position, l, 20));
        new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster("jackal")!, at, asleep: true);
        game.UpdateView();
        vm.Execute(new HoldCommand());
        Assert.StartsWith("A monster! Walk into it to attack. l looks at it", vm.HintText); // 'x' in AVABand's keys
    }

    [AvaloniaFact]
    public void NoticingAHiddenTrap_ExplainsTheSearchSkill()
    {
        var settings = new AppSettings();
        foreach (var seen in new[] { "tutorial", "trap", "monster", "stairs", "floor", "level", "runes" }) settings.SeenHints.Add(seen);
        var (_, vm, _) = Open(settings: settings);
        vm.Game.MarkDebugUsed();
        vm.Execute(new DebugJumpCommand(1));
        vm.DismissHint();
        var game = vm.Game;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        var at = game.Level.AllLocs().First(l => l.DistanceTo(game.Player.Position) == 1 && game.Level.IsEmptyFloor(l));
        game.Level[at].Trap = game.Data.Traps.Single(t => t.Id == "pit").Index;
        game.Level[at].TrapPower = 1;
        game.Known.Forget(at);
        vm.Execute(new HoldCommand());
        Assert.StartsWith("You noticed that trap as it came into sight.", vm.HintText);
    }

    [AvaloniaFact]
    public void TheOption_TurnsHintsOff_AndSeenHintsCarryToTheNextCharacter()
    {
        var (_, vm, settings) = Open();
        vm.SetOption(DisplayOptions.Hints, false);
        vm.Game.Player.Hp = 2;
        vm.Execute(new HoldCommand());
        Assert.False(vm.HasHint);
        Assert.DoesNotContain("hurt", settings.SeenHints);

        vm.SetOption(DisplayOptions.Hints, true);
        vm.Execute(new HoldCommand());
        Assert.Contains("hurt", settings.SeenHints);

        var (_, next, _) = Open(settings: settings); // same settings: a new character
        next.Game.Player.Hp = 2;
        next.Execute(new HoldCommand());
        Assert.DoesNotContain("badly hurt", next.HintText);
    }

    /// <summary>Each condition that kills new characters has a hint, naming what cures it and the key to use.</summary>
    [AvaloniaFact]
    public void Conditions_BringUpAHint_WithTheirCure()
    {
        foreach (var (timed, hint, cure) in new[]
                 {
                     ("blind", "You are blind", "Cure Light Wounds cures it (q)"),
                     ("confused", "You are confused", "Cure Serious Wounds cures it (q"),
                     ("poisoned", "You are poisoned", "Neutralize Poison or Cure Critical Wounds"),
                     ("afraid", "You are afraid", "Boldness, Heroism or Berserk Strength"),
                     ("stun", "You are stunned", "Cure Critical Wounds cures it"),
                     ("cut", "You are bleeding", "Cure Serious Wounds stops it"),
                     ("slow", "You are slowed", "Speed"),
                     ("amnesia", "You are amnesiac", "Cure Critical Wounds"),
                 })
        {
            var (_, vm, settings) = Open();
            vm.Game.Player.Timed.Set(vm.Game.Data.Timed(timed)!, 50, notify: false);
            vm.Game.Player.Hp = vm.Game.Player.MaxHp;
            vm.Execute(new HoldCommand());
            Assert.StartsWith(hint, vm.HintText);
            Assert.Contains(cure, vm.HintText);
            Assert.Contains(timed, settings.SeenHints); // (each hint's id is its condition's)
        }
    }

    [AvaloniaFact]
    public void Paralysis_And_Darkness_And_Drain_BringUpHints()
    {
        var (_, vm, settings) = Open();
        Assert.True(vm.Game.IncreaseTimed("paralyzed", 2, check: false)); // (says "You are paralysed!")
        vm.Game.Player.Hp = vm.Game.Player.MaxHp;
        vm.Execute(new HoldCommand());
        Assert.StartsWith("You were paralysed", vm.HintText);
        Assert.Contains("Free Action", vm.HintText);

        (_, vm, settings) = Open(settings: new AppSettings());
        vm.Execute(new DebugJumpCommand(3));
        vm.DismissHint();
        settings.SeenHints.AddRange(["stairs", "monster", "floor", "trap", "hidden_traps"]);
        var light = vm.Game.Player.Inventory.Light!;
        vm.Game.Player.Inventory.Remove(light, 1, () => vm.Game.Objects.NextSerial++);
        vm.Execute(new HoldCommand());
        Assert.StartsWith("You have no light", vm.HintText);

        vm.DismissHint();
        vm.Game.Player.StatDrain["str"] = 2;
        vm.Execute(new HoldCommand());
        Assert.StartsWith("One of your stats was drained", vm.HintText);
    }

    /// <summary>The newer systems' hints, each the first time it matters (the older hints already seen).</summary>
    [AvaloniaFact]
    public void TheNewerSystems_EachHaveTheirHint_WhenTheyFirstMatter()
    {
        var settings = new AppSettings();
        settings.SeenHints.AddRange(["tutorial", "hurt", "paralysed", "blind", "confused", "poisoned", "afraid", "stun", "cut", "slow",
            "image", "amnesia", "dark", "drained", "exp_drained", "shop", "monster", "trap", "hidden_traps", "pack_full", "floor",
            "hungry", "light", "runes", "level"]);
        var (_, vm, _) = Open(settings: settings);
        var game = vm.Game;
        string Next()
        {
            vm.DismissHint();
            vm.Execute(new HoldCommand());
            return vm.HintText;
        }

        game.Player.Inventory.Add(game.Objects.Create("ruby"));
        Assert.StartsWith("Gems ride in your gem pouch", Next());
        game.AvaQuests.Board.Add(new Angband.Core.Quests.BoardJob { Id = 1, Kind = "scout", Target = "5", Count = 5, Reward = 10, Taken = true });
        Assert.StartsWith("Your quest log (Ctrl+J)", Next());

        game.MarkDebugUsed();
        vm.Execute(new DebugJumpCommand(2));
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        Assert.StartsWith("Hold Ctrl or Alt", Next());
        Assert.StartsWith("Ctrl+E explores", Next());
        Assert.StartsWith("Right-click a square to pin a note", Next());

        game.GainExperience(game.ExperienceForLevel(4));
        Assert.StartsWith("Game → Statistics", Next());
        Assert.Contains("statistics", settings.SeenHints);
    }
}
