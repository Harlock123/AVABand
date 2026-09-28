using Angband.Core.Geometry;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// Hover to look (AVABand's own): the square under the mouse is described in a small label at the
/// foot of the map — the monster there, or what you see or remember — without touching the
/// message line. Off with the option "Describe the square under the mouse".
/// </summary>
public sealed partial class MainWindowViewModel
{
    private Loc? _hoverCell;

    /// <summary>What is under the mouse, or null (the label is hidden).</summary>
    [ObservableProperty] private string? _hoverText;

    public void HoverCell(Loc? cell)
    {
        _hoverCell = cell;
        UpdateHover();
    }

    /// <summary>Redescribes the hovered square (after the mouse moves, and after each turn).</summary>
    private void UpdateHover()
    {
        if (_hoverCell is not { } p || !OptionValue(DisplayOptions.HoverLook) || !_game.Level.InBounds(p) || !_game.Known.IsKnown(p)
            && _game.Level.Monsters.At(p) is not { IsVisible: true })
        {
            HoverText = null;
            return;
        }
        if (_game.Level.Monsters.At(p) is { IsVisible: true, Camouflaged: false } monster && !_game.IsHallucinating)
        {
            var text = _game.LookDescription(monster);
            HoverText = char.ToUpperInvariant(text[0]) + text[1..];
            return;
        }
        var square = DescribeSquare(p);
        HoverText = square.StartsWith("You see ", StringComparison.Ordinal) ? Capitalize(square[8..].TrimEnd('.'))
            : square.StartsWith("You are on ", StringComparison.Ordinal) ? "You, on " + square[11..].TrimEnd('.')
            : square.TrimEnd('.');
    }
}
