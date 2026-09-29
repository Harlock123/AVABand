using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Generation;

// Angband gen-room.c: the room builders, the block map they reserve, and room_build.
public sealed partial class Cave
{
    private delegate bool RoomBuilder(Level c, Loc centre, int rating);

    /// <summary>The builder for a room profile's name (Angband list-rooms.h).</summary>
    private RoomBuilder? BuilderFor(string name) => name switch
    {
        "staircase room" => BuildStaircase,
        "simple room" => BuildSimple,
        "circular room" => BuildCircular,
        "overlap room" => BuildOverlap,
        "crossed room" => BuildCrossed,
        "large room" => BuildLarge,
        "monster nest" => BuildNest,
        "monster pit" => BuildPit,
        "room template" => BuildTemplate,
        "Interesting room" => (c, g, r) => BuildVaultType(c, g, "Interesting room"),
        "Lesser vault" => (c, g, r) => BuildVaultType(c, g, "Lesser vault"),
        "Lesser vault (new)" => (c, g, r) => BuildVaultType(c, g, "Lesser vault (new)"),
        "Medium vault" => (c, g, r) => BuildVaultType(c, g, "Medium vault"),
        "Medium vault (new)" => (c, g, r) => BuildVaultType(c, g, "Medium vault (new)"),
        "Greater vault" => (c, g, r) => HelpGreaterVault(c, g, "Greater vault"),
        "Greater vault (new)" => (c, g, r) => HelpGreaterVault(c, g, "Greater vault (new)"),
        "moria room" => BuildMoria,
        "room of chambers" => BuildRoomOfChambers,
        "huge room" => BuildHuge,
        _ => null,
    };

    /// <summary>The room profile names this generator knows (for checking the data).</summary>
    public static readonly IReadOnlySet<string> RoomNames = new HashSet<string>(
    [
        "staircase room", "simple room", "circular room", "overlap room", "crossed room", "large room", "monster nest",
        "monster pit", "room template", "Interesting room", "Lesser vault", "Lesser vault (new)", "Medium vault",
        "Medium vault (new)", "Greater vault", "Greater vault (new)", "moria room", "room of chambers", "huge room",
    ]);

    private bool OutsideChunk(Level c, Loc centre) => centre.Y >= c.Height || centre.X >= c.Width;

    /// <summary>Angband's occasional light: lit if the depth is at most 1d25.</summary>
    private bool RoomLight(Level c) => c.Depth <= _rng.RandInt1(25);

    // --- The block map ---------------------------------------------------------------------------------

    private bool CheckForUnreservedBlocks(int by1, int bx1, int by2, int bx2)
    {
        if (by1 < 0 || by2 >= _dun.RowBlocks || bx1 < 0 || bx2 >= _dun.ColBlocks) return false;
        for (var by = by1; by <= by2; by++)
        for (var bx = bx1; bx <= bx2; bx++)
            if (_dun.RoomMap[by, bx]) return false;
        return true;
    }

    private void ReserveBlocks(int by1, int bx1, int by2, int bx2)
    {
        for (var by = by1; by <= by2; by++)
        for (var bx = bx1; bx <= bx2; bx++)
            _dun.RoomMap[by, bx] = true;
    }

    /// <summary>Angband find_space: 25 tries at free blocks for a room; its centre, recorded, if found.</summary>
    private bool FindSpace(ref Loc centre, int height, int width)
    {
        var blocksHigh = 1 + (height - 1) / _dun.BlockHgt;
        var blocksWide = 1 + (width - 1) / _dun.BlockWid;
        for (var i = 0; i < 25; i++)
        {
            var by1 = _rng.RandInt0(_dun.RowBlocks);
            var bx1 = _rng.RandInt0(_dun.ColBlocks);
            var by2 = by1 + blocksHigh - 1;
            var bx2 = bx1 + blocksWide - 1;
            if (!CheckForUnreservedBlocks(by1, bx1, by2, bx2)) continue;
            centre = new Loc((bx1 + bx2 + 1) * _dun.BlockWid / 2, (by1 + by2 + 1) * _dun.BlockHgt / 2);
            if (_dun.CentN < GenConstants.LevelRoomMax) _dun.Cent.Add(centre);
            ReserveBlocks(by1, bx1, by2, bx2);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Angband room_build: a room of the profile at a block (or wherever it finds space), if deep
    /// enough and within the level's two pits; counts pits.
    /// </summary>
    private bool RoomBuild(Level c, int by0, int bx0, RoomProfileDef profile, bool findsOwnSpace)
    {
        var builder = BuilderFor(profile.Name);
        if (builder is null || c.Depth < profile.Level) return false;
        if (_dun.PitNum >= GenConstants.LevelPitMax && profile.Pit) return false;

        int by1 = by0, bx1 = bx0;
        var by2 = by0 + profile.Height / _dun.BlockHgt;
        var bx2 = bx0 + profile.Width / _dun.BlockWid;
        if (profile.Height % _dun.BlockHgt != 0) by2++;
        if (profile.Width % _dun.BlockWid != 0) bx2++;

        if (findsOwnSpace)
        {
            if (!builder(c, new Loc(c.Width, c.Height), profile.Rating)) return false;
        }
        else
        {
            if (!CheckForUnreservedBlocks(by1, bx1, by2, bx2)) return false;
            var centre = new Loc((bx1 + bx2 + 1) * _dun.BlockWid / 2, (by1 + by2 + 1) * _dun.BlockHgt / 2);
            if (_dun.CentN < GenConstants.LevelRoomMax) _dun.Cent.Add(centre);
            if (!builder(c, centre, profile.Rating))
            {
                _dun.Cent.RemoveAt(_dun.Cent.Count - 1);
                if (_dun.Ent.Count > _dun.CentN) _dun.Ent.RemoveRange(_dun.CentN, _dun.Ent.Count - _dun.CentN);
                return false;
            }
            ReserveBlocks(by1, bx1, by2, bx2);
        }
        if (profile.Pit) _dun.PitNum++;
        return true;
    }

    // --- Room drawing helpers ----------------------------------------------------------------------------

    private void FillXRange(Level c, int y, int x1, int x2, ushort feat, SquareFlags flag, bool light)
    {
        for (var x = x1; x <= x2; x++)
        {
            var g = new Loc(x, y);
            SetFeat(c, g, feat);
            SqOn(c, g, SquareFlags.Room | flag);
            if (light) SqOn(c, g, SquareFlags.Glow);
        }
    }

    private void FillYRange(Level c, int x, int y1, int y2, ushort feat, SquareFlags flag, bool light)
    {
        for (var y = y1; y <= y2; y++)
        {
            var g = new Loc(x, y);
            SetFeat(c, g, feat);
            SqOn(c, g, SquareFlags.Room | flag);
            if (light) SqOn(c, g, SquareFlags.Glow);
        }
    }

    /// <summary>Angband fill_circle.</summary>
    private void FillCircle(Level c, int y0, int x0, int radius, int border, ushort feat, SquareFlags flag, bool light)
    {
        var last = 0;
        var k = radius;
        var r2i2k2 = 0;
        for (var i = 0; i <= radius; i++)
        {
            var b = border;
            if (border != 0 && last > k) b++;
            FillXRange(c, y0 - i, x0 - k - b, x0 + k + b, feat, flag, light);
            FillXRange(c, y0 + i, x0 - k - b, x0 + k + b, feat, flag, light);
            FillYRange(c, x0 - i, y0 - k - b, y0 + k + b, feat, flag, light);
            FillYRange(c, x0 + i, y0 - k - b, y0 + k + b, feat, flag, light);
            last = k;
            if (i < radius)
            {
                r2i2k2 -= 2 * i + 1;
                while (true)
                {
                    var adj = 2 * k - 1;
                    if (Math.Abs(r2i2k2 + adj) >= Math.Abs(r2i2k2)) break;
                    --k;
                    r2i2k2 += adj;
                }
            }
        }
    }

    /// <summary>Angband generate_plus: a cross of wall through a rectangle's middle.</summary>
    private void GeneratePlus(Level c, int y1, int x1, int y2, int x2, ushort feat, SquareFlags flag)
    {
        var y0 = (y1 + y2) / 2;
        var x0 = (x1 + x2) / 2;
        for (var y = y1; y <= y2; y++) SetFeat(c, new Loc(x0, y), feat);
        if (flag != SquareFlags.None) GenerateMark(c, y1, x0, y2, x0, flag);
        for (var x = x1; x <= x2; x++) SetFeat(c, new Loc(x, y0), feat);
        if (flag != SquareFlags.None) GenerateMark(c, y0, x1, y0, x2, flag);
    }

    /// <summary>Angband generate_open: the middle of every side opened.</summary>
    private void GenerateOpen(Level c, int y1, int x1, int y2, int x2, ushort feat)
    {
        var y0 = (y1 + y2) / 2;
        var x0 = (x1 + x2) / 2;
        SetFeat(c, new Loc(x0, y1), feat);
        SetFeat(c, new Loc(x1, y0), feat);
        SetFeat(c, new Loc(x0, y2), feat);
        SetFeat(c, new Loc(x2, y0), feat);
    }

    /// <summary>Angband generate_hole: the middle of one random side opened.</summary>
    private void GenerateHole(Level c, int y1, int x1, int y2, int x2, ushort feat)
    {
        var y0 = (y1 + y2) / 2;
        var x0 = (x1 + x2) / 2;
        switch (_rng.RandInt0(4))
        {
            case 0: SetFeat(c, new Loc(x0, y1), feat); break;
            case 1: SetFeat(c, new Loc(x1, y0), feat); break;
            case 2: SetFeat(c, new Loc(x0, y2), feat); break;
            case 3: SetFeat(c, new Loc(x2, y0), feat); break;
        }
    }

    /// <summary>Angband set_bordering_walls: a room's edge floors become its outer wall.</summary>
    private void SetBorderingWalls(Level c, int y1, int x1, int y2, int x2)
    {
        var nx = x2 - x1 + 1;
        var walls = new bool[(x2 - x1 + 1) * (y2 - y1 + 1)];
        var ox = x1;
        var oy = y1;
        y1 = Math.Max(0, y1);
        y2 = Math.Min(c.Height - 1, y2);
        x1 = Math.Max(0, x1);
        x2 = Math.Min(c.Width - 1, x2);
        for (var y = y1; y <= y2; y++)
        {
            var adjy1 = Math.Max(0, y - 1);
            var adjy2 = Math.Min(c.Height - 1, y + 1);
            for (var x = x1; x <= x2; x++)
            {
                var g = new Loc(x, y);
                if (!IsFloor(c, g)) continue;
                var adjx1 = Math.Max(0, x - 1);
                var adjx2 = Math.Min(c.Width - 1, x + 1);
                if (adjy2 - adjy1 != 2 || adjx2 - adjx1 != 2)
                {
                    walls[x - ox + nx * (y - oy)] = true;
                    continue;
                }
                var nfloor = 0;
                for (var ay = adjy1; ay <= adjy2; ay++)
                for (var ax = adjx1; ax <= adjx2; ax++)
                    if (IsFloor(c, new Loc(ax, ay))) nfloor++;
                if (nfloor != 9) walls[x - ox + nx * (y - oy)] = true;
            }
        }
        for (var y = y1; y <= y2; y++)
        for (var x = x1; x <= x2; x++)
            if (walls[x - ox + nx * (y - oy)]) SetMarkedGranite(c, new Loc(x, y), SquareFlags.WallOuter);
    }

    // --- Starburst rooms (Angband generate_starburst_room, -LM-) -----------------------------------------

    /// <summary>
    /// Angband generate_starburst_room: a ragged, roughly oval area of the feature — arcs around a
    /// centre each reaching its own distance; long narrow areas are made of two or three.
    /// </summary>
    private bool GenerateStarburstRoom(Level c, int y1, int x1, int y2, int x2, bool light, ushort feat, bool specialOk)
    {
        if (!InBounds(c, new Loc(x1, y1)) || !InBounds(c, new Loc(x2, y2))) return false;
        if (y1 + 2 >= y2 || x1 + 2 >= x2) return false;

        var height = 1 + y2 - y1;
        var width = 1 + x2 - x1;
        var isFloor = _data.Terrain[feat].Has(TerrainFlags.Floor);

        if (height > 5 * width / 2 || width > 5 * height / 2)
        {
            int tmpAy = y2, tmpAx = x2;
            if (height > width) tmpAy = y1 + 2 * height / 3;
            else tmpAx = x1 + 2 * width / 3;
            GenerateStarburstRoom(c, y1, x1, tmpAy, tmpAx, light, feat, false);
            int tmpBy = y1, tmpBx = x1;
            if (height > width) tmpBy = y1 + height / 3;
            else tmpBx = x1 + width / 3;
            GenerateStarburstRoom(c, tmpBy, tmpBx, y2, x2, light, feat, false);
            if (isFloor)
            {
                for (var y = (y1 + tmpAy) / 2; y <= (tmpBy + y2) / 2; y++)
                for (var x = (x1 + tmpAx) / 2; x <= (tmpBx + x2) / 2; x++)
                    SetFeat(c, new Loc(x, y), feat);
            }
            else
            {
                int cy1, cx1, cy2, cx2;
                if (height > width)
                {
                    cy1 = y1 + (height - width) / 2;
                    cx1 = x1;
                    cy2 = cy1 - (height - width) / 2;
                    cx2 = x2;
                }
                else
                {
                    cy1 = y1;
                    cx1 = x1 + (width - height) / 2;
                    cy2 = y2;
                    cx2 = cx1 + (width - height) / 2;
                }
                GenerateStarburstRoom(c, cy1, cx1, cy2, cx2, light, feat, false);
            }
            return true;
        }

        var distConv = width > 44 || height > 44 ? (width > height ? 10 * width / 44 : 10 * height / 44) : 10;
        bool cloverleaf;
        int arcNum;
        if (specialOk && height > 10 && _rng.RandInt0(20) == 0)
        {
            arcNum = 12;
            cloverleaf = true;
        }
        else
        {
            cloverleaf = false;
            arcNum = 8 + height * width / 80;
            arcNum = Math.Clamp(arcNum + 3 - _rng.RandInt0(7), 8, 45);
        }

        var y0 = y1 + height / 2;
        var x0 = x1 + width / 2;
        var arc = new int[45, 2];
        var degreeFirst = 0;
        for (var i = 0; i < arcNum; i++)
        {
            arc[i, 0] = degreeFirst;
            degreeFirst += (180 + _rng.RandInt0(arcNum)) / arcNum;
            if (degreeFirst < 180 * (i + 1) / arcNum) degreeFirst = 180 * (i + 1) / arcNum;
            if (degreeFirst > (180 + arcNum) * (i + 1) / arcNum) degreeFirst = (180 + arcNum) * (i + 1) / arcNum;
            var centreOfArc = degreeFirst + arc[i, 0];
            if ((centreOfArc > 45 && centreOfArc < 135) || (centreOfArc > 225 && centreOfArc < 315))
                arc[i, 1] = height / 4 + _rng.RandInt0((height + 3) / 4);
            else if (centreOfArc < 45 || centreOfArc > 315 || (centreOfArc < 225 && centreOfArc > 135))
                arc[i, 1] = width / 4 + _rng.RandInt0((width + 3) / 4);
            else if (i != 0)
                arc[i, 1] = cloverleaf ? 0 : arc[i - 1, 1] + 3 - _rng.RandInt0(7);

            if (!cloverleaf && i != 0 && i != arcNum - 1)
            {
                // (4.2.5 has no smooth terrain, so only its general rule applies.)
                if (arc[i, 1] > 3 * (arc[i - 1, 1] + 1) / 2) arc[i, 1] = 3 * (arc[i - 1, 1] + 1) / 2;
                if (arc[i, 1] < 2 * (arc[i - 1, 1] - 1) / 3) arc[i, 1] = 2 * (arc[i - 1, 1] - 1) / 3;
            }
            if (i == arcNum - 1 && Math.Abs(arc[i, 1] - arc[0, 1]) > 3)
            {
                if (arc[i, 1] > arc[0, 1]) arc[i, 1] -= _rng.RandInt0(arc[i, 1] - arc[0, 1]);
                else if (arc[i, 1] < arc[0, 1]) arc[i, 1] += _rng.RandInt0(arc[0, 1] - arc[i, 1]);
            }
        }

        var distCheck = 21 * distConv / 10;
        var passable = _data.Terrain[feat].Has(TerrainFlags.Passable);
        for (var y = y1 + 1; y < y2; y++)
        for (var x = x1 + 1; x < x2; x++)
        {
            var g = new Loc(x, y);
            if (IsVault(c, g) || Occupied(c).Contains(g)) continue;
            var dist = new Loc(x0, y0).DistanceTo(g);
            if (dist >= distCheck) continue;
            var ny = 20 + 10 * (y - y0) / distConv;
            var nx = 20 + 10 * (x - x0) / distConv;
            if (ny < 0 || ny > 40 || nx < 0 || nx > 40) continue;
            var degree = AngleToGrid[ny][nx];
            for (var i = arcNum - 1; i >= 0; i--)
            {
                if (arc[i, 0] > degree) continue;
                var maxDist = arc[i, 1];
                if (maxDist >= dist)
                {
                    if (isFloor || !passable)
                    {
                        SetFeat(c, g, feat);
                        if (isFloor) SqOn(c, g, SquareFlags.Room);
                        else SqOff(c, g, SquareFlags.Room);
                        if (light) SqOn(c, g, SquareFlags.Glow);
                        else if (!IsBright(c, g)) SqOff(c, g, SquareFlags.Glow);
                    }
                    else
                    {
                        if (IsFloor(c, g) && _rng.RandInt1(maxDist + 5) >= dist + 5) SetFeat(c, g, feat);
                        if (light) SqOn(c, g, SquareFlags.Glow);
                    }
                }
                break;
            }
        }

        if (isFloor || feat == _f.Granite)
            for (var y = y1 + 1; y < y2; y++)
            for (var x = x1 + 1; x < x2; x++)
            {
                var g = new Loc(x, y);
                if (!IsFloor(c, g)) continue;
                for (var d = 0; d < 8; d++)
                {
                    var g1 = g + Ddd[d];
                    SqOn(c, g1, SquareFlags.Room | SquareFlags.NoStairs);
                    if (light) SqOn(c, g1, SquareFlags.Glow);
                    if (c[g1].Feature == _f.Granite) SetMarkedGranite(c, g1, SquareFlags.WallOuter);
                }
            }
        return true;
    }

    // --- The builders --------------------------------------------------------------------------------------

    /// <summary>Angband build_staircase: a one-grid room holding a staircase of a stored neighbour level.</summary>
    private bool BuildStaircase(Level c, Loc centre, int rating)
    {
        if (_dun.CurrJoin is not { } join) return false;
        if (!OutsideChunk(c, centre)) return false;
        centre = join.Grid;
        if (centre.Y < 1 || centre.Y > c.Height - 2 || centre.X < 1 || centre.X > c.Width - 2) return false;
        var tl = new Loc(centre.X - (centre.X > 1 ? 2 : 1), centre.Y - (centre.Y > 1 ? 2 : 1));
        var br = new Loc(centre.X + (centre.X < c.Width - 2 ? 2 : 1), centre.Y + (centre.Y < c.Height - 2 ? 2 : 1));
        int by1 = tl.Y / _dun.BlockHgt, bx1 = tl.X / _dun.BlockWid, by2 = br.Y / _dun.BlockHgt, bx2 = br.X / _dun.BlockWid;
        if (_dun.BlockHgt > 1 || _dun.BlockWid > 1)
        {
            if (CaveFindInRange(c, tl, br, (l, g) => IsRoom(l, g)) is not null) return false;
        }
        else if (!CheckForUnreservedBlocks(by1, bx1, by2, bx2)) return false;
        ReserveBlocks(by1, bx1, by2, bx2);
        if (_dun.CentN < GenConstants.LevelRoomMax) _dun.Cent.Add(centre);

        GenerateRoom(c, centre.Y - 1, centre.X - 1, centre.Y + 1, centre.X + 1, false);
        DrawRectangle(c, centre.Y - 1, centre.X - 1, centre.Y + 1, centre.X + 1, _f.Granite, SquareFlags.WallOuter, false);
        SetFeat(c, centre, join.Feat);
        return true;
    }

    /// <summary>Angband build_circular: a round room, a large one with a middle chamber.</summary>
    private bool BuildCircular(Level c, Loc centre, int rating)
    {
        var radius = 2 + _rng.RandInt1(2) + _rng.RandInt1(3);
        var light = RoomLight(c);
        if (OutsideChunk(c, centre) && !FindSpace(ref centre, 2 * radius + 10, 2 * radius + 10)) return false;
        FillCircle(c, centre.Y, centre.X, radius + 1, 0, _f.Floor, SquareFlags.None, light);
        SetBorderingWalls(c, centre.Y - radius - 2, centre.X - radius - 2, centre.Y + radius + 2, centre.X + radius + 2);
        if (radius - 4 > 0 && _rng.RandInt0(4) < radius - 4)
        {
            var offset = RandDir();
            DrawRectangle(c, centre.Y - 2, centre.X - 2, centre.Y + 2, centre.X + 2, _f.Granite, SquareFlags.WallInner, false);
            PlaceClosedDoor(c, new Loc(centre.X + offset.X * 2, centre.Y + offset.Y * 2));
            VaultObjects(c, centre, c.Depth, _rng.RandInt0(2));
            VaultMonsters(c, centre, c.Depth + 1, _rng.RandInt0(3));
        }
        return true;
    }

    /// <summary>Angband build_simple: a rectangle, now and then with pillars or ragged sides.</summary>
    private bool BuildSimple(Level c, Loc centre, int rating)
    {
        var height = 1 + _rng.RandInt1(4) + _rng.RandInt1(3);
        var width = 1 + _rng.RandInt1(11) + _rng.RandInt1(11);
        if (OutsideChunk(c, centre) && !FindSpace(ref centre, height + 2, width + 2)) return false;
        var y1 = centre.Y - height / 2;
        var x1 = centre.X - width / 2;
        var y2 = y1 + height - 1;
        var x2 = x1 + width - 1;
        var light = RoomLight(c);
        GenerateRoom(c, y1 - 1, x1 - 1, y2 + 1, x2 + 1, light);
        DrawRectangle(c, y1 - 1, x1 - 1, y2 + 1, x2 + 1, _f.Granite, SquareFlags.WallOuter, false);
        FillRectangle(c, y1, x1, y2, x2, _f.Floor, SquareFlags.None);

        if (_rng.OneIn(20))
        {
            var offx = (x2 - x1) % 2 == 0 ? 0 : _rng.RandInt0(2);
            var offy = (y2 - y1) % 2 == 0 ? 0 : _rng.RandInt0(2);
            for (var y = y1 + offy; y <= y2; y += 2)
            for (var x = x1 + offx; x <= x2; x += 2)
                SetMarkedGranite(c, new Loc(x, y), SquareFlags.WallInner);
            if (offy == 0)
            {
                if (offx == 0) SqOff(c, new Loc(x1 - 1, y1 - 1), SquareFlags.Room | SquareFlags.WallOuter);
                if ((x2 - x1 - offx) % 2 == 0) SqOff(c, new Loc(x2 + 1, y1 - 1), SquareFlags.Room | SquareFlags.WallOuter);
            }
            if ((y2 - y1 - offy) % 2 == 0)
            {
                if (offx == 0) SqOff(c, new Loc(x1 - 1, y2 + 1), SquareFlags.Room | SquareFlags.WallOuter);
                if ((x2 - x1 - offx) % 2 == 0) SqOff(c, new Loc(x2 + 1, y2 + 1), SquareFlags.Room | SquareFlags.WallOuter);
            }
        }
        else if (_rng.OneIn(50))
        {
            var offx = (x2 - x1) % 2 == 0 ? 0 : _rng.RandInt0(2);
            var offy = (y2 - y1) % 2 == 0 ? 0 : _rng.RandInt0(2);
            for (var y = y1 + 2 + offy; y <= y2 - 2; y += 2)
            {
                SetMarkedGranite(c, new Loc(x1, y), SquareFlags.WallInner);
                SetMarkedGranite(c, new Loc(x2, y), SquareFlags.WallInner);
            }
            for (var x = x1 + 2 + offx; x <= x2 - 2; x += 2)
            {
                SetMarkedGranite(c, new Loc(x, y1), SquareFlags.WallInner);
                SetMarkedGranite(c, new Loc(x, y2), SquareFlags.WallInner);
            }
        }
        return true;
    }

    /// <summary>Angband build_overlap: two rectangles laid over each other.</summary>
    private bool BuildOverlap(Level c, Loc centre, int rating)
    {
        var light = RoomLight(c);
        int y1a = _rng.RandInt1(4), x1a = _rng.RandInt1(11), y2a = _rng.RandInt1(3), x2a = _rng.RandInt1(10);
        int y1b = _rng.RandInt1(3), x1b = _rng.RandInt1(10), y2b = _rng.RandInt1(4), x2b = _rng.RandInt1(11);
        var height = 2 * Math.Max(Math.Max(y1a, y2a), Math.Max(y1b, y2b)) + 1;
        var width = 2 * Math.Max(Math.Max(x1a, x2a), Math.Max(x1b, x2b)) + 1;
        if (OutsideChunk(c, centre) && !FindSpace(ref centre, height + 2, width + 2)) return false;
        y1a = centre.Y - y1a; x1a = centre.X - x1a; y2a = centre.Y + y2a; x2a = centre.X + x2a;
        y1b = centre.Y - y1b; x1b = centre.X - x1b; y2b = centre.Y + y2b; x2b = centre.X + x2b;
        GenerateRoom(c, y1a - 1, x1a - 1, y2a + 1, x2a + 1, light);
        GenerateRoom(c, y1b - 1, x1b - 1, y2b + 1, x2b + 1, light);
        DrawRectangle(c, y1a - 1, x1a - 1, y2a + 1, x2a + 1, _f.Granite, SquareFlags.WallOuter, false);
        DrawRectangle(c, y1b - 1, x1b - 1, y2b + 1, x2b + 1, _f.Granite, SquareFlags.WallOuter, false);
        FillRectangle(c, y1a, x1a, y2a, x2a, _f.Floor, SquareFlags.None);
        FillRectangle(c, y1b, x1b, y2b, x2b, _f.Floor, SquareFlags.None);
        return true;
    }

    /// <summary>Angband build_crossed: a plus-shaped room, sometimes with something in the middle.</summary>
    private bool BuildCrossed(Level c, Loc centre, int rating)
    {
        var light = RoomLight(c);
        const int wy = 1, wx = 1;
        var dy = _rng.RandRange(3, 4);
        var dx = _rng.RandRange(3, 11);
        var height = Math.Max(dy + dy + 1, wy + wy + 1);
        var width = Math.Max(wx + wx + 1, dx + dx + 1);
        if (OutsideChunk(c, centre) && !FindSpace(ref centre, height + 2, width + 2)) return false;
        int y1a = centre.Y - dy, x1a = centre.X - wx, y2a = centre.Y + dy, x2a = centre.X + wx;
        int y1b = centre.Y - wy, x1b = centre.X - dx, y2b = centre.Y + wy, x2b = centre.X + dx;
        GenerateRoom(c, y1a - 1, x1a - 1, y2a + 1, x2a + 1, light);
        GenerateRoom(c, y1b - 1, x1b - 1, y2b + 1, x2b + 1, light);
        DrawRectangle(c, y1a - 1, x1a - 1, y2a + 1, x2a + 1, _f.Granite, SquareFlags.WallOuter, false);
        DrawRectangle(c, y1b - 1, x1b - 1, y2b + 1, x2b + 1, _f.Granite, SquareFlags.WallOuter, false);
        FillRectangle(c, y1a, x1a, y2a, x2a, _f.Floor, SquareFlags.None);
        FillRectangle(c, y1b, x1b, y2b, x2b, _f.Floor, SquareFlags.None);

        switch (_rng.RandInt1(4))
        {
            case 2:
                FillRectangle(c, y1b, x1a, y2b, x2a, _f.Granite, SquareFlags.WallInner);
                break;
            case 3:
                DrawRectangle(c, y1b, x1a, y2b, x2a, _f.Granite, SquareFlags.WallInner, false);
                GenerateHole(c, y1b, x1a, y2b, x2a, _f.SecretDoor);
                PlaceObject(c, centre, c.Depth, false, false);
                VaultMonsters(c, centre, c.Depth + 2, _rng.RandInt0(2) + 3);
                VaultTraps(c, centre, 4, 4, _rng.RandInt0(3) + 2);
                break;
            case 4:
                if (_rng.OneIn(3))
                {
                    for (var y = y1b; y <= y2b; y++)
                    {
                        if (y == centre.Y) continue;
                        SetMarkedGranite(c, new Loc(x1a - 1, y), SquareFlags.WallInner);
                        SetMarkedGranite(c, new Loc(x2a + 1, y), SquareFlags.WallInner);
                    }
                    for (var x = x1a; x <= x2a; x++)
                    {
                        if (x == centre.X) continue;
                        SetMarkedGranite(c, new Loc(x, y1b - 1), SquareFlags.WallInner);
                        SetMarkedGranite(c, new Loc(x, y2b + 1), SquareFlags.WallInner);
                    }
                    if (_rng.OneIn(3)) GenerateOpen(c, y1b - 1, x1a - 1, y2b + 1, x2a + 1, _f.ClosedDoor);
                }
                else if (_rng.OneIn(3)) GeneratePlus(c, y1b, x1a, y2b, x2a, _f.Granite, SquareFlags.WallInner);
                else if (_rng.OneIn(3)) SetMarkedGranite(c, centre, SquareFlags.WallInner);
                break;
        }
        return true;
    }

    /// <summary>Angband build_large: a large room around an inner room of five kinds.</summary>
    private bool BuildLarge(Level c, Loc centre, int rating)
    {
        const int height = 9, width = 23;
        var light = RoomLight(c);
        if (OutsideChunk(c, centre) && !FindSpace(ref centre, height + 2, width + 2)) return false;
        int y1 = centre.Y - height / 2, y2 = centre.Y + height / 2, x1 = centre.X - width / 2, x2 = centre.X + width / 2;
        GenerateRoom(c, y1 - 1, x1 - 1, y2 + 1, x2 + 1, light);
        DrawRectangle(c, y1 - 1, x1 - 1, y2 + 1, x2 + 1, _f.Granite, SquareFlags.WallOuter, false);
        FillRectangle(c, y1, x1, y2, x2, _f.Floor, SquareFlags.None);
        y1 += 2; y2 -= 2; x1 += 2; x2 -= 2;
        DrawRectangle(c, y1 - 1, x1 - 1, y2 + 1, x2 + 1, _f.Granite, SquareFlags.WallInner, false);

        switch (_rng.RandInt1(5))
        {
            case 1:
                GenerateHole(c, y1 - 1, x1 - 1, y2 + 1, x2 + 1, _f.ClosedDoor);
                VaultMonsters(c, centre, c.Depth + 2, 1);
                break;
            case 2:
                GenerateHole(c, y1 - 1, x1 - 1, y2 + 1, x2 + 1, _f.ClosedDoor);
                DrawRectangle(c, centre.Y - 1, centre.X - 1, centre.Y + 1, centre.X + 1, _f.Granite, SquareFlags.WallInner, false);
                GenerateHole(c, centre.Y - 1, centre.X - 1, centre.Y + 1, centre.X + 1, _f.ClosedDoor);
                for (var y = centre.Y - 1; y <= centre.Y + 1; y++)
                for (var x = centre.X - 1; x <= centre.X + 1; x++)
                    if (IsClosedDoor(c, new Loc(x, y))) c[new Loc(x, y)].LockPower = (byte)_rng.RandInt1(7);
                VaultMonsters(c, centre, c.Depth + 2, _rng.RandInt1(3) + 2);
                if (_rng.RandInt0(100) < 80 || _dun.Persist) PlaceObject(c, centre, c.Depth, false, false);
                else PlaceRandomStairs(c, centre, _dun.Quest);
                VaultTraps(c, centre, 4, 10, 2 + _rng.RandInt1(3));
                break;
            case 3:
                GenerateHole(c, y1 - 1, x1 - 1, y2 + 1, x2 + 1, _f.ClosedDoor);
                FillRectangle(c, centre.Y - 1, centre.X - 1, centre.Y + 1, centre.X + 1, _f.Granite, SquareFlags.WallInner);
                if (_rng.OneIn(2))
                {
                    if (_rng.OneIn(2))
                    {
                        FillRectangle(c, centre.Y - 1, centre.X - 7, centre.Y + 1, centre.X - 5, _f.Granite, SquareFlags.WallInner);
                        FillRectangle(c, centre.Y - 1, centre.X + 5, centre.Y + 1, centre.X + 7, _f.Granite, SquareFlags.WallInner);
                    }
                    else
                    {
                        FillRectangle(c, centre.Y - 1, centre.X - 6, centre.Y + 1, centre.X - 4, _f.Granite, SquareFlags.WallInner);
                        FillRectangle(c, centre.Y - 1, centre.X + 4, centre.Y + 1, centre.X + 6, _f.Granite, SquareFlags.WallInner);
                    }
                }
                if (_rng.OneIn(3))
                {
                    DrawRectangle(c, centre.Y - 1, centre.X - 5, centre.Y + 1, centre.X + 5, _f.Granite, SquareFlags.WallInner, false);
                    PlaceSecretDoor(c, new Loc(centre.X - 3, centre.Y - 3 + _rng.RandInt1(2) * 2));
                    PlaceSecretDoor(c, new Loc(centre.X + 3, centre.Y - 3 + _rng.RandInt1(2) * 2));
                    VaultMonsters(c, new Loc(centre.X - 2, centre.Y), c.Depth + 2, _rng.RandInt1(2));
                    VaultMonsters(c, new Loc(centre.X + 2, centre.Y), c.Depth + 2, _rng.RandInt1(2));
                    if (_rng.OneIn(3)) PlaceObject(c, new Loc(centre.X - 2, centre.Y), c.Depth, false, false);
                    if (_rng.OneIn(3)) PlaceObject(c, new Loc(centre.X + 2, centre.Y), c.Depth, false, false);
                }
                break;
            case 4:
                GenerateHole(c, y1 - 1, x1 - 1, y2 + 1, x2 + 1, _f.ClosedDoor);
                for (var y = y1; y <= y2; y++)
                for (var x = x1; x <= x2; x++)
                    if (((x + y) & 1) != 0) SetMarkedGranite(c, new Loc(x, y), SquareFlags.WallInner);
                VaultMonsters(c, new Loc(centre.X - 5, centre.Y), c.Depth + 2, _rng.RandInt1(3));
                VaultMonsters(c, new Loc(centre.X + 5, centre.Y), c.Depth + 2, _rng.RandInt1(3));
                VaultTraps(c, new Loc(centre.X - 3, centre.Y), 2, 8, _rng.RandInt1(3));
                VaultTraps(c, new Loc(centre.X + 3, centre.Y), 2, 8, _rng.RandInt1(3));
                VaultObjects(c, centre, c.Depth, 3);
                break;
            case 5:
                GeneratePlus(c, y1, x1, y2, x2, _f.Granite, SquareFlags.WallInner);
                if (_rng.RandInt0(100) < 50)
                {
                    var i = _rng.RandInt1(10);
                    PlaceClosedDoor(c, new Loc(centre.X - i, y1 - 1));
                    PlaceClosedDoor(c, new Loc(centre.X + i, y1 - 1));
                    PlaceClosedDoor(c, new Loc(centre.X - i, y2 + 1));
                    PlaceClosedDoor(c, new Loc(centre.X + i, y2 + 1));
                }
                else
                {
                    var i = _rng.RandInt1(3);
                    PlaceClosedDoor(c, new Loc(x1 - 1, centre.Y + i));
                    PlaceClosedDoor(c, new Loc(x1 - 1, centre.Y - i));
                    PlaceClosedDoor(c, new Loc(x2 + 1, centre.Y + i));
                    PlaceClosedDoor(c, new Loc(x2 + 1, centre.Y - i));
                }
                VaultObjects(c, centre, c.Depth, 2 + _rng.RandInt1(2));
                VaultMonsters(c, new Loc(centre.X - 4, centre.Y + 1), c.Depth + 2, _rng.RandInt1(4));
                VaultMonsters(c, new Loc(centre.X + 4, centre.Y + 1), c.Depth + 2, _rng.RandInt1(4));
                VaultMonsters(c, new Loc(centre.X - 4, centre.Y - 1), c.Depth + 2, _rng.RandInt1(4));
                VaultMonsters(c, new Loc(centre.X + 4, centre.Y - 1), c.Depth + 2, _rng.RandInt1(4));
                break;
        }
        return true;
    }

    /// <summary>Angband build_moria: a starburst room, sometimes huge or long, now and then strewn with rubble.</summary>
    private bool BuildMoria(Level c, Loc centre, int rating)
    {
        var light = c.Depth <= _rng.RandInt1(35);
        var height = 8 + _rng.RandInt0(5);
        var width = 10 + _rng.RandInt0(5);
        for (var i = 0; i < 2; i++)
        {
            if (i == 0 && _rng.OneIn(15))
            {
                height *= 1 + _rng.RandInt1(2);
                width *= 2 + _rng.RandInt1(3);
            }
            else if (!_rng.OneIn(4))
            {
                if (_rng.OneIn(15)) height *= 2 + _rng.RandInt0(2);
                else width *= 2 + _rng.RandInt0(3);
            }
            if (!OutsideChunk(c, centre)) break;
            if (FindSpace(ref centre, height, width)) break;
            if (i == 1) return false;
        }
        var y1 = centre.Y - height / 2;
        var x1 = centre.X - width / 2;
        var y2 = y1 + height - 1;
        var x2 = x1 + width - 1;
        if (!GenerateStarburstRoom(c, y1, x1, y2, x2, light, _f.Floor, true)) return false;
        if (_rng.OneIn(10))
            GenerateStarburstRoom(c, y1 + _rng.RandInt0(height / 4), x1 + _rng.RandInt0(width / 4),
                y2 - _rng.RandInt0(height / 4), x2 - _rng.RandInt0(width / 4), false, _f.PassableRubble, false);
        return true;
    }

    /// <summary>
    /// Angband build_huge: a vast lit starburst, often broken by fields of rubble — only as the
    /// first room of a level, one time in twenty.
    /// </summary>
    private bool BuildHuge(Level c, Loc centre, int rating)
    {
        var findingSpace = OutsideChunk(c, centre);
        var height = 30 + _rng.RandInt0(10);
        var width = 45 + _rng.RandInt0(50);
        if (_dun.CentN - _dun.NStairRoom > (findingSpace ? 0 : 1)) return false;
        if (!_rng.OneIn(20)) return false;
        var light = !_rng.OneIn(3);
        if (findingSpace && !FindSpace(ref centre, height, width)) return false;
        var y1 = centre.Y - height / 2;
        var x1 = centre.X - width / 2;
        var y2 = y1 + height - 1;
        var x2 = x1 + width - 1;
        if (!GenerateStarburstRoom(c, y1, x1, y2, x2, light, _f.Floor, false)) return false;
        if (_rng.RandInt1(5) > 2)
        {
            var count = height * width * _rng.RandInt1(2) / 1100;
            for (var i = 0; i < count; i++)
            {
                var ht = 8 + _rng.RandInt0(16);
                var wt = 10 + _rng.RandInt0(24);
                var ty1 = y1 + _rng.RandInt0(height - ht);
                var tx1 = x1 + _rng.RandInt0(width - wt);
                GenerateStarburstRoom(c, ty1, tx1, ty1 + ht, tx1 + wt, false, _f.PassableRubble, false);
            }
        }
        return true;
    }

    // --- Rooms of chambers -----------------------------------------------------------------------------

    private void MakeInnerChamberWall(Level c, int y, int x)
    {
        var g = new Loc(x, y);
        if (c[g].Feature != _f.Granite && c[g].Feature != _f.Magma) return;
        if (IsWallOuter(c, g) || IsWallSolid(c, g)) return;
        SetMarkedGranite(c, g, SquareFlags.WallInner);
    }

    private void MakeChamber(Level c, int y1, int x1, int y2, int x2)
    {
        FillRectangle(c, y1 + 1, x1 + 1, y2 - 1, x2 - 1, _f.Magma, SquareFlags.None);
        for (var y = y1; y <= y2; y++)
        {
            MakeInnerChamberWall(c, y, x1);
            MakeInnerChamberWall(c, y, x2);
        }
        for (var x = x1; x <= x2; x++)
        {
            MakeInnerChamberWall(c, y1, x);
            MakeInnerChamberWall(c, y2, x);
        }
        for (var i = 0; i < 20; i++)
        {
            int x, y;
            if (_rng.OneIn(2))
            {
                x = _rng.OneIn(2) ? x1 : x2;
                y = y1 + _rng.RandInt0(1 + Math.Abs(y2 - y1));
            }
            else
            {
                y = _rng.OneIn(2) ? y1 : y2;
                x = x1 + _rng.RandInt0(1 + Math.Abs(x2 - x1));
            }
            var g = new Loc(x, y);
            if (!IsWallInner(c, g) || !InBoundsFully(c, g)) continue;
            var count = 0;
            for (var d = 0; d < 9; d++)
            {
                var n = g + Ddd[d];
                if (c[n].Feature == _f.OpenDoor) break;
                if (IsWallInner(c, n)) count++;
                if (count > 3) break;
                if (d == 8)
                {
                    SetFeat(c, g, _f.OpenDoor);
                    return;
                }
            }
        }
    }

    private void HollowOutRoom(Level c, Loc start)
    {
        // Angband's recursive flood, made iterative.
        var stack = new Stack<Loc>([start]);
        while (stack.Count > 0)
        {
            var g = stack.Pop();
            for (var d = 0; d < 9; d++)
            {
                var g1 = g + Ddd[d];
                if (c[g1].Feature == _f.Magma)
                {
                    SetFeat(c, g1, _f.Floor);
                    stack.Push(g1);
                }
                else if (c[g1].Feature == _f.OpenDoor)
                {
                    SetFeat(c, g1, _f.BrokenDoor);
                    stack.Push(g1);
                }
            }
        }
    }

    /// <summary>
    /// Angband build_room_of_chambers (-LM-): a big area of magma-filled chambers, hollowed out
    /// one from another by doors and short tunnels, then filled with a themed crowd.
    /// </summary>
    private bool BuildRoomOfChambers(Level c, Loc centre, int rating)
    {
        var light = _rng.RandInt0(45) > c.Depth;
        var height = 20 + Items.ObjectFactory.MagicBonus(_rng, 20, c.Depth);
        var width = 20 + _rng.RandInt1(20) + Items.ObjectFactory.MagicBonus(_rng, 20, c.Depth);
        if (OutsideChunk(c, centre) && !FindSpace(ref centre, height, width)) return false;
        var y1 = centre.Y - height / 2;
        var x1 = centre.X - width / 2;
        var y2 = centre.Y + (height - 1) / 2;
        var x2 = centre.X + (width - 1) / 2;
        if (!InBounds(c, new Loc(x1, y1)) || !InBounds(c, new Loc(x2, y2))) return false;

        var area = Math.Abs(y2 - y1) * Math.Abs(x2 - x1);
        var numChambers = 10 + area / 80;
        for (var i = 0; i < numChambers; i++)
        {
            var size = 3 + _rng.RandInt0(4);
            var wl = size + _rng.RandInt0(10);
            var hl = size + _rng.RandInt0(4);
            var cy1 = y1 + _rng.RandInt0(1 + y2 - y1 - hl);
            var cx1 = x1 + _rng.RandInt0(1 + x2 - x1 - wl);
            var cy2 = Math.Min(cy1 + hl, y2);
            var cx2 = Math.Min(cx1 + wl, x2);
            MakeChamber(c, cy1, cx1, cy2, cx2);
        }

        for (var y = y1; y <= y2; y++)
        for (var x = x1; x <= x2; x++)
        {
            var g = new Loc(x, y);
            if (!InBoundsFully(c, g)) continue;
            var count = 0;
            for (var d = 0; d < 8; d++)
            {
                var g1 = g + Ddd[d];
                if (c[g1].Feature == _f.Granite && !IsWallOuter(c, g1) && !IsWallSolid(c, g1)) count++;
            }
            if (count == 5 && c[g].Feature != _f.Magma) SetMarkedGranite(c, g, SquareFlags.WallInner);
            else if (count > 5) SetMarkedGranite(c, g, SquareFlags.WallInner);
        }

        var grid = new Loc(x1, y1);
        for (var i = 0; i < 50; i++)
        {
            grid = new Loc(x1 + Math.Abs(x2 - x1) / 4 + _rng.RandInt0(Math.Abs(x2 - x1) / 2),
                y1 + Math.Abs(y2 - y1) / 4 + _rng.RandInt0(Math.Abs(y2 - y1) / 2));
            if (c[grid].Feature == _f.Magma) break;
        }
        SetFeat(c, grid, _f.Floor);
        HollowOutRoom(c, grid);

        for (var i = 0; i < 100; i++)
        {
            var joy = false;
            for (var y = y1; y < y2; y++)
            for (var x = x1; x < x2; x++)
            {
                var g = new Loc(x, y);
                if (c[g].Feature != _f.Magma || !InBoundsFully(c, g)) continue;
                for (var d = 0; d < 4; d++)
                {
                    var g1 = g + Ddd[d];
                    if (!IsWallInner(c, g1)) continue;
                    var g2 = g1 + Ddd[d];
                    if (!InBounds(c, g2)) continue;
                    if (c[g2].Feature == _f.Floor)
                    {
                        joy = true;
                        SetFeat(c, g1, _f.BrokenDoor);
                        SetFeat(c, g, _f.Floor);
                        HollowOutRoom(c, g);
                        break;
                    }
                    if (IsWallInner(c, g2))
                    {
                        var g3 = g2 + Ddd[d];
                        if (!InBounds(c, g3)) continue;
                        if (c[g3].Feature == _f.Floor)
                        {
                            joy = true;
                            SetFeat(c, g1, _f.Floor);
                            SetFeat(c, g2, _f.Floor);
                            SetFeat(c, g, _f.Floor);
                            HollowOutRoom(c, g);
                            break;
                        }
                    }
                }
            }
            if (!joy) break;
        }

        for (var y = y1; y <= y2; y++)
        for (var x = x1; x <= x2; x++)
        {
            var g = new Loc(x, y);
            if (c[g].Feature == _f.OpenDoor) SetMarkedGranite(c, g, SquareFlags.WallInner);
            else if (c[g].Feature == _f.BrokenDoor) PlaceRandomDoor(c, g);
        }

        int ly1 = Math.Max(y1 - 1, 0), ly2 = Math.Min(y2 + 2, c.Height), lx1 = Math.Max(x1 - 1, 0), lx2 = Math.Min(x2 + 2, c.Width);
        for (var y = ly1; y < ly2; y++)
        for (var x = lx1; x < lx2; x++)
        {
            var g = new Loc(x, y);
            if (IsWallInner(c, g) || c[g].Feature == _f.Magma)
            {
                for (var d = 0; d < 9; d++)
                {
                    var g1 = g + Ddd[d];
                    if (!InBounds(c, g1)) continue;
                    if (c[g1].Feature == _f.Floor) break;
                    if (d == 8) SetMarkedGranite(c, g, SquareFlags.None);
                }
            }
            if (IsFloor(c, g))
                for (var d = 0; d < 9; d++)
                {
                    var g1 = g + Ddd[d];
                    if (!InBounds(c, g1)) continue;
                    SqOn(c, g1, SquareFlags.Room | SquareFlags.NoStairs);
                    if (light) SqOn(c, g1, SquareFlags.Glow);
                }
        }

        for (var y = ly1; y < ly2; y++)
        for (var x = lx1; x < lx2; x++)
        {
            var g = new Loc(x, y);
            if (!InBoundsFully(c, g) || !IsWallInner(c, g)) continue;
            for (var d = 0; d < 9; d++)
            {
                var g1 = g + Ddd[d];
                // (4.2.5 tests the chamber wall itself for its marks here, as it has it.)
                if (c[g1].Feature == _f.Granite && !IsWallInner(c, g) && !IsWallOuter(c, g) && !IsWallSolid(c, g))
                {
                    SetMarkedGranite(c, g, SquareFlags.WallOuter);
                    break;
                }
            }
        }

        GetChamberMonsters(c, y1, x1, y2, x2, height * width);
        AddToMonsterRating(c, 10);
        return true;
    }

    // --- Pits and nests --------------------------------------------------------------------------------

    /// <summary>
    /// Angband set_pit_type: each pit profile of the room type draws a depth about its average
    /// (standard deviation 10); the nearest to the level, if it passes its rarity, is chosen.
    /// </summary>
    private void SetPitType(int depth, int type)
    {
        var pits = _data.Pits;
        var pitIdx = 0;
        var pitDist = 999;
        for (var i = 0; i < pits.Count; i++)
        {
            var pit = pits[i];
            if (type != 0 && pit.Room != type) continue;
            var offset = _rng.Normal(pit.AverageLevel, 10);
            var dist = Math.Abs(offset - depth);
            if (dist < pitDist && _rng.OneIn(Math.Max(1, pit.Rarity)))
            {
                pitIdx = i;
                pitDist = dist;
            }
        }
        _dun.PitType = pits.Count == 0 ? null : pits[pitIdx];
    }

    /// <summary>Angband mon_pit_hook: a race fit for the pit theme.</summary>
    public static bool PitHook(PitProfileDef pit, MonsterRaceDef race)
    {
        if (race.IsUnique) return false;
        if (!pit.Flags.All(race.Has)) return false;
        if (pit.ForbiddenFlags.Any(race.Has)) return false;
        if (!pit.Spells.All(race.Spells.Contains)) return false;
        if (pit.ForbiddenSpells.Any(race.Spells.Contains)) return false;
        if (race.InnateFrequency < pit.InnateFrequency) return false;
        if (pit.ForbiddenMonsters.Contains(race.Id)) return false;
        if (pit.Bases.Count > 0 && !pit.Bases.Contains(race.Base)) return false;
        if (pit.Colors.Count > 0 && !pit.Colors.Contains(race.Color)) return false;
        return true;
    }

    private MonsterRaceDef? PitRace(Level c, PitProfileDef pit) =>
        _spawner.PickRace(_rng, c.Depth + 10, new HashSet<string>(), r => PitHook(pit, r));

    /// <summary>Angband build_nest: a moat around a room crowded with a jumble of one theme's monsters.</summary>
    private bool BuildNest(Level c, Loc centre, int rating)
    {
        var sizeVary = _rng.RandInt0(4);
        const int height = 9;
        var width = 11 + 2 * sizeVary;
        if (OutsideChunk(c, centre) && !FindSpace(ref centre, height + 2, width + 2)) return false;
        int y1 = centre.Y - height / 2, y2 = centre.Y + height / 2, x1 = centre.X - width / 2, x2 = centre.X + width / 2;
        GenerateRoom(c, y1 - 1, x1 - 1, y2 + 1, x2 + 1, false);
        DrawRectangle(c, y1 - 1, x1 - 1, y2 + 1, x2 + 1, _f.Granite, SquareFlags.WallOuter, false);
        FillRectangle(c, y1, x1, y2, x2, _f.Floor, SquareFlags.None);
        y1 += 2; y2 -= 2; x1 += 2; x2 -= 2;
        DrawRectangle(c, y1 - 1, x1 - 1, y2 + 1, x2 + 1, _f.Granite, SquareFlags.WallInner, false);
        GenerateHole(c, y1 - 1, x1 - 1, y2 + 1, x2 + 1, _f.ClosedDoor);

        SetPitType(c.Depth, 2);
        if (_dun.PitType is not { } pit) return false;
        var what = new MonsterRaceDef[64];
        for (var i = 0; i < 64; i++)
        {
            if (PitRace(c, pit) is not { } race) return false;
            what[i] = race;
        }
        AddToMonsterRating(c, sizeVary + pit.AverageLevel / 20);
        for (var y = y1; y <= y2; y++)
        for (var x = x1; x <= x2; x++)
        {
            var g = new Loc(x, y);
            PlaceNewMonster(c, g, what[_rng.RandInt0(64)], false, false);
            if (_rng.RandInt0(100) < pit.ObjectRarity) PlaceObject(c, g, c.Depth + 10, _rng.OneIn(3), false);
        }
        return true;
    }

    /// <summary>Angband build_pit: a moat around ranks of one theme's monsters, the toughest in the middle.</summary>
    private bool BuildPit(Level c, Loc centre, int rating)
    {
        const int height = 9, width = 15;
        if (OutsideChunk(c, centre) && !FindSpace(ref centre, height + 2, width + 2)) return false;
        int y1 = centre.Y - height / 2, y2 = centre.Y + height / 2, x1 = centre.X - width / 2, x2 = centre.X + width / 2;
        GenerateRoom(c, y1 - 1, x1 - 1, y2 + 1, x2 + 1, false);
        DrawRectangle(c, y1 - 1, x1 - 1, y2 + 1, x2 + 1, _f.Granite, SquareFlags.WallOuter, false);
        FillRectangle(c, y1, x1, y2, x2, _f.Floor, SquareFlags.None);
        y1 += 2; y2 -= 2; x1 += 2; x2 -= 2;
        DrawRectangle(c, y1 - 1, x1 - 1, y2 + 1, x2 + 1, _f.Granite, SquareFlags.WallInner, false);
        GenerateHole(c, y1 - 1, x1 - 1, y2 + 1, x2 + 1, _f.ClosedDoor);

        SetPitType(c.Depth, 1);
        if (_dun.PitType is not { } pit) return false;
        var what = new MonsterRaceDef[16];
        for (var i = 0; i < 16; i++)
        {
            if (PitRace(c, pit) is not { } race) return false;
            what[i] = race;
        }
        // Angband's bubble sort by level, then every other one.
        for (var i = 0; i < 15; i++)
        for (var j = 0; j < 15; j++)
            if (what[j].Depth > what[j + 1].Depth) (what[j], what[j + 1]) = (what[j + 1], what[j]);
        for (var i = 0; i < 8; i++) what[i] = what[i * 2];
        AddToMonsterRating(c, 3 + pit.AverageLevel / 20);

        void Put(int x, int y, int k) => PlaceNewMonster(c, new Loc(x, y), what[k], false, false);
        Put(centre.X, centre.Y, 7);
        for (var x = centre.X - 3; x <= centre.X + 3; x++) { Put(x, centre.Y - 2, 0); Put(x, centre.Y + 2, 0); }
        for (var x = centre.X - 5; x <= centre.X - 4; x++) { Put(x, centre.Y - 2, 1); Put(x, centre.Y + 2, 1); }
        for (var x = centre.X + 4; x <= centre.X + 5; x++) { Put(x, centre.Y - 2, 1); Put(x, centre.Y + 2, 1); }
        for (var y = centre.Y - 1; y <= centre.Y + 1; y++)
        {
            Put(centre.X - 5, y, 0); Put(centre.X + 5, y, 0);
            Put(centre.X - 4, y, 1); Put(centre.X + 4, y, 1);
            Put(centre.X - 3, y, 2); Put(centre.X + 3, y, 2);
            Put(centre.X - 2, y, 3); Put(centre.X + 2, y, 3);
        }
        Put(centre.X - 1, centre.Y - 1, 4); Put(centre.X + 1, centre.Y - 1, 4);
        Put(centre.X - 1, centre.Y + 1, 4); Put(centre.X + 1, centre.Y + 1, 4);
        for (var x = centre.X - 1; x <= centre.X + 1; x++) { Put(x, centre.Y + 1, 5); Put(x, centre.Y - 1, 5); }
        Put(centre.X + 1, centre.Y, 6); Put(centre.X - 1, centre.Y, 6);

        for (var y = centre.Y - 2; y <= centre.Y + 2; y++)
        for (var x = centre.X - 9; x <= centre.X + 9; x++)
            if (_rng.RandInt0(100) < pit.ObjectRarity) PlaceObject(c, new Loc(x, y), c.Depth + 10, _rng.OneIn(3), false);
        return true;
    }
}
