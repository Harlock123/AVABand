using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Avalonia.ViewModels;

/// <summary>A glyph drawn over the map for a moment: a bolt in flight, a square of a ball's burst.</summary>
public sealed record EffectGlyph(Loc Loc, char Glyph, string Color, uint Argb);

/// <summary>A damage number floating up from a monster (show_damage); <see cref="Age"/> is 0..1.</summary>
public sealed record FloatingNumber(Loc Loc, string Text, double Age);

/// <summary>
/// The map's animations (Angband's projection graphics): bolts and missiles fly square by square,
/// beams leave a trail, balls and breaths spread out from where they burst, and damage numbers rise
/// from what they hit. The game plays a whole turn at once, so its effects are queued here and
/// played afterwards, in order; a new command clears whatever is left, so they never hold play up.
/// Time is advanced by the map view (or a test), in milliseconds.
/// </summary>
public sealed class MapEffects
{
    /// <summary>Milliseconds a missile or bolt spends on each square.</summary>
    public const double StepMs = 22;
    /// <summary>Milliseconds each ring of a ball or breath takes to spread.</summary>
    public const double BurstStepMs = 45;
    /// <summary>How long the finished burst or beam stays up.</summary>
    public const double HoldMs = 90;
    /// <summary>How long a damage number floats.</summary>
    public const double FloatMs = 900;
    /// <summary>Beyond this much queued, new effects play three times faster (a crowd of archers).</summary>
    public const double BusyMs = 1200;

    private abstract record Step(double Duration);
    private sealed record Frame(IReadOnlyList<EffectGlyph> Glyphs, double Duration) : Step(Duration);
    private sealed record Spawn(Loc Loc, string Text) : Step(0);

    private readonly Func<string, uint> _palette;
    private readonly List<Step> _timeline = [];
    private readonly List<(Loc Loc, string Text, double Age)> _floating = [];
    private int _index;
    private double _elapsed;

    /// <param name="palette">Turns an Angband colour name into ARGB.</param>
    public MapEffects(Func<string, uint> palette) => _palette = palette;

    /// <summary>Raised when something is queued while nothing was playing (so the view starts its clock).</summary>
    public event Action? Started;

    public bool IsActive => _index < _timeline.Count || _floating.Count > 0;

    /// <summary>The glyphs of the frame now showing.</summary>
    public IReadOnlyList<EffectGlyph> Glyphs => _index < _timeline.Count && _timeline[_index] is Frame f ? f.Glyphs : [];

    /// <summary>The damage numbers now floating.</summary>
    public IReadOnlyList<FloatingNumber> Numbers => [.. _floating.Select(n => new FloatingNumber(n.Loc, n.Text, n.Age / FloatMs))];

    /// <summary>Drops everything queued and showing.</summary>
    public void Clear()
    {
        _timeline.Clear();
        _floating.Clear();
        _index = 0;
        _elapsed = 0;
    }

    /// <summary>
    /// Queues a projection's frames, keeping only the squares <paramref name="visible"/> allows
    /// (Angband draws projections only where the player can see).
    /// </summary>
    public void AddProjection(ProjectionEvent e, Func<Loc, bool> visible)
    {
        var colour = ElementColour(e.Element);
        var argb = _palette(colour);
        var speed = Remaining() > BusyMs ? 3 : 1;
        var frames = new List<Frame>();
        var previous = e.From;
        var trail = new List<EffectGlyph>();
        foreach (var p in e.Path)
        {
            var glyph = new EffectGlyph(p, BoltGlyph(previous, p), colour, argb);
            previous = p;
            if (!visible(p)) continue;
            if (e.Kind == ProjectionKind.Beam)
            {
                trail.Add(glyph);
                frames.Add(new Frame([.. trail], StepMs / speed));
            }
            else frames.Add(new Frame([glyph], StepMs / speed));
        }
        if (e.Kind == ProjectionKind.Beam && trail.Count > 0) frames.Add(new Frame([.. trail], HoldMs / speed));

        // A burst spreads ring by ring from its centre (the end of the path, or where it came from).
        var burst = e.Burst.Where(visible).ToList();
        if (burst.Count > 0)
        {
            var centre = e.Kind == ProjectionKind.Breath || e.Path.Count == 0 ? e.From : e.Path[^1];
            var rings = burst.GroupBy(p => p.DistanceTo(centre)).OrderBy(g => g.Key).ToList();
            var shown = new List<EffectGlyph>();
            foreach (var ring in rings)
            {
                shown.AddRange(ring.Select(p => new EffectGlyph(p, '*', colour, argb)));
                frames.Add(new Frame([.. shown], BurstStepMs / speed));
            }
            frames.Add(new Frame([.. shown], HoldMs / speed));
        }
        Queue(frames);
    }

    /// <summary>How long the trail of where you've been shows (Ctrl+B).</summary>
    public const double TrailMs = 2500;

    /// <summary>
    /// The trail of where you've been (AVABand's own, Ctrl+B): a dot on each square, oldest first —
    /// the older half dim, the newer bright — for a few seconds (or until the next command).
    /// </summary>
    public void AddTrail(IReadOnlyList<Loc> trail)
    {
        if (trail.Count == 0) return;
        var glyphs = trail.Select((p, i) =>
        {
            var colour = i < trail.Count / 2 ? "Umber" : "Yellow";
            return new EffectGlyph(p, '•', colour, _palette(colour));
        }).ToList();
        Queue([new Frame(glyphs, TrailMs)]);
    }

    /// <summary>A damage number from <paramref name="at"/>, when the effects before it have played.</summary>
    public void AddDamage(Loc at, int damage) => Queue([new Spawn(at, damage.ToString(System.Globalization.CultureInfo.InvariantCulture))]);

    /// <summary>Moves the animation on by <paramref name="ms"/> milliseconds.</summary>
    public void Advance(double ms)
    {
        for (var i = _floating.Count - 1; i >= 0; i--)
        {
            var n = _floating[i];
            if (n.Age + ms >= FloatMs) _floating.RemoveAt(i);
            else _floating[i] = n with { Age = n.Age + ms };
        }
        _elapsed += ms;
        SpawnDue();
        while (_index < _timeline.Count && _elapsed >= _timeline[_index].Duration)
        {
            _elapsed -= _timeline[_index].Duration;
            _index++;
            SpawnDue();
        }
        if (_index >= _timeline.Count)
        {
            _timeline.Clear();
            _index = 0;
            _elapsed = 0;
        }
    }

    private void SpawnDue()
    {
        while (_index < _timeline.Count && _timeline[_index] is Spawn s)
        {
            _floating.Add((s.Loc, s.Text, 0));
            _index++;
        }
    }

    private void Queue(IEnumerable<Step> steps)
    {
        var wasActive = IsActive;
        _timeline.AddRange(steps);
        SpawnDue();
        if (!wasActive && IsActive) Started?.Invoke();
    }

    private double Remaining() => _timeline.Skip(_index).Sum(s => s.Duration) - _elapsed;

    /// <summary>Angband's bolt pictures: a line in the direction of flight.</summary>
    public static char BoltGlyph(Loc from, Loc to) => (Math.Sign(to.X - from.X), Math.Sign(to.Y - from.Y)) switch
    {
        (0, _) => '|',
        (_, 0) => '-',
        (1, 1) or (-1, -1) => '\\',
        _ => '/',
    };

    /// <summary>Angband projection.txt colours, by element (plain missiles are white).</summary>
    public static string ElementColour(string? element) => element switch
    {
        "acid" or "water" => "Slate",
        "elec" => "Blue",
        "fire" or "plasma" or "meteor" => "Red",
        "cold" or "ice" or "arrow" or null => "White",
        "pois" => "Green",
        "light" => "Orange",
        "dark" or "mana" or "holy_orb" => "LightDark",
        "sound" => "Yellow",
        "shards" or "force" => "Umber",
        "nexus" => "LightRed",
        "nether" => "LightGreen",
        "chaos" or "disen" or "missile" => "Violet",
        "gravity" or "inertia" => "LightSlate",
        "time" => "LightBlue",
        _ => "White",
    };
}
