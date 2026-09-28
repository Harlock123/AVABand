namespace Angband.Core.Game;

/// <summary>Arriving on a level: when (game turn), how deep, and what kind of level.</summary>
public sealed record JourneyVisit(long Turn, int Depth, string Profile);

/// <summary>A monster killed: when, where, and what.</summary>
public sealed record JourneyKill(long Turn, int Depth, string RaceId, bool Unique);

/// <summary>What happened on one depth, over the whole game.</summary>
public sealed record JourneyLevel(int Depth, int Visits, long Turns, int Kills, int Uniques, int Artifacts);

// The journey (AVABand's own): each level arrived on and each monster killed, kept with the
// character, so the character sheet can show depth over time and what happened at each depth.
public sealed partial class GameSession
{
    private readonly List<JourneyVisit> _visits = [];
    private readonly List<JourneyKill> _kills = [];

    public IReadOnlyList<JourneyVisit> Visits => _visits;
    public IReadOnlyList<JourneyKill> Kills => _kills;

    private void NoteVisit() => _visits.Add(new JourneyVisit(GameTurn, Player.Depth, Level.ProfileId));

    private void NoteJourneyKill(Monsters.Monster m)
    {
        var race = m.OriginalRace ?? m.Race;
        _kills.Add(new JourneyKill(GameTurn, Player.Depth, race.Id, race.Has(Definitions.MonsterFlags.Unique)));
    }

    internal void RestoreJourney(IEnumerable<JourneyVisit> visits, IEnumerable<JourneyKill> kills)
    {
        _visits.Clear();
        _visits.AddRange(visits);
        _kills.Clear();
        _kills.AddRange(kills);
        if (_visits.Count == 0) NoteVisit(); // an older save: the journey starts here
    }

    /// <summary>
    /// Each depth visited, shallowest first: how many times, how long there (in game turns, up to
    /// now for the level you are on), the kills and uniques there, and the artifacts found there.
    /// </summary>
    public IReadOnlyList<JourneyLevel> JourneyByDepth()
    {
        var time = new Dictionary<int, long>();
        var visits = new Dictionary<int, int>();
        for (var i = 0; i < _visits.Count; i++)
        {
            var v = _visits[i];
            var until = i + 1 < _visits.Count ? _visits[i + 1].Turn : GameTurn;
            time[v.Depth] = time.GetValueOrDefault(v.Depth) + Math.Max(0, until - v.Turn);
            visits[v.Depth] = visits.GetValueOrDefault(v.Depth) + 1;
        }
        var artifacts = History.Where(h => h.Artifact is not null && h.Text.StartsWith("Found ", StringComparison.Ordinal))
            .GroupBy(h => h.Depth).ToDictionary(g => g.Key, g => g.Count());
        return [.. visits.Keys.Union(_kills.Select(k => k.Depth)).Order().Select(d => new JourneyLevel(d, visits.GetValueOrDefault(d),
            time.GetValueOrDefault(d), _kills.Count(k => k.Depth == d), _kills.Count(k => k.Depth == d && k.Unique), artifacts.GetValueOrDefault(d)))];
    }
}
