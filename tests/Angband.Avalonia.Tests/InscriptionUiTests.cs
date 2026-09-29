using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Data;
using Angband.Input;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

public class InscriptionUiTests
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
    public void Brace_AsksForAnItem_ThenTheText()
    {
        var (window, vm) = Open();
        var potion = vm.Game.Player.Inventory.Pack.First(i => i.Kind.Id == "cure_light_wounds");

        vm.HandleAction(InputAction.Inscribe);
        Assert.Equal("Inscribe which item?", vm.PromptTitle);
        vm.PromptKey(vm.PromptRows.Single(r => r.Item == potion).Letter[0]);
        Assert.True(vm.IsInscribing);

        // The text box has the keyboard: letters are text, not commands.
        var box = window.FindControl<TextBox>("InscriptionBox")!;
        window.KeyTextInput("@q1");
        Assert.Equal("@q1", box.Text);
        TileRenderingTests.Save(window, "inscription-entry");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.False(vm.IsInscribing);
        Assert.Equal("@q1", potion.Note);
        Assert.Contains(vm.PackRows, r => r.Name.EndsWith("{@q1}"));

        // '}' takes it off again.
        vm.HandleAction(InputAction.Uninscribe);
        vm.PromptKey(vm.PromptRows.Single(r => r.Item == potion).Letter[0]);
        Assert.Null(potion.Note);
    }

    [AvaloniaFact]
    public void Escape_LeavesTheInscriptionAlone()
    {
        var (window, vm) = Open();
        var potion = vm.Game.Player.Inventory.Pack.First(i => i.Kind.Id == "cure_light_wounds");
        potion.Note = "keep";
        vm.HandleAction(InputAction.Inscribe);
        vm.PromptKey(vm.PromptRows.Single(r => r.Item == potion).Letter[0]);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(vm.IsInscribing);
        Assert.Equal("keep", potion.Note);
    }

    [AvaloniaFact]
    public void ADigit_PicksTheItemInscribedForIt()
    {
        var (window, vm) = Open();
        var phase = vm.Game.Player.Inventory.Pack.First(i => i.Kind.Id == "phase_door");
        phase.Note = "@r1";
        var count = phase.Number;
        window.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.None);
        Assert.True(vm.IsPrompting);
        window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.None);
        Assert.False(vm.IsPrompting);
        Assert.Equal(count - 1, vm.Game.Player.Inventory.Pack.FirstOrDefault(i => i.Kind.Id == "phase_door")?.Number ?? 0);
    }

    [AvaloniaFact]
    public void BangD_AsksBeforeDropping()
    {
        var (window, vm) = Open();
        var food = vm.Game.Player.Inventory.Pack.First(i => i.Kind.Id == "ration_of_food");
        food.Note = "!d";
        vm.HandleAction(InputAction.Drop);
        vm.PromptKey(vm.PromptRows.Single(r => r.Item == food).Letter[0]);
        Assert.True(vm.IsConfirming);
        Assert.StartsWith("Really drop", vm.LastMessage);

        window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);
        Assert.True(vm.Game.Player.Inventory.Contains(food));

        vm.HandleAction(InputAction.Drop);
        vm.PromptKey(vm.PromptRows.Single(r => r.Item == food).Letter[0]);
        window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
        Assert.False(vm.Game.Player.Inventory.Contains(food));
    }

    [AvaloniaFact]
    public void TheKnowledgeWindow_SetsAutoInscriptions()
    {
        var (_, vm) = Open();
        var objects = vm.CreateObjectKnowledge();
        var row = objects.Rows.First(r => r.Kind?.Id == "cure_light_wounds");
        objects.Selected = row;
        Assert.True(objects.CanAutoInscribe);
        objects.AutoInscription = "@q9";
        objects.SetAutoInscription();
        Assert.Equal("@q9", vm.Game.Knowledge.KindNote(row.Kind!));
        Assert.All(vm.Game.Player.Inventory.Pack.Where(i => i.Kind == row.Kind), i => Assert.Equal("@q9", i.Note));
        Assert.Contains(vm.PackRows, r => r.Name.EndsWith("{@q9}"));
    }
}

public class ThrowingUiTests
{
    [AvaloniaFact]
    public void AThrowingWeaponInscribedAtV_JoinsTheQuiver_AndLeadsTheThrowMenu()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42);
        TestKit.Give(vm);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();

        var axe = vm.Game.Objects.Create("throwing_axe", 2);
        vm.Game.Player.Inventory.Add(axe);
        vm.Execute(new Angband.Core.Game.InscribeCommand(axe, "@v1"));
        Assert.Contains(vm.QuiverRows, r => r.Item == axe);

        // Something to throw at.
        var game = vm.Game;
        var p = game.Player.Position;
        var spot = game.Level.AllLocs().First(l => l.DistanceTo(p) is > 1 and < 4 && game.Level.IsEmptyFloor(l)
                                                   && game.Level[l].Has(Angband.Core.World.SquareFlags.Seen));
        var jackal = new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster("jackal")!, spot, asleep: true)!;
        jackal.Hp = jackal.MaxHp = 10_000;
        game.UpdateView();

        vm.HandleAction(InputAction.Throw);
        Assert.Same(axe, vm.PromptRows[0].Item);
        Assert.Same(vm.Game.Player.Inventory.Weapon, vm.PromptRows[^1].Item); // the wielded weapon, last
        TileRenderingTests.Save(window, "throw-menu");
        window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.None); // @v1
        Assert.False(vm.IsPrompting);
        Assert.Equal(1, axe.Number);
    }
}
