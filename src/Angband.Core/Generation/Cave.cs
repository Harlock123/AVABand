using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Randomness;
using Angband.Core.World;

namespace Angband.Core.Generation;

/// <summary>Angband 4.2.5's generation constants (constants.txt <c>world</c>, <c>dun-gen</c>, <c>mon-gen</c>).</summary>
public static class GenConstants
{
    /// <summary>world:dungeon-hgt.</summary>
    public const int DungeonHeight = 66;
    /// <summary>world:dungeon-wid.</summary>
    public const int DungeonWidth = 198;
    /// <summary>mon-gen:level-min: the least number of random monsters on a level.</summary>
    public const int LevelMonsterMin = 14;
    /// <summary>dun-gen:cent-max: the most rooms a level keeps track of.</summary>
    public const int LevelRoomMax = 100;
    /// <summary>dun-gen:door-max: the most tunnel junctions remembered for doors.</summary>
    public const int LevelDoorMax = 200;
    /// <summary>dun-gen:wall-max: the most wall piercings a tunnel makes.</summary>
    public const int WallPierceMax = 500;
    /// <summary>dun-gen:tunn-max: the most grids a tunnel digs.</summary>
    public const int TunnGridMax = 900;
    /// <summary>dun-gen:amt-room: objects in rooms, on average.</summary>
    public const int RoomItemAv = 9;
    /// <summary>dun-gen:amt-item: objects anywhere, on average.</summary>
    public const int BothItemAv = 3;
    /// <summary>dun-gen:amt-gold: piles of gold, on average.</summary>
    public const int BothGoldAv = 3;
    /// <summary>dun-gen:pit-max: the most pits and nests on a level.</summary>
    public const int LevelPitMax = 2;
}

/// <summary>A staircase another level joins to (Angband's connector): where, and which way it goes.</summary>
internal readonly record struct Connector(Loc Grid, ushort Feat);

/// <summary>
/// The state of one generation attempt (Angband's global <c>dun</c>, dun_data): the profile, the
/// block map rooms reserve, room centres and marked entrances, tunnel bookkeeping, the stairs of
/// adjacent persistent levels, the pit theme.
/// </summary>
internal sealed class DunData(DungeonProfileDef profile)
{
    public DungeonProfileDef Profile { get; } = profile;
    public int BlockHgt { get; set; } = 1;
    public int BlockWid { get; set; } = 1;
    public int RowBlocks { get; set; }
    public int ColBlocks { get; set; }
    public bool[,] RoomMap { get; set; } = new bool[0, 0];

    public List<Loc> Cent { get; } = [];
    public int CentN => Cent.Count;
    public List<List<Loc>> Ent { get; } = [];
    public int[,] Ent2Room { get; set; } = new int[0, 0];

    public List<Loc> Door { get; } = [];
    public List<Loc> Wall { get; } = [];
    public List<Loc> Tunn { get; } = [];

    public List<Connector> Join { get; set; } = [];
    public List<Connector> OneOffAbove { get; set; } = [];
    public List<Connector> OneOffBelow { get; set; } = [];
    public Connector? CurrJoin { get; set; }
    public int NStairRoom { get; set; }
    public int PitNum { get; set; }
    public PitProfileDef? PitType { get; set; }
    public bool Persist { get; set; }
    public bool Quest { get; set; }
}

/// <summary>
/// One attempt at a level (Angband generate.c, gen-util.c, gen-room.c, gen-cave.c, gen-chunk.c,
/// gen-monster.c): the chunks it draws on are <see cref="Level"/>s; monsters, objects and gold are
/// left as <see cref="SpawnHint"/>s in the order 4.2.5 places them, for the population step.
/// </summary>
public sealed partial class Cave
{
    private readonly GameData _data;
    private readonly GameRandom _rng;
    private readonly WellKnownTerrain _f;
    private readonly Monsters.MonsterSpawner _spawner;
    private DunData _dun = null!;

    public Cave(GameData data, GameRandom rng)
    {
        _data = data;
        _rng = rng;
        _f = data.Terrain.Ids;
        _spawner = new Monsters.MonsterSpawner(data);
    }

    // Grids a monster or object has been planned for (square_isempty's "no monster, no object").
    private readonly Dictionary<Level, HashSet<Loc>> _occupied = [];
    // Floor grids per chunk (Angband feat_count[FEAT_FLOOR]).
    private readonly Dictionary<Level, int> _floors = [];
    // Monster rating from vaults, pits and chambers (Angband add_to_monster_rating).
    private readonly Dictionary<Level, long> _rating = [];

    private Level NewChunk(int height, int width, int depth)
    {
        var c = new Level(_data.Terrain, width, height, depth);
        _floors[c] = 0;
        _occupied[c] = [];
        _rating[c] = 0;
        return c;
    }

    private HashSet<Loc> Occupied(Level c) => _occupied.TryGetValue(c, out var s) ? s : _occupied[c] = [];

    private void AddToMonsterRating(Level c, int part) => _rating[c] = _rating.GetValueOrDefault(c) + part;

    private int FloorCount(Level c) => _floors.GetValueOrDefault(c);

    // --- Square predicates (cave-square.c) ---------------------------------------------------------

    private static bool InBounds(Level c, Loc g) => c.InBounds(g);
    private static bool InBoundsFully(Level c, Loc g) => c.InBoundsFully(g);
    private TerrainDef Feat(Level c, Loc g) => c.FeatureAt(g);
    private bool IsFloor(Level c, Loc g) => Feat(c, g).Has(TerrainFlags.Floor);
    private bool IsWallFeat(ushort feat) => _data.Terrain[feat].Has(TerrainFlags.Wall);
    private bool IsRock(Level c, Loc g) => Feat(c, g).Has(TerrainFlags.Granite) && !Feat(c, g).Has(TerrainFlags.DoorAny);
    private bool IsGranite(Level c, Loc g) => Feat(c, g).Has(TerrainFlags.Granite);
    private bool IsPerm(Level c, Loc g) => Feat(c, g).Has(TerrainFlags.Permanent) && Feat(c, g).Has(TerrainFlags.Rock);
    private bool IsMagma(Level c, Loc g) => Feat(c, g).Has(TerrainFlags.Magma);
    private bool IsQuartz(Level c, Loc g) => Feat(c, g).Has(TerrainFlags.Quartz);
    private bool IsMineral(Level c, Loc g) => IsRock(c, g) || IsMagma(c, g) || IsQuartz(c, g);
    private bool IsStrongWall(Level c, Loc g) => IsMineral(c, g) || IsPerm(c, g);
    private bool IsPassable(Level c, Loc g) => Feat(c, g).Has(TerrainFlags.Passable);
    private bool IsDoor(Level c, Loc g) => Feat(c, g).Has(TerrainFlags.DoorAny);
    private bool IsClosedDoor(Level c, Loc g) => Feat(c, g).Has(TerrainFlags.DoorClosed);
    private bool IsStairs(Level c, Loc g) => Feat(c, g).Has(TerrainFlags.Stair);
    private bool IsUpStairs(Level c, Loc g) => Feat(c, g).Has(TerrainFlags.UpStair);
    private bool IsDownStairs(Level c, Loc g) => Feat(c, g).Has(TerrainFlags.DownStair);
    private bool IsRubble(Level c, Loc g) => !Feat(c, g).Has(TerrainFlags.Wall) && Feat(c, g).Has(TerrainFlags.Rock);
    private bool IsShop(Level c, Loc g) => Feat(c, g).Has(TerrainFlags.Shop);
    private bool IsBright(Level c, Loc g) => Feat(c, g).Has(TerrainFlags.Bright);
    private static bool IsRoom(Level c, Loc g) => c[g].Has(SquareFlags.Room);
    private static bool IsVault(Level c, Loc g) => c[g].Has(SquareFlags.Vault);
    private static bool IsWallInner(Level c, Loc g) => c[g].Has(SquareFlags.WallInner);
    private static bool IsWallOuter(Level c, Loc g) => c[g].Has(SquareFlags.WallOuter);
    private static bool IsWallSolid(Level c, Loc g) => c[g].Has(SquareFlags.WallSolid);
    private static bool IsNoStairs(Level c, Loc g) => c[g].Has(SquareFlags.NoStairs);
    private static bool IsMonRestrict(Level c, Loc g) => c[g].Has(SquareFlags.MonRestrict);
    private static bool HasTrap(Level c, Loc g) => c[g].Trap != 0;

    /// <summary>Angband square_isempty: floor with no trap, web, monster or object.</summary>
    private bool IsEmpty(Level c, Loc g) => IsFloor(c, g) && !HasTrap(c, g) && !Occupied(c).Contains(g);

    /// <summary>Angband square_canputitem: an object can lie here (no trap, nothing already).</summary>
    private bool CanPutItem(Level c, Loc g) => Feat(c, g).Has(TerrainFlags.Object) && !HasTrap(c, g) && !Occupied(c).Contains(g);

    private bool IsGraniteWithFlag(Level c, Loc g, SquareFlags flag) => c[g].Feature == _f.Granite && c[g].Has(flag);

    private int NumWallsAdjacent(Level c, Loc g) =>
        (IsWallFeat(c[g + new Loc(0, 1)].Feature) ? 1 : 0) + (IsWallFeat(c[g + new Loc(0, -1)].Feature) ? 1 : 0)
        + (IsWallFeat(c[g + new Loc(1, 0)].Feature) ? 1 : 0) + (IsWallFeat(c[g + new Loc(-1, 0)].Feature) ? 1 : 0);

    private int NumWallsDiagonal(Level c, Loc g) =>
        (IsWallFeat(c[g + new Loc(1, 1)].Feature) ? 1 : 0) + (IsWallFeat(c[g + new Loc(-1, -1)].Feature) ? 1 : 0)
        + (IsWallFeat(c[g + new Loc(1, -1)].Feature) ? 1 : 0) + (IsWallFeat(c[g + new Loc(-1, 1)].Feature) ? 1 : 0);

    private bool SuitsStairsWell(Level c, Loc g) =>
        !IsVault(c, g) && !IsNoStairs(c, g) && NumWallsAdjacent(c, g) == 3 && NumWallsDiagonal(c, g) == 4 && IsEmpty(c, g);

    private bool SuitsStairsOk(Level c, Loc g) =>
        !IsVault(c, g) && !IsNoStairs(c, g) && NumWallsAdjacent(c, g) == 2 && NumWallsDiagonal(c, g) == 4 && IsEmpty(c, g);

    /// <summary>Angband's ddgrid_ddd: the four cardinal directions, then the diagonals, then none.</summary>
    private static readonly Loc[] Ddd =
    [
        new(0, 1), new(0, -1), new(1, 0), new(-1, 0),
        new(1, 1), new(-1, 1), new(1, -1), new(-1, -1),
        new(0, 0),
    ];

    /// <summary>Angband ddgrid by direction number (1-9 on the keypad).</summary>
    private static readonly Loc[] DdGrid =
    [
        new(0, 0), new(-1, 1), new(0, 1), new(1, 1), new(-1, 0), new(0, 0), new(1, 0), new(-1, -1), new(0, -1), new(1, -1),
    ];

    /// <summary>Angband's ddd: the eight directions.</summary>
    private static readonly int[] DddDirs = [2, 8, 6, 4, 3, 1, 9, 7, 5];

    private int CountNeighbors(Level c, Loc g, Func<Level, Loc, bool> test, bool under = false)
    {
        var count = 0;
        for (var d = 0; d < (under ? 9 : 8); d++)
        {
            var n = g + Ddd[d];
            if (InBounds(c, n) && test(c, n)) count++;
        }
        return count;
    }

    // --- Square changes -----------------------------------------------------------------------------

    /// <summary>Angband square_set_feat, keeping the floor count.</summary>
    private void SetFeat(Level c, Loc g, ushort feat)
    {
        ref var sq = ref c[g];
        if (sq.Feature == _f.Floor) _floors[c] = FloorCount(c) - 1;
        if (feat == _f.Floor) _floors[c] = FloorCount(c) + 1;
        sq.Feature = feat;
        sq.LockPower = 0;
        // Angband square_set_feat: changing the terrain removes a trap that no longer suits it.
        if (sq.Trap != 0 && !_data.Terrain[feat].Has(TerrainFlags.Trap)) sq.Trap = 0;
    }

    private static void SqOn(Level c, Loc g, SquareFlags f) => c[g].Flags |= f;
    private static void SqOff(Level c, Loc g, SquareFlags f) => c[g].Flags &= ~f;

    /// <summary>Angband generate_mark.</summary>
    private static void GenerateMark(Level c, int y1, int x1, int y2, int x2, SquareFlags flag)
    {
        for (var y = y1; y <= y2; y++)
        for (var x = x1; x <= x2; x++)
            SqOn(c, new Loc(x, y), flag);
    }

    /// <summary>Angband generate_room: marks grids as room, perhaps lit.</summary>
    private static void GenerateRoom(Level c, int y1, int x1, int y2, int x2, bool light)
    {
        for (var y = y1; y <= y2; y++)
        for (var x = x1; x <= x2; x++)
        {
            SqOn(c, new Loc(x, y), SquareFlags.Room);
            if (light) SqOn(c, new Loc(x, y), SquareFlags.Glow);
        }
    }

    /// <summary>Angband fill_rectangle.</summary>
    private void FillRectangle(Level c, int y1, int x1, int y2, int x2, ushort feat, SquareFlags flag)
    {
        for (var y = y1; y <= y2; y++)
        for (var x = x1; x <= x2; x++)
            SetFeat(c, new Loc(x, y), feat);
        if (flag != SquareFlags.None) GenerateMark(c, y1, x1, y2, x2, flag);
    }

    /// <summary>Angband draw_rectangle.</summary>
    private void DrawRectangle(Level c, int y1, int x1, int y2, int x2, ushort feat, SquareFlags flag, bool overwritePerm)
    {
        for (var y = y1; y <= y2; y++)
        {
            if (overwritePerm || !IsPerm(c, new Loc(x1, y))) SetFeat(c, new Loc(x1, y), feat);
            if (overwritePerm || !IsPerm(c, new Loc(x2, y))) SetFeat(c, new Loc(x2, y), feat);
        }
        if (flag != SquareFlags.None)
        {
            GenerateMark(c, y1, x1, y2, x1, flag);
            GenerateMark(c, y1, x2, y2, x2, flag);
        }
        for (var x = x1; x <= x2; x++)
        {
            if (overwritePerm || !IsPerm(c, new Loc(x, y1))) SetFeat(c, new Loc(x, y1), feat);
            if (overwritePerm || !IsPerm(c, new Loc(x, y2))) SetFeat(c, new Loc(x, y2), feat);
        }
        if (flag != SquareFlags.None)
        {
            GenerateMark(c, y1, x1, y1, x2, flag);
            GenerateMark(c, y2, x1, y2, x2, flag);
        }
    }

    /// <summary>Angband set_marked_granite.</summary>
    private void SetMarkedGranite(Level c, Loc g, SquareFlags flag)
    {
        SetFeat(c, g, _f.Granite);
        if (flag != SquareFlags.None) SqOn(c, g, flag);
    }

    // --- Doors, traps, stairs (gen-util.c) ----------------------------------------------------------

    private void PlaceSecretDoor(Level c, Loc g) => SetFeat(c, g, _f.SecretDoor);

    /// <summary>Angband place_closed_door: one in four locked (power 1 to 7).</summary>
    private void PlaceClosedDoor(Level c, Loc g)
    {
        SetFeat(c, g, _f.ClosedDoor);
        if (_rng.OneIn(4)) c[g].LockPower = (byte)_rng.RandInt1(7);
    }

    /// <summary>Angband place_random_door: open 30%, broken 10%, otherwise closed.</summary>
    private void PlaceRandomDoor(Level c, Loc g)
    {
        var tmp = _rng.RandInt0(100);
        if (tmp < 30) SetFeat(c, g, _f.OpenDoor);
        else if (tmp < 40) SetFeat(c, g, _f.BrokenDoor);
        else PlaceClosedDoor(c, g);
    }

    /// <summary>Angband place_trap: a trap for the depth, if the grid can hold one.</summary>
    private void PlaceTrap(Level c, Loc g, int depth)
    {
        if (!InBounds(c, g) || !Feat(c, g).Has(TerrainFlags.Trap) || Occupied(c).Contains(g)) return;
        var trap = _data.PickTrap(_rng, depth, trapDoors: depth < _data.Constants.MaxDepth && !_dun.Quest);
        if (trap is null) return;
        c[g].Trap = trap.Index;
        c[g].TrapPower = trap.RollPower(_rng, depth);
    }

    private void PlaceRubble(Level c, Loc g) => SetFeat(c, g, _rng.OneIn(2) ? _f.Rubble : _f.PassableRubble);

    /// <summary>Angband place_stairs: the town's go down, a quest level's and the bottom's up.</summary>
    private void PlaceStairs(Level c, Loc g, bool quest, ushort feat)
    {
        if (c.Depth == 0) SetFeat(c, g, _f.DownStair);
        else if (quest || c.Depth >= _data.Constants.MaxDepth) SetFeat(c, g, _f.UpStair);
        else SetFeat(c, g, feat);
    }

    private void PlaceRandomStairs(Level c, Loc g, bool quest)
    {
        var feat = _rng.RandInt0(100) < 50 ? _f.UpStair : _f.DownStair;
        if (CanPutItem(c, g)) PlaceStairs(c, g, quest, feat);
    }

    // --- Hints: what 4.2.5 would place now ----------------------------------------------------------

    /// <summary>Angband place_object: an object planned for the grid (at <paramref name="level"/>, of a base if given).</summary>
    private void PlaceObject(Level c, Loc g, int level, bool good, bool great, string? tval = null)
    {
        if (!InBounds(c, g) || !CanPutItem(c, g)) return;
        var kind = great ? SpawnKind.GreatObject : good ? SpawnKind.GoodObject : SpawnKind.Object;
        c.SpawnHints.Add(new SpawnHint(g, kind, level - c.Depth, tval is null ? null : "tval:" + tval));
        Occupied(c).Add(g);
    }

    /// <summary>Angband place_gold.</summary>
    private void PlaceGold(Level c, Loc g, int level)
    {
        if (!InBounds(c, g) || !CanPutItem(c, g)) return;
        c.SpawnHints.Add(new SpawnHint(g, SpawnKind.Gold, level - c.Depth));
        Occupied(c).Add(g);
    }

    /// <summary>
    /// Angband pick_and_place_monster: a monster for <paramref name="depth"/> planned for the grid
    /// (asleep or not, with its group or not), drawn under the current restriction.
    /// </summary>
    private void PickAndPlaceMonster(Level c, Loc g, int depth, bool sleep, bool groupOk, string? restriction = null)
    {
        if (!InBounds(c, g) || !IsEmpty(c, g)) return;
        var tag = (sleep ? "sleep" : "awake") + (groupOk ? ",group" : "") + (restriction is null ? "" : "," + restriction);
        c.SpawnHints.Add(new SpawnHint(g, SpawnKind.Monster, depth - c.Depth, tag));
        Occupied(c).Add(g);
    }

    /// <summary>Angband place_new_monster: a particular race planned for the grid.</summary>
    private void PlaceNewMonster(Level c, Loc g, MonsterRaceDef race, bool sleep, bool groupOk)
    {
        if (!InBounds(c, g) || !IsEmpty(c, g)) return;
        c.SpawnHints.Add(new SpawnHint(g, SpawnKind.Race, 0, $"{race.Id}|{(sleep ? "sleep" : "awake")}{(groupOk ? ",group" : "")}"));
        Occupied(c).Add(g);
    }
}
