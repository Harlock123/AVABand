using Angband.Core.Geometry;

namespace Angband.Core.Items;

/// <summary>Objects lying on a level, as piles per square (Angband square.obj).</summary>
public sealed class ObjectPiles(int width)
{
    private readonly SortedDictionary<int, List<Item>> _piles = new();

    public IReadOnlyList<Item> At(Loc p) => _piles.TryGetValue(Index(p), out var pile) ? pile : [];

    public bool Any(Loc p) => _piles.ContainsKey(Index(p));

    public IEnumerable<(Loc Loc, Item Item)> All =>
        _piles.SelectMany(kv => kv.Value.Select(i => (new Loc(kv.Key % width, kv.Key / width), i)));

    public int Count => _piles.Values.Sum(p => p.Count);

    /// <summary>Adds an object, merging with an identical stack already there.</summary>
    public void Add(Loc p, Item item)
    {
        if (!_piles.TryGetValue(Index(p), out var pile)) _piles[Index(p)] = pile = [];
        var match = pile.FirstOrDefault(i => i.CanStackWith(item) && i.Number + item.Number <= i.Base.MaxStack);
        if (match is not null) match.Absorb(item);
        else pile.Add(item);
    }

    public bool Remove(Loc p, Item item)
    {
        if (!_piles.TryGetValue(Index(p), out var pile) || !pile.Remove(item)) return false;
        if (pile.Count == 0) _piles.Remove(Index(p));
        return true;
    }

    private int Index(Loc p) => p.Y * width + p.X;
}
