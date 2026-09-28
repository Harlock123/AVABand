using Angband.Core.Game;

namespace Angband.Avalonia.ViewModels;

/// <summary>A point on the journey graph: a game turn and the depth from then on.</summary>
public readonly record struct JourneyPoint(long Turn, int Depth);

/// <summary>A marker on the graph: a unique killed, or an artifact found.</summary>
public readonly record struct JourneyMarker(long Turn, int Depth, bool Artifact, string Label);

/// <summary>One line of the table of depths.</summary>
public sealed record JourneyRow(string Depth, string Visits, string Time, string Kills, string Uniques, string Artifacts);

/// <summary>
/// The journey (AVABand's own): depth over time as a graph (uniques killed and artifacts found
/// marked on it), a table of what happened at each depth, and the history as a timeline.
/// </summary>
public sealed class JourneyViewModel
{
    public JourneyViewModel(GameSession game, string history)
    {
        var p = game.Player;
        Title = $"{p.Name}'s journey";
        Points = [.. game.Visits.Select(v => new JourneyPoint(v.Turn, v.Depth)), new JourneyPoint(game.GameTurn, p.Depth)];
        Markers =
        [
            .. game.Kills.Where(k => k.Unique).Select(k => new JourneyMarker(k.Turn, k.Depth, false, game.Data.Monster(k.RaceId)?.Name ?? k.RaceId)),
            .. game.History.Where(h => h.Artifact is not null && h.Text.StartsWith("Found ", StringComparison.Ordinal))
                .Select(h => new JourneyMarker(h.Turn, h.Depth, true, h.Text[6..])),
        ];
        MaxTurn = Math.Max(1, game.GameTurn);
        MaxDepth = Math.Max(1, Points.Max(pt => pt.Depth));
        Rows = [.. game.JourneyByDepth().Select(l => new JourneyRow(
            l.Depth == 0 ? "Town" : $"{l.Depth * 50} ft (L{l.Depth})", l.Visits.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Turns(l.Turns), l.Kills == 0 ? "" : l.Kills.ToString(System.Globalization.CultureInfo.InvariantCulture),
            l.Uniques == 0 ? "" : l.Uniques.ToString(System.Globalization.CultureInfo.InvariantCulture),
            l.Artifacts == 0 ? "" : l.Artifacts.ToString(System.Globalization.CultureInfo.InvariantCulture)))];
        var levels = game.Visits.Count(v => v.Depth > 0);
        Summary = $"{levels} levels entered, deepest {p.MaxDepth * 50} ft; {game.Kills.Count} kills ({game.Kills.Count(k => k.Unique)} uniques); "
                  + $"{Markers.Count(m => m.Artifact)} artifacts found; {game.NormalTurns:N0} turns.";
        History = history;
    }

    public string Title { get; }
    public string Summary { get; }
    public IReadOnlyList<JourneyPoint> Points { get; }
    public IReadOnlyList<JourneyMarker> Markers { get; }
    public long MaxTurn { get; }
    public int MaxDepth { get; }
    public IReadOnlyList<JourneyRow> Rows { get; }
    public string History { get; }

    /// <summary>Game turns as player turns (ten game turns each at normal speed).</summary>
    private static string Turns(long gameTurns) => (gameTurns / 10).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
}
