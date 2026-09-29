using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Generation;

// Angband gen-util.c: finding grids, placing the player and stairs, scattering things.
public sealed partial class Cave
{
    /// <summary>
    /// Angband cave_find_init / cave_find_get_grid: every grid of a rectangle once, in random order.
    /// </summary>
    private IEnumerable<Loc> CaveFind(Loc topLeft, Loc bottomRight)
    {
        var w = bottomRight.X - topLeft.X + 1;
        var h = bottomRight.Y - topLeft.Y + 1;
        var n = w <= 0 || h <= 0 ? 0 : w * h;
        var order = new int[n];
        for (var i = 0; i < n; i++) order[i] = i;
        for (var next = 0; next < n; next++)
        {
            var j = _rng.RandInt0(n - next) + next;
            (order[j], order[next]) = (order[next], order[j]);
            var k = order[next];
            yield return new Loc(k % w + topLeft.X, k / w + topLeft.Y);
        }
    }

    /// <summary>Angband cave_find_in_range: a grid in the rectangle passing the test.</summary>
    private Loc? CaveFindInRange(Level c, Loc topLeft, Loc bottomRight, Func<Level, Loc, bool> pred)
    {
        foreach (var g in CaveFind(topLeft, bottomRight))
            if (pred(c, g)) return g;
        return null;
    }

    private Loc? CaveFindAny(Level c, Func<Level, Loc, bool> pred) =>
        CaveFindInRange(c, new Loc(0, 0), new Loc(c.Width - 1, c.Height - 1), pred);

    /// <summary>Angband find_empty_range.</summary>
    private Loc? FindEmptyRange(Level c, Loc topLeft, Loc bottomRight) => CaveFindInRange(c, topLeft, bottomRight, IsEmpty);

    /// <summary>Angband find_nearby_grid: any fully-in-bounds grid within the given distances of a centre.</summary>
    private Loc? FindNearbyGrid(Level c, Loc centre, int yd, int xd) =>
        CaveFindInRange(c, new Loc(centre.X - xd, centre.Y - yd), new Loc(centre.X + xd, centre.Y + yd),
            (l, g) => InBoundsFully(l, g));

    /// <summary>Angband correct_dir: a cardinal step from one grid toward another.</summary>
    private Loc CorrectDir(Loc from, Loc to)
    {
        var x = Math.Sign(to.X - from.X);
        var y = Math.Sign(to.Y - from.Y);
        if (x == 0 || y == 0) return new Loc(x, y);
        return _rng.RandInt0(100) < 50 ? new Loc(x, 0) : new Loc(0, y);
    }

    /// <summary>Angband rand_dir: a random cardinal step.</summary>
    private Loc RandDir() => Ddd[_rng.RandInt0(4)];

    /// <summary>Angband rand_loc.</summary>
    private Loc RandLoc(Loc g, int xSpread, int ySpread) => new(_rng.Spread(g.X, xSpread), _rng.Spread(g.Y, ySpread));

    /// <summary>
    /// Angband find_start: a staircase-worthy spot — a nook (three walls around, four at the
    /// diagonals), then a corridor, then the most enclosed empty floor to be had.
    /// </summary>
    private Loc? FindStart(Level c)
    {
        var grids = CaveFind(new Loc(1, 1), new Loc(c.Width - 2, c.Height - 2)).ToList();
        foreach (var g in grids)
            if (SuitsStairsWell(c, g)) return g;
        foreach (var g in grids)
            if (SuitsStairsOk(c, g)) return g;
        for (var walls = 6; walls >= 0; walls--)
            foreach (var g in grids)
            {
                if (!IsEmpty(c, g) || IsVault(c, g) || IsNoStairs(c, g)) continue;
                if (NumWallsAdjacent(c, g) + NumWallsDiagonal(c, g) == walls) return g;
            }
        return null;
    }

    /// <summary>
    /// Angband new_player_spot: where the player starts — the staircase they left by on a
    /// persistent level, else a good spot; with connected stairs, a staircase back the way they came.
    /// </summary>
    private bool NewPlayerSpot(Level c, out Loc start)
    {
        start = default;
        if (_request.Persistent && _request.PreferredStart is { } want && InBoundsFully(c, want) && IsStairs(c, want))
            start = want;
        else if (FindStart(c) is { } g)
            start = g;
        else return false;

        var connected = _request.ConnectStairs ?? _data.Constants.ConnectedStairs;
        if (connected && c.Depth > 0)
        {
            if (_request.Arrival == StairArrival.Descended) SetFeat(c, start, _f.UpStair);
            else if (_request.Arrival == StairArrival.Ascended && c.Depth < _data.Constants.MaxDepth) SetFeat(c, start, _f.DownStair);
        }
        _playerStart = start;
        Occupied(c).Add(start);
        return true;
    }

    /// <summary>
    /// Angband alloc_stairs: staircases in grids with walls about (three, then fewer), more than
    /// <paramref name="minSep"/> grids from others (of the same kind, or any if <paramref name="sepAny"/>).
    /// </summary>
    private void AllocStairs(Level c, ushort feat, int num, int minSep, bool sepAny, IReadOnlyList<Connector>? avoid, bool quest)
    {
        var av = new List<Loc>();
        if (minSep > 0)
        {
            foreach (var g in c.AllLocs())
            {
                var hit = sepAny ? IsStairs(c, g) : feat == _f.DownStair ? IsDownStairs(c, g) : IsUpStairs(c, g);
                if (hit) av.Add(g);
            }
            foreach (var a in avoid ?? []) if (a.Feat != feat) av.Add(a.Grid);
        }

        var grids = CaveFind(new Loc(1, 1), new Loc(c.Width - 2, c.Height - 2)).ToList();
        var i = 0;
        for (var walls = 3; i < num && walls >= 0; walls--)
        {
            foreach (var g in grids)
            {
                if (i >= num) break;
                if (!IsEmpty(c, g) || NumWallsAdjacent(c, g) != walls) continue;
                if (minSep > 0)
                {
                    if (av.Any(a => Math.Abs(g.Y - a.Y) <= minSep && Math.Abs(g.X - a.X) <= minSep)) continue;
                    av.Add(g);
                }
                PlaceStairs(c, g, quest, feat);
                i++;
            }
        }
    }

    /// <summary>Where alloc_object may put things (Angband SET_CORR, SET_ROOM, SET_BOTH).</summary>
    [Flags]
    private enum AllocSet { Corridor = 1, Room = 2, Both = 3 }

    /// <summary>What alloc_object places (Angband TYP_*).</summary>
    private enum AllocType { Rubble, Trap, Gold, Object, Good, Great }

    /// <summary>Angband alloc_objects.</summary>
    private void AllocObjects(Level c, AllocSet set, AllocType typ, int num, int depth)
    {
        for (var k = 0; k < num; k++) AllocObject(c, set, typ, depth);
    }

    /// <summary>Angband alloc_object: one thing on a random empty grid of the right sort.</summary>
    private bool AllocObject(Level c, AllocSet set, AllocType typ, int depth)
    {
        foreach (var g in CaveFind(new Loc(1, 1), new Loc(c.Width - 2, c.Height - 2)))
        {
            var matched = ((set & AllocSet.Corridor) != 0 && !IsRoom(c, g)) || ((set & AllocSet.Room) != 0 && IsRoom(c, g));
            if (!IsEmpty(c, g) || !matched) continue;
            switch (typ)
            {
                case AllocType.Rubble: PlaceRubble(c, g); break;
                case AllocType.Trap: PlaceTrap(c, g, depth); break;
                case AllocType.Gold: PlaceGold(c, g, depth); break;
                case AllocType.Object: PlaceObject(c, g, depth, false, false); break;
                case AllocType.Good: PlaceObject(c, g, depth, true, false); break;
                case AllocType.Great: PlaceObject(c, g, depth, true, true); break;
            }
            return true;
        }
        return false;
    }

    /// <summary>Angband vault_objects: up to <paramref name="num"/> objects (or gold) near a grid.</summary>
    private void VaultObjects(Level c, Loc g, int depth, int num)
    {
        for (; num > 0; --num)
            for (var i = 0; i < 11; ++i)
            {
                if (FindNearbyGrid(c, g, 2, 3) is not { } near || !CanPutItem(c, near)) continue;
                if (_rng.RandInt0(100) < 75) PlaceObject(c, near, depth, false, false);
                else PlaceGold(c, near, depth);
                break;
            }
    }

    /// <summary>Angband vault_traps: traps near a grid.</summary>
    private void VaultTraps(Level c, Loc g, int yd, int xd, int num)
    {
        for (var i = 0; i < num; i++)
            for (var tries = 0; tries <= 5; tries++)
            {
                if (FindNearbyGrid(c, g, yd, xd) is not { } near || !IsEmpty(c, near)) continue;
                PlaceTrap(c, near, c.Depth);
                break;
            }
    }

    /// <summary>Angband vault_monsters: sleeping monsters (with their groups) about a grid.</summary>
    private void VaultMonsters(Level c, Loc g, int depth, int num)
    {
        if (!InBounds(c, g)) return;
        for (var k = 0; k < num; k++)
            for (var i = 0; i < 9; i++)
            {
                if (Scatter(c, g, 1, true, IsEmpty) is not { } near) continue;
                PickAndPlaceMonster(c, near, depth, true, true);
                break;
            }
    }

    /// <summary>
    /// Angband scatter_ext for one grid: a random grid passing the test within <paramref name="d"/>
    /// of the centre (and in its line of sight if asked).
    /// </summary>
    private Loc? Scatter(Level c, Loc centre, int d, bool needLos, Func<Level, Loc, bool> pred)
    {
        var feasible = new List<Loc>();
        for (var y = centre.Y - d; y <= centre.Y + d; y++)
        for (var x = centre.X - d; x <= centre.X + d; x++)
        {
            var g = new Loc(x, y);
            if (!InBoundsFully(c, g)) continue;
            if (d > 1 && centre.DistanceTo(g) > d) continue;
            if (needLos && !Combat.ProjectionPath.Projectable(c, centre, g, 255)) continue;
            if (!pred(c, g)) continue;
            feasible.Add(g);
        }
        return feasible.Count == 0 ? null : feasible[_rng.RandInt0(feasible.Count)];
    }

    /// <summary>
    /// Angband pick_and_place_distant_monster: a monster (asleep, with its group) on a random empty
    /// grid further than <paramref name="dis"/> from <paramref name="avoid"/>, never in a vault or
    /// other marked room.
    /// </summary>
    private void PickAndPlaceDistantMonster(Level c, Loc avoid, int dis, bool sleep, int depth, string? restriction = null)
    {
        for (var attempts = 10000; --attempts > 0;)
        {
            var g = new Loc(_rng.RandInt0(c.Width), _rng.RandInt0(c.Height));
            if (!IsEmpty(c, g) || IsMonRestrict(c, g)) continue;
            if (g.DistanceTo(avoid) > dis)
            {
                PickAndPlaceMonster(c, g, depth, sleep, true, restriction);
                return;
            }
        }
    }

    /// <summary>Angband shuffle.</summary>
    private void Shuffle(int[] arr, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var j = _rng.RandInt0(n - i) + i;
            (arr[j], arr[i]) = (arr[i], arr[j]);
        }
    }
}
