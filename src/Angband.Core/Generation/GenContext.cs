using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Randomness;
using Angband.Core.World;

namespace Angband.Core.Generation;

/// <summary>Mutable state and drawing helpers shared by generators and room builders for one attempt.</summary>
internal sealed class GenContext(GameData data, DungeonProfileDef profile, int depth, GameRandom rng)
{
    private Level? _level;

    public GameData Data { get; } = data;
    public DungeonProfileDef Profile { get; } = profile;
    public int Depth { get; } = depth;
    public GameRandom Rng { get; } = rng;
    public WellKnownTerrain F => Data.Terrain.Ids;

    public Level Level => _level ?? throw new InvalidOperationException("CreateLevel has not been called.");

    /// <summary>Connection points of placed rooms, joined by tunnels.</summary>
    public List<Loc> RoomCenters { get; } = [];

    /// <summary>Stairs a persistent level must have (<see cref="LevelRequest.Joins"/>); empty otherwise.</summary>
    public IReadOnlyList<StairJoin> Joins { get; set; } = [];

    /// <summary>Where tunnels met existing corridors; candidates for doors.</summary>
    public List<Loc> Junctions { get; } = [];

    public Level CreateLevel(int width, int height)
    {
        _level = new Level(Data.Terrain, width, height, Depth);
        return _level;
    }

    public static SquareFlags Light(bool lit) => lit ? SquareFlags.Glow : SquareFlags.None;

    /// <summary>Angband: rooms are lit when depth &lt;= randint1(25).</summary>
    public bool RollRoomLight() => Depth <= Rng.RandInt1(Profile.RoomLightDepth);

    public void SetFloor(Loc p, SquareFlags add = SquareFlags.None)
    {
        ref var sq = ref Level[p];
        sq.Feature = F.Floor;
        sq.Flags = (sq.Flags & ~SquareFlags.AnyWallMarker) | add;
        sq.LockPower = 0;
    }

    public void SetWall(Loc p, ushort feature, SquareFlags marker, SquareFlags add = SquareFlags.None)
    {
        ref var sq = ref Level[p];
        sq.Feature = feature;
        sq.Flags = (sq.Flags & ~SquareFlags.AnyWallMarker) | marker | add;
        sq.Trap = 0;
        sq.LockPower = 0;
    }

    public void SetFeature(Loc p, ushort feature)
    {
        ref var sq = ref Level[p];
        sq.Feature = feature;
        sq.Flags &= ~SquareFlags.AnyWallMarker;
        sq.Trap = 0;
        sq.LockPower = 0;
    }

    public void Fill(Rect r, ushort feature, SquareFlags marker = SquareFlags.None)
    {
        foreach (var p in r.Cells()) SetWall(p, feature, marker);
    }

    public void DrawEdge(Rect r, ushort feature, SquareFlags marker, SquareFlags add = SquareFlags.None)
    {
        foreach (var p in r.Edge()) SetWall(p, feature, marker, add);
    }

    /// <summary>Draws a room's outer granite walls around <paramref name="interior"/>, sparing existing room floor.</summary>
    public void DrawRoomWalls(Rect interior, bool lit)
    {
        foreach (var p in interior.Inflate(1).Edge())
        {
            ref var sq = ref Level[p];
            if (sq.Has(SquareFlags.Room) && Level.IsFloor(p)) continue;
            SetWall(p, F.Granite, SquareFlags.WallOuter, SquareFlags.Room | Light(lit));
        }
    }

    public void DrawRoomFloor(Rect interior, bool lit)
    {
        foreach (var p in interior.Cells()) SetFloor(p, SquareFlags.Room | Light(lit));
    }

    public void BuildRoom(Rect interior, bool lit)
    {
        DrawRoomWalls(interior, lit);
        DrawRoomFloor(interior, lit);
    }

    /// <summary>An inner wall inside a room.</summary>
    public void SetInnerWall(Loc p, bool lit) =>
        SetWall(p, F.Granite, SquareFlags.WallInner, SquareFlags.Room | Light(lit));

    public void PlaceRandomDoor(Loc p)
    {
        var d = Profile.Doors;
        var roll = Rng.RandInt0(Math.Max(1, d.OpenWeight + d.BrokenWeight + d.SecretWeight + d.ClosedWeight));
        if ((roll -= d.OpenWeight) < 0) SetFeature(p, F.OpenDoor);
        else if ((roll -= d.BrokenWeight) < 0) SetFeature(p, F.BrokenDoor);
        else if ((roll -= d.SecretWeight) < 0) PlaceSecretDoor(p);
        else PlaceClosedDoor(p);
    }

    public void PlaceClosedDoor(Loc p)
    {
        SetFeature(p, F.ClosedDoor);
        if (Rng.Percent(Profile.Doors.LockedChance))
            Level[p].LockPower = (byte)Rng.RandInt1(Profile.Doors.MaxLockPower);
    }

    public void PlaceSecretDoor(Loc p) => SetFeature(p, F.SecretDoor);

    /// <summary>Places a depth-appropriate trap on a floor square. Returns false if none is eligible.</summary>
    public bool PlaceTrap(Loc p)
    {
        if (!Level.Has(p, TerrainFlags.Trap)) return false;
        var noTrapDoor = Depth >= Data.Constants.MaxDepth;
        var eligible = Data.Traps
            .Where(t => t.MinDepth <= Depth && t.MaxDepth >= Depth && !(noTrapDoor && t.IsTrapDoor))
            .ToList();
        var trap = Rng.PickWeighted(eligible, t => t.Weight);
        if (trap is null) return false;
        Level[p].Trap = trap.Index;
        return true;
    }

    public void AddHint(Loc p, SpawnKind kind, int depthBonus = 0, string? tag = null) =>
        Level.SpawnHints.Add(new SpawnHint(p, kind, depthBonus, tag));

    /// <summary>Orthogonal direction towards <paramref name="to"/>; never diagonal (Angband correct_dir).</summary>
    public Loc CorrectDir(Loc from, Loc to)
    {
        var dx = Math.Sign(to.X - from.X);
        var dy = Math.Sign(to.Y - from.Y);
        if (dx != 0 && dy != 0)
        {
            if (Rng.OneIn(2)) dx = 0;
            else dy = 0;
        }
        return new Loc(dx, dy);
    }

    public Loc RandomOrthogonal() => Rng.Pick(DirectionExtensions.Orthogonal).Offset();
}
