using Angband.Core.Geometry;
using Angband.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// Locate (Angband do_cmd_locate, 'L' / roguelike 'W'): the view scrolls half a screen at a time in
/// any direction, naming the map sector shown and where it lies from yours; Escape (or any other
/// key) brings it back to the player.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private Loc _locateCentre;
    private bool _centreBeforeLocate;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isLocating;

    /// <summary>The map's size on screen in squares (set by the window; a fallback before it is shown).</summary>
    public Func<(int Cols, int Rows)> ViewportCells { get; set; } = () => (66, 22);

    public void BeginLocate()
    {
        _centreBeforeLocate = CenterPlayer;
        CenterPlayer = true; // the view follows the locate centre, whatever the panel option
        _locateCentre = _game.Player.Position;
        IsLocating = true;
        ShowLocate();
    }

    /// <summary>A key while locating: a direction scrolls; anything else ends it. Always taken.</summary>
    private void LocateAction(InputAction action)
    {
        if ((action.ToDirection() ?? action.ToRunDirection()) is { } dir && dir != Direction.Here)
        {
            var (cols, rows) = ViewportCells();
            var step = dir.Offset();
            var level = _game.Level;
            _locateCentre = new Loc(
                Math.Clamp(_locateCentre.X + step.X * Math.Max(1, cols / 2), cols / 2, Math.Max(cols / 2, level.Width - 1 - cols / 2)),
                Math.Clamp(_locateCentre.Y + step.Y * Math.Max(1, rows / 2), rows / 2, Math.Max(rows / 2, level.Height - 1 - rows / 2)));
            ShowLocate();
            return;
        }
        EndLocate();
    }

    private void EndLocate()
    {
        IsLocating = false;
        CenterPlayer = _centreBeforeLocate;
        LastMessage = "";
        OnPropertyChanged(nameof(Map));
        Revision++;
    }

    /// <summary>"Map sector [1,2], which is south-east of your sector.  Direction?"</summary>
    private void ShowLocate()
    {
        var (cols, rows) = ViewportCells();
        (int Y, int X) Sector(Loc centre) => (
            2 * Math.Max(0, centre.Y - rows / 2) / Math.Max(1, rows),
            2 * Math.Max(0, centre.X - cols / 2) / Math.Max(1, cols));
        var here = Sector(_locateCentre);
        var mine = Sector(_game.Player.Position);
        var ns = here.Y < mine.Y ? " north" : here.Y > mine.Y ? " south" : "";
        var ew = here.X < mine.X ? " west" : here.X > mine.X ? " east" : "";
        var relation = ns.Length + ew.Length == 0 ? "" : $"{ns}{(ns.Length > 0 && ew.Length > 0 ? "-" + ew.Trim() : ew)} of";
        LastMessage = $"Map sector [{here.Y},{here.X}], which is{relation} your sector.  Direction?";
        OnPropertyChanged(nameof(Map));
        Revision++;
    }
}
