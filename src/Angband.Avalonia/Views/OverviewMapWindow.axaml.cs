using Angband.Avalonia.Rendering;
using Angband.Avalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Angband.Avalonia.Views;

/// <summary>
/// The level map ('M'): the whole level squeezed to fit the window — the fewer squares per block,
/// the bigger the window. Esc, Enter or B closes it.
/// </summary>
public partial class OverviewMapWindow : Window
{
    /// <summary>The smallest a block may be drawn, in device-independent pixels, before blocks get bigger.</summary>
    public const double MinCellWidth = 6, MinCellHeight = 8;

    public OverviewMapWindow()
    {
        InitializeComponent();
        DialogKeys.CloseOnEscape(this, alsoEnter: true);
        Map.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty) Squeeze();
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Squeeze();
    }

    /// <summary>
    /// Picks the block size so the level fits (Angband display_map's ratio): each block at least a
    /// few pixels — its tile or letter at the smallest size that is still something to look at.
    /// </summary>
    private void Squeeze()
    {
        if (DataContext is not OverviewMapSource source || Map.Bounds.Width <= 0 || Map.Bounds.Height <= 0) return;
        var (cellWidth, cellHeight) = source.UseTiles && source.Tileset is { } t
            ? (Math.Max(MinCellWidth, t.TileWidth * 0.25), Math.Max(MinCellHeight, t.TileHeight * 0.25))
            : (Math.Max(MinCellWidth, new AsciiRenderer(source.FontSize).CellSize.Width), Math.Max(MinCellHeight, new AsciiRenderer(source.FontSize).CellSize.Height));
        var levelWidth = source.Width * source.Block;
        var levelHeight = source.Height * source.Block;
        var block = (int)Math.Ceiling(Math.Max(levelWidth * cellWidth / Map.Bounds.Width, levelHeight * cellHeight / Map.Bounds.Height));
        source.Block = Math.Max(1, block);
        Legend.Text = source.Block == 1
            ? "The whole level. Esc or B closes."
            : $"The whole level, {source.Block}×{source.Block} squares to a block (what matters most in each is shown). Esc or B closes.";
        Map.Revision++;
        Map.InvalidateVisual();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
