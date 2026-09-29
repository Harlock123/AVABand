using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Angband.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

/// <summary>The hotbar: spells and items on Alt+1 .. Alt+0.</summary>
public class HotbarUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open(string cls)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, cls);
        TestKit.Give(vm);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        return (window, vm);
    }

    private static void Pick(MainWindowViewModel vm, string label)
    {
        var index = vm.MenuLabels.ToList().IndexOf(label);
        Assert.True(index >= 0, $"no '{label}' in [{string.Join(", ", vm.MenuLabels)}]");
        vm.PromptKey((char)('a' + index));
    }

    [AvaloniaFact]
    public void AnEmptySlot_IsFilledWithAnItem_ThenAltOneUsesIt_UntilNoneAreLeft()
    {
        var (window, vm) = Open("warrior");
        var game = vm.Game;
        Assert.Equal(10, vm.HotbarSlots.Count);
        Assert.All(vm.HotbarSlots, s => Assert.Equal("empty", s.Detail));
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8", "9", "0"], vm.HotbarSlots.Select(s => s.Key));

        window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Alt); // empty: what to put there?
        Assert.Equal(["Put an item here"], vm.MenuLabels); // a warrior has no spells
        Pick(vm, "Put an item here");
        Assert.Equal("Put which item in the hotbar?", vm.PromptTitle);
        var potion = vm.PromptRows.First(r => r.Item.Kind.Id == "cure_light_wounds");
        vm.PromptKey(potion.Letter[0]);
        Assert.False(vm.IsPrompting);
        Assert.StartsWith("Alt+1 now uses", vm.LastMessage);
        var slot = vm.HotbarSlots[0];
        Assert.Equal("!", slot.Glyph);
        Assert.Equal("x2", slot.Detail);
        Assert.True(slot.Available);

        game.Player.Hp = game.Player.MaxHp / 2;
        window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Alt);
        Assert.Equal("x1", vm.HotbarSlots[0].Detail);
        window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Alt);
        Assert.Equal("none", vm.HotbarSlots[0].Detail);
        Assert.False(vm.HotbarSlots[0].Available);
        window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Alt);
        Assert.StartsWith("You have no", vm.LastMessage);

        // Alt+Shift changes it; clearing empties it.
        window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Alt | RawInputModifiers.Shift);
        Pick(vm, "Clear this slot");
        Assert.Null(game.Hotbar[0]);
        Assert.Equal("empty", vm.HotbarSlots[0].Detail);
    }

    [AvaloniaFact]
    public void ASpellSlot_Casts_AndAClickOnAnEmptySlotOffersToFillIt()
    {
        var (window, vm) = Open("mage");
        var game = vm.Game;
        var first = game.ClassSpells.First(s => game.HasBookFor(s));
        if (!game.Player.LearnedSpells.Contains(first.Id)) game.Player.LearnedSpells.Add(first.Id);

        vm.OpenHotbarMenu(9);
        Assert.Equal("Hotbar slot 0:", vm.PromptTitle);
        Pick(vm, $"Put a {game.PlayerRealm!.SpellNoun} here");
        var row = vm.SpellPromptRows.Count > 0 ? vm.SpellPromptRows.First(r => r.Spell.Id == first.Id).Letter[0] : 'a';
        vm.PromptKey(row);
        if (vm.SpellPromptRows.Count > 0) vm.PromptKey(vm.SpellPromptRows.First(r => r.Spell.Id == first.Id).Letter[0]); // book first
        Assert.Equal(HotbarEntry.ForSpell(first.Id), game.Hotbar[9]);
        Assert.Equal(first.Name, vm.HotbarSlots[9].Label);

        var at = game.Level.AllLocs().First(l => l.DistanceTo(game.Player.Position) == 3 && game.Level.IsEmptyFloor(l)
            && Angband.Core.Combat.ProjectionPath.Projectable(game.Level, game.Player.Position, l, 20));
        var jackal = new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster("jackal")!, at, asleep: true)!;
        jackal.Hp = jackal.MaxHp = 10_000;
        game.UpdateView();
        var mana = game.Player.Mana;
        window.KeyPressQwerty(PhysicalKey.Digit0, RawInputModifiers.Alt);
        Assert.True(game.Player.Mana < mana || vm.IsAwaitingDirection, vm.LastMessage); // cast, or asking where

        if (vm.IsAwaitingDirection) window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "hotbar");
        var slots = window.GetVisualDescendants().OfType<Border>().Where(b => b.DataContext is HotbarSlotRow).ToList();
        var second = slots.First(b => ((HotbarSlotRow)b.DataContext!).Index == 1);
        var centre = second.TranslatePoint(new Point(second.Bounds.Width / 2, second.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Assert.Equal("Hotbar slot 2:", vm.PromptTitle);
        Assert.True(vm.IsPrompting);
    }

    private static Point SlotCentre(MainWindow window, int index)
    {
        window.CaptureRenderedFrame();
        var slot = window.GetVisualDescendants().OfType<Border>().First(b => b.DataContext is HotbarSlotRow r && r.Index == index);
        return slot.TranslatePoint(new Point(slot.Bounds.Width / 2, slot.Bounds.Height / 2), window)!.Value;
    }

    private static void Drop(MainWindow window, int slot, string payload)
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(MainWindow.HotbarFormat, payload));
        var at = SlotCentre(window, slot);
        window.DragDrop(at, global::Avalonia.Input.Raw.RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
        window.DragDrop(at, global::Avalonia.Input.Raw.RawDragEventType.DragOver, data, DragDropEffects.Copy, RawInputModifiers.None);
        window.DragDrop(at, global::Avalonia.Input.Raw.RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
    }

    [AvaloniaFact]
    public void DroppingAnItem_OrASpell_OnASlot_FillsIt_AndSlotsSwapByDragging()
    {
        var (window, vm) = Open("mage");
        var game = vm.Game;
        var potion = game.Player.Inventory.Pack.First(i => i.Kind.Id == "cure_light_wounds");
        Drop(window, 2, MainWindowViewModel.DragPayload(potion));
        Assert.Equal(HotbarEntry.ForKind("cure_light_wounds"), game.Hotbar[2]);
        Assert.Equal("x2", vm.HotbarSlots[2].Detail);

        var spell = game.ClassSpells.First(s => game.HasBookFor(s));
        if (!game.Player.LearnedSpells.Contains(spell.Id)) game.Player.LearnedSpells.Add(spell.Id);
        vm.BeginSpellPrompt(SpellPromptKind.Cast); // dragged out of the spell list: the list closes
        Drop(window, 5, MainWindowViewModel.DragPayload(spell));
        Assert.False(vm.IsPrompting);
        Assert.Equal(HotbarEntry.ForSpell(spell.Id), game.Hotbar[5]);

        Drop(window, 5, MainWindowViewModel.DragPayload(2)); // slot 3 onto slot 6: they swap
        Assert.Equal(HotbarEntry.ForKind("cure_light_wounds"), game.Hotbar[5]);
        Assert.Equal(HotbarEntry.ForSpell(spell.Id), game.Hotbar[2]);

        // Armour can't be used from the hotbar; a spell not learned isn't taken.
        var armour = game.Player.Inventory.Add(game.Objects.Create("soft_leather_armour"))!;
        Drop(window, 0, MainWindowViewModel.DragPayload(armour));
        Assert.Null(game.Hotbar[0]);
        Assert.EndsWith("can't be used from the hotbar.", vm.LastMessage);
        var unknown = game.ClassSpells.First(s => !game.Player.LearnedSpells.Contains(s.Id));
        Assert.False(vm.DropOnHotbar(0, MainWindowViewModel.DragPayload(unknown)));
    }

    [AvaloniaFact]
    public void AGamepad_StepsThroughTheHotbar_UsesASlot_AndOpensTheMapMenu()
    {
        var (window, vm) = Open("warrior");
        var game = vm.Game;
        game.SetHotbar(1, HotbarEntry.ForKind("cure_light_wounds"));
        vm.Execute(new HoldCommand());
        Assert.DoesNotContain(vm.HotbarSlots, s => s.Selected); // no cursor until the gamepad moves it

        vm.HandleAction(InputAction.HotbarNext);
        Assert.True(vm.HotbarSlots[1].Selected);
        Assert.StartsWith("Hotbar slot 2: ", vm.LastMessage);
        vm.HandleAction(InputAction.HotbarPrevious);
        vm.HandleAction(InputAction.HotbarPrevious); // wraps round to the tenth
        Assert.True(vm.HotbarSlots[9].Selected);
        vm.HandleAction(InputAction.HotbarNext);
        vm.HandleAction(InputAction.HotbarNext);
        vm.HandleAction(InputAction.HotbarUse);
        Assert.Equal("x1", vm.HotbarSlots[1].Detail);

        vm.HandleAction(InputAction.ContextMenu); // not looking: the menu for you
        Assert.Contains("Rest", vm.MenuLabels);
        vm.CancelPrompt();

        var at = game.Level.AllLocs().First(l => l.DistanceTo(game.Player.Position) == 2 && game.Level.IsEmptyFloor(l)
            && Angband.Core.Combat.ProjectionPath.Projectable(game.Level, game.Player.Position, l, 20));
        new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster("jackal")!, at, asleep: true);
        game.UpdateView();
        vm.HandleAction(InputAction.Look); // the cursor on the jackal
        Assert.Equal(at, vm.Cursor);
        vm.HandleAction(InputAction.ContextMenu);
        Assert.False(vm.IsLooking);
        Assert.Contains("Recall info", vm.MenuLabels);
        Assert.Contains("Throw to", vm.MenuLabels);
    }
}
