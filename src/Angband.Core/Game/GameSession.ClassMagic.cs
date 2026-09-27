using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Combat;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Time;
using Angband.Core.World;

namespace Angband.Core.Game;

// The abilities of Angband 4.2's rogue, paladin, necromancer and blackguard (class.txt player-flags),
// and the effects of their spells that no item has: the necromancer's darkness and life-draining
// rituals, the blackguard's fighting rituals, and the rogue's stealing.
public sealed partial class GameSession
{
    /// <summary>Whether the player's class has an ability flag (<see cref="ClassFlags"/>).</summary>
    public bool ClassHas(string flag) => Player.Class?.Flags.Contains(flag) == true;

    // --- Unlight (necromancers) ---------------------------------------------------------------------

    /// <summary>
    /// Angband UNLIGHT (cave-view.c): with little or no light, a necromancer still sees the squares
    /// close by — within 1 + level/6 − light.
    /// </summary>
    public int UnlightRadius =>
        ClassHas(ClassFlags.Unlight) && Player.LightRadius <= 1 ? Math.Max(0, 1 + Player.Level / 6 - Player.LightRadius) : 0;

    /// <summary>Angband spell_chance: necromancers are punished (+25% failure) on a lit square.</summary>
    private bool UnlightPenalty =>
        ClassHas(ClassFlags.Unlight) && (Player.LightRadius > 0 || Level[Player.Position].Has(SquareFlags.Glow));

    // --- Combat regeneration (blackguards) ---------------------------------------------------------

    /// <summary>Adds (or removes) mana in 1/65536 points (Angband player_adjust_mana_precise).</summary>
    private void AdjustManaPrecise(long gain)
    {
        var total = Math.Clamp((long)Player.Mana * 65536 + Player.ManaFraction + gain, 0, (long)Player.MaxMana * 65536);
        Player.Mana = (int)(total / 65536);
        Player.ManaFraction = (int)(total % 65536);
    }

    /// <summary>Adds hit points in 1/65536 points (Angband player_adjust_hp_precise), up to the maximum.</summary>
    private void AdjustHpPrecise(long gain)
    {
        var total = Math.Min((long)Player.Hp * 65536 + Player.HpFraction + gain, (long)Player.MaxHp * 65536);
        Player.Hp = (int)(total / 65536);
        Player.HpFraction = (int)(total % 65536);
    }

    /// <summary>
    /// Angband convert_mana_to_hp: a blackguard spending X% of their mana regains X/2% of the hit
    /// points they have lost (at most a quarter of them).
    /// </summary>
    private void ConvertManaToHp(long spent)
    {
        if (spent <= 0 || Player.MaxMana == 0 || Player.Hp >= Player.MaxHp) return;
        var lost = (long)(Player.MaxHp - Player.Hp) * 65536 - Player.HpFraction;
        var ratio = Math.Max(4, Math.Max(10L, Player.MaxMana) * 131072 / spent);
        AdjustHpPrecise(lost / ratio);
    }

    /// <summary>Angband py_attack: a blackguard gets 5% of their mana (at least half a point) for each attack.</summary>
    private void CombatRegenOnAttack()
    {
        if (!ClassHas(ClassFlags.CombatRegen)) return;
        AdjustManaPrecise(Math.Max(Player.MaxMana, 10) * 16384L / 5);
    }

    /// <summary>
    /// Angband take_hit: a blackguard losing X% of their hit points gains X% of their mana (not from
    /// poison, bleeding or starvation).
    /// </summary>
    private void CombatRegenOnWound(int damage, string killer)
    {
        if (!ClassHas(ClassFlags.CombatRegen) || damage <= 0 || killer is "poison" or "a fatal wound" or "starvation") return;
        AdjustManaPrecise(Math.Max(Player.MaxMana, 10) * 65536L / Math.Max(1, Player.MaxHp) * damage);
    }

    /// <summary>
    /// Angband player_regen_mana for COMBAT_REGEN: mana ebbs at half the usual regeneration rate
    /// (faster while resting when badly hurt), and what is lost heals at double efficiency.
    /// </summary>
    private void CombatRegenDecay()
    {
        var percent = RegenNormal;
        if (Player.Hp <= Player.MaxHp / 2 && Player.IsResting) percent *= 2;
        percent /= -2;
        var before = (long)Player.Mana * 65536 + Player.ManaFraction;
        AdjustManaPrecise((long)Player.MaxMana * percent);
        var lost = before - ((long)Player.Mana * 65536 + Player.ManaFraction);
        ConvertManaToHp(lost * 2);
    }

    // --- Stealing (rogues) ---------------------------------------------------------------------------

    /// <summary>
    /// The treasure a monster carries: in Angband monsters hold their drop from birth; here it is
    /// rolled the first time anyone tries to steal it (or when the monster dies).
    /// </summary>
    private void RollCarriedLoot(Monster monster)
    {
        if (monster.LootRolled) return;
        monster.LootRolled = true;
        monster.Carried.AddRange(RollMonsterLoot(monster));
    }

    /// <summary>
    /// Angband steal_monster_item (rogues, 's'): the monster's guard (level, speed, halved if asleep)
    /// against stealth and dexterity. A clean theft may barely wake it; a clumsy one wakes it; a
    /// bungled one makes it cry out and wakes everything nearby.
    /// </summary>
    private int Steal(Direction dir)
    {
        if (!ClassHas(ClassFlags.Steal))
        {
            Publish(new MessageEvent("You don't have the skill to steal."));
            return 0;
        }
        var p = Player.Position.Step(dir);
        if (!Level.InBounds(p) || Level.Monsters.At(p) is not { } monster || !monster.IsVisible)
        {
            Publish(new MessageEvent("There is no one there to steal from."));
            return 0;
        }
        var name = MonsterName(monster);
        RollCarriedLoot(monster);
        if (monster.Carried.Count == 0)
        {
            Publish(new MessageEvent($"You can find nothing to steal from {name}."));
            if (Rng.OneIn(3)) WakeMonster(monster);
            return EnergyTable.MoveEnergy;
        }
        var loot = monster.Carried[Rng.RandInt0(monster.Carried.Count)];

        var guard = monster.Race.Depth * (monster.Race.IsUnique ? 4 : 3) / 4 + ((IActor)monster).Speed - Player.Speed;
        var dex = Magic.StatTables.ToHit[Magic.StatTables.Index(Player.Stats.GetValueOrDefault("dex", 15))];
        var skill = Player.Stealth + dex;
        if (Player.IsBlind || Player.Timed.Has(TimedIds.Confused) || Player.Timed.Has("image")) skill /= 4;
        if (monster.Sleep > 0) guard /= 2;
        var reaction = guard / 2 + Rng.RandInt1(Math.Max(guard, 1)) + loot.Weight / 20;

        if (reaction < skill)
        {
            monster.Carried.Remove(loot);
            if (loot.IsGold)
            {
                Player.Gold += loot.GoldValue;
                Publish(new MessageEvent($"You steal {loot.GoldValue} gold pieces worth of treasure."));
            }
            else
            {
                Publish(new MessageEvent($"You steal {Describe(loot)} from {name}."));
                if (Player.Inventory.Add(loot) is null)
                {
                    DropNear(loot, Player.Position);
                    Publish(new MessageEvent($"You drop {Describe(loot)}."));
                }
                RecalculateBonuses();
            }
            // The mark stirs a little (Angband: its sleep drops by 35 less your stealth).
            if (monster.Sleep > 0)
            {
                monster.Sleep = Math.Max(0, monster.Sleep - Math.Max(0, 35 - Player.Stealth));
                if (monster.Sleep == 0) Publish(new MessageEvent($"{Capitalize(name)} wakes up."));
            }
        }
        else if (reaction / 2 < skill)
        {
            Publish(new MessageEvent($"You fail to steal {(loot.IsGold ? "treasure" : Describe(loot))} from {name}."));
            WakeMonster(monster);
        }
        else
        {
            WakeMonster(monster);
            Publish(new MessageEvent($"{Capitalize(name)} cries out in anger!"));
            foreach (var m in Level.Monsters.All.Where(m => m.Position.DistanceTo(p) <= Data.Constants.MaxSight)) m.Sleep = 0;
        }
        HitAndRun(); // whatever came of it, a prepared thief vanishes
        return EnergyTable.MoveEnergy;
    }

    // --- Spell effects ---------------------------------------------------------------------------------

    /// <summary>Class spell effects that no item has. False if not one of these.</summary>
    private bool ApplyClassSpellEffect(ItemEffect e)
    {
        switch (e.Name)
        {
            case "darken_area":
                DarkenRoom();
                return true;
            case "darken_level":
                DarkenLevel();
                return true;
            case "detect_minds":
                DetectMinds(e.Int(0), Math.Max(1, e.Int(1)));
                return true;
            case "detect_fearful":
                DetectMonstersWhere(e.Int(0), m => !m.Race.Has(MonsterFlags.NoFear), "monsters that know fear");
                return true;
            case "detect_stairs":
                DetectStairsAndDoors(e.Int(0));
                return true;
            case "tap_unlife":
                TapUnlife(e.Dice(0));
                return true;
            case "crush":
                Crush(e.Int(0));
                return true;
            case "vampire_strike":
                VampireStrike(e.Int(0));
                return true;
            case "sweep":
                Sweep(Math.Max(1, e.Int(0)));
                return true;
            case "leap":
                Leap(Math.Max(1, e.Int(0)));
                return true;
            case "blows":
                SpellBlows(e.Arg(0), Math.Max(1, e.Int(1)));
                return true;
            default:
                return ApplyLateSpellEffect(e);
        }
    }

    /// <summary>Angband DARKEN_AREA: darkness around the player and through the room they are in.</summary>
    private void DarkenRoom()
    {
        foreach (var p in RoomSquares(Player.Position)) Level[p].Flags &= ~SquareFlags.Glow;
        Darken(Player.Position, 3);
    }

    /// <summary>The squares of the room containing a point (the point itself if not in a room).</summary>
    private IEnumerable<Loc> RoomSquares(Loc start)
    {
        if (!Level[start].Has(SquareFlags.Room)) yield break;
        var queue = new Queue<Loc>([start]);
        var seen = new HashSet<Loc> { start };
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            yield return p;
            if (!Level.IsPassable(p) && p != start) continue;
            foreach (var n in Level.Neighbors(p))
                if (Level[n].Has(SquareFlags.Room) && seen.Add(n)) queue.Enqueue(n);
        }
    }

    /// <summary>Angband DARKEN_LEVEL (Fume of Mordor): the whole level goes dark, but is mapped and its objects sensed.</summary>
    private void DarkenLevel()
    {
        if (Level.Depth == 0)
        {
            Publish(new MessageEvent("The sunlit town will not be darkened."));
            return;
        }
        foreach (var p in Level.AllLocs()) Level[p].Flags &= ~SquareFlags.Glow;
        Known.RememberAll(Level);
        Publish(new MessageEvent("A foul fume spreads over the level, and its shape forms in your mind."));
        DetectObjects(999);
        UpdateView();
    }

    /// <summary>Detects the monsters that match; true if the magic worked (even finding nothing).</summary>
    private bool DetectMonstersWhere(int radius, Func<Monster, bool> match, string what)
    {
        var found = 0;
        foreach (var m in Level.Monsters.All.Where(m => !m.Camouflaged && m.Position.ChebyshevTo(Player.Position) <= radius && match(m)))
        {
            m.IsDetected = true;
            found++;
        }
        Publish(new MessageEvent(found > 0 ? $"You sense the presence of {what}!" : $"You sense no {what}."));
        return true;
    }

    /// <summary>Angband READ_MINDS: monsters with minds are detected, and the area around each is mapped.</summary>
    private void DetectMinds(int radius, int mapRadius)
    {
        var minds = Level.Monsters.All
            .Where(m => !m.Camouflaged && !m.Race.Has("EMPTY_MIND") && m.Position.ChebyshevTo(Player.Position) <= radius).ToList();
        foreach (var m in minds)
        {
            m.IsDetected = true;
            foreach (var p in Level.AllLocs().Where(p => p.ChebyshevTo(m.Position) <= mapRadius))
                if (Level.IsPassable(p) || Level.Neighbors(p).Any(Level.IsPassable))
                    Known.Remember(Level, p);
        }
        Publish(new MessageEvent(minds.Count > 0 ? "You sense the presence of minds, and see through their eyes." : "You sense no minds."));
    }

    /// <summary>Doors and stairs nearby are revealed (the rogue's Find Traps, Doors and Stairs).</summary>
    private void DetectStairsAndDoors(int radius)
    {
        var found = 0;
        foreach (var p in Level.AllLocs().Where(p => p.ChebyshevTo(Player.Position) <= radius))
        {
            if (!Level.FeatureAt(p).HasAny(TerrainFlags.Stair | TerrainFlags.DoorAny)) continue;
            Known.Remember(Level, p);
            found++;
        }
        Publish(new MessageEvent(found > 0 ? "You sense the presence of doors and stairs!" : "You sense no doors or stairs."));
    }

    /// <summary>The visible monsters in view that match, nearest first.</summary>
    private List<Monster> MonstersInView(Func<Monster, bool> match) =>
        Level.Monsters.All
            .Where(m => m.IsVisible && Level[m.Position].Has(SquareFlags.View) && match(m))
            .OrderBy(m => m.Position.DistanceTo(Player.Position)).ThenBy(m => m.Id).ToList();

    private static bool IsLiving(MonsterRaceDef race) => !race.Has(MonsterFlags.Undead) && !race.Has("NONLIVING");

    /// <summary>Angband TAP_UNLIFE: the nearest undead in view is drained, and the damage becomes mana.</summary>
    private void TapUnlife(Randomness.Dice dice)
    {
        if (MonstersInView(m => m.Race.Has(MonsterFlags.Undead)).FirstOrDefault() is not { } undead)
        {
            Publish(new MessageEvent("You sense no undead to tap."));
            return;
        }
        var damage = Math.Min(dice.Roll(Rng), Math.Max(0, undead.Hp + 1));
        Publish(new MessageEvent($"You draw power from {MonsterName(undead)}."));
        Publish(new PlayerAttackEvent(undead.Id, Hit: true, damage, Combat.CriticalGrade.None));
        DamageMonster(undead, damage);
        Player.Mana = Math.Min(Player.MaxMana, Player.Mana + damage);
    }

    /// <summary>Angband MON_CRUSH: every monster in view with fewer hit points than the power dies.</summary>
    private void Crush(int power)
    {
        var victims = MonstersInView(m => m.Hp < power);
        foreach (var m in victims)
        {
            Publish(new MessageEvent($"{Capitalize(MonsterName(m))} is crushed."));
            DamageMonster(m, m.Hp + 1);
        }
        if (victims.Count == 0) Publish(new MessageEvent("Nothing in view is weak enough to crush."));
    }

    /// <summary>
    /// Angband JUMP_AND_BITE (Vampire Strike): leap beside the nearest living monster in view and
    /// drain its blood, healing and feeding the player.
    /// </summary>
    private void VampireStrike(int power)
    {
        var victim = MonstersInView(m => IsLiving(m.Race))
            .FirstOrDefault(m => Level.Neighbors(m.Position).Any(n => n == Player.Position || (Level.IsPassable(n) && Level[n].Monster == 0)));
        if (victim is null)
        {
            Publish(new MessageEvent("There is no living creature in view to feed on."));
            return;
        }
        if (Player.Position.ChebyshevTo(victim.Position) > 1)
        {
            var spot = Level.Neighbors(victim.Position)
                .Where(n => Level.IsPassable(n) && Level[n].Monster == 0)
                .OrderBy(n => n.DistanceTo(Player.Position)).ThenBy(n => n.Y).ThenBy(n => n.X).First();
            var from = Player.Position;
            Player.Position = spot;
            Publish(new PlayerMovedEvent(from, spot));
            Publish(new PlayerTeleportedEvent(from, spot));
            UpdateView();
        }
        var drained = Math.Min(power, Math.Max(0, victim.Hp + 1));
        Publish(new MessageEvent($"You bite {MonsterName(victim)}."));
        Publish(new PlayerAttackEvent(victim.Id, Hit: true, drained, Combat.CriticalGrade.None));
        DamageMonster(victim, drained);
        Player.Hp = Math.Min(Player.MaxHp, Player.Hp + drained);
        SetFood(Player.Food + drained * 10);
        Publish(new MessageEvent("You feel refreshed."));
    }

    /// <summary>Angband SWEEP (Whirlwind Attack): blows against every adjacent monster.</summary>
    private void Sweep(int blows)
    {
        var targets = Level.Neighbors(Player.Position).Select(Level.Monsters.At).OfType<Monster>().OrderBy(m => m.Id).ToList();
        if (targets.Count == 0)
        {
            Publish(new MessageEvent("You swing wildly at the empty air."));
            return;
        }
        foreach (var m in targets)
        {
            Reveal(m);
            WakeMonster(m);
            for (var i = 0; i < blows && m.IsActive && !Player.IsDead; i++) PlayerBlow(m);
        }
    }

    /// <summary>
    /// Angband MOVE_ATTACK (Leap into Battle): run up to 4 squares at the target, then strike it; each
    /// square run costs a quarter of the blows (rounded).
    /// </summary>
    private void Leap(int blows)
    {
        var aim = _effectTarget ?? AimPoint();
        if (aim is not { } target || Level.Monsters.At(target) is not { } monster)
        {
            Publish(new MessageEvent("You have no one to leap at."));
            return;
        }
        var moved = 0;
        if (Player.Position.ChebyshevTo(monster.Position) > 1)
        {
            var path = ProjectionPath.Compute(Level, Player.Position, monster.Position, 5, PathFlags.StopAtCreature,
                p => Level.Monsters.At(p) is not null);
            var from = Player.Position;
            foreach (var step in path)
            {
                if (step == monster.Position || moved >= 4 || !Level.IsPassable(step) || Level[step].Monster != 0) break;
                Player.Position = step;
                moved++;
                if (Player.Position.ChebyshevTo(monster.Position) <= 1) break;
            }
            if (moved > 0)
            {
                Publish(new PlayerMovedEvent(from, Player.Position));
                UpdateView();
            }
        }
        if (Player.Position.ChebyshevTo(monster.Position) > 1)
        {
            Publish(new MessageEvent($"You can't reach {MonsterName(monster)}."));
            return;
        }
        var count = (int)Math.Round(blows * (4 - moved) / 4.0, MidpointRounding.AwayFromZero);
        if (count <= 0)
        {
            Publish(new MessageEvent("You arrive too winded to strike."));
            return;
        }
        WakeMonster(monster);
        for (var i = 0; i < count && monster.IsActive && !Player.IsDead; i++) PlayerBlow(monster);
    }

    /// <summary>
    /// Angband MELEE_BLOWS: blows at the adjacent monster in the chosen direction. <c>stun</c>
    /// (Maim Foe) stuns it if any blow lands; <c>force</c> (Forceful Blow) hurls it back and may stun it.
    /// </summary>
    private void SpellBlows(string kind, int blows)
    {
        var dir = _effectDirection ?? Direction.Here;
        var p = Player.Position.Step(dir);
        if (dir == Direction.Here || Level.Monsters.At(p) is not { } monster)
        {
            Publish(new MessageEvent("There is nothing there to strike."));
            return;
        }
        Reveal(monster);
        WakeMonster(monster);
        var landed = false;
        for (var i = 0; i < blows && monster.IsActive && !Player.IsDead; i++)
        {
            var damage = PlayerBlow(monster);
            if (damage <= 0 || !monster.IsActive) continue;
            landed = true;
            if (kind == "force")
            {
                // The force of the blow does the weapon's damage again and knocks the foe back.
                var extra = (Player.Inventory.Weapon?.Damage ?? BareHands.Damage).Roll(Rng);
                if (DamageMonster(monster, extra)) break;
                KnockBack(monster, dir, 3);
                if (Rng.OneIn(2)) StunMonster(monster, 3 + Rng.RandInt1(3));
            }
        }
        if (landed && kind == "stun" && monster.IsActive) StunMonster(monster, 6);
    }

    private void StunMonster(Monster monster, int turns)
    {
        if (monster.Race.Has(MonsterFlags.NoStun))
        {
            LearnMonsterFlag(monster.Race, MonsterFlags.NoStun);
            return;
        }
        monster.Stun = Math.Max(monster.Stun, turns);
        if (monster.IsVisible) Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} is dazed."));
    }

    /// <summary>Pushes a monster up to a few squares away along a direction, stopping at walls and creatures.</summary>
    private void KnockBack(Monster monster, Direction dir, int squares)
    {
        var moved = false;
        for (var i = 0; i < squares; i++)
        {
            var next = monster.Position.Step(dir);
            if (!Level.InBounds(next) || !Level.IsPassable(next) || Level[next].Monster != 0 || next == Player.Position) break;
            Level.Monsters.Move(monster, next);
            moved = true;
        }
        if (moved)
        {
            if (monster.IsVisible) Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} is knocked back!"));
            UpdateView();
        }
    }
}
