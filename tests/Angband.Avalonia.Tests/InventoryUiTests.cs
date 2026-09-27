using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Angband.Data.Tiles;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

public class InventoryUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open(bool tiles = false)
    {
        var tilesets = TilesetCatalog.Discover([TilesetCatalog.DefaultDirectory]);
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), tilesets,
            new AppSettings { UseTiles = tiles, TilesetId = "gervais", TileScale = 1.5 }, save: null);
        vm.StartGame(42);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    [AvaloniaFact]
    public void Panel_ShowsTheStartingKit()
    {
        var (_, vm) = Open();
        Assert.Contains(vm.EquipmentRows, r => r.Name.StartsWith("a Dagger"));
        Assert.Contains(vm.PackRows, r => r.Name == "2 Potions of Cure Light Wounds");
        Assert.Contains(vm.QuiverRows, r => r.Name.StartsWith("40 Iron Shots"));
    }

    [AvaloniaFact]
    public void QuaffPrompt_ListsPotions_AndALetterQuaffsOne()
    {
        var (window, vm) = Open();

        window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.None);
        Assert.True(vm.IsPrompting);
        Assert.Equal("Quaff which potion?", vm.PromptTitle);
        Assert.Single(vm.PromptRows);
        TileRenderingTests.Save(window, "prompt-quaff");

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        Assert.False(vm.IsPrompting);
        Assert.Contains(vm.PackRows, r => r.Name == "a Potion of Cure Light Wounds");
    }

    [AvaloniaFact]
    public void Escape_CancelsAPrompt()
    {
        var (window, vm) = Open();
        window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
        Assert.True(vm.IsPrompting);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(vm.IsPrompting);
        Assert.Equal("Cancelled.", vm.LastMessage);
    }

    [AvaloniaFact]
    public void TakeOffThenWield_RoundTrips()
    {
        var (window, vm) = Open();
        window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.None);
        var dagger = vm.PromptRows.First(r => r.Name.StartsWith("a Dagger"));
        window.KeyPressQwerty(Enum.Parse<PhysicalKey>(dagger.Letter.ToUpperInvariant()), RawInputModifiers.None);
        Assert.DoesNotContain(vm.EquipmentRows, r => r.Name.StartsWith("a Dagger"));

        window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
        var again = vm.PromptRows.First(r => r.Name.StartsWith("a Dagger"));
        window.KeyPressQwerty(Enum.Parse<PhysicalKey>(again.Letter.ToUpperInvariant()), RawInputModifiers.None);
        Assert.Contains(vm.EquipmentRows, r => r.Name.StartsWith("a Dagger"));
    }

    [AvaloniaFact]
    public void DungeonWithObjects_Renders_InAsciiAndTiles()
    {
        foreach (var tiles in new[] { false, true })
        {
            var (window, vm) = Open(tiles);
            vm.Execute(new DebugJumpCommand(4));
            vm.ShowWholeMap = true;
            TileRenderingTests.Save(window, tiles ? "items-tiles" : "items-ascii");
        }
    }
}
