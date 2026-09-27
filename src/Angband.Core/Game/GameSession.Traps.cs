using System.Text.RegularExpressions;
using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Generation;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Randomness;
using Angband.Core.Time;
using Angband.Core.World;

namespace Angband.Core.Game;

// Traps and chests (Angband trap.c, obj-chest.c, cmd-cave.c): floor traps go off when walked onto
// and can be disarmed; chests are locked and often trapped, picked open or disarmed with the disarm
// skill, and spill their treasure when opened.
public sealed partial class GameSession
{
    // --- Trap effects --------------------------------------------------------------------------

    /// <summary>
    /// Runs a trap effect string: <c>damage:2d6</c>, <c>cut</c>, <c>timed:poisoned:10+1d20</c>,
    /// <c>drain:str</c>, <c>element:fire:4d6</c>, <c>teleport:100</c>, <c>summon:2+1d3</c>,
    /// <c>aggravate</c>, <c>fall_through</c> (separated by <c>;</c>).
    /// </summary>
    private void ApplyTrapEffects(string effects, string killer)
    {
        foreach (var e in ItemEffects.Parse(effects))
        {
            if (Player.IsDead) return;
            switch (e.Name)
            {
                case "damage":
                    TakeHit(Amount(e.Arg(0)), killer);
                    break;
                case "cut":
                    IncreaseTimed(TimedIds.Cut, Rng.Damroll(2, 6));
                    break;
                case "timed":
                    var protection = e.Arg(0) switch
                    {
                        "poisoned" => "pois", "confused" => "conf", "paralyzed" => "free_act", "blind" => "blind",
                        "afraid" => "fear", "slow" => "free_act", _ => null,
                    };
                    if (protection is not null && Player.Resists.GetValueOrDefault(protection) > 0)
                        Publish(new MessageEvent("You are unaffected!"));
                    else IncreaseTimed(e.Arg(0), Amount(e.Arg(1)));
                    break;
                case "drain":
                    DrainStat(e.Arg(0));
                    break;
                case "element":
                    ElementalHit(e.Arg(0), Amount(e.Arg(1)), killer);
                    break;
                case "teleport":
                    TeleportPlayer(e.Int(0));
                    break;
                case "summon":
                    SummonNearPlayer(Amount(e.Arg(0)), null);
                    break;
                case "aggravate":
                    foreach (var m in Level.Monsters.All) m.Sleep = 0;
                    Publish(new MessageEvent("A high-pitched shriek fills the air!"));
                    break;
                case "fall_through":
                    if (Player.Depth <= 0 || Player.Depth >= Data.Constants.MaxDepth || QuestAt(Player.Depth) is not null) break;
                    Publish(new MessageEvent("You fall through a trap door!"));
                    TakeHit(Rng.Damroll(2, 8), "a trap door");
                    if (!Player.IsDead) ChangeLevel(Player.Depth + 1, StairArrival.None);
                    return;
            }
        }
    }

    /// <summary>"10+1d20", "2d6", "5" → a roll.</summary>
    private int Amount(string text)
    {
        var normal = Regex.Replace(text.Replace(" ", ""), @"^(\d+)\+(\d*)d(\d+)$", m => $"{(m.Groups[2].Value.Length == 0 ? "1" : m.Groups[2].Value)}d{m.Groups[3].Value}+{m.Groups[1].Value}");
        return Math.Max(0, Dice.Parse(normal).Roll(Rng));
    }

    /// <summary>The player walks onto a trap (Angband hit_trap): it becomes known and goes off.</summary>
    private void HitTrap(Loc p)
    {
        ref var sq = ref Level[p];
        if (sq.Trap == 0 || Data.TrapByIndex(sq.Trap) is not { } trap || trap.Warding) return;
        sq.Flags |= SquareFlags.TrapVisible;

        // Darts can miss (Angband: a 125-power attack against your armour).
        if (trap.Id.Contains("dart") && !CombatMath.TestHit(Rng, 125, Player.Armour, visible: true))
        {
            Publish(new MessageEvent($"{Capitalize(Article(trap.Name))} whizzes past you."));
            return;
        }
        Publish(new MessageEvent($"You set off {Article(trap.Name)}!"));
        Publish(new TrapSprungEvent(p, trap.Id));
        ApplyTrapEffects(trap.Effect, Article(trap.Name));
    }

    // --- Disarming -------------------------------------------------------------------------------

    /// <summary>The disarm skill, cut to a tenth when blind, in the dark or confused (Angband).</summary>
    public int EffectiveDisarmSkill
    {
        get
        {
            var skill = Player.DisarmSkill;
            if (Player.IsBlind || !Level[Player.Position].Has(SquareFlags.Seen)) skill /= 10;
            if (Player.Timed.Has(TimedIds.Confused)) skill /= 10;
            return skill;
        }
    }

    /// <summary>Disarm a chest or a known trap in a direction (or underfoot).</summary>
    private int Disarm(Direction dir)
    {
        var p = Player.Position.Step(dir);
        if (!Level.InBounds(p)) return 0;
        if (ChestAt(p, trapped: true) is { } chest) return DisarmChest(chest, p);
        if (Level[p].Trap != 0 && Level[p].Has(SquareFlags.TrapVisible)) return DisarmTrap(p);
        if (ChestAt(p, trapped: false) is not null)
        {
            Publish(new MessageEvent("The chest is not trapped."));
            return 0;
        }
        Publish(new MessageEvent("You see nothing there to disarm."));
        return 0;
    }

    /// <summary>Angband do_cmd_disarm_aux: skill less the trap's power; failing badly sets it off.</summary>
    private int DisarmTrap(Loc p)
    {
        var trap = Data.TrapByIndex(Level[p].Trap)!;
        var chance = Math.Max(2, EffectiveDisarmSkill - trap.DisarmPower);
        if (Rng.RandInt0(100) < chance)
        {
            Publish(new MessageEvent($"You have disarmed the {trap.Name}."));
            Level[p].Trap = 0;
            Level[p].Flags &= ~SquareFlags.TrapVisible;
            GainExperience(trap.DisarmPower);
        }
        else if (Rng.RandInt0(100) < chance)
            Publish(new MessageEvent($"You failed to disarm the {trap.Name}."));
        else
        {
            Publish(new MessageEvent($"You set off the {trap.Name}!"));
            ApplyTrapEffects(trap.Effect, Article(trap.Name));
        }
        return EnergyTable.MoveEnergy;
    }

    // --- Chests ----------------------------------------------------------------------------------

    /// <summary>The first chest on a square (full ones; or specifically trapped ones).</summary>
    public Item? ChestAt(Loc p, bool trapped = false) =>
        Level.Objects.At(p).FirstOrDefault(i => i.IsChest && (trapped ? i.ChestState > 1 : i.ChestState != 0));

    /// <summary>Angband do_cmd_disarm_chest.</summary>
    private int DisarmChest(Item chest, Loc p)
    {
        // AVABand has one disarm skill for mechanical and magical traps alike.
        var chance = Math.Max(2, EffectiveDisarmSkill - chest.ChestState);
        if (Rng.RandInt0(100) < chance)
        {
            Publish(new MessageEvent("You have disarmed the chest."));
            GainExperience(chest.ChestState);
            chest.ChestState = -chest.ChestState;
        }
        else if (Rng.RandInt0(100) < chance)
            Publish(new MessageEvent("You failed to disarm the chest."));
        else
        {
            Publish(new MessageEvent("You set off a trap!"));
            SpringChestTraps(chest);
        }
        return EnergyTable.MoveEnergy;
    }

    /// <summary>
    /// Angband do_cmd_open_chest: a locked chest must be picked (skill less its trap value); once
    /// open its traps go off, then whatever is inside spills out.
    /// </summary>
    private int OpenChest(Item chest, Loc p)
    {
        if (chest.ChestState > 0)
        {
            var chance = Math.Max(2, EffectiveDisarmSkill - chest.ChestState);
            if (Rng.RandInt0(100) >= chance)
            {
                Publish(new MessageEvent("You failed to pick the lock."));
                Publish(new LockPickFailedEvent(p));
                return EnergyTable.MoveEnergy;
            }
            Publish(new MessageEvent("You have picked the lock."));
            Publish(new LockPickedEvent(p));
            GainExperience(1);
            SpringChestTraps(chest);
        }
        if (!Player.IsDead) ChestDeath(chest, p);
        return EnergyTable.MoveEnergy;
    }

    private IEnumerable<ChestTrapDef> TrapsOf(Item chest) =>
        chest.ChestState > 1 ? Data.ChestTraps.Where(t => t.Bit > 1 && (chest.ChestState & t.Bit) != 0) : [];

    /// <summary>Angband chest_trap: every trap on the chest goes off (an explosion destroys the contents).</summary>
    private void SpringChestTraps(Item chest)
    {
        foreach (var trap in TrapsOf(chest).ToList())
        {
            if (Player.IsDead) return;
            if (trap.Message.Length > 0) Publish(new MessageEvent(trap.Message));
            ApplyTrapEffects(trap.Effect, trap.DeathMessage);
            if (trap.Destroy) chest.ChestState = 0;
        }
    }

    /// <summary>
    /// Angband chest_death: wooden chests hold one object, iron two, steel three — good ones, from
    /// five levels deeper than where the chest was found (great ones for large chests).
    /// </summary>
    private void ChestDeath(Item chest, Loc p)
    {
        if (chest.ChestState == 0)
        {
            Publish(new MessageEvent("The chest is empty."));
            return;
        }
        var name = chest.Kind.Name.ToLowerInvariant();
        var number = name.Contains("wooden") ? 1 : name.Contains("iron") ? 2 : name.Contains("steel") ? 3 : Rng.RandInt1(3);
        var large = name.Contains("large");
        var level = Math.Max(chest.OriginDepth, Level.Depth) + 5;
        for (var tries = 0; number > 0 && tries < 50; tries++)
        {
            if (Objects.Make(Rng, level, good: true, great: large) is not { } treasure || treasure.IsChest) continue;
            treasure.OriginDepth = chest.OriginDepth;
            DropNear(treasure, p);
            number--;
        }
        chest.ChestState = 0;
        Publish(new MessageEvent("You open the chest."));
        UpdateView();
    }
}
