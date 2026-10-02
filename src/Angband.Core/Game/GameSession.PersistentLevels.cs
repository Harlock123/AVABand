using Angband.Core.Definitions;
using Angband.Core.Generation;
using Angband.Core.Geometry;
using Angband.Core.Sight;
using Angband.Core.Time;
using Angband.Core.World;

namespace Angband.Core.Game;

// Persistent levels (Angband 4.2 birth_levels_persist, generate.c prepare_next_level): a level left
// behind is kept, with what the player remembers of it, and is there again on coming back — its
// monsters having recovered for the time away (mon-move.c restore_monsters). New levels are built to
// meet their stored neighbours: an up staircase under each down staircase of the level above, and a
// down staircase over each up staircase of the level below; and the player arrives where they left.
public sealed partial class GameSession
{
    /// <summary>A level left behind in a persistent dungeon, and when.</summary>
    public sealed record StoredLevel(Level Level, KnownMap Known, long Turn);

    private readonly Dictionary<int, StoredLevel> _storedLevels = [];

    /// <summary>Whether this character's dungeon keeps its levels (Angband birth_levels_persist).</summary>
    public bool PersistentLevels => Options[OptionIds.LevelsPersist];

    /// <summary>The levels kept, by depth (not the one the player is on).</summary>
    public IReadOnlyDictionary<int, StoredLevel> StoredLevels => _storedLevels;

    /// <summary>Puts a stored level back (loading a saved game).</summary>
    internal void RestoreStoredLevel(Level level, KnownMap known, long turn) => _storedLevels[level.Depth] = new(level, known, turn);

    private void StoreCurrentLevel()
    {
        if (Level.ProfileId == "arena") return;
        _storedLevels[Level.Depth] = new StoredLevel(Level, Known, GameTurn);
    }

    /// <summary>
    /// Angband get_join_info: the stairs a new level must have to meet its stored neighbours, and
    /// where there is no neighbour kept but one two away, that level's stairs facing this way, which
    /// this level's own stairs must keep clear of.
    /// </summary>
    private (List<StairJoin> Joins, List<StairJoin> OneOffAbove, List<StairJoin> OneOffBelow) JoinsFor(int depth)
    {
        var joins = new List<StairJoin>();
        var oneOffAbove = new List<StairJoin>();
        var oneOffBelow = new List<StairJoin>();
        if (_storedLevels.TryGetValue(depth - 1, out var above))
            joins.AddRange(above.Level.FindFeature(TerrainFlags.DownStair).Select(p => new StairJoin(p, Down: false)));
        else if (_storedLevels.TryGetValue(depth - 2, out var twoAbove))
            oneOffAbove.AddRange(twoAbove.Level.FindFeature(TerrainFlags.DownStair).Select(p => new StairJoin(p, Down: true)));
        if (_storedLevels.TryGetValue(depth + 1, out var below))
            joins.AddRange(below.Level.FindFeature(TerrainFlags.UpStair).Select(p => new StairJoin(p, Down: true)));
        else if (_storedLevels.TryGetValue(depth + 2, out var twoBelow))
            oneOffBelow.AddRange(twoBelow.Level.FindFeature(TerrainFlags.UpStair).Select(p => new StairJoin(p, Down: false)));
        return (joins, oneOffAbove, oneOffBelow);
    }

    /// <summary>Uniques alive on a stored level, who can't turn up anywhere else meanwhile.</summary>
    private IEnumerable<string> UniquesOnStoredLevels() =>
        _storedLevels.Values.SelectMany(s => s.Level.Monsters.All).Where(m => m.Race.IsUnique).Select(m => m.Race.Id);

    /// <summary>
    /// Angband restore_monsters: a stored level's monsters regenerate for the time the player was away,
    /// and their timed effects wear off as they would have.
    /// </summary>
    private void RecoverMonsters(Level level, long turns)
    {
        if (turns <= 0) return;
        foreach (var m in level.Monsters.All)
        {
            var steps = (int)Math.Min(turns / 100, 100_000);
            var gain = m.MaxHp / 100;
            var total = gain > 0 ? (long)gain * steps : steps / 2;
            if (m.Race.Has(MonsterFlags.Regenerate)) total *= 2;
            m.Hp = (int)Math.Min(m.MaxHp, m.Hp + total);

            var worn = (int)Math.Min(int.MaxValue, turns * EnergyTable.EnergyPerTurn(m.Speed) / EnergyTable.MoveEnergy);
            m.Fear = Math.Max(0, m.Fear - worn);
            m.Confused = Math.Max(0, m.Confused - worn);
            m.Stun = Math.Max(0, m.Stun - worn);
            m.Held = Math.Max(0, m.Held - worn);
            m.Fast = Math.Max(0, m.Fast - worn);
            m.Slow = Math.Max(0, m.Slow - worn);
        }
    }

    /// <summary>
    /// Back to a stored level (Angband: the player keeps their coordinates, so arriving by stairs
    /// lands on the staircase that leads back; anywhere else, on the nearest open square).
    /// </summary>
    private void EnterStoredLevel(StoredLevel stored, Loc from)
    {
        if (Level is not null) Vision.Reset(Level);
        ClearTarget();
        Level = stored.Level;
        Level.Births.Clear(); // (a breeder's count starts again on each arrival)
        Known = stored.Known;
        Scent.Reset(Level);
        Noise.Reset(Level);
        RecoverMonsters(Level, GameTurn - stored.Turn);
        Player.Position = OpenSquareNear(from);
    }

    /// <summary>The square itself if the player can stand there, else the nearest one that will do.</summary>
    private Loc OpenSquareNear(Loc want)
    {
        bool Ok(Loc p) => Level.InBoundsFully(p) && Level.IsPassable(p) && Level.Monsters.At(p) is null;
        if (Ok(want)) return want;
        return Level.AllLocs().Where(Ok).OrderBy(p => p.DistanceTo(want)).ThenBy(p => p.Y).ThenBy(p => p.X)
            .DefaultIfEmpty(want).First();
    }
}
