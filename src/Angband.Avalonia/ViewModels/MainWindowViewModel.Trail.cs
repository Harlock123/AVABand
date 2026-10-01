using Angband.Core.Geometry;

namespace Angband.Avalonia.ViewModels;

// "Where have I been?" (AVABand's own; Ctrl+B, Show where you've been): after a run, a teleport, a
// trap door or time away, the squares you last stood on on this level are dotted on the map for a
// moment — the older half dim, the newer bright — and the message says where the trail began.
public sealed partial class MainWindowViewModel
{
    /// <summary>How many squares the trail remembers.</summary>
    public const int TrailLength = 40;

    private readonly List<Loc> _trail = [];

    /// <summary>The squares you last stood on on this level, oldest first (not where you stand now).</summary>
    public IReadOnlyList<Loc> Trail => _trail;

    private void NoteTrail(Loc from)
    {
        if (_trail.Count > 0 && _trail[^1] == from) return;
        _trail.Add(from);
        if (_trail.Count > TrailLength) _trail.RemoveAt(0);
    }

    /// <summary>Ctrl+B: dots where you've been, and says where the trail began.</summary>
    public void ShowTrail()
    {
        var here = _game.Player.Position;
        var shown = _trail.Where(p => p != here).ToList();
        if (shown.Count == 0)
        {
            AddMessage("You haven't moved yet on this level.");
            Refresh();
            return;
        }
        Effects.AddTrail(shown);
        var start = shown[0] - here;
        var where = new List<string>();
        if (start.Y != 0) where.Add($"{Math.Abs(start.Y)} {(start.Y < 0 ? "north" : "south")}");
        if (start.X != 0) where.Add($"{Math.Abs(start.X)} {(start.X < 0 ? "west" : "east")}");
        AddMessage($"Where you've been: your last {shown.Count} {(shown.Count == 1 ? "square" : "squares")}, "
                   + $"the bright ones newest; the trail began {string.Join(" ", where)} of here.");
        Refresh();
    }
}
