using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Time;
using Angband.Core.World;

namespace Angband.Core.Game;

// AVABand's hireling in a fight (see GameSession.Hireling.cs): their turn — fall back when badly hurt,
// go for a foe (the one you named, or the nearest awake one they can see near you), swing or shoot,
// otherwise keep close — and the level's monsters going for them: one beside them that isn't beside
// you, or that they have hurt, strikes at them. Your bolts, beams, arrows and throws pass them by, and
// your spells that pick out monsters leave them be; only blasts (balls, breaths, arcs, earthquakes)
// catch them. Monsters' spells and breath are aimed at you alone; only their blows reach a hireling.
public sealed partial class GameSession
{
    /// <summary>How close the hireling keeps to you, following (they step nearer when further off).</summary>
    public const int HirelingFollowDistance = 2;

    /// <summary>How far from you a foe may be for the hireling to go for it unbidden (they don't run off).</summary>
    public const int HirelingLeash = 7;

    /// <summary>How far off a warrior sees a foe worth going for.</summary>
    public const int HirelingSight = 6;

    /// <summary>Hit points they regain every 100 game turns: a fiftieth of the most they have (at least 1).</summary>
    public const int HirelingRegenDivisor = 50;

    // --- Their turn ------------------------------------------------------------------------------------

    /// <summary>
    /// The hireling's turn: they lose heart below a quarter of their hit points (and take it again at
    /// half), keeping away from foes; otherwise they swing at a foe beside them, shoot one in a clear
    /// line (archers), step toward one (warriors, unless holding), or come back to your side.
    /// </summary>
    private int HirelingTurn(Monster body)
    {
        if (Player.IsDead || !body.IsActive || Hireling is not { } h || h.Body != body) return EnergyTable.MoveEnergy;
        var name = h.Name;
        if (!h.Fleeing && body.Hp < body.MaxHp / 4)
        {
            h.Fleeing = true;
            if (body.IsVisible) Publish(new MessageEvent($"{name} falls back, badly hurt."));
        }
        else if (h.Fleeing && body.Hp >= body.MaxHp / 2)
        {
            h.Fleeing = false;
            if (body.IsVisible) Publish(new MessageEvent($"{name} is ready to fight again."));
        }
        if (h.Fleeing)
        {
            if (!FleeFromFoes(body)) FollowPlayer(body);
            return EnergyTable.MoveEnergy;
        }

        if (HirelingFoe(body, h) is { } foe)
        {
            if (body.Position.ChebyshevTo(foe.Position) <= 1)
            {
                HirelingMelee(body, foe);
                return EnergyTable.MoveEnergy;
            }
            if (h.Role == HirelingRole.Archer && ClearShot(body, foe, Hireling.RangeFor(h.Level)))
            {
                HirelingShoot(body, foe);
                return EnergyTable.MoveEnergy;
            }
            if (h.Order != HirelingOrder.Hold && (h.Role == HirelingRole.Warrior || h.Order == HirelingOrder.Attack)
                && StepHireling(body, foe.Position))
                return EnergyTable.MoveEnergy;
        }
        if (h.Order != HirelingOrder.Hold) FollowPlayer(body);
        return EnergyTable.MoveEnergy;
    }

    /// <summary>
    /// A monster the hireling would fight: no friend of theirs, there to be seen — not hidden, and not
    /// invisible unless it is right beside them (they feel its blows, and strike back).
    /// </summary>
    private static bool IsFoeOfHireling(Monster m, Monster body) =>
        !m.IsAlly && m.IsActive && !m.Camouflaged && (!m.Race.Has(MonsterFlags.Invisible) || m.Position.ChebyshevTo(body.Position) <= 1);

    /// <summary>
    /// The foe the hireling goes for: the one you told them to (while it lives); else the nearest awake
    /// one they can see — within their sight (a warrior's, or an archer's range) and, following, near
    /// you; holding, a warrior fights only what is beside them.
    /// </summary>
    private Monster? HirelingFoe(Monster body, Hireling h)
    {
        if (h.Order == HirelingOrder.Attack)
        {
            if (Level.Monsters[h.AttackTarget] is { } named && IsFoeOfHireling(named, body)) return named;
            h.Order = HirelingOrder.Follow;
            h.AttackTarget = 0;
        }
        var sight = h.Role == HirelingRole.Archer ? Hireling.RangeFor(h.Level) : HirelingSight;
        if (h.Order == HirelingOrder.Hold && h.Role == HirelingRole.Warrior) sight = 1;
        Monster? best = null;
        var bestDistance = int.MaxValue;
        foreach (var m in Level.Monsters.All)
        {
            if (!IsFoeOfHireling(m, body) || m.IsAsleep || IsPleading(m)) continue; // (a pleading ghost only if you say so)
            var d = body.Position.DistanceTo(m.Position);
            if (d > sight || d > bestDistance || (d == bestDistance && best is not null && m.Id > best.Id)) continue;
            if (h.Order != HirelingOrder.Hold && m.Position.DistanceTo(Player.Position) > HirelingLeash) continue;
            if (!ProjectionPath.Projectable(Level, body.Position, m.Position, sight + 1)) continue;
            best = m;
            bestDistance = d;
        }
        return best;
    }

    /// <summary>Whether an arrow from the hireling would reach the foe first (not you, nor another monster in the way).</summary>
    private bool ClearShot(Monster body, Monster foe, int range)
    {
        if (body.Position.DistanceTo(foe.Position) > range) return false;
        var path = ProjectionPath.Compute(Level, body.Position, foe.Position, range, PathFlags.StopAtCreature,
            p => p == Player.Position || Level.Monsters.At(p) is not null);
        return path.Count > 0 && path[^1] == foe.Position;
    }

    /// <summary>Their blows: each tests their skill against the foe's armour; what lands is added up.</summary>
    private void HirelingMelee(Monster body, Monster foe)
    {
        var h = Hireling!;
        var (dice, bonus) = Hireling.BlowFor(h.Role, h.Level);
        var skill = Hireling.MeleeSkillFor(h.Role, h.Level);
        int damage = 0, hits = 0;
        for (var i = 0; i < Hireling.BlowsFor(h.Role, h.Level); i++)
        {
            if (!CombatMath.TestHit(Rng, skill, foe.Race.Armour, visible: true)) continue;
            damage += Math.Max(0, dice.Roll(Rng) + bonus);
            hits++;
        }
        HirelingStrikes(body, foe, hits > 0, damage, hits > 0 ? "hits" : "misses", pain: false);
    }

    /// <summary>An archer's shot: the arrow flies (and is shown), tests their skill less the range, and hits for its dice times the bow.</summary>
    private void HirelingShoot(Monster body, Monster foe)
    {
        var h = Hireling!;
        var range = Hireling.RangeFor(h.Level);
        var path = ProjectionPath.Compute(Level, body.Position, foe.Position, range, PathFlags.StopAtCreature,
            p => p == Player.Position || Level.Monsters.At(p) is not null);
        if (body.IsVisible || foe.IsVisible) ShowProjection(body.Position, path, null, null, ProjectionKind.Missile);
        var chance = CombatMath.MissileChance(Hireling.BowSkillFor(h.Level), 0, body.Position.DistanceTo(foe.Position));
        var hit = CombatMath.TestHit(Rng, chance, foe.Race.Armour, visible: true);
        var (dice, bonus, multiplier) = Hireling.ArrowFor(h.Level);
        var damage = hit ? (dice.Roll(Rng) + bonus) * multiplier : 0;
        HirelingStrikes(body, foe, hit, damage, hit ? "'s arrow hits" : "'s arrow misses", pain: true);
    }

    /// <summary>
    /// The hireling's blow or arrow lands (or not): said if either is in sight, and the foe, awake now,
    /// bears them a grudge. The damage is dealt as yours is, but the kill is theirs (half its worth to you).
    /// </summary>
    private void HirelingStrikes(Monster body, Monster foe, bool hit, int damage, string verb, bool pain)
    {
        var h = Hireling!;
        var seen = body.IsVisible || foe.IsVisible;
        var foeName = MonsterName(foe);
        if (seen) Publish(new MessageEvent($"{h.Name}{(verb.StartsWith('\'') ? "" : " ")}{verb} {foeName}{(hit ? DamageNote(damage) : "")}."));
        foe.HatesAlly = true;
        if (!hit)
        {
            WakeMonster(foe);
            return;
        }
        DamageMonster(foe, damage, pain: pain && seen, deathNote: seen ? $"{h.Name} has slain {foeName}." : "", byHireling: true);
    }

    // --- Moving ---------------------------------------------------------------------------------------

    /// <summary>Whether the hireling may step into a square: open ground (or a door to open), no one there, not burning, not a shop.</summary>
    private bool HirelingCanEnter(Loc p) =>
        Level.InBoundsFully(p) && p != Player.Position && Level[p].Monster == 0 && !Level.Has(p, TerrainFlags.Fiery)
        && Level.FeatureAt(p).Shop is null && Level.FeatureAt(p).Id != "trapped_apprentice"
        && (Level.IsPassable(p) || Level.Has(p, TerrainFlags.DoorClosed));

    /// <summary>Moves (or opens a door) into the square; true if the turn was spent.</summary>
    private bool MoveHirelingTo(Monster body, Loc p)
    {
        if (Level.Has(p, TerrainFlags.DoorClosed))
        {
            Level[p].Feature = Data.Terrain.Ids.OpenDoor;
            Level[p].LockPower = 0;
            UpdateView();
            return true;
        }
        Level.Monsters.Move(body, p);
        return true;
    }

    /// <summary>A step that brings the hireling nearer the goal (the nearest such in Angband's direction order); false if none does.</summary>
    private bool StepHireling(Monster body, Loc goal)
    {
        var here = body.Position.DistanceTo(goal);
        Loc? best = null;
        var bestDistance = here;
        foreach (var d in Ddd)
        {
            var p = body.Position + Keypad[d];
            if (!HirelingCanEnter(p)) continue;
            var dist = p.DistanceTo(goal);
            if (dist < bestDistance)
            {
                best = p;
                bestDistance = dist;
            }
        }
        return best is { } to && MoveHirelingTo(body, to);
    }

    /// <summary>
    /// Back toward you, more than <see cref="HirelingFollowDistance"/> away: down your noise (the way
    /// round walls a hunting monster would take), or straight at you while there is none.
    /// </summary>
    private void FollowPlayer(Monster body)
    {
        if (body.Position.ChebyshevTo(Player.Position) <= HirelingFollowDistance) return;
        var here = Noise[body.Position];
        if (here != NoiseMap.Unreached)
        {
            Loc? best = null;
            var bestNoise = here;
            foreach (var d in Ddd)
            {
                var p = body.Position + Keypad[d];
                if (!HirelingCanEnter(p)) continue;
                var noise = Noise[p];
                if (noise != NoiseMap.Unreached && noise < bestNoise)
                {
                    best = p;
                    bestNoise = noise;
                }
            }
            if (best is { } to)
            {
                MoveHirelingTo(body, to);
                return;
            }
        }
        StepHireling(body, Player.Position);
    }

    /// <summary>Badly hurt: a step away from the nearest foe they can see (toward you, if it's as good); false with none about.</summary>
    private bool FleeFromFoes(Monster body)
    {
        var foes = Level.Monsters.All.Where(m => IsFoeOfHireling(m, body) && !m.IsAsleep && !IsPleading(m) && m.Position.DistanceTo(body.Position) <= HirelingSight
                                                 && ProjectionPath.Projectable(Level, body.Position, m.Position, HirelingSight + 1)).ToList();
        if (foes.Count == 0) return false;
        int Safety(Loc p) => foes.Min(m => m.Position.DistanceTo(p));
        Loc? best = null;
        var (bestSafety, bestNearYou) = (Safety(body.Position), body.Position.DistanceTo(Player.Position));
        foreach (var d in Ddd)
        {
            var p = body.Position + Keypad[d];
            if (!HirelingCanEnter(p) || !Level.IsPassable(p)) continue;
            var (safety, nearYou) = (Safety(p), p.DistanceTo(Player.Position));
            if (safety > bestSafety || (safety == bestSafety && nearYou < bestNearYou))
            {
                best = p;
                (bestSafety, bestNearYou) = (safety, nearYou);
            }
        }
        if (best is { } to) MoveHirelingTo(body, to);
        return true;
    }

    // --- Monsters against the hireling -----------------------------------------------------------------

    /// <summary>
    /// A monster with a grudge against the hireling goes for them while they are nearer than you and
    /// in its sight (otherwise, as ever, it goes for you).
    /// </summary>
    private Loc? HirelingTargetFor(Monster monster)
    {
        if (!monster.HatesAlly || HirelingHere is not { } ally || monster.IsAlly) return null;
        var d = monster.Position.DistanceTo(ally.Position);
        if (d >= monster.Position.DistanceTo(Player.Position) || d > Data.Constants.MaxSight) return null;
        return ProjectionPath.Projectable(Level, monster.Position, ally.Position, Data.Constants.MaxSight) ? ally.Position : null;
    }

    /// <summary>
    /// Before it moves: a monster beside the hireling strikes at them if it isn't beside you, or bears
    /// them a grudge (an afraid one, or one that never strikes, doesn't). True if it did.
    /// </summary>
    private bool MonsterFightsHireling(Monster monster)
    {
        if (monster.IsAlly || monster.Fear > 0 || monster.Race.Has(MonsterFlags.NeverBlow) || HirelingHere is not { } ally) return false;
        if (monster.Position.ChebyshevTo(ally.Position) > 1) return false;
        if (!monster.HatesAlly && monster.Position.ChebyshevTo(Player.Position) <= 1) return false;
        MonsterAttacksHireling(monster, ally);
        return true;
    }

    /// <summary>
    /// A monster's blows at the hireling (Angband's to-hit, effect power plus three times its level,
    /// against their armour); what lands is added up, without the blows' side effects.
    /// </summary>
    private void MonsterAttacksHireling(Monster monster, Monster ally)
    {
        var race = monster.Race;
        var rlev = Math.Max(race.Depth, 1);
        int damage = 0, hits = 0;
        foreach (var blow in race.Blows)
        {
            if (Data.BlowEffect(blow.Effect) is not { } effect || Data.BlowMethod(blow.Method) is null) continue;
            var toHit = effect.Power + rlev * 3;
            if (monster.Stun > 0) toHit = toHit * (100 - MonsterStunReduction) / 100;
            if (effect.Id != "none" && !CombatMath.TestHit(Rng, toHit, ally.Race.Armour, visible: true)) continue;
            damage += blow.Damage.Roll(Rng);
            hits++;
        }
        // (A miss is said only when you can see who missed.)
        if (hits > 0 ? monster.IsVisible || ally.IsVisible : monster.IsVisible)
            Publish(new MessageEvent(hits == 0 ? $"{Capitalize(MonsterName(monster))} misses {Hireling!.Name}."
                : $"{Capitalize(MonsterName(monster))} hits {Hireling!.Name}{DamageNote(damage)}."));
        if (hits > 0) HurtHireling(damage, race.IsUnique ? race.Name : Article(race.Name));
    }

    /// <summary>The hireling is hurt (by a monster, a trap, or a blast of yours); you are disturbed. At below zero, they fall.</summary>
    private void HurtHireling(int damage, string by)
    {
        if (HirelingHere is not { } ally || damage <= 0) return;
        Publish(new MonsterDamagedEvent(ally.Id, ally.Position, damage, Ally: true));
        ally.Hp -= damage;
        Disturb();
        if (ally.Hp < 0) HirelingDies(by);
    }

    /// <summary>Every 100 game turns, the hireling heals a fiftieth of their hit points (at least one).</summary>
    private void HirelingUpkeep(long gameTurn)
    {
        if (gameTurn % 100 != 0 || HirelingHere is not { } ally || ally.Hp >= ally.MaxHp) return;
        ally.Hp = Math.Min(ally.MaxHp, ally.Hp + Math.Max(1, ally.MaxHp / HirelingRegenDivisor));
    }
}
