using Angband.Core.Geometry;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Angband.Avalonia.ViewModels;

// AVABand's own: looking around the map without moving. Hold Ctrl or Alt (Cmd or Option on a Mac) with
// the arrows or the keypad, hold Shift and turn the wheel (or scroll with two fingers on a trackpad), drag
// with the middle mouse button, or push the gamepad's right stick, and the view slides
// over what you've seen of the level — the remembered squares lit a little brighter while you look,
// a marker at the view's centre and the status line naming what's there. Let go, and it comes back to
// you. Nothing moves and no time passes.
public sealed partial class MainWindowViewModel
{
    /// <summary>Squares the view slides with each step of a key or the stick.</summary>
    public const int PeekStep = 3;

    private Loc _peekCentre;
    private bool _centreBeforePeek;

    /// <summary>Looking around: the view is away from you (the map draws a border and a marker).</summary>
    [ObservableProperty] private bool _isPeeking;

    /// <summary>The label over the map while looking around.</summary>
    public string PeekHint => "Looking around — let go (of Ctrl, Alt or Shift, the mouse button or the stick) to come back";

    /// <summary>The square at the middle of the view while looking around.</summary>
    public Loc PeekCentre => _peekCentre;

    /// <summary>A step of looking around, the way given (starting it if it hasn't started).</summary>
    public void Peek(Direction direction)
    {
        if (direction == Direction.Here) return;
        var step = direction.Offset();
        PeekBy(step.X * PeekStep, step.Y * PeekStep);
    }

    /// <summary>Slides the view by so many squares (a drag of the mouse, a step of a key).</summary>
    public void PeekBy(int dx, int dy)
    {
        if (!IsPeeking)
        {
            if (IsPrompting || IsLocating || IsLooking || _game.Player.IsDead) return;
            _centreBeforePeek = CenterPlayer;
            CenterPlayer = true; // the view follows the peek centre, whatever the panel option
            _peekCentre = _game.Player.Position;
            IsPeeking = true;
        }
        var level = _game.Level;
        _peekCentre = new Loc(Math.Clamp(_peekCentre.X + dx, 0, level.Width - 1), Math.Clamp(_peekCentre.Y + dy, 0, level.Height - 1));
        var what = _game.Level.Monsters.At(_peekCentre) is { IsVisible: true } m
            ? Capitalize(_game.LookDescription(m)) + "."
            : _peekCentre == _game.Player.Position ? "You are here." : DescribeSquare(_peekCentre);
        LastMessage = $"Looking around: {what}";
        OnPropertyChanged(nameof(Map));
        Revision++;
    }

    /// <summary>Let go: the view comes back to you.</summary>
    public void EndPeek()
    {
        if (!IsPeeking) return;
        IsPeeking = false;
        CenterPlayer = _centreBeforePeek;
        LastMessage = "";
        RecentreCount++;
        OnPropertyChanged(nameof(Map));
        Revision++;
    }
}
