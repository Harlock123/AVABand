using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Views;

/// <summary>
/// Every dialog closes with Escape or the controller's B button (and has a button that does the
/// same), so none depends on the window manager's title bar — tiling setups such as
/// Hyprland/Omarchy show none.
/// </summary>
public static class DialogKeys
{
    private static readonly ConditionalWeakTable<Window, Func<bool>?> CancelFirst = new();
    private static readonly ConditionalWeakTable<Window, Func<bool>> Busy = new();

    /// <summary>
    /// Closes <paramref name="window"/> on Escape — after the focused control has had its say, so an
    /// open drop-down closes first. <paramref name="alsoEnter"/> makes Enter close it too (read-only
    /// pages); <paramref name="escapeFirst"/> can claim the key for something else (cancelling a
    /// key capture), returning true when it did.
    /// </summary>
    public static void CloseOnEscape(Window window, bool alsoEnter = false, Func<bool>? escapeFirst = null, Func<bool>? busy = null)
    {
        CancelFirst.AddOrUpdate(window, escapeFirst);
        if (busy is not null) Busy.AddOrUpdate(window, busy);
        window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Handled) return;
            if (e.Key == Key.Escape)
            {
                if (escapeFirst?.Invoke() != true) window.Close();
                e.Handled = true;
            }
            else if (alsoEnter && e.Key is Key.Enter or Key.Return)
            {
                window.Close();
                e.Handled = true;
            }
        }, RoutingStrategies.Bubble);
    }

    /// <summary>Whether the dialog is waiting on the keyboard (a key being bound), so the controller only backs out.</summary>
    public static bool IsBusy(Window window) => Busy.TryGetValue(window, out var busy) && busy();

    /// <summary>Whether this window is one of the dialogs set up with <see cref="CloseOnEscape"/>.</summary>
    public static bool IsDialog(Window window) => CancelFirst.TryGetValue(window, out _);

    /// <summary>
    /// The controller's B button in a dialog: closes an open drop-down, else whatever the dialog
    /// cancels first (a rebind in Settings), else the dialog — as Escape does.
    /// </summary>
    public static void Cancel(Window window)
    {
        if (window.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault(c => c.IsDropDownOpen) is { } open)
        {
            open.IsDropDownOpen = false;
            return;
        }
        if (CancelFirst.TryGetValue(window, out var first) && first?.Invoke() == true) return;
        window.Close();
    }
}
