using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Generation;

// Angband gen-cave.c's level builders and gen-chunk.c's chunk_copy.
public sealed partial class Cave
{
    private LevelRequest _request = null!;
    private Loc _playerStart;

    /// <summary>
    /// One attempt (Angband cave_generate's loop body): the profile's builder makes the level, or
    /// says why it couldn't.
    /// </summary>
    private Connector ToConnector(StairJoin j) => new(j.Loc, j.Down ? _f.DownStair : _f.UpStair);

    public (Level? Level, Loc Start, string? Error) Build(LevelRequest request, DungeonProfileDef profile)
    {
        _request = request;
        _dun = new DunData(profile)
        {
            Persist = request.Persistent,
            Quest = request.Quest,
            Join = [.. (request.Joins ?? []).Select(ToConnector)],
            OneOffAbove = [.. (request.OneOffAbove ?? []).Select(ToConnector)],
            OneOffBelow = [.. (request.OneOffBelow ?? []).Select(ToConnector)],
        };
        _occupied.Clear();
        _floors.Clear();
        _rating.Clear();

        Level? c;
        string? error = null;
        switch (profile.Name)
        {
            case "classic": c = ClassicGen(ref error); break;
            case "labyrinth": c = LabyrinthGen(ref error); break;
            case "cavern": c = CavernGen(ref error); break;
            case "modified": c = ModifiedGen(ref error); break;
            case "moria": c = MoriaGen(ref error); break;
            case "lair": c = LairGen(ref error); break;
            case "gauntlet": c = GauntletGen(ref error); break;
            case "hard centre": c = HardCentreGen(ref error); break;
            default: return (null, default, $"no builder for profile '{profile.Name}'");
        }
        if (c is null) return (null, default, error ?? "unspecified level builder failure");

        // Angband cave_generate: clear the generation flags.
        foreach (var g in c.AllLocs())
            c[g].Flags &= ~(SquareFlags.WallInner | SquareFlags.WallOuter | SquareFlags.WallSolid | SquareFlags.MonRestrict);
        c.MonsterRatingBonus = _rating.GetValueOrDefault(c);
        return (c, _playerStart, null);
    }

    private int ScaledSizePercent()
    {
        var i = _rng.RandInt1(10) + _request.Depth / 24;
        if (_dun.Quest) return 100;
        return i switch { < 2 => 75, < 3 => 80, < 4 => 85, < 5 => 90, < 6 => 95, _ => 100 };
    }

    /// <summary>Angband's rarity roll: each step up with (50 + depth/2) in <c>unusual</c>, up to the profile's most.</summary>
    private int RollRarity(int depth)
    {
        var i = 0;
        var rarity = 0;
        while (i == rarity && i < _dun.Profile.Params.MaxRarity)
        {
            if (_rng.RandInt0(_dun.Profile.Params.Unusual) < 50 + depth / 2) rarity++;
            i++;
        }
        return rarity;
    }

    /// <summary>Angband build_staircase_rooms: a staircase room at each of a persistent level's joins.</summary>
    private bool BuildStaircaseRooms(Level c)
    {
        if (_dun.Profile.Rooms.FirstOrDefault(r => r.Name == "staircase room") is not { } profile) return _dun.Join.Count == 0;
        foreach (var join in _dun.Join)
        {
            _dun.CurrJoin = join;
            if (!RoomBuild(c, (join.Grid.Y - 1) / _dun.BlockHgt, (join.Grid.X - 1) / _dun.BlockWid, profile, true)) return false;
            ++_dun.NStairRoom;
        }
        _dun.CurrJoin = null;
        return true;
    }

    /// <summary>Angband handle_level_stairs: stairs well apart; on a persistent level, none where a stored neighbour gives them.</summary>
    private void HandleLevelStairs(Level c, int downCount, int upCount)
    {
        var persistent = _dun.Persist;
        var minSep = Math.Max(Math.Min(c.Width, c.Height) / 4, persistent ? 4 : 0);
        // (A stored neighbour is known by its joins too: a level above gives this one's up stairs.)
        var belowStored = _request.BelowStored || _dun.Join.Any(j => j.Feat == _f.DownStair);
        var aboveStored = _request.AboveStored || _dun.Join.Any(j => j.Feat == _f.UpStair);
        if (!persistent || !belowStored)
            AllocStairs(c, _f.DownStair, downCount, minSep, false, _dun.OneOffBelow, _dun.Quest);
        if (!persistent || !aboveStored)
            AllocStairs(c, _f.UpStair, upCount, minSep, false, _dun.OneOffAbove, _dun.Quest);
    }

    private void InitRoomMap(Level c)
    {
        _dun.RowBlocks = c.Height / _dun.BlockHgt;
        _dun.ColBlocks = c.Width / _dun.BlockWid;
        _dun.RoomMap = new bool[_dun.RowBlocks, _dun.ColBlocks];
        _dun.PitNum = 0;
        _dun.Cent.Clear();
        ResetEntranceData(c);
    }

    /// <summary>Angband's k: the general amount of rubble, traps and monsters.</summary>
    private static int GeneralAmount(int depth) => Math.Max(Math.Min(depth / 3, 10), 2);

    /// <summary>Angband's random monsters, room objects, objects and gold for a level (the common ending).</summary>
    private void StandardAllocation(Level c, int k, string? restriction = null)
    {
        for (var i = GenConstants.LevelMonsterMin + _rng.RandInt1(8) + k; i > 0; i--)
            PickAndPlaceDistantMonster(c, _playerStart, 0, true, c.Depth, restriction);
        AllocObjects(c, AllocSet.Room, AllocType.Object, _rng.Normal(GenConstants.RoomItemAv, 3), c.Depth);
        AllocObjects(c, AllocSet.Both, AllocType.Object, _rng.Normal(GenConstants.BothItemAv, 3), c.Depth);
        AllocObjects(c, AllocSet.Both, AllocType.Gold, _rng.Normal(GenConstants.BothGoldAv, 3), c.Depth);
    }

    // --- Classic ---------------------------------------------------------------------------------------

    /// <summary>
    /// Angband classic_gen: rooms placed block by block (each block tried once, in random order;
    /// the room chosen by a key against each profile's cutoff, of a rarity rolled), joined by
    /// tunnels, streamers, stairs near walls, rubble and a few traps, then monsters and objects.
    /// </summary>
    private Level? ClassicGen(ref string? error)
    {
        var depth = _request.Depth;
        var sizePercent = ScaledSizePercent();
        var numRooms = _dun.Profile.Params.Rooms * sizePercent / 100;
        _dun.BlockHgt = _dun.Profile.Params.BlockSize;
        _dun.BlockWid = _dun.Profile.Params.BlockSize;
        var c = NewChunk(GenConstants.DungeonHeight, GenConstants.DungeonWidth, depth);
        FillRectangle(c, 0, 0, c.Height - 1, c.Width - 1, _f.Granite, SquareFlags.None);
        InitRoomMap(c);
        var blocksTried = new bool[_dun.RowBlocks, _dun.ColBlocks];

        if (_dun.Persist && !BuildStaircaseRooms(c))
        {
            error = "could not build the staircase rooms";
            return null;
        }
        // AVABand's quests: the quest's room goes in first, wherever it fits.
        if (_request.QuestRoom is { } questRoom)
        {
            var room = _data.Vaults.FirstOrDefault(v => v.Id == questRoom)
                       ?? throw new GameDataException($"No quest room '{questRoom}'.");
            if (!BuildVault(c, new Loc(c.Width, c.Height), room))
            {
                error = "could not fit the quest room";
                return null;
            }
            c.Vaults.Add(room.Name);
        }

        var built = 0;
        while (built < numRooms)
        {
            int j = 0, tby = 0, tbx = 0;
            for (var by = 0; by < _dun.RowBlocks; by++)
            for (var bx = 0; bx < _dun.ColBlocks; bx++)
            {
                if (blocksTried[by, bx]) continue;
                j++;
                if (_rng.OneIn(j))
                {
                    tby = by;
                    tbx = bx;
                }
            }
            if (j == 0) break;
            blocksTried[tby, tbx] = true;
            var key = _rng.RandInt0(100);
            var rarity = RollRarity(c.Depth);
            foreach (var profile in _dun.Profile.Rooms)
            {
                if (profile.Rarity > rarity || profile.Cutoff <= key) continue;
                if (RoomBuild(c, tby, tbx, profile, false))
                {
                    built++;
                    break;
                }
            }
        }

        DrawRectangle(c, 0, 0, c.Height - 1, c.Width - 1, _f.Permanent, SquareFlags.None, true);
        DoTraditionalTunneling(c);
        EnsureConnectedness(c, true);
        var str = _dun.Profile.Streamers;
        for (var i = 0; i < str.Magma; i++) BuildStreamer(c, _f.Magma, str.MagmaTreasure);
        for (var i = 0; i < str.Quartz; i++) BuildStreamer(c, _f.Quartz, str.QuartzTreasure);
        HandleLevelStairs(c, _rng.RandRange(3, 4), _rng.RandRange(1, 2));

        var k = GeneralAmount(c.Depth);
        AllocObjects(c, AllocSet.Corridor, AllocType.Rubble, _rng.RandInt1(k), c.Depth);
        AllocObjects(c, AllocSet.Corridor, AllocType.Trap, _rng.RandInt1(k) / 5, c.Depth);
        if (!NewPlayerSpot(c, out _))
        {
            error = "could not place player";
            return null;
        }
        StandardAllocation(c, k);
        return c;
    }

    // --- Labyrinth -------------------------------------------------------------------------------------

    private static void LabGetAdjoin(int i, int w, out int a, out int b)
    {
        var g = new Loc(i % w, i / w);
        if (g.X % 2 == 0)
        {
            a = (g.Y - 1) * w + g.X;
            b = (g.Y + 1) * w + g.X;
        }
        else
        {
            a = g.Y * w + g.X - 1;
            b = g.Y * w + g.X + 1;
        }
    }

    private bool LabIsTunnel(Level c, Loc g)
    {
        bool Open(Loc p) => IsPassable(c, p) || IsClosedDoor(c, p);
        var west = Open(g + new Loc(-1, 0));
        var east = Open(g + new Loc(1, 0));
        var north = Open(g + new Loc(0, -1));
        var south = Open(g + new Loc(0, 1));
        return north == south && west == east && north != west;
    }

    /// <summary>
    /// Angband labyrinth_chunk: a maze by randomised Kruskal over a grid of cells, in permanent rock
    /// or (softer) granite, a door per hundred squares; unlit ones hide good objects, hard ones great.
    /// </summary>
    private Level LabyrinthChunk(int depth, int h, int w, bool lit, bool soft)
    {
        var n = h * w;
        var c = NewChunk(h + 2, w + 2, depth);
        DrawRectangle(c, 0, 0, h + 1, w + 1, _f.Permanent, SquareFlags.None, true);
        if (soft) FillRectangle(c, 1, 1, h, w, _f.Granite, SquareFlags.WallSolid);
        else FillRectangle(c, 1, 1, h, w, _f.Permanent, SquareFlags.None);

        var sets = new int[n];
        var walls = new int[n];
        for (var i = 0; i < n; i++)
        {
            walls[i] = i;
            sets[i] = -1;
        }
        for (var y = 0; y < h; y += 2)
        for (var x = 0; x < w; x += 2)
        {
            var k = y * w + x;
            sets[k] = k;
            var diag = new Loc(x + 1, y + 1);
            SetFeat(c, diag, _f.Floor);
            if (lit) SqOn(c, diag, SquareFlags.Glow);
        }
        Shuffle(walls, n);
        for (var i = 0; i < n; i++)
        {
            var j = walls[i];
            var g = new Loc(j % w, j / w);
            if ((g.X < 1 && g.Y < 1) || (g.X > w - 2 && g.Y > h - 2)) continue;
            if (g.X % 2 == g.Y % 2) continue;
            LabGetAdjoin(j, w, out var a, out var b);
            if (sets[a] == sets[b]) continue;
            int sa = sets[a], sb = sets[b];
            var diag = g + new Loc(1, 1);
            SetFeat(c, diag, _f.Floor);
            if (lit) SqOn(c, diag, SquareFlags.Glow);
            for (var k = 0; k < n; k++) if (sets[k] == sb) sets[k] = sa;
        }

        var doors = n / 100;
        foreach (var g in CaveFind(new Loc(1, 1), new Loc(c.Width - 2, c.Height - 2)))
        {
            if (doors <= 0) break;
            if (IsEmpty(c, g) && LabIsTunnel(c, g))
            {
                PlaceClosedDoor(c, g);
                --doors;
            }
        }
        if (!lit) AllocObjects(c, AllocSet.Both, AllocType.Good, _rng.Normal(3, 2), c.Depth);
        if (!soft) AllocObjects(c, AllocSet.Both, AllocType.Great, _rng.Normal(2, 1), c.Depth);
        return c;
    }

    /// <summary>Angband labyrinth_gen: a maze level, most lit, many known, most diggable.</summary>
    private Level? LabyrinthGen(ref string? error)
    {
        var depth = _request.Depth;
        var h = 15 + _rng.RandInt0(depth / 10) * 2;
        var w = 51 + _rng.RandInt0(depth / 10) * 2;
        var lit = _rng.RandInt0(depth) < 25 || _rng.RandInt0(2) < 1;
        var known = lit && _rng.RandInt0(depth) < 25;
        var soft = _rng.RandInt0(depth) < 35 || _rng.RandInt0(3) < 2;
        if (_dun.Persist)
        {
            error = "no labyrinth levels in persistent dungeons";
            return null;
        }
        var c = LabyrinthChunk(depth, h, w, lit, soft);
        if (!NewPlayerSpot(c, out _))
        {
            error = "could not place player";
            return null;
        }
        if (CaveFindAny(c, IsUpStairs) is null) AllocStairs(c, _f.UpStair, 1, 0, false, null, _dun.Quest);
        if (CaveFindAny(c, IsDownStairs) is null) AllocStairs(c, _f.DownStair, 1, 0, false, null, _dun.Quest);

        var k = GeneralAmount(c.Depth);
        k = 3 * k * (h * w) / (GenConstants.DungeonHeight * GenConstants.DungeonWidth);
        AllocObjects(c, AllocSet.Both, AllocType.Rubble, _rng.RandInt1(k), c.Depth);
        AllocObjects(c, AllocSet.Corridor, AllocType.Trap, _rng.RandInt1(k), c.Depth);
        for (var i = GenConstants.LevelMonsterMin + _rng.RandInt1(8) + k; i > 0; i--)
            PickAndPlaceDistantMonster(c, _playerStart, 0, true, c.Depth);
        AllocObjects(c, AllocSet.Both, AllocType.Object, _rng.Normal(k * 6, 2), c.Depth);
        AllocObjects(c, AllocSet.Both, AllocType.Gold, _rng.Normal(k * 3, 2), c.Depth);
        AllocObjects(c, AllocSet.Both, AllocType.Good, _rng.RandInt1(2), c.Depth);
        if (known)
        {
            c.IsKnown = true;
            foreach (var g in c.AllLocs()) SqOn(c, g, SquareFlags.Mark);
        }
        return c;
    }

    // --- Caverns ---------------------------------------------------------------------------------------

    /// <summary>
    /// Angband init_cavern: all rock, the joins' stairs built in (with floor toward the middle and
    /// permanent rock behind), then random floor to the density.
    /// </summary>
    private void InitCavern(Level c, int density, IReadOnlyList<Connector>? join)
    {
        var h = c.Height;
        var w = c.Width;
        var count = h * w * density / 100;
        FillRectangle(c, 0, 0, h - 1, w - 1, _f.Granite, SquareFlags.WallSolid);
        foreach (var j in join ?? [])
        {
            if (j.Grid.Y <= 0 || j.Grid.Y >= h - 1 || j.Grid.X <= 0 || j.Grid.X >= w - 1 || IsStairs(c, j.Grid)) continue;
            var bcrit = _rng.RandInt0(h) + (j.Grid.Y > h / 2 ? -10 : 10);
            var rcrit = _rng.RandInt0(w) + (j.Grid.X > w / 2 ? -10 : 10);
            var offy = bcrit > j.Grid.Y ? 1 : -1;
            var offx = rcrit > j.Grid.X ? 1 : -1;
            if (!IsFloor(c, j.Grid)) --count;
            SetFeat(c, j.Grid, j.Feat);
            foreach (var adj in new[] { new Loc(j.Grid.X + offx, j.Grid.Y + offy), new Loc(j.Grid.X, j.Grid.Y + offy), new Loc(j.Grid.X + offx, j.Grid.Y) })
                if (!IsStairs(c, adj) && !IsFloor(c, adj))
                {
                    --count;
                    SetFeat(c, adj, _f.Floor);
                }
            foreach (var adj in new[] { new Loc(j.Grid.X - offx, j.Grid.Y - offy), new Loc(j.Grid.X, j.Grid.Y - offy), new Loc(j.Grid.X - offx, j.Grid.Y) })
                if (IsRock(c, adj)) SetFeat(c, adj, _f.Permanent);
        }
        while (count > 0)
        {
            var g = new Loc(_rng.RandInt1(w - 2), _rng.RandInt1(h - 2));
            if (!IsRock(c, g)) continue;
            SetFeat(c, g, _f.Floor);
            count--;
        }
    }

    /// <summary>Angband mutate_cavern: one pass of the (4, 5) cellular automaton.</summary>
    private void MutateCavern(Level c)
    {
        var h = c.Height;
        var w = c.Width;
        var temp = new ushort[h * w];
        for (var y = 1; y < h - 1; y++)
        for (var x = 1; x < w - 1; x++)
        {
            var g = new Loc(x, y);
            var count = 8 - CountNeighbors(c, g, IsPassable);
            if (IsStairs(c, g) || IsPerm(c, g)) temp[y * w + x] = c[g].Feature;
            else if (count > 5) temp[y * w + x] = _f.Granite;
            else if (count < 4) temp[y * w + x] = _f.Floor;
            else temp[y * w + x] = c[g].Feature;
        }
        for (var y = 1; y < h - 1; y++)
        for (var x = 1; x < w - 1; x++)
        {
            var g = new Loc(x, y);
            if (temp[y * w + x] == _f.Granite) SetMarkedGranite(c, g, SquareFlags.WallSolid);
            else SetFeat(c, g, temp[y * w + x]);
        }
    }

    /// <summary>
    /// Angband cavern_chunk: random floor grown by a few passes of the automaton until a thirteenth
    /// is open (ten tries), small pockets filled, the rest joined.
    /// </summary>
    private Level? CavernChunk(int depth, int h, int w, IReadOnlyList<Connector>? join)
    {
        var size = h * w;
        var limit = size / 13;
        var density = _rng.RandRange(25, 40);
        var times = _rng.RandRange(3, 6);
        var c = NewChunk(h, w, depth);
        var ok = false;
        for (var tries = 0; tries < 10; tries++)
        {
            InitCavern(c, density, join);
            for (var i = 0; i < times; i++) MutateCavern(c);
            if (FloorCount(c) >= limit)
            {
                ok = true;
                break;
            }
        }
        if (!ok) return null;

        var colors = new int[size];
        var counts = new int[size];
        var stairs = join is null ? null : new bool[size];
        BuildColors(c, colors, counts, stairs, false);
        ClearSmallRegions(c, colors, counts, stairs);
        JoinRegions(c, colors, counts, true);
        foreach (var j in join ?? [])
            for (var i = 0; i < 8; i++)
            {
                var adj = j.Grid + Ddd[i];
                if (InBounds(c, adj) && IsPerm(c, adj)) SetMarkedGranite(c, adj, SquareFlags.WallSolid);
            }
        return c;
    }

    /// <summary>Angband cavern_gen: a cave of half to three quarters the usual size, sparsely peopled.</summary>
    private Level? CavernGen(ref string? error)
    {
        var h = _rng.RandRange(GenConstants.DungeonHeight / 2, GenConstants.DungeonHeight * 3 / 4);
        var w = _rng.RandRange(GenConstants.DungeonWidth / 2, GenConstants.DungeonWidth * 3 / 4);
        var c = CavernChunk(_request.Depth, h, w, _dun.Join);
        if (c is null)
        {
            error = "cavern chunk could not be created";
            return null;
        }
        DrawRectangle(c, 0, 0, h - 1, w - 1, _f.Permanent, SquareFlags.None, true);
        HandleLevelStairs(c, _rng.RandRange(1, 3), _rng.RandRange(1, 2));
        var k = GeneralAmount(c.Depth);
        k = Math.Max(4 * k * (h * w) / (GenConstants.DungeonHeight * GenConstants.DungeonWidth), 6);
        AllocObjects(c, AllocSet.Both, AllocType.Rubble, _rng.RandInt1(k), c.Depth);
        AllocObjects(c, AllocSet.Corridor, AllocType.Trap, _rng.RandInt1(k), c.Depth);
        if (!NewPlayerSpot(c, out _))
        {
            error = "could not place player";
            return null;
        }
        for (var i = _rng.RandInt1(8) + k; i > 0; i--) PickAndPlaceDistantMonster(c, _playerStart, 0, true, c.Depth);
        AllocObjects(c, AllocSet.Both, AllocType.Object, _rng.Normal(k, 2), c.Depth + 5);
        AllocObjects(c, AllocSet.Both, AllocType.Gold, _rng.Normal(k / 2, 2), c.Depth);
        AllocObjects(c, AllocSet.Both, AllocType.Good, _rng.RandInt0(k / 4), c.Depth);
        return c;
    }

    // --- Modified and moria ----------------------------------------------------------------------------

    /// <summary>
    /// Angband modified_chunk / moria_chunk: rooms that find their own space until a seventh of the
    /// area is floor (and there are two rooms), joined by tunnels; the edge granite again after.
    /// </summary>
    private Level? RoomsUntilFloorChunk(int depth, int height, int width, bool persistent)
    {
        var c = NewChunk(height, width, depth);
        var numFloors = c.Height * c.Width / 7;
        FillRectangle(c, 0, 0, c.Height - 1, c.Width - 1, _f.Granite, SquareFlags.None);
        DrawRectangle(c, 0, 0, c.Height - 1, c.Width - 1, _f.Permanent, SquareFlags.None, true);
        InitRoomMap(c);
        if (persistent && !BuildStaircaseRooms(c)) return null;

        for (var attempt = 0; ; attempt++)
        {
            if (FloorCount(c) >= numFloors && _dun.CentN >= 2) break;
            if (attempt > 500) return null;
            var key = _rng.RandInt0(100);
            var rarity = RollRarity(c.Depth);
            foreach (var profile in _dun.Profile.Rooms)
            {
                if (profile.Rarity > rarity || profile.Cutoff <= key) continue;
                if (RoomBuild(c, 0, 0, profile, true)) break;
            }
        }
        DoTraditionalTunneling(c);
        EnsureConnectedness(c, true);
        DrawRectangle(c, 0, 0, c.Height - 1, c.Width - 1, _f.Granite, SquareFlags.None, true);
        return c;
    }

    private (int Height, int Width) ScaledDungeonSize()
    {
        var sizePercent = ScaledSizePercent();
        var y = GenConstants.DungeonHeight * (sizePercent - 5 + _rng.RandInt0(10)) / 100;
        var x = GenConstants.DungeonWidth * (sizePercent - 5 + _rng.RandInt0(10)) / 100;
        return (Math.Min(Math.Max(y, 1), GenConstants.DungeonHeight), Math.Min(Math.Max(x, 1), GenConstants.DungeonWidth));
    }

    /// <summary>Angband modified_gen: the modern level — rooms of every kind, sized to the floor wanted.</summary>
    private Level? ModifiedGen(ref string? error) => RoomsLevel(ref error, null, "modified chunk could not be created");

    /// <summary>Angband moria_gen: moria rooms and a crowd of Moria dwellers.</summary>
    private Level? MoriaGen(ref string? error) => RoomsLevel(ref error, "moria_dwellers", "moria chunk could not be created");

    private Level? RoomsLevel(ref string? error, string? pit, string failure)
    {
        var (ySize, xSize) = ScaledDungeonSize();
        _dun.BlockHgt = _dun.Profile.Params.BlockSize;
        _dun.BlockWid = _dun.Profile.Params.BlockSize;
        var c = RoomsUntilFloorChunk(_request.Depth, ySize, xSize, _dun.Persist);
        if (c is null)
        {
            error = failure;
            return null;
        }
        DrawRectangle(c, 0, 0, c.Height - 1, c.Width - 1, _f.Permanent, SquareFlags.None, true);
        var str = _dun.Profile.Streamers;
        for (var i = 0; i < str.Magma; i++) BuildStreamer(c, _f.Magma, str.MagmaTreasure);
        for (var i = 0; i < str.Quartz; i++) BuildStreamer(c, _f.Quartz, str.QuartzTreasure);
        HandleLevelStairs(c, _rng.RandRange(3, 4), _rng.RandRange(1, 2));
        var k = GeneralAmount(c.Depth);
        AllocObjects(c, AllocSet.Corridor, AllocType.Rubble, _rng.RandInt1(k), c.Depth);
        AllocObjects(c, AllocSet.Corridor, AllocType.Trap, _rng.RandInt1(k) / 5, c.Depth);
        if (!NewPlayerSpot(c, out _))
        {
            error = "could not place player";
            return null;
        }
        StandardAllocation(c, k, pit is null ? null : $"pit:{pit}");
        return c;
    }

    // --- Chunks put together ---------------------------------------------------------------------------

    /// <summary>
    /// Angband chunk_copy: a chunk's terrain, flags, traps and planned monsters and objects written
    /// into a bigger one (turned and reflected if asked).
    /// </summary>
    private bool ChunkCopy(Level dest, Level source, int y0, int x0, int rotate, bool reflect)
    {
        int h = source.Height, w = source.Width;
        if (h + y0 > dest.Height || w + x0 > dest.Width) return false;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var from = new Loc(x, y);
            var to = SymmetryTransform(from, y0, x0, h, w, rotate, reflect);
            SetFeat(dest, to, source[from].Feature);
            ref var d = ref dest[to];
            ref var s = ref source[from];
            d.Flags = s.Flags;
            d.Trap = s.Trap;
            d.TrapPower = s.TrapPower;
            d.LockPower = s.LockPower;
        }
        foreach (var hint in source.SpawnHints)
        {
            var to = SymmetryTransform(hint.Loc, y0, x0, h, w, rotate, reflect);
            dest.SpawnHints.Add(hint with { Loc = to });
            Occupied(dest).Add(to);
        }
        if (_playerStart != default && source == _startChunk)
        {
            _playerStart = SymmetryTransform(_playerStart, y0, x0, h, w, rotate, reflect);
            Occupied(dest).Add(_playerStart);
            _startChunk = dest;
        }
        dest.Vaults.AddRange(source.Vaults);
        AddToMonsterRating(dest, (int)_rating.GetValueOrDefault(source));
        return true;
    }

    private Level? _startChunk;

    /// <summary>Angband find_joinfree_vertical_seam: two adjacent columns with no join, nearest the preferred one.</summary>
    private static int FindJoinfreeVerticalSeam(IReadOnlyList<Connector> join, int colpref, int range, int rowmin, int rowmax)
    {
        var metric = range + 1;
        var result = -1;
        var disallowed = new bool[range + range + 1];
        foreach (var j in join)
            if (Math.Abs(j.Grid.X - colpref) <= range && j.Grid.Y >= rowmin && j.Grid.Y <= rowmax)
                disallowed[range + j.Grid.X - colpref] = true;
        var i = 0;
        while (i < range + range)
        {
            if (!disallowed[i])
            {
                if (!disallowed[i + 1])
                {
                    if (metric > Math.Abs(i - range))
                    {
                        metric = Math.Abs(i - range);
                        result = colpref + i - range;
                    }
                    ++i;
                }
                else i += 2;
            }
            else ++i;
        }
        return result;
    }

    /// <summary>Angband transform_join_list (untranslated only, as 4.2.5's builders use it): the joins within a sub-chunk.</summary>
    private static List<Connector> TransformJoinList(IReadOnlyList<Connector> join, int nrow, int ncol, int y0, int x0) =>
        [.. join.Select(j => j with { Grid = new Loc(j.Grid.X - x0, j.Grid.Y - y0) })
            .Where(j => j.Grid.Y >= 0 && j.Grid.Y < nrow && j.Grid.X >= 0 && j.Grid.X < ncol)];

    // --- Lair ------------------------------------------------------------------------------------------

    /// <summary>
    /// Angband lair_gen: half a modified level (where the player starts, lightly peopled), half a
    /// cavern crowded with one theme's monsters, side by side and joined.
    /// </summary>
    private Level? LairGen(ref string? error)
    {
        var depth = _request.Depth;
        var (ySize, xSize) = ScaledDungeonSize();
        _dun.BlockHgt = _dun.Profile.Params.BlockSize;
        _dun.BlockWid = _dun.Profile.Params.BlockSize;
        var cachedJoin = _dun.Join;
        int leftWidth;
        if (_dun.Persist)
        {
            leftWidth = 1 + FindJoinfreeVerticalSeam(cachedJoin, xSize / 2, Math.Min(5, xSize / 20), 0, ySize - 1);
            if (leftWidth < 4 || xSize - leftWidth < 4)
            {
                error = "no seam free of joins";
                return null;
            }
        }
        else leftWidth = xSize / 2;
        int normalWidth, normalOffset, lairWidth, lairOffset;
        if (_rng.OneIn(2))
        {
            normalWidth = leftWidth;
            normalOffset = 0;
            lairWidth = xSize - leftWidth;
            lairOffset = leftWidth;
        }
        else
        {
            normalWidth = xSize - leftWidth;
            normalOffset = leftWidth;
            lairWidth = leftWidth;
            lairOffset = 0;
        }

        _dun.Join = TransformJoinList(cachedJoin, ySize, normalWidth, 0, normalOffset);
        var normal = RoomsUntilFloorChunk(depth, ySize, normalWidth, _dun.Persist);
        _dun.Join = cachedJoin;
        if (normal is null)
        {
            error = "modified chunk could not be created";
            return null;
        }
        var lair = CavernChunk(depth, ySize, lairWidth, TransformJoinList(cachedJoin, ySize, lairWidth, 0, lairOffset));
        if (lair is null)
        {
            error = "cavern chunk could not be created";
            return null;
        }

        var k = GeneralAmount(depth) / 2;
        if (!NewPlayerSpot(normal, out _))
        {
            error = "could not place player";
            return null;
        }
        _startChunk = normal;
        for (var i = _rng.RandInt1(4) + k; i > 0; i--) PickAndPlaceDistantMonster(normal, _playerStart, 0, true, depth);
        var str = _dun.Profile.Streamers;
        for (var i = 0; i < str.Magma; i++) BuildStreamer(normal, _f.Magma, str.MagmaTreasure);
        for (var i = 0; i < str.Quartz; i++) BuildStreamer(normal, _f.Quartz, str.QuartzTreasure);

        var n = GenConstants.LevelMonsterMin + _rng.RandInt1(20) + k;
        SetPitType(lair.Depth, 0);
        if (_dun.PitType is { } pit)
            SpreadMonsters(lair, pit, lair.Depth, n, lair.Height / 2, lair.Width / 2, lair.Height / 2, lair.Width / 2);

        var c = NewChunk(ySize, xSize, depth);
        ChunkCopy(c, normal, 0, normalOffset, 0, false);
        ChunkCopy(c, lair, 0, lairOffset, 0, false);
        DrawRectangle(c, 0, 0, c.Height - 1, c.Width - 1, _f.Permanent, SquareFlags.None, true);
        EnsureConnectedness(c, true);
        HandleLevelStairs(c, _rng.RandRange(3, 4), _rng.RandRange(1, 2));
        AllocObjects(c, AllocSet.Corridor, AllocType.Rubble, _rng.RandInt1(k), c.Depth);
        AllocObjects(c, AllocSet.Corridor, AllocType.Trap, _rng.RandInt1(k) / 5, c.Depth);
        AllocObjects(c, AllocSet.Room, AllocType.Object, _rng.Normal(GenConstants.RoomItemAv, 3), c.Depth);
        AllocObjects(c, AllocSet.Both, AllocType.Object, _rng.Normal(GenConstants.BothItemAv, 3), c.Depth);
        AllocObjects(c, AllocSet.Both, AllocType.Gold, _rng.Normal(GenConstants.BothGoldAv, 3), c.Depth);
        return c;
    }

    // --- Gauntlet --------------------------------------------------------------------------------------

    /// <summary>
    /// Angband gauntlet_gen: two caverns with a hard, unmappable labyrinth between them; no
    /// teleporting in the arrival cavern or the maze; up stairs on the left, down on the right.
    /// </summary>
    private Level? GauntletGen(ref string? error)
    {
        var depth = _request.Depth;
        var gauntletHgt = 2 * _rng.RandInt1(5) + 3;
        var gauntletWid = 2 * _rng.RandInt1(10) + 19;
        var ySize = GenConstants.DungeonHeight - _rng.RandInt0(25 - gauntletHgt);
        var xSize = (GenConstants.DungeonWidth - gauntletWid - 2) / 2 - _rng.RandInt0(45 - gauntletWid);
        if (_dun.Persist)
        {
            error = "no gauntlet levels in persistent dungeons";
            return null;
        }
        var gauntlet = LabyrinthChunk(depth, gauntletHgt, gauntletWid, false, false);
        var left = CavernChunk(depth, ySize, xSize, null);
        var right = CavernChunk(depth, ySize, xSize, null);
        if (left is null || right is null)
        {
            error = "cavern chunk could not be generated";
            return null;
        }
        var line1 = left.Width;
        var line2 = line1 + gauntlet.Width;
        GenerateMark(left, 0, 0, left.Height - 1, left.Width - 1, SquareFlags.NoTeleport);
        GenerateMark(gauntlet, 0, 0, gauntlet.Height - 1, gauntlet.Width - 1, SquareFlags.NoMap | SquareFlags.NoTeleport);
        AllocStairs(right, _f.DownStair, _rng.RandRange(2, 3), 0, false, null, _dun.Quest);
        AllocStairs(left, _f.UpStair, _rng.RandRange(1, 3), 0, false, null, _dun.Quest);

        foreach (var (x, dx) in new[] { (0, 1), (gauntlet.Width - 1, -1) })
        {
            var opened = false;
            for (var i = 0; i < 20; i++)
            {
                var g = new Loc(x, _rng.RandInt1(gauntlet.Height - 2));
                if (IsPerm(gauntlet, g + new Loc(dx, 0))) continue;
                SetFeat(gauntlet, g, _f.Granite);
                opened = true;
                break;
            }
            if (!opened)
            {
                error = "could not open entrance to the labyrinth";
                return null;
            }
        }

        var k = GeneralAmount(depth) / 2;
        var arrival = _request.Arrival == StairArrival.Ascended ? right : left;
        if (!NewPlayerSpot(arrival, out var start))
        {
            error = "could not place player";
            return null;
        }
        _startChunk = arrival;
        Loc inRight, inLeft;
        if (arrival == right)
        {
            inRight = start;
            inLeft = new Loc(line2 + start.X, start.Y);
        }
        else
        {
            inLeft = start;
            inRight = new Loc(start.X - line2, start.Y);
        }
        for (var i = GenConstants.LevelMonsterMin + _rng.RandInt1(4) + k; i > 0; i--) PickAndPlaceDistantMonster(left, inLeft, 0, true, depth);
        for (var i = GenConstants.LevelMonsterMin + _rng.RandInt1(4) + k; i > 0; i--) PickAndPlaceDistantMonster(right, inRight, 0, true, depth);
        var n = GenConstants.LevelMonsterMin + _rng.RandInt1(6) + k;
        SetPitType(gauntlet.Depth, 0);
        if (_dun.PitType is { } pit)
            SpreadMonsters(gauntlet, pit, gauntlet.Depth, n, gauntlet.Height / 2, gauntlet.Width / 2, gauntlet.Height / 2, gauntlet.Width / 2);

        var c = NewChunk(ySize, left.Width + gauntlet.Width + right.Width, depth);
        FillRectangle(c, 0, 0, c.Height - 1, c.Width - 1, _f.Granite, SquareFlags.None);
        FillRectangle(c, 0, line1, c.Height - 1, line2 - 1, _f.Permanent, SquareFlags.None);
        ChunkCopy(c, left, 0, 0, 0, false);
        ChunkCopy(c, gauntlet, (ySize - gauntlet.Height) / 2, line1, 0, false);
        ChunkCopy(c, right, 0, line2, 0, false);
        DrawRectangle(c, 0, 0, c.Height - 1, c.Width - 1, _f.Permanent, SquareFlags.None, true);
        EnsureConnectedness(c, true);
        AllocObjects(c, AllocSet.Corridor, AllocType.Rubble, _rng.RandInt1(k), c.Depth);
        AllocObjects(c, AllocSet.Corridor, AllocType.Trap, _rng.RandInt1(k), c.Depth);
        AllocObjects(c, AllocSet.Room, AllocType.Object, _rng.Normal(GenConstants.RoomItemAv, 3), c.Depth);
        AllocObjects(c, AllocSet.Both, AllocType.Object, _rng.Normal(GenConstants.BothItemAv, 3), c.Depth);
        AllocObjects(c, AllocSet.Both, AllocType.Gold, _rng.Normal(GenConstants.BothGoldAv, 3), c.Depth);
        return c;
    }

    // --- Hard centre -----------------------------------------------------------------------------------

    /// <summary>Angband vault_chunk: a chunk holding only a greater vault.</summary>
    private Level? VaultChunk(int depth)
    {
        var name = _rng.OneIn(2) ? "Greater vault (new)" : "Greater vault";
        if (RandomVault(depth, name) is not { } v) return null;
        var c = NewChunk(v.Height, v.Width, depth);
        FillRectangle(c, 0, 0, v.Height - 1, v.Width - 1, _f.Granite, SquareFlags.None);
        _dun.Cent.Clear();
        ResetEntranceData(c);
        _dun.Cent.Add(new Loc(v.Width / 2, v.Height / 2));
        if (!BuildVault(c, new Loc(v.Width / 2, v.Height / 2), v)) return null;
        c.Vaults.Add(v.Name);
        AddToMonsterRating(c, v.Rating);
        return c;
    }

    /// <summary>Angband connect_caverns: left to upper, right to lower, then the two halves.</summary>
    private void ConnectCaverns(Level c, Loc[] floor)
    {
        var size = c.Width * c.Height;
        var colors = new int[size];
        var counts = new int[size];
        BuildColors(c, colors, counts, null, true);
        var colorOf = floor.Select(g => colors[g.Y * c.Width + g.X]).ToArray();
        JoinRegion(c, colors, counts, colorOf[0], colorOf[1], false);
        JoinRegion(c, colors, counts, colorOf[2], colorOf[3], false);
        for (var i = 1; i < 3; i++) colorOf[i] = colors[floor[i].Y * c.Width + floor[i].X];
        JoinRegion(c, colors, counts, colorOf[1], colorOf[2], false);
    }

    /// <summary>Angband hard_centre_gen: a greater vault in the middle of the level, caverns all around.</summary>
    private Level? HardCentreGen(ref string? error)
    {
        var depth = _request.Depth;
        var centre = VaultChunk(depth);
        if (centre is null)
        {
            error = "no greater vault for the centre";
            return null;
        }
        if (_dun.Persist)
        {
            error = "no hard centre levels in persistent dungeons";
            return null;
        }

        var hasEntrances = EntrancesOf(0).Count > 0;
        var k = 1 + (hasEntrances ? _rng.RandInt1(3) : 0);
        _dun.Wall.Clear();
        for (var i = 0; i < k; i++)
        {
            Loc grid;
            if (!hasEntrances)
            {
                if (CaveFindAny(centre, (l, g) => IsWallOuter(l, g)) is not { } g)
                {
                    if (i == 0)
                    {
                        error = "no outer wall grid for an entrance to the centre vault";
                        return null;
                    }
                    break;
                }
                grid = g;
            }
            else
            {
                grid = ChooseRandomEntrance(centre, 0, null, 0, [.. _dun.Wall.Take(i)]);
                if (grid == new Loc(0, 0))
                {
                    if (i == 0)
                    {
                        error = "random selection of entrance to the centre vault failed";
                        return null;
                    }
                    break;
                }
            }
            PierceOuterWall(centre, grid);
            SetFeat(centre, grid, _f.Floor);
        }

        int rotate = 0, centreCavernYpos, centreCavernHgt, centreCavernWid;
        if (centre.Height > centre.Width)
        {
            rotate = 1;
            centreCavernYpos = (GenConstants.DungeonHeight - centre.Width) / 2;
            centreCavernHgt = centre.Width;
            centreCavernWid = centre.Height;
        }
        else
        {
            centreCavernYpos = (GenConstants.DungeonHeight - centre.Height) / 2;
            centreCavernHgt = centre.Height;
            centreCavernWid = centre.Width;
        }
        var upperHgt = centreCavernYpos;
        var lowerHgt = GenConstants.DungeonHeight - upperHgt - centreCavernHgt;
        var lowerYpos = centreCavernYpos + centreCavernHgt;
        var upper = CavernChunk(depth, upperHgt, centreCavernWid, null);
        var lower = CavernChunk(depth, lowerHgt, centreCavernWid, null);
        var leftWid = (GenConstants.DungeonWidth - centreCavernWid) / 2;
        var rightWid = GenConstants.DungeonWidth - leftWid - centreCavernWid;
        var left = CavernChunk(depth, GenConstants.DungeonHeight, leftWid, null);
        var right = CavernChunk(depth, GenConstants.DungeonHeight, rightWid, null);
        if (upper is null || lower is null || left is null || right is null)
        {
            error = "could not create one or more of the surrounding caverns";
            return null;
        }

        var c = NewChunk(GenConstants.DungeonHeight, GenConstants.DungeonWidth, depth);
        var floor = new Loc[4];
        ChunkCopy(c, left, 0, 0, 0, false);
        floor[0] = FindEmptyRange(c, new Loc(0, 0), new Loc(leftWid - 1, GenConstants.DungeonHeight - 1)) ?? default;
        ChunkCopy(c, upper, 0, leftWid, 0, false);
        floor[1] = FindEmptyRange(c, new Loc(leftWid, 0), new Loc(leftWid + centreCavernWid - 1, upperHgt - 1)) ?? default;
        ChunkCopy(c, centre, centreCavernYpos, leftWid, rotate, false);
        ChunkCopy(c, lower, lowerYpos, leftWid, 0, false);
        floor[3] = FindEmptyRange(c, new Loc(leftWid, lowerYpos), new Loc(leftWid + centreCavernWid - 1, GenConstants.DungeonHeight - 1)) ?? default;
        ChunkCopy(c, right, 0, leftWid + centreCavernWid, 0, false);
        floor[2] = FindEmptyRange(c, new Loc(leftWid + centreCavernWid, 0), new Loc(GenConstants.DungeonWidth - 1, GenConstants.DungeonHeight - 1)) ?? default;

        DrawRectangle(c, 0, 0, c.Height - 1, c.Width - 1, _f.Permanent, SquareFlags.None, true);
        ConnectCaverns(c, floor);
        EnsureConnectedness(c, false);

        var cavernArea = (leftWid + rightWid) * GenConstants.DungeonHeight + centreCavernWid * (upperHgt + lowerHgt);
        AllocStairs(c, _f.DownStair, _rng.RandRange(1, 3), 0, false, null, _dun.Quest);
        AllocStairs(c, _f.UpStair, _rng.RandRange(1, 2), 0, false, null, _dun.Quest);
        k = GeneralAmount(c.Depth);
        k = k * cavernArea / (GenConstants.DungeonHeight * GenConstants.DungeonWidth);
        AllocObjects(c, AllocSet.Both, AllocType.Rubble, _rng.RandInt1(k), c.Depth);
        AllocObjects(c, AllocSet.Corridor, AllocType.Trap, _rng.RandInt1(k), c.Depth);
        if (!NewPlayerSpot(c, out _))
        {
            error = "could not place player";
            return null;
        }
        for (var i = _rng.RandInt1(8) + k; i > 0; i--) PickAndPlaceDistantMonster(c, _playerStart, 0, true, c.Depth);
        AllocObjects(c, AllocSet.Both, AllocType.Object, _rng.Normal(k, 2), c.Depth + 5);
        AllocObjects(c, AllocSet.Both, AllocType.Gold, _rng.Normal(k / 2, 2), c.Depth);
        AllocObjects(c, AllocSet.Both, AllocType.Good, _rng.RandInt0(k / 4), c.Depth);
        return c;
    }
}
