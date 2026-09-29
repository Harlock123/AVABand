using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Generation;

// Angband gen-cave.c: streamers, room entrances, tunnels, doors at junctions, and making sure
// every part of a level can be reached.
public sealed partial class Cave
{
    /// <summary>Angband build_streamer: a vein of magma or quartz wandering to the level's edge.</summary>
    private void BuildStreamer(Level c, ushort feat, int chance)
    {
        var g = RandLoc(new Loc(c.Width / 2, c.Height / 2), 15, 10);
        var dir = DdGrid[DddDirs[_rng.RandInt0(8)]];
        var s = _dun.Profile.Streamers;
        while (true)
        {
            for (var i = 0; i < s.Density; i++)
            {
                if (FindNearbyGrid(c, g, s.Range, s.Range) is not { } change) continue;
                if (!IsRock(c, change)) continue;
                SetFeat(c, change, feat);
                if (chance > 0 && _rng.OneIn(chance))
                {
                    if (c[change].Feature == _f.Magma) SetFeat(c, change, _f.MagmaTreasure);
                    else if (c[change].Feature == _f.Quartz) SetFeat(c, change, _f.QuartzTreasure);
                }
            }
            g += dir;
            if (!InBounds(c, g)) break;
        }
    }

    /// <summary>Angband reset_entrance_data: no marked room entrances yet on this chunk.</summary>
    private void ResetEntranceData(Level c)
    {
        _dun.Ent.Clear();
        var map = new int[c.Height, c.Width];
        for (var y = 0; y < c.Height; y++)
        for (var x = 0; x < c.Width; x++)
            map[y, x] = -1;
        _dun.Ent2Room = map;
    }

    /// <summary>Angband append_entrance: an entrance for the room last placed.</summary>
    private void AppendEntrance(Loc g)
    {
        if (_dun.CentN <= 0 || _dun.CentN > GenConstants.LevelRoomMax) return;
        var ridx = _dun.CentN - 1;
        while (_dun.Ent.Count <= ridx) _dun.Ent.Add([]);
        _dun.Ent[ridx].Add(g);
        _dun.Ent2Room[g.Y, g.X] = ridx;
    }

    private IReadOnlyList<Loc> EntrancesOf(int ridx) => ridx < _dun.Ent.Count ? _dun.Ent[ridx] : [];

    /// <summary>
    /// Angband choose_random_entrance: one of a room's marked entrances still open to piercing,
    /// perhaps biased toward <paramref name="tgt"/>, never beside the excluded grids; (0, 0) if none.
    /// </summary>
    private Loc ChooseRandomEntrance(Level c, int ridx, Loc? tgt, int bias, IReadOnlyList<Loc> exc)
    {
        var ents = EntrancesOf(ridx);
        if (ents.Count == 0) return new Loc(0, 0);
        var accum = new int[ents.Count + 1];
        var nchoice = 0;
        for (var i = 0; i < ents.Count; i++)
        {
            var included = IsGraniteWithFlag(c, ents[i], SquareFlags.WallOuter);
            if (included)
                foreach (var e in exc)
                {
                    var d = ents[i] - e;
                    if (Math.Abs(d.X) <= 1 && Math.Abs(d.Y) <= 1 && (d.X != 0 || d.Y != 0))
                    {
                        included = false;
                        break;
                    }
                }
            if (included)
            {
                if (tgt is { } t)
                {
                    var d = ents[i].DistanceTo(t);
                    if (d == 0) return ents[i];
                    var biased = Math.Max(1, bias - d);
                    accum[i + 1] = accum[i] + biased * biased;
                }
                else accum[i + 1] = accum[i] + 1;
                nchoice++;
            }
            else accum[i + 1] = accum[i];
        }
        if (nchoice == 0) return new Loc(0, 0);
        var chosen = _rng.RandInt0(accum[ents.Count]);
        for (var i = 0; i < ents.Count; i++)
            if (accum[i] <= chosen && accum[i + 1] > chosen) return ents[i];
        return new Loc(0, 0);
    }

    /// <summary>Angband pierce_outer_wall: remember the piercing and forbid others beside it.</summary>
    private void PierceOuterWall(Level c, Loc g)
    {
        if (_dun.Wall.Count < GenConstants.WallPierceMax) _dun.Wall.Add(g);
        for (var y = g.Y - 1; y <= g.Y + 1; y++)
        for (var x = g.X - 1; x <= g.X + 1; x++)
        {
            var adj = new Loc(x, y);
            // 4.2.5 tests the coordinates themselves against zero here (as it has it).
            if (adj.X != 0 && adj.Y != 0 && InBounds(c, adj) && IsGraniteWithFlag(c, adj, SquareFlags.WallOuter))
                SetMarkedGranite(c, adj, SquareFlags.WallSolid);
        }
    }

    /// <summary>Angband handle_post_wall_step: the first step after piercing a wall.</summary>
    private void HandlePostWallStep(Level c, ref Loc g, ref Loc dir, ref bool doorFlag, ref int bendIntvl)
    {
        if (dir.X != 0 && dir.Y != 0)
        {
            g += dir;
            if (!IsRoom(c, g) && IsGranite(c, g))
            {
                if (_dun.Tunn.Count < GenConstants.TunnGridMax) _dun.Tunn.Add(g);
                doorFlag = false;
            }
            bendIntvl = 0;
            dir = _rng.RandInt0(32768) < 16384 ? new Loc(0, dir.Y) : new Loc(dir.X, 0);
        }
        else bendIntvl = 1;
    }

    /// <summary>Angband find_normal_to_wall: a direction away from (or into) a room through its wall.</summary>
    private Loc FindNormalToWall(Level c, Loc g, bool inner)
    {
        var choices = new List<Loc>();
        var ncardinal = 0;
        for (var i = 0; i < 8; i++)
        {
            var chk = g + Ddd[i];
            if (InBounds(c, chk) && !IsPerm(c, chk) && IsRoom(c, chk) == inner
                && !IsGraniteWithFlag(c, chk, SquareFlags.WallOuter) && !IsGraniteWithFlag(c, chk, SquareFlags.WallSolid)
                && !IsGraniteWithFlag(c, chk, SquareFlags.WallInner))
            {
                choices.Add(Ddd[i]);
                if (i < 4) ncardinal++;
            }
        }
        var n = choices.Count > 1 && ncardinal > 0 ? ncardinal : choices.Count;
        return n == 0 ? new Loc(0, 0) : choices[_rng.RandInt0(n)];
    }

    /// <summary>Angband allows_wall_piercing_door: open ground on both sides of the piercing.</summary>
    private bool AllowsWallPiercingDoor(Level c, Loc g)
    {
        int inside = 0, outside = 0;
        for (var y = g.Y - 1; y <= g.Y + 1; y++)
        for (var x = g.X - 1; x <= g.X + 1; x++)
        {
            var chk = new Loc(x, y);
            // 4.2.5 compares the coordinates themselves with zero here too.
            if ((chk.Y == 0 && chk.X == 0) || !InBounds(c, chk)) continue;
            if ((IsPassable(c, chk) || IsRubble(c, chk)) && !IsDoor(c, chk) && !IsShop(c, chk))
            {
                if (IsRoom(c, chk)) inside++;
                else outside++;
            }
        }
        return outside > 0 && inside > 0;
    }

    /// <summary>
    /// Angband build_tunnel: dig from one grid to another, turning now and then (sometimes at
    /// random), crossing rooms freely, piercing their outer walls (through marked entrances where
    /// a room has them), noting junctions with other corridors, and perhaps giving up at one once
    /// far enough along. Then the dug grids become floor and the piercings floor or doors.
    /// </summary>
    private void BuildTunnel(Level c, Loc grid1, Loc grid2)
    {
        var tun = _dun.Profile.Tunnel;
        var mainLoopCount = 0;
        var start = grid1;
        var bendIntvl = 0;
        var doorFlag = false;
        _dun.Tunn.Clear();
        _dun.Wall.Clear();
        var offset = CorrectDir(grid1, grid2);

        while (grid1 != grid2)
        {
            if (mainLoopCount++ > 2000) break;

            if (bendIntvl == 0)
            {
                if (_rng.RandInt0(100) < tun.Change)
                {
                    offset = CorrectDir(grid1, grid2);
                    if (_rng.RandInt0(100) < tun.Random) offset = RandDir();
                }
            }
            else --bendIntvl;

            var tmp = grid1 + offset;
            while (!InBounds(c, tmp))
            {
                offset = CorrectDir(grid1, grid2);
                if (_rng.RandInt0(100) < tun.Random) offset = RandDir();
                tmp = grid1 + offset;
            }

            // Avoid obstacles.
            if ((IsPerm(c, tmp) && !c[tmp].Has(SquareFlags.WallInner)) || IsGraniteWithFlag(c, tmp, SquareFlags.WallSolid))
                continue;

            if (IsGraniteWithFlag(c, tmp, SquareFlags.WallOuter))
            {
                var nxtdir = grid2 - tmp;
                if (nxtdir.X == 0 && nxtdir.Y == 0)
                {
                    grid1 = tmp;
                    PierceOuterWall(c, grid1);
                    continue;
                }
                if (Math.Abs(nxtdir.X) <= 1 && Math.Abs(nxtdir.Y) <= 1 && IsGraniteWithFlag(c, grid2, SquareFlags.WallOuter))
                    continue;
                var iroom = _dun.Ent2Room[tmp.Y, tmp.X];
                if (iroom != -1)
                {
                    if (IsRoom(c, grid1))
                    {
                        nxtdir = FindNormalToWall(c, tmp, false);
                        if (nxtdir.X == 0 && nxtdir.Y == 0) continue;
                        grid1 = tmp;
                        PierceOuterWall(c, grid1);
                    }
                    else
                    {
                        var bias = 80 - 80 * Math.Clamp(tun.Change, 0, 100) * Math.Clamp(tun.Random, 0, 100) / 10000;
                        int ntry = 0, mtry = 20;
                        Loc[] exc = [tmp, grid2];
                        var chk = new Loc(0, 0);
                        while (true)
                        {
                            if (ntry >= mtry) break;
                            chk = ChooseRandomEntrance(c, iroom, grid2, bias, exc);
                            if (chk.X == 0 && chk.Y == 0)
                            {
                                ntry = mtry;
                                break;
                            }
                            nxtdir = FindNormalToWall(c, chk, false);
                            if (nxtdir.X != 0 || nxtdir.Y != 0) break;
                            ++ntry;
                            bias = bias * 8 / 10;
                        }
                        if (ntry >= mtry) continue;
                        PierceOuterWall(c, tmp);
                        PierceOuterWall(c, chk);
                        grid1 = chk;
                    }
                    offset = nxtdir;
                    HandlePostWallStep(c, ref grid1, ref offset, ref doorFlag, ref bendIntvl);
                    continue;
                }

                nxtdir = FindNormalToWall(c, tmp, !IsRoom(c, grid1));
                if (nxtdir.X == 0 && nxtdir.Y == 0) continue;
                grid1 = tmp;
                PierceOuterWall(c, grid1);
                offset = nxtdir;
                HandlePostWallStep(c, ref grid1, ref offset, ref doorFlag, ref bendIntvl);
            }
            else if (IsRoom(c, tmp))
            {
                grid1 = tmp;
            }
            else if (IsGranite(c, tmp))
            {
                grid1 = tmp;
                if (_dun.Tunn.Count < GenConstants.TunnGridMax) _dun.Tunn.Add(grid1);
                doorFlag = false;
            }
            else
            {
                grid1 = tmp;
                if (!doorFlag)
                {
                    if (_dun.Door.Count < GenConstants.LevelDoorMax) _dun.Door.Add(grid1);
                    doorFlag = true;
                }
                if (_rng.RandInt0(100) >= tun.Continue)
                {
                    var d = grid1 - start;
                    if (Math.Abs(d.X) > 10 || Math.Abs(d.Y) > 10) break;
                }
            }
        }

        foreach (var g in _dun.Tunn) SetFeat(c, g, _f.Floor);
        foreach (var g in _dun.Wall)
        {
            SetFeat(c, g, _f.Floor);
            if (_rng.RandInt0(100) < tun.PierceDoor && AllowsWallPiercingDoor(c, g)) PlaceRandomDoor(c, g);
        }
    }

    /// <summary>Angband next_to_corr: corridor floors beside a grid (not diagonally).</summary>
    private int NextToCorr(Level c, Loc g)
    {
        var k = 0;
        for (var i = 0; i < 4; i++)
        {
            var g1 = g + Ddd[i];
            if (IsFloor(c, g1) && !IsRoom(c, g1)) k++;
        }
        return k;
    }

    /// <summary>Angband possible_doorway: between two corridors and two walls.</summary>
    private bool PossibleDoorway(Level c, Loc g)
    {
        if (NextToCorr(c, g) < 2) return false;
        if (IsStrongWall(c, g + new Loc(0, -1)) && IsStrongWall(c, g + new Loc(0, 1))) return true;
        return IsStrongWall(c, g + new Loc(-1, 0)) && IsStrongWall(c, g + new Loc(1, 0));
    }

    /// <summary>Angband try_door: a door (or now and then a trap) where a junction makes a doorway.</summary>
    private void TryDoor(Level c, Loc g)
    {
        if (IsStrongWall(c, g) || IsRoom(c, g) || HasTrap(c, g) || IsDoor(c, g)) return;
        var jct = _dun.Profile.Tunnel.JunctionDoor;
        if (_rng.RandInt0(100) < jct && PossibleDoorway(c, g)) PlaceRandomDoor(c, g);
        else if (_rng.RandInt0(500) < jct && PossibleDoorway(c, g)) PlaceTrap(c, g, c.Depth);
    }

    /// <summary>
    /// Angband do_traditional_tunneling: join the rooms in a scrambled ring, entrance to entrance,
    /// then try doors beside every junction.
    /// </summary>
    private void DoTraditionalTunneling(Level c)
    {
        var n = _dun.CentN;
        if (n == 0) return;
        var scrambled = new int[n];
        for (var i = 0; i < n; i++) scrambled[i] = i;
        for (var i = 0; i < n; i++)
        {
            var pick1 = _rng.RandInt0(n);
            var pick2 = _rng.RandInt0(n);
            (scrambled[pick1], scrambled[pick2]) = (scrambled[pick2], scrambled[pick1]);
        }
        _dun.Door.Clear();

        var grid = ChooseRandomEntrance(c, scrambled[n - 1], null, 80, []);
        if (grid.X == 0 && grid.Y == 0) grid = _dun.Cent[scrambled[n - 1]];
        for (var i = 0; i < n; i++)
        {
            var next = ChooseRandomEntrance(c, scrambled[i], grid, 80, []);
            if (next.X == 0 && next.Y == 0) next = _dun.Cent[scrambled[i]];
            BuildTunnel(c, next, grid);
            grid = next;
        }

        foreach (var d in _dun.Door.ToList())
        {
            TryDoor(c, d + new Loc(-1, 0));
            TryDoor(c, d + new Loc(1, 0));
            TryDoor(c, d + new Loc(0, -1));
            TryDoor(c, d + new Loc(0, 1));
        }
    }

    // --- Connectedness (colouring regions) -----------------------------------------------------------

    private bool IgnorePoint(Level c, int[] colors, Loc g)
    {
        if (!InBounds(c, g)) return true;
        if (colors[g.Y * c.Width + g.X] != 0) return true;
        if (IsPassable(c, g)) return false;
        return !IsDoor(c, g);
    }

    private void BuildColorPoint(Level c, int[] colors, int[] counts, bool[]? stairs, Loc g, int color, bool diagonal)
    {
        var w = c.Width;
        var added = new bool[c.Width * c.Height];
        var queue = new Queue<int>();
        queue.Enqueue(g.Y * w + g.X);
        counts[color] = 0;
        while (queue.Count > 0)
        {
            var n1 = queue.Dequeue();
            var g1 = new Loc(n1 % w, n1 / w);
            if (IgnorePoint(c, colors, g1)) continue;
            colors[n1] = color;
            counts[color]++;
            if (stairs is not null && IsStairs(c, g1)) stairs[color] = true;
            for (var i = 0; i < (diagonal ? 8 : 4); i++)
            {
                var g2 = g1 + Ddd[i];
                if (IgnorePoint(c, colors, g2)) continue;
                var n2 = g2.Y * w + g2.X;
                if (added[n2]) continue;
                queue.Enqueue(n2);
                added[n2] = true;
            }
        }
    }

    private void BuildColors(Level c, int[] colors, int[] counts, bool[]? stairs, bool diagonal)
    {
        var color = 1;
        for (var y = 0; y < c.Height; y++)
        for (var x = 0; x < c.Width; x++)
        {
            if (IgnorePoint(c, colors, new Loc(x, y))) continue;
            BuildColorPoint(c, colors, counts, stairs, new Loc(x, y), color, diagonal);
            color++;
        }
    }

    private void ClearSmallRegions(Level c, int[] colors, int[] counts, bool[]? stairs)
    {
        var size = c.Width * c.Height;
        var deleted = new bool[size];
        for (var i = 0; i < size; i++)
            if (counts[i] < 9 && (stairs is null || !stairs[i]))
            {
                deleted[i] = true;
                counts[i] = 0;
            }
        for (var y = 1; y < c.Height - 1; y++)
        for (var x = 1; x < c.Width - 1; x++)
        {
            var i = y * c.Width + x;
            if (!deleted[colors[i]]) continue;
            colors[i] = 0;
            SetMarkedGranite(c, new Loc(x, y), SquareFlags.WallSolid);
        }
    }

    private static int CountColors(int[] counts) => counts.Count(k => k > 0);

    private static int FirstColor(int[] counts)
    {
        for (var i = 0; i < counts.Length; i++) if (counts[i] > 0) return i;
        return -1;
    }

    private static void FixColors(int[] colors, int[] counts, int from, int to)
    {
        for (var i = 0; i < colors.Length; i++) if (colors[i] == from) colors[i] = to;
        counts[to] += counts[from];
        counts[from] = 0;
    }

    /// <summary>Angband join_region: tunnel from a region to its nearest neighbour (or a given one).</summary>
    private void JoinRegion(Level c, int[] colors, int[] counts, int color, int newColor, bool allowVaultDisconnect)
    {
        var w = c.Width;
        var size = c.Width * c.Height;
        var previous = new int[size];
        Array.Fill(previous, -1);
        var queue = new Queue<int>();
        for (var i = 0; i < size; i++)
            if (colors[i] == color)
            {
                queue.Enqueue(i);
                previous[i] = i;
            }

        while (queue.Count > 0)
        {
            var n1 = queue.Dequeue();
            var color2 = colors[n1];
            if (newColor == -1 && color2 != 0 && color2 != color) newColor = color2;
            if (color2 == newColor)
            {
                while (colors[n1] != color)
                {
                    var g = new Loc(n1 % w, n1 / w);
                    if (colors[n1] > 0) --counts[colors[n1]];
                    ++counts[color];
                    colors[n1] = color;
                    if (!IsPerm(c, g) && !IsVault(c, g) && !(IsPassable(c, g) || IsDoor(c, g))) SetFeat(c, g, _f.Floor);
                    n1 = previous[n1];
                }
                FixColors(colors, counts, color2, color);
                break;
            }
            for (var i = 0; i < 4; i++)
            {
                var g = new Loc(n1 % w, n1 / w) + Ddd[i];
                if (!InBounds(c, g)) continue;
                var n2 = g.Y * w + g.X;
                if (previous[n2] >= 0) continue;
                if (IsPerm(c, g)) continue;
                if (IsVault(c, g) && !allowVaultDisconnect) continue;
                queue.Enqueue(n2);
                previous[n2] = n1;
            }
        }
    }

    private void JoinRegions(Level c, int[] colors, int[] counts, bool allowVaultDisconnect)
    {
        var num = CountColors(counts);
        while (num > 1)
        {
            JoinRegion(c, colors, counts, FirstColor(counts), -1, allowVaultDisconnect);
            num--;
        }
    }

    /// <summary>Angband ensure_connectedness: colour the regions, then tunnel them together.</summary>
    private void EnsureConnectedness(Level c, bool allowVaultDisconnect)
    {
        var size = c.Width * c.Height;
        var colors = new int[size];
        var counts = new int[size];
        BuildColors(c, colors, counts, null, true);
        JoinRegions(c, colors, counts, allowVaultDisconnect);
    }
}
