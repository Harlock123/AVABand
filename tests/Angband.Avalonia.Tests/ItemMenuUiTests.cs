using Angband.Avalonia.ViewModels;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Data;
using Angband.Input;
using Angband.Avalonia.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

/// <summary>
/// The item menu (Angband 4.2.5 ui-context.c context_menu_object) — right-click in the sidebar, or
/// 'i' / 'e' — and AVABand's "Clear out junk" checklist.
/// </summary>
public class ItemMenuUiTests
{
    private static MainWindowViewModel Start()
    {
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        TestKit.Give(vm);
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        return vm;
    }

    private static Item Carry(MainWindowViewModel vm, string kind, int count = 1)
    {
        var item = vm.Game.Objects.Create(kind, count);
        vm.Game.Knowledge.LearnKind(item.Kind);
        var carried = vm.Game.Player.Inventory.Add(item)!;
        vm.Refresh();
        return carried;
    }

    private static void Choose(MainWindowViewModel vm, string label) =>
        vm.PromptKey((char)('a' + vm.MenuLabels.ToList().FindIndex(l => l == label || l.StartsWith(label))));

    [AvaloniaFact]
    public void A_potion_offers_what_4_2_5_offers_and_quaffing_from_the_menu_drinks_it()
    {
        var vm = Start();
        var potion = Carry(vm, "cure_serious_wounds");
        vm.OpenItemMenu(potion);
        Assert.StartsWith("A Potion of Cure Serious Wounds", vm.PromptTitle);
        Assert.Equal(["Inspect", "Quaff", "Drop", "Throw", "Inscribe", "Ignore"], vm.MenuLabels);
        Choose(vm, "Quaff");
        Assert.False(vm.Game.Player.Inventory.Contains(potion));
    }

    [AvaloniaFact]
    public void A_stack_can_be_dropped_one_at_a_time_or_all_at_once()
    {
        var vm = Start();
        var flasks = vm.Game.Player.Inventory.Pack.Single(i => i.Kind.Id == "flask_of_oil");
        vm.OpenItemMenu(flasks);
        Assert.Contains("Drop all", vm.MenuLabels);
        Choose(vm, "Drop");
        Assert.Equal(1, flasks.Number);
    }

    [AvaloniaFact]
    public void Sticky_gear_offers_no_way_off()
    {
        var vm = Start();
        var game = vm.Game;
        var ring = game.Objects.Create("ring_of_protection");
        ring.Flags.Add("STICKY"); // as a sticky curse makes it
        game.Player.Inventory.Wield(ring, () => game.Objects.NextSerial++);
        vm.Refresh();
        vm.OpenItemMenu(ring);
        Assert.DoesNotContain("Take off", vm.MenuLabels);
        Assert.DoesNotContain("Throw", vm.MenuLabels);

        vm.CancelPrompt();
        var weapon = game.Player.Inventory.Weapon!;
        vm.OpenItemMenu(weapon);
        Assert.Contains("Take off", vm.MenuLabels);
        Assert.Contains("Throw", vm.MenuLabels); // a wielded weapon can be thrown (4.2.5 obj_can_throw)
    }

    [AvaloniaFact]
    public void I_and_e_pick_an_item_for_its_menu()
    {
        var vm = Start();
        vm.HandleAction(InputAction.Inventory);
        Assert.Equal("Inventory — which item?", vm.PromptTitle);
        var potion = vm.PromptRows.First(r => r.Item.Kind.Id == "cure_light_wounds");
        vm.PromptKey(potion.Letter[0]);
        Assert.StartsWith("2 Potions of Cure Light Wounds", vm.PromptTitle);

        vm.CancelPrompt();
        vm.HandleAction(InputAction.Equipment);
        Assert.Equal("Equipment — which item?", vm.PromptTitle);
        Assert.All(vm.PromptRows, r => Assert.Contains(r.Item, vm.Game.Player.Inventory.Equipped));
    }

    [AvaloniaFact]
    public void Ignore_from_the_menu_opens_the_ignore_choices()
    {
        var vm = Start();
        var potion = Carry(vm, "cure_serious_wounds");
        vm.OpenItemMenu(potion);
        Choose(vm, "Ignore");
        Assert.StartsWith("Ignore a Potion of Cure Serious Wounds", vm.PromptTitle);
        Assert.Contains(vm.ChoiceRows, r => r.Text == "This item only");
    }

    [AvaloniaFact]
    public void Clearing_out_junk_ignores_the_ticked_and_leaves_the_rest()
    {
        var vm = Start();
        var game = vm.Game;
        var sleep = Carry(vm, "sleep");                // a Potion of Sleep: worthless once known
        var flesh = Carry(vm, "scrap_of_flesh");        // worthless too
        var dagger = game.Objects.Create("dagger");     // cursed, but still worth a lot
        dagger.ToHit = dagger.ToDam = 20;
        Assert.True(game.Objects.AddCurse(new Angband.Core.Randomness.GameRandom(1), dagger, game.Data.Curse("teleportation")!, 1));
        game.Knowledge.LearnRune(RuneIds.Curse("teleportation"));
        game.Player.Inventory.Add(dagger);
        var keep = Carry(vm, "confusion");               // worthless, but inscribed !k
        keep.Note = "!k";
        vm.Refresh();

        vm.HandleAction(InputAction.ClearJunk);
        var labels = vm.MenuLabels;
        Assert.True(labels[0] == "Ignore the 2 ticked", string.Join(" | ", labels));
        Assert.Contains(labels, l => l.StartsWith("[x]") && l.Contains("Sleep") && l.EndsWith("worthless"));
        Assert.Contains(labels, l => l.StartsWith("[x]") && l.Contains("Flesh"));
        Assert.Contains(labels, l => l.StartsWith("[ ]") && l.Contains("Dagger") && l.Contains("cursed (teleportation), but worth"));
        Assert.Equal(4, labels.Count);

        // Letters toggle; the list comes back with the change, the cursor where it was.
        var daggerRow = labels.ToList().FindIndex(l => l.Contains("Dagger"));
        vm.PromptKey((char)('a' + daggerRow));
        Assert.StartsWith("[x]", vm.MenuLabels[daggerRow]);
        Assert.Equal(daggerRow, vm.PromptSelectedIndex);
        var fleshRow = vm.MenuLabels.ToList().FindIndex(l => l.Contains("Flesh"));
        vm.PromptKey((char)('a' + fleshRow));
        Assert.Equal("Ignore the 2 ticked", vm.MenuLabels[0]);

        vm.PromptKey('a');
        var pack = game.Player.Inventory.Pack;
        Assert.DoesNotContain(sleep, pack);
        Assert.DoesNotContain(dagger, pack);
        Assert.Contains(flesh, pack);
        Assert.Contains(keep, pack);
        Assert.True(game.IsMarkedIgnored(sleep) && game.IsMarkedIgnored(dagger));
    }

    [AvaloniaFact]
    public void Right_clicking_an_item_in_the_sidebar_opens_its_menu()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = Start();
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        window.CaptureRenderedFrame();
        var row = window.GetVisualDescendants().OfType<Control>()
            .First(c => c.DataContext is ItemRow r && r.Item.Kind.Id == "cure_light_wounds" && c.Bounds.Height > 0 && c is Grid);
        var centre = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Right);
        window.MouseUp(centre, MouseButton.Right);
        Assert.True(vm.IsPrompting);
        Assert.StartsWith("2 Potions of Cure Light Wounds", vm.PromptTitle);
        Assert.Contains("Quaff", vm.MenuLabels);
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "item-menu");
    }

    [AvaloniaFact]
    public void Flasks_offer_Refill_only_with_a_lantern_to_fill()
    {
        var vm = Start();
        var game = vm.Game;
        var flask = game.Player.Inventory.Pack.First(i => i.IsFuel);
        vm.OpenItemMenu(flask);
        Assert.DoesNotContain("Refill", vm.MenuLabels); // a torch can't take oil
        vm.CancelPrompt();

        var lantern = game.Objects.Create("lantern");
        lantern.Fuel = 1000;
        game.Player.Inventory.Add(lantern);
        game.Execute(new WieldCommand(lantern));
        vm.Refresh();
        vm.OpenItemMenu(flask);
        Choose(vm, "Refill");
        Assert.True(lantern.Fuel > 8000);
    }

    [AvaloniaFact]
    public void With_no_junk_it_says_so()
    {
        var vm = Start();
        vm.HandleAction(InputAction.ClearJunk);
        Assert.False(vm.IsPrompting);
        Assert.Contains("nothing that looks like junk", vm.LastMessage);
    }

    /// <summary>AVABand's bracers: a gem's menu sets it into bracers with room, and the bracers' menu takes a gem.</summary>
    [AvaloniaFact]
    public void A_gem_is_set_into_bracers_from_either_menu()
    {
        var vm = Start();
        var gem = Carry(vm, "ruby");
        vm.OpenItemMenu(gem);
        Assert.DoesNotContain(vm.MenuLabels, l => l.StartsWith("Set into"));
        vm.PromptKey('\u001b');

        var bracers = Carry(vm, "leather_bracers");
        vm.Execute(new WieldCommand(bracers));
        var worn = vm.Game.Player.Inventory.InSlot(EquipSlot.Arms)!;
        vm.OpenItemMenu(gem);
        Assert.Contains("Set into your Pair of Leather Bracers", vm.MenuLabels);
        Choose(vm, "Set into your");
        Assert.Single(worn.Gems);
        Assert.Contains(vm.EquipmentRows, r => r.Name.Contains("Leather Bracers") && r.Name.Contains("Ruby"));

        var bigger = Carry(vm, "iron_bracers");
        var sapphire = Carry(vm, "sapphire");
        vm.OpenItemMenu(bigger);
        Assert.Contains("Set Sapphire here", vm.MenuLabels);
        Choose(vm, "Set Sapphire here");
        Assert.Single(bigger.Gems);
        Assert.False(vm.Game.Player.Inventory.Contains(sapphire));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        TileRenderingTests.Save(window, "bracers-and-gems");
    }

    /// <summary>AVABand's Humans: a light weapon's menu offers the off hand, and the sidebar names it so.</summary>
    [AvaloniaFact]
    public void A_Human_can_wield_a_second_weapon_from_its_menu()
    {
        var vm = Start(); // a Human warrior
        var dagger = Carry(vm, "main_gauche");
        vm.OpenItemMenu(dagger);
        Assert.Contains("Wield in off hand", vm.MenuLabels);
        Choose(vm, "Wield in off hand");
        Assert.Equal("main_gauche", vm.Game.OffHand?.Kind.Id);
        Assert.Contains(vm.EquipmentRows, r => r.Letter.StartsWith("off hand") && r.Name.Contains("Main Gauche"));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        TileRenderingTests.Save(window, "dual-wield");
    }

    /// <summary>The sidebar names the burden and what it costs.</summary>
    [AvaloniaFact]
    public void The_sidebar_names_the_burden()
    {
        var vm = Start();
        Assert.DoesNotContain("speed)", vm.BurdenText);
        while (vm.Game.BurdenPenalty < 2) { vm.Game.Player.Inventory.Add(vm.Game.Objects.Create("flask_of_oil", 1)); vm.Game.RecalculateBonuses(); }
        vm.Refresh();
        Assert.Contains($"· Strained (-{vm.Game.BurdenPenalty} speed)", vm.BurdenText);
        Assert.Contains("Strained", vm.StatusText);
    }

    /// <summary>The shops' comparison, anywhere: inspecting something in the pack, and looking at it on the floor.</summary>
    [AvaloniaFact]
    public void Inspecting_or_looking_at_gear_says_whether_it_suits_you()
    {
        var vm = Start(); // wielding a dagger
        var axe = Carry(vm, "main_gauche"); // (light: as many blows as the dagger, bigger dice)
        vm.Game.Knowledge.LearnRune(Angband.Core.Definitions.RuneIds.ToHit);
        vm.Game.Knowledge.LearnRune(Angband.Core.Definitions.RuneIds.ToDam);
        vm.OpenItemMenu(axe);
        Choose(vm, "Inspect");
        Assert.Contains(vm.Messages, m => m.Contains("Better — vs your Dagger: +") && m.Contains("damage a turn"));

        // On the floor, seen by looking.
        var game = vm.Game;
        var spot = game.Level.AllLocs().First(l => l.ChebyshevTo(game.Player.Position) == 1 && game.Level.IsEmptyFloor(l));
        var cap = game.Objects.Create("metal_cap");
        game.Knowledge.LearnKind(cap.Kind);
        game.Level.Objects.Add(spot, cap);
        game.UpdateView();
        vm.Refresh();
        vm.HandleAction(InputAction.Look);
        for (var i = 0; i < 10 && !vm.LastMessage.Contains("Metal Cap"); i++) vm.CursorKey(" ");
        Assert.Contains("for your empty helm slot", vm.LastMessage);
    }

    /// <summary>The Wear/Wield prompt: each choice with the shops' note — better or worse than what it would replace.</summary>
    [AvaloniaFact]
    public void The_wear_and_wield_prompt_says_whether_each_suits_you()
    {
        var vm = Start(); // wielding a dagger
        Carry(vm, "main_gauche");
        var cap = Carry(vm, "metal_cap");
        vm.Game.Knowledge.LearnRune(Angband.Core.Definitions.RuneIds.ToHit);
        vm.Game.Knowledge.LearnRune(Angband.Core.Definitions.RuneIds.ToDam);
        vm.HandleAction(InputAction.Wield);
        Assert.Equal("Wear or wield which item?", vm.PromptTitle);
        var gauche = vm.PromptRows.Single(r => r.Item.Kind.Id == "main_gauche");
        Assert.StartsWith("Better — vs your Dagger: +", gauche.Advice);
        Assert.StartsWith(gauche.Name + ". Better", gauche.Spoken);
        var capRow = vm.PromptRows.Single(r => r.Item == cap);
        Assert.True(capRow.HasAdvice);
        Assert.Contains("helm slot", capRow.Advice);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "wield-prompt");
        vm.CancelPrompt();

        // Other prompts carry no notes.
        vm.HandleAction(InputAction.Drop);
        Assert.All(vm.PromptRows, r => Assert.False(r.HasAdvice));
        vm.CancelPrompt();
    }

    /// <summary>A full pack says so in the sidebar, and standing on something that won't fit brings a hint.</summary>
    [AvaloniaFact]
    public void A_full_pack_is_shown_and_explained()
    {
        var vm = Start();
        var game = vm.Game;
        foreach (var kind in game.Data.Objects.Where(k => k.Base is "potion" or "scroll" && k.Commonness > 0).Select(k => k.Id))
            if (game.Player.Inventory.SlotsUsed < game.Player.Inventory.PackSize - 1) game.Player.Inventory.Add(game.Objects.Create(kind));
        vm.Refresh();
        Assert.EndsWith("(1 left)", vm.BurdenText);
        game.Player.Inventory.Add(game.Objects.Create("shovel")); // (nothing it can join)
        vm.Refresh();
        Assert.EndsWith("(full)", vm.BurdenText);

        vm.DismissHint();
        game.Level.Objects.Add(game.Player.Position, game.Objects.Create("pick")); // (nothing in the pack it could join)
        vm.Execute(new HoldCommand());
        Assert.StartsWith("Your pack is full, so you can't pick this up.", vm.HintText);
    }
}
