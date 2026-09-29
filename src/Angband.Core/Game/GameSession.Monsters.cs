using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Time;

namespace Angband.Core.Game;

/// <summary>
/// Monster AI (Angband mon-move.c, simplified). Each turn a monster: wakes gradually, may breed,
/// may cast a spell, then moves. Movement priorities: erratic/confused → random; afraid → flee
/// toward safety (fighting back if cornered); pack animals lurk out of sight while the player is
/// in a corridor; otherwise hunt by sight, sound (the player's noise flow) or scent, and when the
/// player cannot be sensed at all, wander.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>The player's scent trail (followed by monsters with a sense of smell).</summary>
    public ScentMap Scent { get; } = new();

    /// <summary>One monster's turn, on demand (for tests).</summary>
    internal int RunMonsterTurn(Monster monster) => MonsterTurn(monster);

    private int MonsterTurn(Monster monster)
    {
        if (Player.IsDead || !monster.IsActive) return EnergyTable.MoveEnergy;
        if (monster == Commanded && Player.Timed.Has("command")) return EnergyTable.MoveEnergy; // it waits on the player's word

        if (monster.IsAsleep)
        {
            ReduceSleep(monster);
            return EnergyTable.MoveEnergy;
        }
        if (monster.Held > 0) return EnergyTable.MoveEnergy;
        if (StuckInWeb(monster)) return EnergyTable.MoveEnergy;
        if (monster.Camouflaged) return LurkingTurn(monster);
        MaybeRevertMonsterShape(monster);
        if (monster.Stun > 0 && Rng.OneIn(10)) return EnergyTable.MoveEnergy; // dazed

        var race = monster.Race;
        if (race.Has(MonsterFlags.Multiply) && TryMultiply(monster)) return EnergyTable.MoveEnergy;
        if (monster.IsVisible) Lore.For(race.Id).TurnsWatched++;
        if (RangedAttackKind(monster) is { } innate && TryCastSpell(monster, innate)) return EnergyTable.MoveEnergy;

        var adjacent = monster.Position.ChebyshevTo(Player.Position) == 1;
        if (race.Has(MonsterFlags.NeverMove))
        {
            if (adjacent && !monster.IsAfraid && !race.Has(MonsterFlags.NeverBlow)) MonsterMelee(monster);
            return EnergyTable.MoveEnergy;
        }

        foreach (var dir in ChooseMove(monster))
        {
            var target = monster.Position.Step(dir);
            if (AttackDecoy(monster, target)) return EnergyTable.MoveEnergy;
            if (target == Player.Position)
            {
                if (monster.IsAfraid || race.Has(MonsterFlags.NeverBlow)) continue;
                if (!PassesWarding(monster, target)) return EnergyTable.MoveEnergy; // held back by a glyph
                MonsterMelee(monster);
                return EnergyTable.MoveEnergy;
            }
            if (TryMonsterStep(monster, target)) return EnergyTable.MoveEnergy;
        }
        return EnergyTable.MoveEnergy;
    }

    /// <summary>
    /// Angband monster_turn: a monster in a web passes through it (PASS_WEB, or passing walls), tears
    /// it down on the way (tunnelling through walls), spends its turn clearing it (CLEAR_WEB), or is
    /// stuck. True when the turn is used up.
    /// </summary>
    private bool StuckInWeb(Monster monster)
    {
        if (!IsWebbed(monster.Position)) return false;
        var race = monster.Race;
        if (race.Has("PASS_WEB") || race.Has("PASS_WALL")) return false;
        if (race.Has("KILL_WALL"))
        {
            ClearWeb(monster.Position);
            return false;
        }
        if (race.Has("CLEAR_WEB")) ClearWeb(monster.Position);
        return true;
    }

    /// <summary>Whether the monster can hear the player (Angband monster_can_hear).</summary>
    public bool CanHear(Monster monster) =>
        Noise[monster.Position] < monster.Race.Hearing - Player.Stealth / 3;

    /// <summary>Symmetric FOV: the monster sees the player exactly when the player could see its square.</summary>
    public bool CanSee(Monster monster) =>
        !Player.IsDead && Level[monster.Position].Has(World.SquareFlags.View)
        // Angband COVERTRACKS: only monsters close by (a quarter of sight) can see the player.
        && !(Player.Timed.Has("covertracks") && monster.Position.DistanceTo(Player.Position) > Data.Constants.MaxSight / 4);

    public bool CanSmell(Monster monster) =>
        monster.Race.Smell > 0 && Scent.Age(monster.Position) <= monster.Race.Smell;

    /// <summary>
    /// Angband monster_take_terrain_damage: lava burns a monster that isn't immune to fire (100 + 1d100);
    /// one that dies of it is gone, dropping what it carried, and no one earns anything.
    /// </summary>
    private void MonsterTerrainDamage(Monster monster)
    {
        if (!Level.Has(monster.Position, TerrainFlags.Fiery) || monster.Race.Has("IM_FIRE")) return;
        var name = Capitalize(MonsterName(monster));
        monster.Hp -= 100 + Rng.RandInt1(100);
        if (monster.Hp >= 0)
        {
            if (monster.IsVisible) Publish(new MessageEvent($"{name} catches fire!"));
            return;
        }
        if (monster.IsVisible) Publish(new MessageEvent($"{name} disintegrates!"));
        QuestKill(monster);
        Level.Monsters.Remove(monster);
        DropCarried(monster);
        DropMonsterLoot(monster);
        if (monster == Commanded) ReleaseCommand(announce: false);
    }

    /// <summary>Angband monster_hates_grid: lava, for a monster that isn't immune to fire.</summary>
    private bool HatesGrid(Monster monster, Loc p) =>
        Level.Has(p, TerrainFlags.Fiery) && !monster.Race.Has("IM_FIRE");

    /// <summary>Directions to try, best first.</summary>
    private IEnumerable<Direction> ChooseMove(Monster monster)
    {
        var race = monster.Race;
        var randomChance = (race.Has(MonsterFlags.Rand25) ? 25 : 0) + (race.Has(MonsterFlags.Rand50) ? 50 : 0);
        if (monster.Confused > 0 || (randomChance > 0 && Rng.RandInt0(100) < randomChance))
            return [Rng.Pick(DirectionExtensions.Compass)];

        var here = monster.Position;
        var candidates = DirectionExtensions.Compass
            .Select(d => (Dir: d, To: here.Step(d)))
            .Where(c => Level.InBounds(c.To) && !HatesGrid(monster, c.To))
            .ToList();

        if (monster.IsAfraid) return FleeMoves(monster, candidates);
        if (KeepsAway(monster)) return KeepAwayMoves(monster, candidates);
        if (GuardMoves(monster, candidates) is { } guarding) return guarding;

        // Angband: a monster that can see the decoy goes for it.
        if (DecoyFor(monster) is { } decoy)
            return candidates.Where(c => c.To.DistanceTo(decoy) < here.DistanceTo(decoy))
                .OrderBy(c => c.To.DistanceTo(decoy)).Select(c => c.Dir).ToList();

        var seen = CanSee(monster);
        var heard = CanHear(monster);
        var smelled = !seen && !heard && CanSmell(monster);
        if (!seen && !heard && !smelled) return WanderMoves(monster, candidates);
        monster.WanderTarget = null;

        // Pack animals don't follow the player into corridors: they wait out of sight (Angband group AI).
        if (race.Has(MonsterFlags.GroupAi) && !Level[Player.Position].Has(World.SquareFlags.Room)
            && here.ChebyshevTo(Player.Position) > 1)
            return LurkMoves(monster, candidates);

        IEnumerable<(Direction Dir, Loc To)> ordered;
        if (heard)
            ordered = candidates.Where(c => Noise[c.To] < Noise[here])
                .OrderBy(c => Noise[c.To]).ThenBy(c => c.To.DistanceTo(Player.Position));
        else if (seen)
            ordered = candidates.Where(c => c.To.DistanceTo(Player.Position) < here.DistanceTo(Player.Position))
                .OrderBy(c => c.To.DistanceTo(Player.Position));
        else // follow the freshest scent
            ordered = candidates.Where(c => Scent.Stamp(c.To) > Scent.Stamp(here))
                .OrderByDescending(c => Scent.Stamp(c.To)).ThenBy(c => c.To.DistanceTo(Player.Position));
        return ordered.Select(c => c.Dir).ToList();
    }

    /// <summary>
    /// Flee (Angband get_move_flee/find_safety): prefer squares further along the sound flow and out of
    /// the player's view. A cornered monster next to the player recovers its nerve and fights.
    /// </summary>
    private IEnumerable<Direction> FleeMoves(Monster monster, List<(Direction Dir, Loc To)> candidates)
    {
        var here = monster.Position;
        int Safety(Loc p) =>
            Math.Min(Noise[p], 100) * 4 + p.DistanceTo(Player.Position) * 2 + (Level[p].Has(World.SquareFlags.View) ? 0 : 20);

        var open = candidates.Where(c => Level.IsPassable(c.To) && Level.Monsters.At(c.To) is null && c.To != Player.Position).ToList();
        var better = open.Where(c => Safety(c.To) > Safety(here)).OrderByDescending(c => Safety(c.To)).ToList();

        if (better.Count == 0 && here.ChebyshevTo(Player.Position) == 1)
        {
            monster.Fear = 0;
            if (monster.IsVisible) Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} turns to fight!"));
            return [DirectionExtensions.FromOffset(Player.Position.X - here.X, Player.Position.Y - here.Y)];
        }
        return better.Select(c => c.Dir).ToList();
    }

    /// <summary>
    /// Angband get_move_bodyguard: a bodyguard more than a step from its leader moves back towards
    /// it — by a square that also brings it nearer the player, if there is one — unless the leader
    /// is out of sight and more than 10 squares off (then it looks after itself). Null when it
    /// needn't (or can't): it then moves as any monster would.
    /// </summary>
    private List<Direction>? GuardMoves(Monster monster, List<(Direction Dir, Loc To)> candidates)
    {
        if (LeaderOf(monster) is not { } leader) return null;
        var here = monster.Position;
        var distance = here.DistanceTo(leader.Position);
        if (distance <= 1) return null;
        if (distance > 10 && !ProjectionPath.Projectable(Level, here, leader.Position, MaxRange)) return null;
        var towardPlayer = here.DistanceTo(Player.Position);
        var closer = candidates
            .Where(c => c.To.DistanceTo(leader.Position) < distance && (Level.IsPassable(c.To) || c.To == Player.Position))
            .OrderBy(c => c.To.DistanceTo(Player.Position) < towardPlayer ? 0 : 1)
            .ThenBy(c => c.To.DistanceTo(leader.Position))
            .Select(c => c.Dir).ToList();
        return closer.Count > 0 ? closer : null;
    }

    /// <summary>
    /// Morale broken (Angband min_range = flee range, without fear): make for safety as a frightened
    /// monster would, but — not being afraid — hit back when cornered next to the player, and stay
    /// put once far enough away.
    /// </summary>
    private IEnumerable<Direction> KeepAwayMoves(Monster monster, List<(Direction Dir, Loc To)> candidates)
    {
        var here = monster.Position;
        if (here.DistanceTo(Player.Position) >= FleeRange) return [];
        int Safety(Loc p) =>
            Math.Min(Noise[p], 100) * 4 + p.DistanceTo(Player.Position) * 2 + (Level[p].Has(World.SquareFlags.View) ? 0 : 20);
        var better = candidates
            .Where(c => Level.IsPassable(c.To) && Level.Monsters.At(c.To) is null && c.To != Player.Position && Safety(c.To) > Safety(here))
            .OrderByDescending(c => Safety(c.To)).Select(c => c.Dir).ToList();
        if (better.Count == 0 && here.ChebyshevTo(Player.Position) == 1)
            return [DirectionExtensions.FromOffset(Player.Position.X - here.X, Player.Position.Y - here.Y)];
        return better;
    }

    /// <summary>Hide from the player's view while staying close; hold still once hidden.</summary>
    private IEnumerable<Direction> LurkMoves(Monster monster, List<(Direction Dir, Loc To)> candidates)
    {
        if (!Level[monster.Position].Has(World.SquareFlags.View)) return [];
        return candidates
            .Where(c => Level.IsPassable(c.To) && !Level[c.To].Has(World.SquareFlags.View) && Level.Monsters.At(c.To) is null)
            .OrderBy(c => Noise[c.To])
            .Select(c => c.Dir)
            .ToList();
    }

    /// <summary>Amble toward a random nearby spot; pick a new one when there or stuck.</summary>
    private IEnumerable<Direction> WanderMoves(Monster monster, List<(Direction Dir, Loc To)> candidates)
    {
        var here = monster.Position;
        if (monster.WanderTarget is not { } target || target == here || monster.WanderStuck >= 3)
        {
            monster.WanderStuck = 0;
            monster.WanderTarget = null;
            for (var i = 0; i < 20 && monster.WanderTarget is null; i++)
            {
                var p = new Loc(here.X + Rng.Spread(0, 10), here.Y + Rng.Spread(0, 10));
                if (Level.InBounds(p) && Level.IsPassable(p) && p != here) monster.WanderTarget = p;
            }
            if (monster.WanderTarget is null) return [];
            target = monster.WanderTarget.Value;
        }

        var moves = candidates
            .Where(c => c.To.DistanceTo(target) < here.DistanceTo(target))
            .OrderBy(c => c.To.DistanceTo(target))
            .Select(c => c.Dir)
            .ToList();
        monster.WanderStuck++; // reset below only if a step succeeds (see TryMonsterStep)
        return moves;
    }

    /// <summary>
    /// Moves into a square: opens, unlocks or bashes doors if the race can, and pushes past or
    /// tramples weaker monsters (MOVE_BODY / KILL_BODY). False if blocked.
    /// </summary>
    private bool TryMonsterStep(Monster monster, Loc target)
    {
        var race = monster.Race;
        if (!PassesWarding(monster, target)) return false;
        if (Level.Monsters.At(target) is { } other)
        {
            if (other.Race.Experience >= race.Experience) return false;
            if (race.Has(MonsterFlags.KillBody))
            {
                if (monster.IsVisible || other.IsVisible)
                    Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} tramples over {MonsterName(other)}."));
                Level.Monsters.Remove(other);
            }
            else if (race.Has(MonsterFlags.MoveBody))
            {
                Level.Monsters.Swap(monster, other);
                monster.WanderStuck = 0;
                return true;
            }
            else return false;
        }

        var feature = Level.FeatureAt(target);
        if (feature.Has(TerrainFlags.DoorClosed))
        {
            var canOpen = race.Has(MonsterFlags.OpenDoor);
            var canBash = race.Has(MonsterFlags.BashDoor);
            if (!canOpen && !canBash) return false;
            ref var sq = ref Level[target];

            if (canOpen && sq.LockPower == 0)
            {
                sq.Feature = Data.Terrain.Ids.OpenDoor;
                if (Level[target].Has(World.SquareFlags.Seen)) Publish(new MessageEvent("You see a door open."));
                return true;
            }

            // Angband: a door gives way when randint0(hp / 10) beats its lock power.
            var strength = Rng.RandInt0(Math.Max(1, monster.Hp / 10));
            if (canOpen && strength > sq.LockPower)
            {
                sq.LockPower = 0; // unlocked; it opens next turn
            }
            else if (canBash && strength > sq.LockPower)
            {
                sq.LockPower = 0;
                sq.Feature = Rng.OneIn(2) ? Data.Terrain.Ids.BrokenDoor : Data.Terrain.Ids.OpenDoor;
                Publish(new MessageEvent(Level[target].Has(World.SquareFlags.Seen) ? "The door bursts open!" : "You hear a door burst open!"));
                Level.Monsters.Move(monster, target);
            }
            return true; // the attempt uses the turn either way
        }

        if (TryWallStep(monster, feature, target) is { } walled) return walled;
        if (!feature.Has(TerrainFlags.Passable)) return false;
        Level.Monsters.Move(monster, target);
        monster.WanderStuck = 0;
        return true;
    }

    /// <summary>
    /// Angband process_monster_multiply: breeders reproduce into an adjacent empty square, less often
    /// the more crowded they are, up to a level-wide cap.
    /// </summary>
    private bool TryMultiply(Monster monster)
    {
        var breeders = Level.Monsters.All.Count(m => m.Race.Has(MonsterFlags.Multiply));
        if (breeders >= Data.Constants.MaxBreeders) return false;

        var neighbours = Level.Monsters.All.Count(m => m != monster && m.Position.ChebyshevTo(monster.Position) <= 1);
        if (neighbours >= 4) return false;
        if (neighbours > 0 && !Rng.OneIn(neighbours * Data.Constants.BreedRate)) return false;

        var spots = Level.Neighbors(monster.Position)
            .Where(p => Level.IsPassable(p) && Level[p].Monster == 0 && p != Player.Position)
            .ToList();
        if (spots.Count == 0) return false;

        var child = _spawner.Place(Level, Rng, monster.Race, Rng.Pick(spots), asleep: false);
        Scheduler.Add(child);
        child.IsVisible = MonsterVisible(child);
        Publish(new MonsterBredEvent(monster.Race.Id, child.Position, child.IsVisible));
        if (child.IsVisible) LearnMonsterFlag(monster.Race, MonsterFlags.Multiply);
        return true;
    }

    /// <summary>
    /// Angband monster_reduce_sleep: a sleeping monster that can hear the player may stir, more often
    /// the noisier (less stealthy) and closer the player is.
    /// </summary>
    private void ReduceSleep(Monster monster)
    {
        // Angband monster_reduce_sleep: aggravation (AGGRAVATE gear) wakes it outright.
        if (Player.HasGearFlag(ItemFlags.Aggravate))
        {
            monster.Sleep = 0;
            monster.IsVisible = MonsterVisible(monster);
            if (monster.IsVisible)
            {
                Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} wakes up."));
                LearnRune(RuneIds.Flag(ItemFlags.Aggravate));
            }
            return;
        }
        if (!CanHear(monster)) return;
        var distance = Noise[monster.Position];

        long playerNoise = 1L << Math.Clamp(30 - Player.Stealth, 0, 62);
        long notice = Rng.RandInt0(1024);
        if (notice * notice * notice > playerNoise) return;

        monster.Sleep = Math.Max(0, monster.Sleep - Math.Max(1, 100 / Math.Max(1, distance)));
        if (monster.Sleep == 0)
        {
            monster.IsVisible = MonsterVisible(monster);
            if (monster.IsVisible) Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} wakes up."));
        }
    }

    /// <summary>Angband make_attack_normal: each blow tests to hit, then applies its effect.</summary>
    private void MonsterMelee(Monster monster)
    {
        var race = monster.Race;
        var name = Capitalize(MonsterName(monster));
        var killer = race.IsUnique ? race.Name : Article(race.Name);

        _attacker = race.Id;
        _actingMonster = monster;
        var blowIndex = -1;
        foreach (var blow in race.Blows)
        {
            blowIndex++;
            if (Player.IsDead || !monster.IsActive) break;
            if (monster.IsVisible) Lore.For(race.Id).SeeBlow(blowIndex);
            var method = Data.BlowMethod(blow.Method);
            var effect = Data.BlowEffect(blow.Effect);
            if (method is null || effect is null) continue;

            // Protection from evil (Angband): evil monsters no deeper than you are usually repelled.
            if (Player.Timed.Has("prot_evil") && race.Has(MonsterFlags.Evil) && Player.Level >= race.Depth
                && Rng.RandInt0(100) + Player.Level > 50)
            {
                if (monster.IsVisible) Publish(new MessageEvent($"{name} is repelled."));
                continue;
            }

            var chance = effect.Power + race.Depth * 3;
            if (!CombatMath.TestHit(Rng, chance, Player.Armour, visible: true))
            {
                if (monster.IsVisible && method.Miss) Publish(new MessageEvent($"{name} {method.MissMessage}."));
                Publish(new MonsterAttackEvent(monster.Id, Hit: false, 0, blow.Method));
                continue;
            }

            var rolled = blow.Damage.Roll(Rng);
            var damage = rolled;
            Publish(new MessageEvent($"{name} {method.Message}."));

            if (effect.Element is { } elementId && Data.Element(elementId) is { } element)
            {
                damage = CombatMath.ResistElement(Rng, element, damage, Player.Resists.GetValueOrDefault(elementId));
                // Being hit by an element reveals gear that resists it.
                foreach (var item in Player.Inventory.Equipped.Where(i => i.Resists.Contains(elementId)).ToList())
                    LearnRune(RuneIds.Resist(elementId));
                LearnAboutPlayer(monster, elementId);
            }
            if (effect.ArmourReduces)
            {
                damage = CombatMath.ArmourReduce(damage, Player.Armour);
                // Being struck reveals your armour's magical protection.
                foreach (var item in Player.Inventory.Equipped.ToList()) LearnRunesOf(item, RuneIds.ToAc);
            }

            // Angband melee_effect_elemental: fire, acid, lightning and cold blows harm the pack too.
            if (effect.Element is { } harming && damage > 0) InventoryDamage(harming, Math.Min(damage * 5, 300));
            TakeHit(damage, killer);
            // Angband BLACK_BREATH: one blow in five adds a little of the Black Breath (unresistable).
            if (effect.Id == "black_breath" && !Player.IsDead && Rng.OneIn(5) && damage / 10 > 0)
                IncreaseTimed("blackbreath", damage / 10);
            if (effect.Id == "shatter" && !Player.IsDead) Shatter(monster, damage);
            Publish(new MonsterAttackEvent(monster.Id, Hit: true, damage, blow.Method));
            if (Player.IsDead) break;

            if (effect.Timed is { } timedId) ApplyBlowStatus(effect, timedId, rolled, race.Depth);
            if (ApplyBlowSpecial(monster, effect.Id, rolled))
            {
                // A thief that got what it came for vanishes (Angband: "There is a puff of smoke!").
                Publish(new MessageEvent(monster.IsVisible ? "There is a puff of smoke!" : "You feel something brush past you."));
                TeleportMonster(monster, Data.Constants.MaxSight * 2 + 5);
                break;
            }

            if (effect.ArmourReduces && (method.Cut || method.Stun))
            {
                var doCut = method.Cut && (!method.Stun || Rng.OneIn(2));
                var grade = CombatMath.MonsterCritical(Rng, blow.Damage, rolled);
                if (grade > 0)
                {
                    if (doCut) IncreaseTimed(TimedIds.Cut, CombatMath.CutAmount(Rng, grade));
                    else IncreaseTimed(TimedIds.Stun, CombatMath.StunAmount(Rng, grade));
                }
            }
        }
        _attacker = null;
        _actingMonster = null;
    }

    /// <summary>
    /// Angband melee_effect_handler_SHATTER: a blow hard enough (over 23) sets off an earthquake
    /// around the monster, radius damage / 12; one over 100 may throw you back, 1 + (damage - 100) / 40
    /// squares, when a roll up to damage - 100 comes to more than 40.
    /// </summary>
    private void Shatter(Monster monster, int damage)
    {
        if (damage > 23) Earthquake(damage / 12, monster.Position);
        if (damage <= 100) return;
        var value = damage - 100;
        if (Rng.RandInt1(value) > 40) ThrustAway(monster.Position, 1 + value / 40);
    }

    private void ApplyBlowStatus(BlowEffectDef effect, string timedId, int damage, int monsterLevel)
    {
        LearnAboutPlayer(_actingMonster, effect.PreventedBy);
        if (effect.PreventedBy is { } protection && Player.Resists.GetValueOrDefault(protection) > 0)
        {
            Publish(new MessageEvent("You are unaffected!"));
            return;
        }
        if (effect.Save && Rng.RandInt0(100) < Player.SkillSave)
        {
            Publish(new MessageEvent("You resist the effects!"));
            return;
        }
        var scale = effect.DurationScale switch { "damage" => damage, "half_level" => monsterLevel / 2, _ => monsterLevel };
        IncreaseTimed(timedId, effect.DurationBase + Rng.RandInt1(Math.Max(1, scale)));
    }

    /// <summary>Monster timed effects wear off; monsters regenerate every 100 game turns.</summary>
    private void MonsterUpkeep(long gameTurn)
    {
        foreach (var monster in Level.Monsters.All.ToList())
        {
            if (monster.Fear > 0 && --monster.Fear == 0 && monster.IsVisible)
                Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} recovers its courage."));
            if (monster.Confused > 0) monster.Confused--;
            if (monster.Stun > 0) monster.Stun--;
            if (monster.Held > 0) monster.Held--;
            if (monster.Slow > 0 && --monster.Slow == 0 && monster.IsVisible)
                Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} is no longer slow."));
            if (monster.Fast > 0 && --monster.Fast == 0 && monster.IsVisible)
                Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} is no longer fast."));

            if (gameTurn % 100 == 0 && monster.Hp < monster.MaxHp)
            {
                var gain = monster.MaxHp / 100;
                if (gain == 0 && Rng.OneIn(2)) gain = 1;
                if (monster.Race.Has(MonsterFlags.Regenerate)) gain *= 2;
                monster.Hp = Math.Min(monster.MaxHp, monster.Hp + gain);
            }
        }
    }
}
