using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Data;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;

namespace Angband.Avalonia.Tests;

public class MagicUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open(string cls)
    {
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42, cls);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    [AvaloniaFact]
    public void NewGameAsMenu_ListsEveryClass()
    {
        var (window, _) = Open("warrior");
        var menu = window.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Name == "NewGameAsMenu");
        Assert.Equal(["Warrior", "Mage", "Priest", "Ranger", "Druid", "Rogue", "Paladin", "Necromancer", "Blackguard"], menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!));
    }

    [AvaloniaFact]
    public void Mage_StudiesThenSeesTheSpellInTheCastList()
    {
        var (window, vm) = Open("mage");
        Assert.Contains("Mage L1", vm.StatusText);
        Assert.Contains("SP 2/2", vm.StatusText);

        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Shift);
        Assert.True(vm.IsPrompting);
        // Angband 4.2's [First Spells] offers three spells at level 1.
        Assert.Equal(["Magic Missile", "Light Room", "Find Traps, Doors & Stairs"], vm.SpellPromptRows.Select(r => r.Name));
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        Assert.Contains("You have learned the spell of Magic Missile.", vm.Messages);

        window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.None);
        var row = Assert.Single(vm.SpellPromptRows);
        Assert.Equal(20, row.Fail);
        Assert.Equal("untried", row.Note);
        TileRenderingTests.Save(window, "spell-cast-prompt");

        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        Assert.Equal("You have no target.", vm.LastMessage); // nothing to shoot
    }

    [AvaloniaFact]
    public void Warriors_AreToldTheyCannotUseMagic()
    {
        var (window, vm) = Open("warrior");
        window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.None);
        Assert.False(vm.IsPrompting);
        Assert.Equal("You cannot use magic.", vm.LastMessage);
    }

    [AvaloniaFact]
    public void S_StealsForRogues_AndHoldsForEveryoneElse()
    {
        var (rogueWindow, rogue) = Open("rogue");
        rogueWindow.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.None);
        Assert.Equal("Direction?", rogue.LastMessage);
        rogueWindow.KeyPressQwerty(PhysicalKey.L, RawInputModifiers.None);
        Assert.Equal("There is no one there to steal from.", rogue.LastMessage);

        var (window, warrior) = Open("warrior");
        var turn = warrior.Game.GameTurn;
        window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.None);
        Assert.True(warrior.Game.GameTurn > turn);
    }

    [AvaloniaFact]
    public void Necromancer_PerformsRitualsFromTheShadowBook()
    {
        var (window, vm) = Open("necromancer");
        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Shift);
        Assert.Equal("Nether Bolt", Assert.Single(vm.SpellPromptRows).Name);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        Assert.Contains("You have learned the ritual of Nether Bolt.", vm.Messages);
        TileRenderingTests.Save(window, "necromancer");
    }
}
