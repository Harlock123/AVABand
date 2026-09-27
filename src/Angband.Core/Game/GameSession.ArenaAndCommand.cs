using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Sight;
using Angband.Core.Time;
using Angband.Core.World;

namespace Angband.Core.Game;

// The last of Angband 4.2's class spells: the rogue's Hit and Run, the blackguard's Bloodlust and
// Relentless Taunting, the necromancer's [Corruption of Spirit] (Power Sacrifice, Zone of Unmagic,
// Vampire Form, Curse, Command) and the paladin's Clairvoyance and [Battle Blessings] (Smite Evil,
// Demon Bane, Enchant Weapon/Armour, Single Combat).
public sealed partial class GameSession
{
    // --- Spell effects ------------------------------------------------------------------------------

    /// <summary>The later spells' effects (called from <see cref="ApplyClassSpellEffect"/>). False if not one.</summary>
    private bool ApplyLateSpellEffect(ItemEffect e)
    {
        switch (e.Name)
        {
            case "gain_mana":
                Player.Mana = Math.Min(Player.MaxMana, Player.Mana + e.Int(0));
                Publish(new MessageEvent("You feel your head clear."));
                return true;
            case "spot":
                Spot(e.Arg(0), e.Int(1), Math.Max(0, e.Int(2)));
                return true;
            case "curse":
                CurseMonster(Math.Max(1, e.Int(0)));
                return true;
            case "command":
                CommandMonster(Math.Max(1, e.Dice(0).Roll(Rng)));
                return true;
            case "light_level":
                LightLevel();
                return true;
            case "single_combat":
                SingleCombat();
                return true;
            default:
                return ApplyRealmSpellEffect(e);
        }
    }

    /// <summary>The monster the spell is aimed at: the explicit target, or the one aimed at by default.</summary>
    private Monster? AimedMonster() => (_effectTarget ?? AimPoint()) is { } p ? Level.Monsters.At(p) : null;

    /// <summary>
    /// Angband SPOT (Zone of Unmagic): an explosion centred on the player that hurts the monsters
    /// around (less further out) — and the player too.
    /// </summary>
    private void Spot(string element, int damage, int radius)
    {
        var elementId = NullIfNone(element);
        Publish(new MessageEvent("A wave of unmaking spreads from you."));
        foreach (var m in Level.Monsters.All
                     .Where(m => m.Position.DistanceTo(Player.Position) <= radius && ProjectionPath.Projectable(Level, Player.Position, m.Position, radius + 1))
                     .OrderBy(m => m.Position.DistanceTo(Player.Position)).ThenBy(m => m.Id).ToList())
            ProjectileHitsMonster(m, "zone", elementId, damage / (m.Position.DistanceTo(Player.Position) + 1));
        ElementalHit(elementId, damage, "a zone of unmagic");
    }

    /// <summary>
    /// Angband CURSE: direct damage to the targeted monster — (level/12 + 1) dice, each with 50 sides
    /// plus the percentage of its hit points already lost.
    /// </summary>
    private void CurseMonster(int dice)
    {
        if (AimedMonster() is not { } m)
        {
            Publish(new MessageEvent("No monster selected!"));
            return;
        }
        var lost = m.MaxHp <= 0 ? 0 : 100 - Math.Clamp(m.Hp, 0, m.MaxHp) * 100 / m.MaxHp;
        var damage = Rng.Damroll(dice, 50 + lost);
        Publish(new MessageEvent($"Your curse strikes {MonsterName(m)}{DamageNote(damage)}."));
        Publish(new PlayerAttackEvent(m.Id, Hit: true, damage, Combat.CriticalGrade.None));
        DamageMonster(m, damage);
    }

    /// <summary>
    /// Angband LIGHT_LEVEL (Clairvoyance): the whole level is lit and mapped, and its objects sensed.
    /// </summary>
    private void LightLevel()
    {
        if (Level.Depth == 0)
        {
            Publish(new MessageEvent("The town is already bright enough."));
            return;
        }
        foreach (var p in Level.AllLocs())
            if (Level.IsPassable(p) || Level.Neighbors(p).Any(Level.IsPassable))
                Level[p].Flags |= SquareFlags.Glow;
        Known.RememberAll(Level);
        Publish(new MessageEvent("An image of your surroundings forms in your mind..."));
        DetectObjects(999);
        UpdateView();
    }

    // --- Hit and Run (rogues) ------------------------------------------------------------------------

    /// <summary>Angband steal_monster_item: with Hit and Run ready, the thief vanishes after the attempt.</summary>
    private void HitAndRun()
    {
        if (!Player.Timed.Has("att_run") || Data.Timed("att_run") is not { } run) return;
        Publish(new MessageEvent("You vanish into the shadows!"));
        TeleportPlayer(20);
        if (Player.Timed.Set(run, 0) is { } ended) Publish(new MessageEvent(ended));
    }

    // --- Bloodlust (blackguards) ---------------------------------------------------------------------

    private int Bloodlust => Player.Timed["bloodlust"];

    /// <summary>Angband player_over_exert: a chance (in %) of each harm, of up to <paramref name="amount"/>.</summary>
    private void OverExert(int chance, int amount, bool hp = false, bool cut = false, bool slow = false, bool confuse = false,
        bool scramble = false, bool con = false)
    {
        if (chance <= 0) return;
        amount = Math.Max(1, amount);
        if (con && Rng.RandInt0(100) < chance)
        {
            Publish(new MessageEvent("You have damaged your health!"));
            DrainStat("con");
        }
        if (scramble && Rng.RandInt0(100) < chance) IncreaseTimed("scrambled", Rng.RandInt1(amount));
        if (cut && Rng.RandInt0(100) < chance)
        {
            Publish(new MessageEvent("Wounds appear on your body!"));
            IncreaseTimed(TimedIds.Cut, Rng.RandInt1(amount));
        }
        if (confuse && Rng.RandInt0(100) < chance) IncreaseTimed(TimedIds.Confused, Rng.RandInt1(amount));
        if (slow && Rng.RandInt0(100) < chance)
        {
            Publish(new MessageEvent("You feel suddenly lethargic."));
            IncreaseTimed(TimedIds.Slow, Rng.RandInt1(amount));
        }
        if (hp && Rng.RandInt0(100) < chance)
        {
            Publish(new MessageEvent("You cry out in sudden pain!"));
            TakeHit(Rng.RandInt1(amount), "over-exertion");
        }
    }

    /// <summary>Angband mon-util.c: each kill feeds the bloodlust (+10), clouding the mind a little.</summary>
    private void BloodlustOnKill()
    {
        if (Bloodlust <= 0) return;
        IncreaseTimed("bloodlust", 10);
        OverExert(5, 3, confuse: true);
    }

    /// <summary>Angband process_world: as the bloodlust fades, the body pays (more, the lower it is).</summary>
    private void BloodlustUpkeep()
    {
        if (Bloodlust <= 0) return;
        OverExert(Math.Max(0, 10 - Bloodlust), Player.Hp / 10, hp: true, cut: true, slow: true);
    }

    // --- Command (necromancers) ----------------------------------------------------------------------

    /// <summary>The monster under the player's command (Angband MON_TMD_COMMAND), if any.</summary>
    public Monster? Commanded { get; private set; }

    internal void RestoreCommanded(int monsterId) => Commanded = Level.Monsters[monsterId];

    /// <summary>Angband COMMAND: a monster that fails its save (d(level) against d(its level)) is yours to move.</summary>
    private void CommandMonster(int turns)
    {
        if (AimedMonster() is not { } m)
        {
            Publish(new MessageEvent("No monster selected!"));
            return;
        }
        WakeMonster(m);
        if (Rng.RandInt1(Player.Level) < Rng.RandInt1(Math.Max(1, m.Race.Depth)))
        {
            Publish(new MessageEvent($"{Capitalize(MonsterName(m))} resists your command!"));
            return;
        }
        Commanded = m;
        if (Data.Timed("command") is { } def) Player.Timed.Set(def, 0);
        IncreaseTimed("command", turns);
        Publish(new MessageEvent($"You command {MonsterName(m)}. Move it with the direction keys; anything else lets it go."));
    }

    /// <summary>Lets go of a commanded monster.</summary>
    private void ReleaseCommand(bool announce)
    {
        if (Commanded is { } m && announce && m.IsActive) Publish(new MessageEvent($"You release {MonsterName(m)}."));
        Commanded = null;
        if (Player.Timed.Has("command") && Data.Timed("command") is { } def) Player.Timed.Set(def, 0);
    }

    /// <summary>
    /// While commanding, a direction moves the monster instead (into another monster, it attacks it);
    /// holding keeps it still. Returns the energy used, or null if the command isn't a move.
    /// </summary>
    private int? CommandedAction(GameCommand command)
    {
        if (Commanded is not { } m) return null;
        if (!m.IsActive || !Player.Timed.Has("command"))
        {
            ReleaseCommand(announce: false);
            return null;
        }
        switch (command)
        {
            case HoldCommand:
                return EnergyTable.MoveEnergy;
            case WalkCommand walk:
            {
                var to = m.Position.Step(walk.Direction);
                if (!Level.InBounds(to) || to == Player.Position) return 0;
                if (Level.Monsters.At(to) is { } victim)
                {
                    CommandedAttack(m, victim);
                    return EnergyTable.MoveEnergy;
                }
                if (!Level.IsPassable(to))
                {
                    Publish(new MessageEvent($"{Capitalize(MonsterName(m))} can't go that way."));
                    return 0;
                }
                Level.Monsters.Move(m, to);
                UpdateView();
                return EnergyTable.MoveEnergy;
            }
            case FeelingCommand or InscribeCommand or UninscribeCommand or ToggleUnignoreCommand or IgnoreCommand:
                return null; // things that take no time leave the command in place
            default:
                ReleaseCommand(announce: true);
                return null;
        }
    }

    /// <summary>A commanded monster attacks another with its blows (the player earns no experience for it).</summary>
    private void CommandedAttack(Monster attacker, Monster victim)
    {
        var damage = attacker.Race.Blows.Sum(b => b.Damage.Roll(Rng));
        Publish(new MessageEvent($"{Capitalize(MonsterName(attacker))} attacks {MonsterName(victim)}{DamageNote(damage)}."));
        WakeMonster(victim);
        victim.Hp -= damage;
        if (victim.Hp >= 0) return;
        if (victim.IsVisible) Publish(new MessageEvent($"{Capitalize(MonsterName(victim))} dies."));
        if (victim.Race.IsUnique) KilledUniques.Add(victim.Race.Id);
        Level.Monsters.Remove(victim);
        DropCarried(victim);
        DropMonsterLoot(victim);
        UpdateView();
    }

    // --- Single Combat (paladins) --------------------------------------------------------------------

    /// <summary>The level left behind while fighting in an arena, and where the player stood on it.</summary>
    public (Level Level, KnownMap Known, Loc Position)? ArenaReturn { get; private set; }

    public bool InArena => ArenaReturn is not null;

    internal void RestoreArena(Level level, KnownMap known, Loc position) => ArenaReturn = (level, known, position);

    /// <summary>
    /// Angband SINGLE_COMBAT: the player and the targeted monster are sealed in a stone cell (the
    /// "arena") until one of them dies; monsters of great power may resist. The level waits.
    /// </summary>
    private void SingleCombat()
    {
        if (InArena)
        {
            Publish(new MessageEvent("You are already in single combat!"));
            return;
        }
        if (AimedMonster() is not { } foe)
        {
            Publish(new MessageEvent("No monster selected!"));
            return;
        }
        // Angband: randint0(spell power) > player level resists; AVABand uses the monster's level.
        if (Rng.RandInt0(Math.Max(1, foe.Race.Depth)) > Player.Level)
        {
            Publish(new MessageEvent($"{Capitalize(MonsterName(foe))} resists!"));
            return;
        }

        ArenaReturn = (Level, Known, Player.Position);
        Level.Monsters.Remove(foe);
        foe.IsRemoved = false;
        var arena = BuildArena(Level.Depth);
        foe.Position = new Loc(arena.Width - 4, arena.Height / 2);
        arena.Monsters.Restore(foe);
        WakeMonster(foe);

        Level = arena;
        Known = new KnownMap(arena.Width, arena.Height);
        Known.RememberAll(arena);
        Scent.Reset(arena);
        Player.Position = new Loc(3, arena.Height / 2);
        ClearTarget();
        SetTarget(foe);
        Scheduler.Clear();
        Scheduler.Add(Player);
        Scheduler.Add(foe);
        Publish(new MessageEvent($"You and {MonsterName(foe)} are sealed in a cell of stone. Only one of you will leave it."));
        UpdateView();
    }

    /// <summary>A lit room of permanent rock, 15 × 9.</summary>
    private Level BuildArena(int depth)
    {
        var level = new Level(Data.Terrain, 15, 9, depth) { ProfileId = "arena" };
        foreach (var p in level.AllLocs())
        {
            var edge = p.X == 0 || p.Y == 0 || p.X == level.Width - 1 || p.Y == level.Height - 1;
            level[p].Feature = edge ? Data.Terrain.Ids.Permanent : Data.Terrain.Ids.Floor;
            level[p].Flags |= SquareFlags.Glow | SquareFlags.Room;
        }
        return level;
    }

    /// <summary>The fight is over: back to the level (and the square) the player left.</summary>
    private void LeaveArena()
    {
        if (ArenaReturn is not { } back) return;
        ArenaReturn = null;
        Level = back.Level;
        Known = back.Known;
        Scent.Reset(Level);
        Player.Position = back.Position;
        if (Level.Monsters.At(Player.Position) is { } squatter) TeleportMonster(squatter, 10);
        ClearTarget();
        Scheduler.Clear();
        Scheduler.Add(Player);
        foreach (var m in Level.Monsters.All) Scheduler.Add(m);
        Publish(new MessageEvent("The walls of the cell fall away. You have won the fight!"));
        UpdateView();
    }

    /// <summary>Things that can't be done from an arena (Angband: no recall, no leaving the level).</summary>
    private bool ArenaForbids()
    {
        if (!InArena) return false;
        Publish(new MessageEvent("Nothing happens."));
        return true;
    }
}
