using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

public class IgnoreUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42);
        TestKit.Give(vm);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    [AvaloniaFact]
    public void CtrlD_OffersAngbandsIgnoreMenu()
    {
        var (window, vm) = Open();
        var potions = vm.Game.Player.Inventory.Pack.First(i => i.Kind.Id == "cure_light_wounds");

        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Control);
        Assert.Equal("Ignore which item?", vm.PromptTitle);
        vm.PromptKey(vm.PromptRows.Single(r => r.Item == potions).Letter[0]);
        Assert.True(vm.IsPrompting);
        Assert.Equal(["This item only", "All Potions of Cure Light Wounds"], vm.ChoiceRows.Select(r => r.Text));
        TileRenderingTests.Save(window, "ignore-menu");

        window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.None);
        Assert.False(vm.IsPrompting);
        Assert.DoesNotContain(vm.PackRows, r => r.Item == potions);
        Assert.Empty(vm.FloorRows); // dropped, and hidden
        Assert.Contains("You drop", string.Join("|", vm.Messages));
    }

    [AvaloniaFact]
    public void IgnoredObjects_LeaveTheMap_UntilKShowsThem()
    {
        var (window, vm) = Open();
        var game = vm.Game;
        var spot = game.Player.Position + new Loc(1, 0);
        var dagger = game.Objects.Create("dagger");
        game.Level.Objects.Add(spot, dagger);
        game.UpdateView();
        Assert.StartsWith("object:", vm.GetCell(spot.X, spot.Y).TileKey);

        dagger.Ignored = true;
        Assert.DoesNotContain("object:", vm.GetCell(spot.X, spot.Y).TileKey);

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Shift);
        Assert.True(game.Unignoring);
        Assert.StartsWith("object:", vm.GetCell(spot.X, spot.Y).TileKey);
    }

    [AvaloniaFact]
    public void TheOptionsPage_SetsQualityIgnoring()
    {
        var (_, vm) = Open();
        var sharp = vm.IgnoreQualityRows.Single(r => r.Type.Id == "sharp");
        Assert.Equal(0, sharp.SelectedIndex);
        Assert.Equal(["no ignore", "bad", "average", "good", "non-artifact"], sharp.Choices);
        sharp.SelectedIndex = (int)IgnoreLevel.Average;
        Assert.Equal(IgnoreLevel.Average, vm.Game.Ignore.QualityFor("sharp"));
    }

    [AvaloniaFact]
    public void TheKnowledgeWindow_IgnoresKinds()
    {
        var (_, vm) = Open();
        var objects = vm.CreateObjectKnowledge();
        objects.Selected = objects.Rows.First(r => r.Kind?.Id == "phase_door");
        Assert.True(objects.CanIgnore);
        objects.IsIgnored = true;
        Assert.True(vm.Game.IsKindIgnored(objects.Selected.Kind!));
        Assert.DoesNotContain(vm.PackRows, r => r.Item.Kind.Id == "phase_door");

        objects.Selected = objects.Rows.First(r => r.Kind?.Id == "dagger");
        Assert.False(objects.CanIgnore); // weapons go by quality, not kind
    }
}
