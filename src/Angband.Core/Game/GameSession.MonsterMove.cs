using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.World;

namespace Angband.Core.Game;

// Angband mon-move.c: how a monster decides where to go, and goes there.
public sealed partial class GameSession
{
    /// <summary>Angband's keypad directions as offsets (ddgrid): 1 south-west ... 9 north-east.</summary>
    private static readonly Loc[] Keypad =
        [new(0, 0), new(-1, 1), new(0, 1), new(1, 1), new(-1, 0), new(0, 0), new(1, 0), new(-1, -1), new(0, -1), new(1, -1)];

    /// <summary>Angband ddd: the eight directions in its usual order.</summary>
    private static readonly int[] Ddd = [2, 8, 6, 4, 3, 1, 9, 7];

    /// <summary>Angband side_dirs: a direction, then those either side of it, and so on (biased right, then left).</summary>
    private static readonly int[,] SideDirs =
    {
        { 0, 0, 0, 0, 0, 0, 0, 0 }, { 1, 4, 2, 7, 3, 8, 6, 9 }, { 2, 1, 3, 4, 6, 7, 9, 8 }, { 3, 2, 6, 1, 9, 4, 8, 7 },
        { 4, 7, 1, 8, 2, 9, 3, 6 }, { 5, 5, 5, 5, 5, 5, 5, 5 }, { 6, 3, 9, 2, 8, 1, 7, 4 }, { 7, 8, 4, 9, 1, 6, 2, 3 },
        { 8, 9, 7, 6, 4, 3, 1, 2 }, { 9, 6, 8, 3, 7, 2, 4, 1 },
        { 0, 0, 0, 0, 0, 0, 0, 0 }, { 1, 2, 4, 3, 7, 6, 8, 9 }, { 2, 3, 1, 6, 4, 9, 7, 8 }, { 3, 6, 2, 9, 1, 8, 4, 7 },
        { 4, 1, 7, 2, 8, 3, 9, 6 }, { 5, 5, 5, 5, 5, 5, 5, 5 }, { 6, 9, 3, 8, 2, 7, 1, 4 }, { 7, 4, 8, 1, 9, 2, 6, 3 },
        { 8, 7, 9, 4, 6, 1, 3, 2 }, { 9, 8, 6, 7, 3, 4, 2, 1 },
    };

    /// <summary>Angband dist_offsets: the grids at each distance 0 to 9, in its order (which settles ties).</summary>
    private static readonly Loc[][] DistOffsets =
    [
        [],
        [new(-1, -1), new(0, -1), new(1, -1), new(-1, 0), new(1, 0), new(-1, 1), new(0, 1), new(1, 1)],
        [new(-2, -1), new(2, -1), new(-1, -2), new(0, -2), new(1, -2), new(-2, 0), new(2, 0), new(-2, 1), new(2, 1), new(-1, 2), new(0, 2), new(1, 2)],
        [new(-3, -1), new(3, -1), new(-2, -2), new(2, -2), new(-1, -3), new(0, -3), new(1, -3), new(-3, 0), new(3, 0), new(-3, 1), new(3, 1), new(-2, 2), new(2, 2), new(-1, 3), new(0, 3), new(1, 3)],
        [new(-4, -1), new(4, -1), new(-3, -2), new(3, -2), new(-2, -3), new(-3, -3), new(2, -3), new(3, -3), new(-1, -4), new(0, -4), new(1, -4), new(-4, 0), new(4, 0), new(-4, 1), new(4, 1), new(-3, 2), new(3, 2), new(-2, 3), new(-3, 3), new(2, 3), new(3, 3), new(-1, 4), new(0, 4), new(1, 4)],
        [new(-5, -1), new(5, -1), new(-4, -2), new(4, -2), new(-4, -3), new(4, -3), new(-2, -4), new(-3, -4), new(2, -4), new(3, -4), new(-1, -5), new(0, -5), new(1, -5), new(-5, 0), new(5, 0), new(-5, 1), new(5, 1), new(-4, 2), new(4, 2), new(-4, 3), new(4, 3), new(-2, 4), new(-3, 4), new(2, 4), new(3, 4), new(-1, 5), new(0, 5), new(1, 5)],
        [new(-6, -1), new(6, -1), new(-5, -2), new(5, -2), new(-5, -3), new(5, -3), new(-4, -4), new(4, -4), new(-2, -5), new(-3, -5), new(2, -5), new(3, -5), new(-1, -6), new(0, -6), new(1, -6), new(-6, 0), new(6, 0), new(-6, 1), new(6, 1), new(-5, 2), new(5, 2), new(-5, 3), new(5, 3), new(-4, 4), new(4, 4), new(-2, 5), new(-3, 5), new(2, 5), new(3, 5), new(-1, 6), new(0, 6), new(1, 6)],
        [new(-7, -1), new(7, -1), new(-6, -2), new(6, -2), new(-6, -3), new(6, -3), new(-5, -4), new(5, -4), new(-4, -5), new(-5, -5), new(4, -5), new(5, -5), new(-2, -6), new(-3, -6), new(2, -6), new(3, -6), new(-1, -7), new(0, -7), new(1, -7), new(-7, 0), new(7, 0), new(-7, 1), new(7, 1), new(-6, 2), new(6, 2), new(-6, 3), new(6, 3), new(-5, 4), new(5, 4), new(-4, 5), new(-5, 5), new(4, 5), new(5, 5), new(-2, 6), new(-3, 6), new(2, 6), new(3, 6), new(-1, 7), new(0, 7), new(1, 7)],
        [new(-8, -1), new(8, -1), new(-7, -2), new(7, -2), new(-7, -3), new(7, -3), new(-6, -4), new(6, -4), new(-6, -5), new(6, -5), new(-4, -6), new(-5, -6), new(4, -6), new(5, -6), new(-2, -7), new(-3, -7), new(2, -7), new(3, -7), new(-1, -8), new(0, -8), new(1, -8), new(-8, 0), new(8, 0), new(-8, 1), new(8, 1), new(-7, 2), new(7, 2), new(-7, 3), new(7, 3), new(-6, 4), new(6, 4), new(-6, 5), new(6, 5), new(-4, 6), new(-5, 6), new(4, 6), new(5, 6), new(-2, 7), new(-3, 7), new(2, 7), new(3, 7), new(-1, 8), new(0, 8), new(1, 8)],
        [new(-9, -1), new(9, -1), new(-8, -2), new(8, -2), new(-8, -3), new(8, -3), new(-7, -4), new(7, -4), new(-7, -5), new(7, -5), new(-6, -6), new(6, -6), new(-4, -7), new(-5, -7), new(4, -7), new(5, -7), new(-2, -8), new(-3, -8), new(2, -8), new(3, -8), new(-1, -9), new(0, -9), new(1, -9), new(-9, 0), new(9, 0), new(-9, 1), new(9, 1), new(-8, 2), new(8, 2), new(-8, 3), new(8, 3), new(-7, 4), new(7, 4), new(-7, 5), new(7, 5), new(-6, 6), new(6, 6), new(-4, 7), new(-5, 7), new(4, 7), new(5, 7), new(-2, 8), new(-3, 8), new(2, 8), new(3, 8), new(-1, 9), new(0, 9), new(1, 9)],
    ];

    private enum Stagger { None, Confused, Innate }

    /// <summary>Angband CONF_ERRATIC_CHANCE and STUN_MISS_CHANCE.</summary>
    private const int ConfErraticChance = 30, StunMissChance = 10;

    // --- What the monster can sense (mon-move.c) ---------------------------------------------

    /// <summary>Angband monster_can_hear: the player's noise here is within its hearing (less a third of stealth).</summary>
    public bool CanHear(Monster monster)
    {
        var noise = Noise[monster.Position];
        return noise != NoiseMap.Unreached && monster.Race.Hearing - Player.Stealth / 3 > noise;
    }

    /// <summary>Angband monster_can_see_player: the monster's square is in view (a quarter of sight while covering tracks).</summary>
    public bool CanSee(Monster monster) =>
        !Player.IsDead && Level[monster.Position].Has(SquareFlags.View)
        && !(Player.Timed.Has("covertracks") && monster.Position.DistanceTo(Player.Position) > Data.Constants.MaxSight / 4);

    /// <summary>Angband monster_can_smell: the scent here is younger than its sense of smell reaches.</summary>
    public bool CanSmell(Monster monster)
    {
        var scent = Scent[monster.Position];
        return scent != 0 && monster.Race.Smell > scent;
    }

    /// <summary>Angband monster_hates_grid: lava, for a monster that isn't immune to fire.</summary>
    private bool HatesGrid(Monster monster, Loc p) =>
        Level.Has(p, TerrainFlags.Fiery) && !monster.Race.Has("IM_FIRE");

    private bool TakingTerrainDamage(Monster monster) => HatesGrid(monster, monster.Position);

    /// <summary>Angband monster_passes_walls: PASS_WALL or KILL_WALL.</summary>
    private static bool PassesWalls(Monster monster) =>
        monster.Race.Has(MonsterFlags.PassWall) || monster.Race.Has(MonsterFlags.KillWall);

    /// <summary>What a monster's experience is worth, for trampling and pushing (Angband compare_monsters).</summary>
    private static long Worth(Monster m) => (m.OriginalRace ?? m.Race).Experience;

    /// <summary>Angband monster_can_kill: the square is empty, or KILL_BODY lets it trample a lesser, non-unique monster there.</summary>
    private bool MonsterCanKill(Monster monster, Loc grid)
    {
        if (Level.Monsters.At(grid) is not { } other) return true;
        if (other.Race.IsUnique || other.OriginalRace?.IsUnique == true) return false;
        return monster.Race.Has(MonsterFlags.KillBody) && Worth(monster) > Worth(other);
    }

    /// <summary>Angband monster_can_move: the square is empty, or MOVE_BODY lets it push past a lesser monster there.</summary>
    private bool MonsterCanMove(Monster monster, Loc grid) =>
        Level.Monsters.At(grid) is not { } other || (monster.Race.Has(MonsterFlags.MoveBody) && Worth(monster) > Worth(other));

    /// <summary>Angband monster_near_permwall: permanent rock (or a 5% whim) between a wall-passer and an unseen player.</summary>
    private bool NearPermWall(Monster monster)
    {
        if (ProjectionPath.Projectable(Level, monster.Position, Player.Position, MaxRange)) return false;
        if (Rng.RandInt0(99) < 5) return true;
        foreach (var g in ProjectionPath.Compute(Level, monster.Position, Player.Position, Data.Constants.MaxSight, PathFlags.Rock))
        {
            if (Level.Has(g, TerrainFlags.Permanent)) return true;
            if (g == Player.Position) return false;
        }
        return false;
    }

    // --- The turn (mon-move.c process_monsters and monster_turn) ------------------------------

    /// <summary>
    /// Angband monster_check_active: a monster takes its turn only if it can reach the player
    /// straight through walls, is hurt, stands in view, hears or smells the player, or is burning.
    /// </summary>
    private bool CheckActive(Monster monster)
    {
        monster.Active = (monster.Position.DistanceTo(Player.Position) <= monster.Race.Hearing && PassesWalls(monster))
                         || monster.Hp < monster.MaxHp || Level[monster.Position].Has(SquareFlags.View)
                         || CanHear(monster) || CanSmell(monster) || TakingTerrainDamage(monster);
        return monster.Active;
    }

    /// <summary>
    /// Angband process_monster_timed: sleep (and nothing else), or its conditions wear off by a turn
    /// (fear by up to a tenth of its level); held (or commanded) it loses the turn, and stunned, one
    /// time in ten. True if the turn is lost.
    /// </summary>
    private bool ProcessMonsterTimed(Monster monster)
    {
        if (monster.Sleep > 0)
        {
            ReduceSleep(monster);
            return true;
        }
        if (Rng.OneIn(10) && monster.Active) monster.Aware = true;
        var name = Capitalize(MonsterName(monster));
        void Ended(string text)
        {
            if (monster.IsVisible) Publish(new MessageEvent($"{name} {text}"));
        }
        if (monster.Fast > 0 && --monster.Fast == 0) Ended("is no longer fast.");
        if (monster.Slow > 0 && --monster.Slow == 0) Ended("is no longer slow.");
        if (monster.Held > 0 && --monster.Held == 0) Ended("is no longer held.");
        if (monster.Disenchanted > 0 && --monster.Disenchanted == 0) Ended("seems magical again.");
        if (monster.Stun > 0 && --monster.Stun == 0) Ended("is no longer stunned.");
        if (monster.Confused > 0 && --monster.Confused == 0) Ended("is no longer confused.");
        MaybeRevertMonsterShape(monster);
        if (monster.Fear > 0)
        {
            monster.Fear = Math.Max(0, monster.Fear - Rng.RandInt1(monster.Race.Depth / 10 + 1));
            if (monster.Fear == 0) Ended("recovers its courage.");
        }
        if (monster.Held > 0 || (monster == Commanded && Player.Timed.Has("command"))) return true;
        return monster.Stun > 0 && Rng.OneIn(StunMissChance);
    }

    /// <summary>Angband monster_group_rouse: a monster aware of the player may wake the sleeping friends it can see.</summary>
    private void GroupRouse(Monster monster)
    {
        if (!monster.Aware) return;
        foreach (var friend in Level.Monsters.All.Where(f => f != monster && f.GroupId == monster.GroupId && f.Sleep > 0).ToList())
        {
            if (!ProjectionPath.Projectable(Level, monster.Position, friend.Position, MaxRange)) continue;
            if (Rng.OneIn(monster.Position.DistanceTo(friend.Position) * 20))
            {
                WakeMonster(friend);
                friend.Aware = Rng.RandInt0(100) < 50;
            }
        }
    }

    /// <summary>Angband group_monster_tracking: an active friend that is tracking the player.</summary>
    private Monster? GroupTracker(Monster monster) =>
        Level.Monsters.All.FirstOrDefault(t => t != monster && t.GroupId == monster.GroupId && t.Tracking && t.Active);

    /// <summary>
    /// Angband monster_turn after the ranged attack: a stagger (confusion, RAND_25/RAND_50) or a
    /// chosen direction; then up to five tries — the direction and those either side, in 4.2.5's
    /// order (a monster tracking by sound or scent out of view tries only the first) — at moving,
    /// opening or bashing a door, tunnelling, breaking a glyph, attacking the player, or pushing
    /// past another monster. A frightened monster that can't move freezes (held).
    /// </summary>
    private void MonsterMoveTurn(Monster monster)
    {
        var stagger = ShouldStagger(monster);
        var dir = 0;
        var tracking = false;
        if (stagger == Stagger.None && !GetMove(monster, out dir, out tracking)) return;

        var didSomething = false;
        for (var i = 0; i < 5 && !didSomething; i++)
        {
            var d = stagger != Stagger.None ? Ddd[Rng.RandInt0(8)] : SideDirs[dir, i];
            var grid = monster.Position + Keypad[d];
            if (i > 0 && stagger == Stagger.None && !Level[monster.Position].Has(SquareFlags.View) && tracking) break;
            if (!Level.InBounds(grid)) continue;
            if (!TurnCanMove(monster, grid, stagger == Stagger.Confused, ref didSomething)) continue;
            // A glyph may be tried more than once a turn: a failure doesn't end the tries.
            if (!PassesWarding(monster, grid)) continue;
            if (Level.Decoy == grid)
            {
                if (monster.IsVisible) LearnMonsterFlag(monster.Race, MonsterFlags.NeverBlow);
                if (monster.Race.Has(MonsterFlags.NeverBlow)) continue;
                AttackDecoy(monster, grid);
                didSomething = true;
                break;
            }
            if (grid == Player.Position)
            {
                if (monster.IsVisible) LearnMonsterFlag(monster.Race, MonsterFlags.NeverBlow);
                if (monster.Race.Has(MonsterFlags.NeverBlow)) continue;
                MonsterMelee(monster);
                didSomething = true;
                break;
            }
            if (monster.Race.Has(MonsterFlags.NeverMove))
            {
                if (monster.IsVisible) LearnMonsterFlag(monster.Race, MonsterFlags.NeverMove);
                return;
            }
            if (Level.Monsters.At(grid) is not null) didSomething = TryPush(monster, grid);
            else
            {
                Level.Monsters.Move(monster, grid);
                didSomething = true;
            }
        }
        if (!didSomething && monster.Fear > 0)
        {
            // Angband: cornered and afraid, it freezes.
            var amount = monster.Fear;
            monster.Fear = 0;
            if (!monster.Race.Has("NO_HOLD"))
            {
                monster.Held = Math.Min(50, Math.Max(monster.Held, amount));
                if (monster.IsVisible) Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} is held."));
            }
        }
        if (didSomething && monster.Camouflaged) Reveal(monster);
    }

    /// <summary>Angband monster_turn_should_stagger: a confused monster's chance grows with its confusion; RAND_25 and RAND_50 add theirs.</summary>
    private Stagger ShouldStagger(Monster monster)
    {
        var chance = 0;
        var level = Math.Min((monster.Confused + 9) / 10, 5); // monster_effect_level: 50 turns at most, in fifths
        for (; level > 0; level--) chance = 100 - (100 - chance) * (100 - ConfErraticChance) / 100;
        var confusedChance = chance;
        if (monster.Race.Has(MonsterFlags.Rand25))
        {
            chance += 25;
            if (monster.IsVisible) LearnMonsterFlag(monster.Race, MonsterFlags.Rand25);
        }
        if (monster.Race.Has(MonsterFlags.Rand50))
        {
            chance += 50;
            if (monster.IsVisible) LearnMonsterFlag(monster.Race, MonsterFlags.Rand50);
        }
        var roll = Rng.RandInt0(100);
        return roll < confusedChance ? Stagger.Confused : roll < chance ? Stagger.Innate : Stagger.None;
    }

    // --- Choosing where to go (get_move and its helpers) --------------------------------------

    /// <summary>
    /// Angband get_move: towards what it senses (sight, sound, then scent), a tracking friend, the
    /// last place it tracked to, or at random; out of burning ground; a pack hides from a player in
    /// a corridor and surrounds one in the open; a monster that has lost its nerve flees. False if
    /// it stays put.
    /// </summary>
    private bool GetMove(Monster monster, out int dir, out bool tracking)
    {
        dir = 0;
        var target = DecoyFor(monster) ?? Player.Position;
        var groupAi = monster.Race.Has(MonsterFlags.GroupAi);
        var (minRange, _) = CombatRange(monster);
        var offset = new Loc(0, 0);
        var done = false;

        if (GetMoveAdvance(monster, out tracking))
        {
            offset = monster.MoveTarget - monster.Position;
            monster.Tracking = true;
        }
        else if (GroupTracker(monster) is { } tracker && ProjectionPath.Projectable(Level, monster.Position, tracker.Position, MaxRange))
        {
            offset = tracker.Position - monster.Position;
            monster.Tracking = false;
        }
        else
        {
            if (monster.Tracking) offset = monster.MoveTarget - monster.Position;
            if (offset == new Loc(0, 0))
            {
                offset = GetMoveRandom(monster);
                monster.Tracking = false;
            }
        }

        if (TakingTerrainDamage(monster) && FindSafety(monster))
        {
            Flee(monster);
            offset = monster.MoveTarget - monster.Position;
            done = true;
        }

        if (!done && groupAi && !PassesWalls(monster))
        {
            var open = Ddd.Count(d => Level.InBounds(target + Keypad[d])
                                      && (Level.IsPassable(target + Keypad[d]) || Level[target + Keypad[d]].Has(SquareFlags.Room)));
            if (open < 5 && Player.Hp > Player.MaxHp / 2 && FindHiding(monster))
            {
                done = true;
                offset = monster.MoveTarget - monster.Position;
                monster.Tracking = false;
            }
        }

        if (!done && minRange == FleeRange)
        {
            if (FindSafety(monster))
            {
                Flee(monster);
                offset = monster.MoveTarget - monster.Position;
            }
            else offset = new Loc(-offset.X, -offset.Y);
            monster.Tracking = false;
            done = true;
        }

        if (!done && groupAi && Level[monster.Position].Has(SquareFlags.View))
        {
            // Surround the player: a free square beside it, from a random start.
            var grid = monster.MoveTarget;
            if (monster.Position.DistanceTo(Player.Position) > 1)
            {
                var start = Rng.RandInt0(8);
                for (var i = 0; i < 8; i++)
                {
                    grid = target + Keypad[Ddd[(start + i) % 8]];
                    if (IsEmptyForMonster(grid)) break;
                }
            }
            offset = grid - monster.Position;
        }

        if (offset == new Loc(0, 0)) return false;
        dir = ChooseDirection(offset);
        return true;
    }

    /// <summary>Angband square_isempty: open floor with no monster, player or object.</summary>
    private bool IsEmptyForMonster(Loc g) =>
        Level.InBounds(g) && Level.IsEmptyFloor(g) && g != Player.Position && !Level.Objects.Any(g);

    /// <summary>
    /// Angband get_move_advance: a bodyguard back to its leader; straight at a player it can pass
    /// walls to or see; else the neighbouring square where the noise is loudest (or as loud), else
    /// where the scent is freshest. False if it senses nothing.
    /// </summary>
    private bool GetMoveAdvance(Monster monster, out bool track)
    {
        track = false;
        var target = DecoyFor(monster) ?? Player.Position;
        var baseHearing = monster.Race.Hearing - Player.Stealth / 3;
        var noiseHere = Noise[monster.Position];
        var currentNoise = baseHearing - noiseHere;
        if (LeaderOf(monster) is not null && GetMoveBodyguard(monster)) return true;
        if (PassesWalls(monster) && !NearPermWall(monster))
        {
            monster.MoveTarget = target;
            return true;
        }
        if (CanSee(monster))
        {
            monster.MoveTarget = target;
            return true;
        }
        Loc? best = null, backup = null;
        if (CanHear(monster))
        {
            foreach (var d in Ddd)
            {
                var grid = monster.Position + Keypad[d];
                if (!Level.InBounds(grid)) continue;
                var noise = Noise[grid];
                if (noise == NoiseMap.Unreached) continue;
                if (!MonsterCanKill(monster, grid) && !MonsterCanMove(monster, grid)) continue;
                if (HatesGrid(monster, grid)) continue;
                var heard = baseHearing - noise;
                if (heard > currentNoise)
                {
                    best = grid;
                    break;
                }
                if (heard == currentNoise) backup = grid;
            }
        }
        if (best is null && CanSmell(monster))
        {
            var bestScent = 0;
            foreach (var d in Ddd)
            {
                var grid = monster.Position + Keypad[d];
                if (!Level.InBounds(grid)) continue;
                var scent = Scent[grid];
                var smelled = monster.Race.Smell - scent;
                if (smelled > bestScent && scent != 0)
                {
                    bestScent = smelled;
                    best = grid;
                }
            }
        }
        if ((best ?? backup) is { } go)
        {
            monster.MoveTarget = go;
            track = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Angband get_move_bodyguard: more than a step from its leader (and the leader in sight or
    /// within 10), a bodyguard steps nearer it — by a square nearer the player too, if there is one.
    /// </summary>
    private bool GetMoveBodyguard(Monster monster)
    {
        if (LeaderOf(monster) is not { } leader) return false;
        var dist = monster.Position.DistanceTo(leader.Position);
        if (dist <= 1) return false;
        if (!ProjectionPath.Projectable(Level, monster.Position, leader.Position, MaxRange) && dist > 10) return false;
        var playerDist = monster.Position.DistanceTo(Player.Position);
        Loc? best = null;
        for (var i = 0; i < 8; i++)
        {
            var grid = monster.Position + Keypad[Ddd[i]];
            if (!Level.InBounds(grid)) continue;
            if (!MonsterCanKill(monster, grid) && !MonsterCanMove(monster, grid)) continue;
            if (HatesGrid(monster, grid)) continue;
            if (grid.DistanceTo(leader.Position) < dist)
            {
                best = grid;
                if (grid.DistanceTo(Player.Position) < playerDist) break;
            }
        }
        if (best is not { } go) return false;
        monster.MoveTarget = go;
        return true;
    }

    /// <summary>Angband get_move_random: a random walkable, bearable neighbour (none if there is none).</summary>
    private Loc GetMoveRandom(Monster monster)
    {
        int[] attempts = [0, 1, 2, 3, 4, 5, 6, 7];
        var left = 8;
        while (left > 0)
        {
            var i = Rng.RandInt0(left);
            var step = Keypad[Ddd[attempts[i]]];
            var grid = monster.Position + step;
            if (Level.InBounds(grid) && Level.IsPassable(grid) && Level.Monsters.At(grid) is null && !HatesGrid(monster, grid))
                return step;
            left--;
            (attempts[i], attempts[left]) = (attempts[left], attempts[i]);
        }
        return new Loc(0, 0);
    }

    /// <summary>
    /// Angband get_move_find_safety: the nearest ring (to 9) holding a passable, bearable square
    /// out of the player's view and not much louder than here; of those, the one furthest from the player.
    /// </summary>
    private bool FindSafety(Monster monster)
    {
        var noiseHere = Noise[monster.Position];
        for (var d = 1; d < 10; d++)
        {
            Loc? best = null;
            var gdis = 0;
            foreach (var off in DistOffsets[d])
            {
                var grid = monster.Position + off;
                if (!Level.InBoundsFully(grid) || !Level.IsPassable(grid)) continue;
                var noise = Noise[grid];
                if (noise > noiseHere + 2 * d) continue;
                if (HatesGrid(monster, grid)) continue;
                if (Level[grid].Has(SquareFlags.View)) continue;
                var dis = grid.DistanceTo(Player.Position);
                if (dis > gdis)
                {
                    best = grid;
                    gdis = dis;
                }
            }
            if (gdis > 0 && best is { } go)
            {
                monster.MoveTarget = go;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Angband get_move_find_hiding: the nearest ring holding an empty square out of view that the
    /// monster could shoot to, at least three quarters its distance from the player (plus 2); of
    /// those, the one nearest the player.
    /// </summary>
    private bool FindHiding(Monster monster)
    {
        var min = Player.Position.DistanceTo(monster.Position) * 3 / 4 + 2;
        for (var d = 1; d < 10; d++)
        {
            Loc? best = null;
            var gdis = 999;
            foreach (var off in DistOffsets[d])
            {
                var grid = monster.Position + off;
                if (!Level.InBoundsFully(grid) || !IsEmptyForMonster(grid)) continue;
                if (Level[grid].Has(SquareFlags.View)) continue;
                if (!ProjectionPath.Projectable(Level, monster.Position, grid, MaxRange, PathFlags.StopAtCreature,
                        p => Level.Monsters.At(p) is not null)) continue;
                var dis = grid.DistanceTo(Player.Position);
                if (dis < gdis && dis >= min)
                {
                    best = grid;
                    gdis = dis;
                }
            }
            if (gdis < 999 && best is { } go)
            {
                monster.MoveTarget = go;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Angband get_move_flee: unless burning, only when nearer than it likes and sensing the player;
    /// then the neighbour best balancing nearness to its safe square against distance from the noise.
    /// </summary>
    private void Flee(Monster monster)
    {
        if (!TakingTerrainDamage(monster))
        {
            if (monster.Position.DistanceTo(Player.Position) >= CombatRange(monster).Best) return;
            if (!CanHear(monster) && !CanSmell(monster)) return;
        }
        Loc best = new(0, 0);
        var bestScore = -1;
        for (var i = 7; i >= 0; i--)
        {
            var grid = monster.Position + Keypad[Ddd[i]];
            if (!Level.InBounds(grid)) continue;
            var dis = grid.DistanceTo(monster.MoveTarget);
            var noise = Noise[grid];
            var score = Math.Max(0, 5000 / (dis + 3) - 500 / (noise + 1));
            if (score < bestScore) continue;
            bestScore = score;
            best = grid;
        }
        monster.MoveTarget = best;
    }

    /// <summary>
    /// Angband get_move_choose_direction: the keypad direction nearest the offset, biased (+10: the
    /// other way round in side_dirs) to break even ties by the game turn.
    /// </summary>
    private int ChooseDirection(Loc offset)
    {
        int dx = offset.X, dy = offset.Y, ay = Math.Abs(dy), ax = Math.Abs(dx);
        var even = GameTurn % 2 == 0;
        int dir;
        if (ay > ax * 2)
        {
            if (dy > 0) { dir = 2; if (dx > 0 || (dx == 0 && even)) dir += 10; }
            else { dir = 8; if (dx < 0 || (dx == 0 && even)) dir += 10; }
        }
        else if (ax > ay * 2)
        {
            if (dx > 0) { dir = 6; if (dy < 0 || (dy == 0 && even)) dir += 10; }
            else { dir = 4; if (dy > 0 || (dy == 0 && even)) dir += 10; }
        }
        else if (dy > 0)
        {
            if (dx > 0) { dir = 3; if (ay < ax || (ay == ax && even)) dir += 10; }
            else { dir = 1; if (ay > ax || (ay == ax && even)) dir += 10; }
        }
        else
        {
            if (dx > 0) { dir = 9; if (ay > ax || (ay == ax && even)) dir += 10; }
            else { dir = 7; if (ay < ax || (ay == ax && even)) dir += 10; }
        }
        return dir;
    }

    // --- Moving (monster_turn_can_move, monster_turn_try_push) --------------------------------

    /// <summary>
    /// Angband monster_turn_can_move: whether the monster may step into the grid — the player's or
    /// open ground; through rock with PASS_WALL, or tearing it down with SMASH_WALL or KILL_WALL; a
    /// closed door opened or bashed (a locked one worked at, which uses the turn); a confused
    /// monster stumbles against what it can't pass.
    /// </summary>
    private bool TurnCanMove(Monster monster, Loc grid, bool confused, ref bool didSomething)
    {
        var race = monster.Race;
        var name = Capitalize(MonsterName(monster));
        if (grid == Player.Position || Level.Decoy == grid) return true;
        if (!confused && HatesGrid(monster, grid)) return false;
        var feature = Level.FeatureAt(grid);
        if (feature.Has(TerrainFlags.Passable)) return true;
        void Stumble(ref bool did)
        {
            did = true;
            if (monster.IsVisible && Level[monster.Position].Has(SquareFlags.View))
                Publish(new MessageEvent($"{name} stumbles."));
            if (monster.Stun < 5 && Rng.OneIn(3)) MonIncTimed(monster, MonsterCondition.Stun, 3);
        }
        if (feature.Has(TerrainFlags.Permanent))
        {
            if (confused) Stumble(ref didSomething);
            return false;
        }
        if (monster.IsVisible)
        {
            LearnMonsterResponse(monster, MonsterFlags.PassWall);
            LearnMonsterResponse(monster, MonsterFlags.KillWall);
            LearnMonsterResponse(monster, "SMASH_WALL");
        }
        if (race.Has(MonsterFlags.PassWall)) return true;
        if (race.Has("SMASH_WALL") || race.Has(MonsterFlags.KillWall))
        {
            if (race.Has("SMASH_WALL")) SmashWall(grid);
            else DestroyWall(grid);
            return true;
        }
        if (feature.Has(TerrainFlags.DoorClosed) || feature.Has(TerrainFlags.Secret))
        {
            var canOpen = race.Has(MonsterFlags.OpenDoor) && !confused;
            var canBash = race.Has(MonsterFlags.BashDoor) && (!confused || Rng.OneIn(3));
            var willBash = false;
            if (canOpen || canBash) didSomething = true;
            if (!confused && monster.IsVisible)
            {
                LearnMonsterResponse(monster, MonsterFlags.OpenDoor);
                LearnMonsterResponse(monster, MonsterFlags.BashDoor);
            }
            if (canOpen) willBash = canBash && Rng.OneIn(2);
            else if (canBash) willBash = true;
            else
            {
                if (confused) Stumble(ref didSomething);
                return false;
            }
            ref var sq = ref Level[grid];
            if (sq.LockPower > 0)
            {
                var k = sq.LockPower;
                if (Rng.RandInt0(monster.Hp / 10) > k)
                {
                    Publish(new MessageEvent(willBash ? $"{name} slams against the door." : $"{name} fiddles with the lock."));
                    sq.LockPower = (byte)(k - 1);
                }
                if (confused && monster.Stun < 5 && Rng.OneIn(3)) MonIncTimed(monster, MonsterCondition.Stun, 3);
            }
            else if (willBash)
            {
                sq.Feature = Data.Terrain.Ids.BrokenDoor;
                Publish(new MessageEvent("You hear a door burst open!"));
                Disturb();
                if (confused && monster.Stun < 5 && Rng.OneIn(3)) MonIncTimed(monster, MonsterCondition.Stun, 3);
                UpdateView();
                return true;
            }
            else
            {
                sq.Feature = Data.Terrain.Ids.OpenDoor;
                UpdateView();
            }
            return false;
        }
        if (confused) Stumble(ref didSomething);
        return false;
    }

    /// <summary>Angband square_destroy_wall: the rock (or door, or rubble) becomes floor.</summary>
    private void DestroyWall(Loc grid)
    {
        ref var sq = ref Level[grid];
        sq.Feature = Data.Terrain.Ids.Floor;
        sq.Flags &= ~SquareFlags.AnyWallMarker;
        sq.LockPower = 0;
        if (Level[grid].Has(SquareFlags.View)) Publish(new MessageEvent("You hear a grinding noise."));
        UpdateView();
    }

    /// <summary>Angband square_smash_wall: the grid and the non-permanent rock around it become floor.</summary>
    private void SmashWall(Loc grid)
    {
        DestroyWall(grid);
        foreach (var n in Level.Neighbors(grid))
            if (Level.InBoundsFully(n) && !Level.IsPassable(n) && !Level.Has(n, TerrainFlags.Permanent)
                && !Level.Has(n, TerrainFlags.DoorAny))
                DestroyWall(n);
    }

    /// <summary>Angband monster_turn_try_push: trample (KILL_BODY) or push past (MOVE_BODY) a lesser monster.</summary>
    private bool TryPush(Monster monster, Loc grid)
    {
        if (Level.Monsters.At(grid) is not { } other) return false;
        var kill = MonsterCanKill(monster, grid);
        var move = MonsterCanMove(monster, grid) && Level.IsPassable(monster.Position);
        if (!kill && !move) return false;
        if (monster.IsVisible)
        {
            LearnMonsterResponse(monster, MonsterFlags.KillBody);
            LearnMonsterResponse(monster, MonsterFlags.MoveBody);
        }
        if (other.Camouflaged) Reveal(other);
        if (monster.IsVisible && Level[monster.Position].Has(SquareFlags.View))
            Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} {(kill ? "tramples over" : "pushes past")} {MonsterName(other)}."));
        if (kill)
        {
            Level.Monsters.Remove(other);
            Level.Monsters.Move(monster, grid);
        }
        else Level.Monsters.Swap(monster, other);
        return true;
    }
}
