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

    /// <summary>Angband group-max: the most monsters one group puddles out to.</summary>
    public const int GroupMax = 25;
    /// <summary>Angband group-dist: how far from the leader an escort of another race may start.</summary>
    public const int GroupDistance = 5;

    /// <summary>
    /// Angband place_new_monster with groups allowed: the monster, then each of its escorts in turn —
    /// with that escort's percent chance, a number rolled from its dice, of its own race, a named
    /// race, or a race of a base drawn for the leader's level.
    /// </summary>
    public List<Monster> PlaceWithFriends(Level level, GameRandom rng, MonsterRaceDef race, Loc at, ISet<string> uniques,
        bool asleep = true)
    {
        var placed = new List<Monster> { Place(level, rng, race, at, asleep) };
        if (race.IsUnique) uniques.Add(race.Id);

        foreach (var friend in race.Friends)
        {
            if (rng.RandInt0(100) >= friend.Chance) continue;
            var total = friend.Number.Roll(rng);
            MonsterRaceDef? friendRace;
            if (friend.IsSame) friendRace = race;
            else if (friend.Race is { } id) friendRace = data.Monster(id);
            else
            {
                // Angband: get_mon_num(race->level) restricted to the base; no race, no more escorts.
                friendRace = PickRace(rng, Math.Max(1, race.Depth), uniques, r => r.Glyph == friend.Glyph && !r.IsUnique);
                if (friendRace is null) break;
            }
            if (friendRace is null) continue;
            var escorts = PlaceFriends(level, rng, race, friendRace, total, at, asleep, uniques);
            // Angband group roles: bodyguards are bound to the monster they escort.
            if (friend.IsBodyguard)
                foreach (var guard in escorts.Where(g => g != placed[0])) guard.BodyguardOf = placed[0].Id;
            placed.AddRange(escorts);
        }
        return placed;
    }

    /// <summary>
    /// Angband place_friends: a group of escorts. Escorts more than four levels out of depth come
    /// alone or not at all, and groups shrink within five levels of their native depth. Escorts of
    /// the leader's own race puddle out from the leader; others start a little way off.
    /// </summary>
    private List<Monster> PlaceFriends(Level level, GameRandom rng, MonsterRaceDef race, MonsterRaceDef friendRace, int total,
        Loc at, bool asleep, ISet<string> uniques)
    {
        var levelDifference = level.Depth - friendRace.Depth + 5;
        if (friendRace.IsUnique)
        {
            if (uniques.Contains(friendRace.Id)) return [];
        }
        else
        {
            if (levelDifference <= 0) return [];
            if (levelDifference < 10)
            {
                // The fraction left over is the chance of one more.
                var extraChance = total * levelDifference % 10;
                total = total * levelDifference / 10;
                if (rng.RandInt0(10) > extraChance) total++;
            }
        }
        if (total <= 0) return [];

        if (friendRace == race) return PlaceGroup(level, rng, race, at, total, asleep, uniques, leaderPlaced: true);

        // Angband scatter_ext: any open square within group-dist of the leader.
        var spots = new List<Loc>();
        for (var dy = -GroupDistance; dy <= GroupDistance; dy++)
        for (var dx = -GroupDistance; dx <= GroupDistance; dx++)
        {
            var p = at + new Loc(dx, dy);
            if (level.InBounds(p) && p.DistanceTo(at) <= GroupDistance && level.IsEmptyFloor(p)) spots.Add(p);
        }
        if (spots.Count == 0) return [];
        return PlaceGroup(level, rng, friendRace, rng.Pick(spots), total, asleep, uniques, leaderPlaced: false);
    }

    /// <summary>
    /// Angband place_new_monster_group: monsters puddle out breadth-first from a square, onto empty
    /// floor, until the group (counting the one already there) numbers <paramref name="total"/>.
    /// </summary>
    private List<Monster> PlaceGroup(Level level, GameRandom rng, MonsterRaceDef race, Loc start, int total, bool asleep,
        ISet<string> uniques, bool leaderPlaced)
    {
        var placed = new List<Monster>();
        total = Math.Min(total, GroupMax);
        if (!leaderPlaced)
        {
            placed.Add(Place(level, rng, race, start, asleep));
            if (race.IsUnique) { uniques.Add(race.Id); return placed; }
        }
        var squares = new List<Loc> { start };
        for (var n = 0; n < squares.Count && squares.Count < total; n++)
            foreach (var dir in Direction8)
            {
                if (squares.Count >= total) break;
                var p = squares[n] + dir;
                if (!level.InBounds(p) || !level.IsEmptyFloor(p)) continue;
                placed.Add(Place(level, rng, race, p, asleep));
                squares.Add(p);
            }
        return placed;
    }

    /// <summary>Angband ddgrid_ddd: the eight directions, in its order.</summary>
    private static readonly Loc[] Direction8 =
        [new(0, 1), new(0, -1), new(1, 0), new(-1, 0), new(1, 1), new(-1, 1), new(1, -1), new(-1, -1)];

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
                    // A vault's letter: a monster of that kind — or nothing, if none is native this
                    // shallow. Angband get_vault_monsters places these awake and without escorts.
                    if (PickRace(rng, level.Depth + hint.DepthBonus, unavailableUniques, r => r.Glyph == g) is { } kind)
                    {
                        Place(level, rng, kind, hint.Loc, asleep: false);
                        if (kind.IsUnique) unavailableUniques.Add(kind.Id);
                    }
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

    /// <summary>
    /// Angband pick_and_place_distant_monster: a monster of the depth (with its friends) on an empty
    /// square more than <paramref name="distance"/> from the player, outside vaults. Null if none fits.
    /// </summary>
    public List<Monster>? PlaceDistant(Level level, GameRandom rng, Loc player, int distance, int depth, ISet<string> uniques,
        bool asleep = true)
    {
        var spot = FindSpot(level, rng, p => level.IsEmptyFloor(p) && p.DistanceTo(player) > distance && !level[p].Has(SquareFlags.Vault));
        if (spot is not { } s || PickRace(rng, depth, uniques) is not { } race) return null;
        return PlaceWithFriends(level, rng, race, s, uniques, asleep);
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
}
