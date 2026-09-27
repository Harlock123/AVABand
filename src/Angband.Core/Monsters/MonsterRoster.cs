using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Monsters;

/// <summary>The monsters on a level, indexed by slot id and cross-referenced from squares.</summary>
public sealed class MonsterRoster(Level level)
{
    private readonly List<Monster?> _slots = [null]; // slot 0 = "no monster"

    public IEnumerable<Monster> All => _slots.OfType<Monster>();

    public int Count => _slots.Count(m => m is not null);

    public int NextId => _slots.Count;

    public Monster? this[int id] => id > 0 && id < _slots.Count ? _slots[id] : null;

    public Monster? At(Loc p) => level.InBounds(p) ? this[level[p].Monster] : null;

    public void Add(Monster monster)
    {
        if (monster.Id != _slots.Count) throw new InvalidOperationException("Monster ids must be allocated with NextId.");
        if (level[monster.Position].Monster != 0) throw new InvalidOperationException($"Square {monster.Position} is occupied.");
        _slots.Add(monster);
        level[monster.Position].Monster = (ushort)monster.Id;
    }

    /// <summary>Puts a monster back at its saved slot id (loading a save).</summary>
    public void Restore(Monster monster)
    {
        while (_slots.Count <= monster.Id) _slots.Add(null);
        if (_slots[monster.Id] is not null) throw new InvalidOperationException($"Monster slot {monster.Id} is taken.");
        _slots[monster.Id] = monster;
        level[monster.Position].Monster = (ushort)monster.Id;
    }

    /// <summary>Reserves slot ids up to <paramref name="nextId"/> so new monsters don't reuse saved ids.</summary>
    public void ReserveUpTo(int nextId)
    {
        while (_slots.Count < nextId) _slots.Add(null);
    }

    public void Move(Monster monster, Loc to)
    {
        if (level[to].Monster != 0) throw new InvalidOperationException($"Square {to} is occupied.");
        level[monster.Position].Monster = 0;
        monster.Position = to;
        level[to].Monster = (ushort)monster.Id;
    }

    /// <summary>Swaps the positions of two monsters (pushing past).</summary>
    public void Swap(Monster a, Monster b)
    {
        (a.Position, b.Position) = (b.Position, a.Position);
        level[a.Position].Monster = (ushort)a.Id;
        level[b.Position].Monster = (ushort)b.Id;
    }

    public void Remove(Monster monster)
    {
        if (level[monster.Position].Monster == monster.Id) level[monster.Position].Monster = 0;
        _slots[monster.Id] = null;
        monster.IsRemoved = true;
    }
}
