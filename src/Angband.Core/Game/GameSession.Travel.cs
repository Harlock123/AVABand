using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Game;

/// <summary>Click-to-travel (Angband's pathfind-to-location).</summary>
public sealed partial class GameSession
{
    public const int MaxTravelSteps = 400;

    private bool Travel(Loc target)
    {
        if (!Level.InBounds(target) || target == Player.Position) return false;
        var hp = Player.Hp;
        var moved = false;
        var seen = VisibleMonsters();
        _disturbed = false;

        for (var step = 0; step < MaxTravelSteps && !IsGameOver && Player.Position != target; step++)
        {
            var path = FindPath(Player.Position, target);
            if (path is null)
            {
                if (!moved) Publish(new MessageEvent("You don't know a way there."));
                break;
            }

            var next = path[0];
            var energy = Walk(DirectionExtensions.FromOffset(next.X - Player.Position.X, next.Y - Player.Position.Y));
            if (energy <= 0) break;
            SpendAndAdvance(energy);
            moved = true;

            // Angband disturbs travel when anything interesting happens: being hurt, a monster coming
            // into view, or (with disturb_near) one in view moving.
            if (Player.Hp < hp || _disturbed || MonstersDisturb(seen) || Level.Monsters.At(next) is not null) break;
            seen = VisibleMonsters();
            if (Level.Objects.Any(Player.Position)) break;
        }
        return moved;
    }

    /// <summary>
    /// Shortest 8-way path over squares the player knows to be walkable (or closed doors), avoiding
    /// visible traps; excludes the start, ends at the target. Null when there is no known way.
    /// </summary>
    public List<Loc>? FindPath(Loc from, Loc to)
    {
        if (!Level.InBounds(to) || !Known.IsKnown(to)) return null;
        var prev = new Dictionary<Loc, Loc>();
        var queue = new Queue<Loc>([from]);
        prev[from] = from;

        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (p == to) break;
            foreach (var d in DirectionExtensions.Compass)
            {
                var n = p.Step(d);
                if (!Level.InBounds(n) || prev.ContainsKey(n) || !Known.IsKnown(n)) continue;
                var feature = Data.Terrain[Known.Feature(n)];
                var walkable = feature.Has(TerrainFlags.Passable) || feature.Has(TerrainFlags.DoorClosed);
                if (!walkable) continue;
                if (n != to && Level[n].Trap != 0 && Level[n].Has(SquareFlags.TrapVisible)) continue;
                prev[n] = p;
                queue.Enqueue(n);
            }
        }

        if (!prev.ContainsKey(to)) return null;
        var path = new List<Loc>();
        for (var p = to; p != from; p = prev[p]) path.Add(p);
        path.Reverse();
        return path;
    }
}
