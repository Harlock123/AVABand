using Avalonia;
using Avalonia.Controls;

namespace Angband.Avalonia.Views;

/// <summary>
/// Keeps a dialog no bigger than the screen it opens on. A window asked for taller than the screen
/// (a 900-high sheet at 200% on an 824-high display) is not just cut short everywhere: tiling
/// window managers such as Hyprland shrink an oversized floating XWayland window to a fraction of
/// its size, leaving tiny, unreadable contents. So the dialog is sized to fit before it is shown;
/// its contents scroll as they do in any smaller window.
/// </summary>
public static class DialogFit
{
    /// <summary>Room left around a dialog for the bar, borders and gaps, in device-independent pixels.</summary>
    public const double Margin = 48;

    /// <summary>The interface size (Settings → Display): dialogs are drawn this much larger.</summary>
    public static double InterfaceScale { get; set; } = 1.0;

    /// <summary>Scales <paramref name="dialog"/> to the interface size, shrinks it to fit the screen <paramref name="owner"/> is on, then shows it.</summary>
    public static void Show(Window dialog, Window owner)
    {
        Scale(dialog, InterfaceScale);
        FitTo(dialog, AvailableSize(owner));
        dialog.Show(owner);
    }

    /// <summary>Draws a dialog's contents <paramref name="scale"/> times larger, and the window with them.</summary>
    public static void Scale(Window dialog, double scale)
    {
        if (Math.Abs(scale - 1) < 0.01 || dialog.Content is not Control content) return;
        dialog.Content = null;
        dialog.Content = new LayoutTransformControl { LayoutTransform = new global::Avalonia.Media.ScaleTransform(scale, scale), Child = content };
        if (!double.IsNaN(dialog.Width)) dialog.Width *= scale;
        if (!double.IsNaN(dialog.Height)) dialog.Height *= scale;
        dialog.MinWidth *= scale;
        dialog.MinHeight *= scale;
    }

    /// <summary>Shrinks the dialog's requested size to at most <paramref name="available"/>.</summary>
    public static void FitTo(Window dialog, Size? available)
    {
        if (available is not { } room) return;
        var width = Math.Max(room.Width - Margin, dialog.MinWidth);
        var height = Math.Max(room.Height - Margin, dialog.MinHeight);
        if (!double.IsNaN(dialog.Width) && dialog.Width > width) dialog.Width = width;
        if (!double.IsNaN(dialog.Height) && dialog.Height > height) dialog.Height = height;
    }

    /// <summary>The working area of the owner's screen in device-independent pixels, if it is known.</summary>
    public static Size? AvailableSize(Window owner)
    {
        var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        if (screen is null) return null;
        var area = screen.WorkingArea.Width > 0 && screen.WorkingArea.Height > 0 ? screen.WorkingArea : screen.Bounds;
        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
        return area.Width <= 0 || area.Height <= 0 ? null : new Size(area.Width / scaling, area.Height / scaling);
    }
}
