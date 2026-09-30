using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Data;
using Angband.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

/// <summary>Long lists scroll, with the mouse wheel, rather than run off the window.</summary>
public class ScrollingUiTests
{
    /// <summary>The first ScrollViewer inside a control.</summary>
    private static ScrollViewer ScrollerOf(Control c) => c.GetVisualDescendants().OfType<ScrollViewer>().First();

    private static void Wheel(Window window, Control over)
    {
        var middle = over.TranslatePoint(new Point(over.Bounds.Width / 2, over.Bounds.Height / 2), window)!.Value;
        window.MouseWheel(middle, new Vector(0, -3), RawInputModifiers.None);
        window.CaptureRenderedFrame();
    }

    [AvaloniaFact]
    public void A_long_prompt_fits_the_window_and_scrolls()
    {
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        var game = vm.Game;
        foreach (var kind in game.Data.Objects.Where(k => k.Base is "potion" or "scroll" && k.Commonness > 0).Select(k => k.Id))
            if (game.Player.Inventory.SlotsUsed < game.Player.Inventory.PackSize) game.Player.Inventory.Add(game.Objects.Create(kind));
        vm.Refresh();
        var window = new MainWindow { DataContext = vm, Width = 1100, Height = 480 };
        window.Show();
        window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.None); // drop which item? (all 23)
        window.CaptureRenderedFrame();
        Assert.True(vm.IsPrompting);
        var box = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PromptBox");
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "PromptList");
        var bottom = box.TranslatePoint(new Point(0, box.Bounds.Height), window)!.Value.Y;
        Assert.True(bottom <= window.Bounds.Height, $"the prompt runs to {bottom}, past the window's {window.Bounds.Height}");
        var scroller = ScrollerOf(list);
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height, "the list should need scrolling in a small window");
        Wheel(window, list);
        Assert.True(scroller.Offset.Y > 0);
        TileRenderingTests.Save(window, "long-prompt");
    }

    [AvaloniaFact]
    public void The_windows_long_lists_all_scroll()
    {
        // Every list that can grow is in a ScrollViewer (or is a ListBox, which has its own), none turned off.
        var views = typeof(MainWindow).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(Window)) && t.GetConstructor(Type.EmptyTypes) is not null);
        foreach (var type in views)
        {
            if (Activator.CreateInstance(type) is not Window w) continue;
            foreach (var items in w.GetLogicalDescendants().OfType<ItemsControl>())
            {
                if (items is ListBox { IsHitTestVisible: false }) Assert.Fail($"{type.Name}: {items.Name} can't take the mouse wheel");
                if (items is ListBox or Menu or MenuItem or TabControl or ComboBox) continue; // (these scroll, or hold a handful)
                var scrolls = items.GetLogicalAncestors().OfType<ScrollViewer>().Any() || items.GetLogicalAncestors().OfType<ListBox>().Any();
                if (!scrolls && !Short.Contains($"{type.Name}.{items.Name}"))
                    Assert.Fail($"{type.Name}: the list {items.Name ?? items.GetType().Name} isn't in anything that scrolls");
            }
            w.Close();
        }
    }

    /// <summary>Lists that are always short (a few lines, a handful of choices), by window and name.</summary>
    private static readonly HashSet<string> Short = ["GameOverWindow.", "GameOverWindow.ChoiceList", "MainWindow.StatusBar", "MainWindow.Hotbar",
        "MainWindow.", "CharacterCard."];
}
