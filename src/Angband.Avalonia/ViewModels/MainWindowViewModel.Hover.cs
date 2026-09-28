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
        UpdatePath();
    }

    // --- Showing what a click or a shot would do --------------------------------------------------

    public IReadOnlyList<Loc> ShownPath { get; private set; } = [];
    public bool PathIsAim { get; private set; }

    /// <summary>Whether Shift is held: the travel route shows only then.</summary>
    public bool RouteKeyHeld { get; private set; }

    /// <summary>Shift pressed or let go (the view reports it, from keys and from the mouse).</summary>
    public void SetRouteKeyHeld(bool held)
    {
        if (held == RouteKeyHeld) return;
        RouteKeyHeld = held;
        UpdatePath();
    }

    /// <summary>The longest route a hover shows (a travel of more is still allowed).</summary>
    public const int MaxShownPath = 120;

    /// <summary>
    /// What the map shows beyond itself: in look or target mode, the line a shot would take from you
    /// to the cursor, up to where it would stop (Angband's target path); otherwise, while Shift is
    /// held with the mouse over a known square, the route a click would travel (none if there is
    /// no known way).
    /// </summary>
    private void UpdatePath()
    {
        var old = ShownPath;
        var player = _game.Player.Position;
        if (IsLooking && _cursor != player)
        {
            var level = _game.Level;
            ShownPath = Angband.Core.Combat.ProjectionPath.Compute(level, player, _cursor, Angband.Core.Game.GameSession.MaxRange,
                Angband.Core.Combat.PathFlags.StopAtCreature, p => level.Monsters.At(p) is { IsVisible: true });
            PathIsAim = true;
        }
        else if (RouteKeyHeld && !IsLooking && !IsPrompting && !IsInStore && _hoverCell is { } to && to != player
                 && OptionValue(DisplayOptions.MouseMovement) && _game.Level.InBounds(to) && _game.Known.IsKnown(to)
                 && _game.FindPath(player, to) is { Count: > 0 and <= MaxShownPath } route)
        {
            ShownPath = route;
            PathIsAim = false;
        }
        else ShownPath = [];
        if (!old.SequenceEqual(ShownPath)) Revision++;
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
