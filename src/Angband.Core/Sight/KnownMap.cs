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

    public void Remember(Level level, Loc p)
    {
        _features[p.Y * Width + p.X] = level[p].Feature;
        if (level[p].Trap != 0) level[p].Flags |= SquareFlags.TrapVisible;
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
