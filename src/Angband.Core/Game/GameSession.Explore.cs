using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Game;

/// <summary>Explore (AVABand's own; Ctrl+E): walk on to the nearest part of the level not yet seen.</summary>
public sealed record ExploreCommand : GameCommand;

// AVABand's own: auto-explore. Each step heads for the nearest edge of what you've seen — a square you
// know you can walk on with an unknown square beside it — by the way you know, over doors (they open as
// you walk into them) and round known traps, and stops as travel does: a monster coming into view, being
// hurt, anything disturbing you, an object underfoot. When there's no edge left it says so.
public sealed partial class GameSession
{
    /// <summary>The most steps one explore takes before handing back (press again to go on).</summary>
    public const int MaxExploreSteps = 600;

    private bool Explore()
    {
        var hp = Player.Hp;
        var moved = false;
        var seen = VisibleMonsters();
        var reached = new HashSet<Loc>();
        _disturbed = false;
        for (var step = 0; step < MaxExploreSteps && !IsGameOver; step++)
        {
            if (NearestUnexplored(reached) is not { } target)
            {
                Publish(new MessageEvent(moved ? "You have seen all you can reach here." : "There's nothing left here to explore that you can reach."));
                break;
            }
            if (target == Player.Position)
            {
                reached.Add(target); // (standing at an edge that stays unknown — the dark, or blindness)
                continue;
            }
            var path = FindPath(Player.Position, target);
            if (path is null)
            {
                reached.Add(target);
                continue;
            }
            var next = path[0];
            var energy = Walk(DirectionExtensions.FromOffset(next.X - Player.Position.X, next.Y - Player.Position.Y));
            if (energy <= 0) break;
            SpendAndAdvance(energy);
            moved = true;
            if (Player.Position == target) reached.Add(target);
            if (Player.Hp < hp || _disturbed || MonstersDisturb(seen) || Level.Monsters.At(next) is not null) break;
            seen = VisibleMonsters();
            if (Level.Objects.Any(Player.Position)) break; // (something here to look at)
        }
        return moved;
    }

    /// <summary>
    /// The nearest square you know you can walk to that has an unknown square beside it (the edge of what
    /// you've seen), by the way there; null when there's none left.
    /// </summary>
    public Loc? NearestUnexplored(IReadOnlySet<Loc>? skip = null)
    {
        var visited = new HashSet<Loc> { Player.Position };
        var queue = new Queue<Loc>([Player.Position]);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (skip?.Contains(p) != true && Level.Neighbors(p).Any(n => !Known.IsKnown(n))) return p;
            foreach (var d in DirectionExtensions.Compass)
            {
                var n = p.Step(d);
                if (!Level.InBounds(n) || !visited.Add(n) || !Known.IsKnown(n)) continue;
                var feature = Data.Terrain[Known.Feature(n)];
                if (!feature.Has(TerrainFlags.Passable) && !feature.Has(TerrainFlags.DoorClosed)) continue;
                if (Level[n].Trap != 0 && Level[n].Has(SquareFlags.TrapVisible)) continue;
                if (feature.Has(TerrainFlags.Fiery)) continue;
                queue.Enqueue(n);
            }
        }
        return null;
    }
}
