using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Sight;

/// <summary>
/// The player's memory of the current level (Angband's "player cave"): the terrain last seen on
/// each square. Memory can be out of date, e.g. a door that was closed out of sight still shows open.
/// </summary>
public sealed class KnownMap
{
    private const ushort Unknown = ushort.MaxValue;
    private readonly ushort[] _features;

    public KnownMap(int width, int height)
    {
        Width = width;
        Height = height;
        _features = new ushort[width * height];
        Array.Fill(_features, Unknown);
    }

    public int Width { get; }
    public int Height { get; }

    public bool IsKnown(Loc p) => _features[p.Y * Width + p.X] != Unknown;

    /// <summary>The remembered terrain index; only meaningful when <see cref="IsKnown"/>.</summary>
    public ushort Feature(Loc p) => _features[p.Y * Width + p.X];

    /// <summary>The player's search skill: a trap is noticed when seen only if it is at least the trap's power.</summary>
    public int SearchSkill { get; set; } = int.MaxValue;

    /// <summary>Traps noticed since last asked (the game says "You have found a trap.").</summary>
    public int TrapsNoticed { get; set; }

    public void Remember(Level level, Loc p)
    {
        _features[p.Y * Width + p.X] = level[p].Feature;
        // Angband square_reveal_trap: a trap is seen if the player's search skill reaches its power.
        ref var sq = ref level[p];
        if (sq.Trap != 0 && !sq.Has(SquareFlags.TrapVisible) && sq.TrapPower <= SearchSkill)
        {
            sq.Flags |= SquareFlags.TrapVisible;
            TrapsNoticed++;
        }
    }

    public void Forget(Loc p) => _features[p.Y * Width + p.X] = Unknown;

    private readonly Dictionary<int, Items.Item> _objects = [];

    /// <summary>Records the top object seen on a square (or that it is empty).</summary>
    public void RememberObject(Loc p, Items.Item? top)
    {
        if (top is null) _objects.Remove(p.Y * Width + p.X);
        else _objects[p.Y * Width + p.X] = top;
    }

    /// <summary>The object the player last saw here, if any.</summary>
    public Items.Item? RememberedObject(Loc p) => _objects.GetValueOrDefault(p.Y * Width + p.X);

    public void RememberAll(Level level)
    {
        foreach (var p in level.AllLocs()) Remember(level, p);
    }

    /// <summary>Raw storage for save games.</summary>
    public ReadOnlySpan<ushort> Raw => _features;

    /// <summary>The remembered-object memory, by square index (for saving).</summary>
    public IReadOnlyDictionary<int, Items.Item> RememberedObjects => _objects;

    /// <summary>Restores a remembered feature by square index (loading a save).</summary>
    internal void RestoreFeature(int index, ushort feature) => _features[index] = feature;
}
