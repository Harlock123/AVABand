using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

public class DeviceUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        vm.Game.Player.SkillDevice = 500;
        return (window, vm);
    }

    private static Angband.Core.Items.Item Give(MainWindowViewModel vm, string kind, int charges)
    {
        var item = vm.Game.Objects.Create(kind);
        item.Charges = charges;
        vm.Game.Knowledge.LearnKind(item.Kind);
        return vm.Game.Player.Inventory.Add(item)!;
    }

    [AvaloniaFact]
    public void A_AimsAWand_AtTheNearestMonster()
    {
        var (window, vm) = Open();
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        var wand = Give(vm, "wand_of_magic_missile", 3);
        var p = vm.Game.Player.Position;
        var spot = vm.Game.Level.AllLocs().First(l => l.DistanceTo(p) is > 1 and < 4 && vm.Game.Level.IsEmptyFloor(l)
                                                     && vm.Game.Level[l].Has(Angband.Core.World.SquareFlags.Seen));
        var spawner = new Angband.Core.Monsters.MonsterSpawner(vm.Game.Data);
        var target = spawner.Place(vm.Game.Level, vm.Game.Rng, vm.Game.Data.Monster("cave_orc")!, spot, asleep: false);
        target.Hp = target.MaxHp = 1000;
        vm.Game.Scheduler.Add(target);
        vm.Game.UpdateView();

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        Assert.True(vm.IsPrompting);
        var row = Assert.Single(vm.PromptRows);
        Assert.Contains("Magic Missile", row.Name);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);

        Assert.Equal(2, wand.Charges);
        Assert.True(target.Hp < 1000);
    }

    [AvaloniaFact]
    public void StoneToMud_AsksForADirection()
    {
        var (window, vm) = Open();
        var wand = Give(vm, "wand_of_stone_to_mud", 5);
        // Even a master sometimes fumbles a device (Angband: 2 in skill), so allow a retry or two.
        for (var i = 0; i < 3 && wand.Charges == 5; i++)
        {
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
            vm.PromptKey(vm.PromptRows.Single(r => r.Item == wand).Letter[0]);
            Assert.Equal("Direction?", vm.LastMessage);
            window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.None); // north
        }
        Assert.Equal(4, wand.Charges);
    }

    [AvaloniaFact]
    public void ShiftZ_UsesAStaff_AndZ_ZapsARod()
    {
        var (window, vm) = Open();
        var staff = Give(vm, "staff_of_detect_evil", 4);
        var rod = Give(vm, "rod_of_treasure_location", 0);

        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Shift);
        vm.PromptKey(vm.PromptRows.Single(r => r.Item == staff).Letter[0]);
        Assert.Equal(3, staff.Charges);

        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.None);
        vm.PromptKey(vm.PromptRows.Single(r => r.Item == rod).Letter[0]);
        Assert.True(rod.Timeout > 0);
    }

    [AvaloniaFact]
    public void T_TunnelsInADirection()
    {
        var (window, vm) = Open();
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        vm.Game.Player.Inventory.Add(vm.Game.Objects.Create("mattock"));
        var east = vm.Game.Player.Position + new Loc(1, 0);
        vm.Game.Level[east].Feature = vm.Game.Data.Terrain.Ids.Rubble;

        for (var i = 0; i < 5 && !vm.Game.Level.IsFloor(east); i++)
        {
            window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Shift);
            Assert.Equal("Direction?", vm.LastMessage);
            window.KeyPressQwerty(PhysicalKey.L, RawInputModifiers.None); // east
        }
        Assert.True(vm.Game.Level.IsFloor(east));
        Assert.Contains("You have removed the rubble.", vm.Messages);
    }

    [AvaloniaFact]
    public void A_ActivatesWornGear_AndInspectExplainsIt()
    {
        var (window, vm) = Open();
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        var phial = vm.Game.Objects.CreateArtifact(vm.Game.Data.Artifacts.Single(a => a.Id == "galadriel"));
        vm.Execute(new WieldCommand(vm.Game.Player.Inventory.Add(phial)!));

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Shift);
        var row = Assert.Single(vm.PromptRows);
        Assert.Same(phial, row.Item);
        vm.PromptKey(row.Letter[0]);
        Assert.True(phial.Timeout > 0);

        window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Shift);
        vm.PromptKey(vm.PromptRows.Single(r => r.Item == phial).Letter[0]);
        Assert.Contains("When activated, it lights up the surrounding area", vm.LastMessage);
    }

    [AvaloniaFact]
    public void O_ThenHold_OpensTheChestUnderfoot_AndD_Disarms()
    {
        var (window, vm) = Open();
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        vm.Game.Player.DisarmSkill = 500;
        var here = vm.Game.Player.Position;
        var chest = vm.Game.Objects.Create("small_wooden_chest");
        chest.ChestState = 2; // gas trap
        vm.Game.Level.Objects.Add(here, chest);

        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Shift);
        Assert.Equal("Direction?", vm.LastMessage);
        window.KeyPressQwerty(PhysicalKey.Digit5, RawInputModifiers.None);
        Assert.Equal(-2, chest.ChestState);

        window.KeyPressQwerty(PhysicalKey.O, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Digit5, RawInputModifiers.None);
        Assert.Equal(0, chest.ChestState);
        Assert.Contains("You open the chest.", vm.Messages);
    }
}
