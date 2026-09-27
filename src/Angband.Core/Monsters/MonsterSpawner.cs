using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Randomness;
using Angband.Core.Time;
using Angband.Core.World;

namespace Angband.Core.Monsters;

/// <summary>Chooses and places monsters (Angband get_mon_num / place_new_monster).</summary>
public sealed class MonsterSpawner(GameData data)
{
    /// <summary>1-in-N chance of an out-of-depth pick (Angband ood_monster_chance).</summary>
    public const int OutOfDepthChance = 25;
    public const int OutOfDepthAmount = 10;

    /// <summary>
    /// Picks a race for <paramref name="depth"/>, weighted by 1/rarity. Town monsters only appear in
    /// town; unavailable uniques are skipped.
    /// </summary>
    public MonsterRaceDef? PickRace(GameRandom rng, int depth, ISet<string> unavailableUniques,
        Func<MonsterRaceDef, bool>? filter = null, bool allowOutOfDepth = true)
    {
        var levelDepth = depth;
        if (allowOutOfDepth && depth > 0 && rng.OneIn(OutOfDepthChance))
            depth += Math.Min(depth / 4 + 2, OutOfDepthAmount);

        // Quest monsters (Sauron, Morgoth) only ever appear where their quest puts them, and
        // FORCE_DEPTH ones never out of depth (Angband get_mon_num).
        var eligible = data.Monsters.Where(r =>
                (depth == 0 ? r.Depth == 0 : r.Depth > 0 && r.Depth <= depth)
                && !r.Has(MonsterFlags.Questor)
                && !(r.Has(MonsterFlags.ForceDepth) && r.Depth > levelDepth)
                && !(r.IsUnique && unavailableUniques.Contains(r.Id))
                && (filter?.Invoke(r) ?? true))
            .ToList();
        return rng.PickWeighted(eligible, r => Math.Max(1, 100 / Math.Max(1, r.Rarity)));
    }

    /// <summary>Creates a monster on an empty square (Angband mon_create).</summary>
    public Monster Place(Level level, GameRandom rng, MonsterRaceDef race, Loc at, bool asleep = true)
    {
        int hp;
        if (race.IsUnique) hp = race.HitPoints;
        else
        {
            var stdDev = (race.HitPoints * 10 / 8 + 5) / 10 + (race.HitPoints > 1 ? 1 : 0);
            hp = Math.Max(1, rng.Normal(race.HitPoints, stdDev));
        }

        var speed = race.Speed;
        if (!race.IsUnique)
        {
            var spread = EnergyTable.EnergyPerTurn(speed) / 10;
            if (spread > 0) speed += rng.Spread(0, spread);
        }

        var monster = new Monster(level.Monsters.NextId, race, at, hp, speed)
        {
            Energy = rng.RandInt0(50),
            Camouflaged = race.Has(MonsterFlags.Unaware),
        };
        if (asleep && race.Sleep > 0 && !race.Has(MonsterFlags.NoSleep))
            monster.Sleep = race.Sleep * 2 + rng.RandInt1(race.Sleep * 10);
        level.Monsters.Add(monster);
        return monster;
    }

    /// <summary>Places a monster and, for FRIENDS races, a small pack around it.</summary>
    public List<Monster> PlaceWithFriends(Level level, GameRandom rng, MonsterRaceDef race, Loc at, ISet<string> uniques)
    {
        var placed = new List<Monster> { Place(level, rng, race, at) };
        if (race.IsUnique) uniques.Add(race.Id);
        if (!race.Has(MonsterFlags.Friends)) return placed;

        var extra = rng.RandRange(1, 5);
        foreach (var p in NearbyEmpty(level, at, extra))
            placed.Add(Place(level, rng, race, p));
        return placed;
    }

    /// <summary>
    /// Fills generator spawn hints (vault guardians, pits, nests) and then scatters the level's
    /// monster budget away from the player.
    /// </summary>
    public void Populate(Level level, GameRandom rng, Loc player, ISet<string> unavailableUniques)
    {
        // Each pit or nest is a connected block of hints; give each block a single theme (monster glyph).
        var clusters = PitClusters(level.SpawnHints);
        var themes = new Dictionary<int, char?>();
        foreach (var hint in level.SpawnHints)
        {
            if (!level.InBounds(hint.Loc) || !level.IsPassable(hint.Loc) || level[hint.Loc].Monster != 0) continue;
            if (hint.Loc.ChebyshevTo(player) <= 1) continue;

            switch (hint.Kind)
            {
                case SpawnKind.Monster when hint.Tag is ['g', 'l', 'y', 'p', 'h', ':', var g]:
                    // A vault's letter: a monster of that kind, falling back to anything if none fits.
                    if ((PickRace(rng, level.Depth + hint.DepthBonus, unavailableUniques, r => r.Glyph == g)
                         ?? PickRace(rng, level.Depth + hint.DepthBonus, unavailableUniques)) is { } kind)
                        PlaceWithFriends(level, rng, kind, hint.Loc, unavailableUniques);
                    break;
                case SpawnKind.Monster:
                case SpawnKind.MonsterOrObject when rng.OneIn(2):
                    if (PickRace(rng, level.Depth + hint.DepthBonus, unavailableUniques) is { } race)
                        PlaceWithFriends(level, rng, race, hint.Loc, unavailableUniques);
                    break;

                case SpawnKind.PitMonster:
                case SpawnKind.NestMonster:
                    var glyph = ThemeFor(hint.Loc);
                    if (glyph is null) break;
                    var member = PickRace(rng, level.Depth + hint.DepthBonus, unavailableUniques,
                        r => r.Glyph == glyph && !r.IsUnique, allowOutOfDepth: false);
                    if (member is not null) Place(level, rng, member, hint.Loc);
                    break;
            }
        }

        for (var i = 0; i < level.Population.Monsters; i++)
        {
            var spot = FindSpot(level, rng, p => level.IsEmptyFloor(p) && p.DistanceTo(player) > 10
                                                 && !level[p].Has(SquareFlags.Vault));
            if (spot is not { } s) break;
            if (PickRace(rng, level.Depth, unavailableUniques) is { } race)
                PlaceWithFriends(level, rng, race, s, unavailableUniques);
        }

        char? ThemeFor(Loc p)
        {
            var cluster = clusters[p];
            if (!themes.TryGetValue(cluster, out var glyph))
                themes[cluster] = glyph = PickRace(rng, level.Depth + 5, unavailableUniques, r => !r.IsUnique, allowOutOfDepth: false)?.Glyph;
            return glyph;
        }
    }

    /// <summary>Labels 8-connected groups of pit/nest hints; each group is one pit or nest room.</summary>
    public static Dictionary<Loc, int> PitClusters(IEnumerable<SpawnHint> hints)
    {
        var locs = hints.Where(h => h.Kind is SpawnKind.PitMonster or SpawnKind.NestMonster).Select(h => h.Loc).ToHashSet();
        var labels = new Dictionary<Loc, int>();
        var next = 0;
        foreach (var start in locs.OrderBy(l => l.Y).ThenBy(l => l.X))
        {
            if (labels.ContainsKey(start)) continue;
            var queue = new Queue<Loc>([start]);
            labels[start] = next;
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var d in DirectionExtensions.Compass)
                {
                    var n = p.Step(d);
                    if (locs.Contains(n) && labels.TryAdd(n, next)) queue.Enqueue(n);
                }
            }
            next++;
        }
        return labels;
    }

    private static Loc? FindSpot(Level level, GameRandom rng, Func<Loc, bool> ok)
    {
        for (var i = 0; i < 500; i++)
        {
            var p = new Loc(rng.RandRange(1, level.Width - 2), rng.RandRange(1, level.Height - 2));
            if (ok(p)) return p;
        }
        return null;
    }

    /// <summary>Empty floor squares spiralling out from <paramref name="center"/>, nearest first.</summary>
    private static IEnumerable<Loc> NearbyEmpty(Level level, Loc center, int count)
    {
        var found = 0;
        for (var r = 1; r <= 3 && found < count; r++)
        for (var dy = -r; dy <= r && found < count; dy++)
        for (var dx = -r; dx <= r && found < count; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
            var p = new Loc(center.X + dx, center.Y + dy);
            if (!level.InBoundsFully(p) || !level.IsEmptyFloor(p)) continue;
            found++;
            yield return p;
        }
    }
}
