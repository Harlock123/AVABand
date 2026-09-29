using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Randomness;
using Angband.Core.World;

namespace Angband.Core.Generation;

/// <summary>Mutable state and drawing helpers for AVABand's own town.</summary>
internal sealed class GenContext(GameData data, DungeonProfileDef profile, int depth, GameRandom rng)
{
    private Level? _level;

    public GameData Data { get; } = data;
    public DungeonProfileDef Profile { get; } = profile;
    public int Depth { get; } = depth;
    public GameRandom Rng { get; } = rng;
    public WellKnownTerrain F => Data.Terrain.Ids;

    public Level Level => _level ?? throw new InvalidOperationException("CreateLevel has not been called.");

    public Level CreateLevel(int width, int height)
    {
        _level = new Level(Data.Terrain, width, height, Depth);
        return _level;
    }

    public static SquareFlags Light(bool lit) => lit ? SquareFlags.Glow : SquareFlags.None;

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

    public void AddHint(Loc p, SpawnKind kind, int depthBonus = 0, string? tag = null) =>
        Level.SpawnHints.Add(new SpawnHint(p, kind, depthBonus, tag));

}
