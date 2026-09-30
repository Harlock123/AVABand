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
    /// Runs a trap effect string (Angband trap.txt effects, separated by <c>;</c>): <c>damage:2d6</c>,
    /// <c>timed:poisoned:10+1d20</c> (unless protected), <c>timed_nores:slow:20+1d20</c>,
    /// <c>drain:str</c>, <c>element:fire:4d6</c> (on you), <c>spot:shards:2:30</c> (a blast around the
    /// trap: you, the monsters near it, and <c>kill_wall</c> for walls), <c>teleport:M80</c>,
    /// <c>summon:1d3:UNDEAD:boost</c>, <c>wake</c>, <c>aggravate</c>, <c>project_los:haste:25</c>,
    /// <c>earthquake:5</c>, <c>rubble</c>, <c>granite</c>, <c>drain_light:100+1d100</c>,
    /// <c>drain_mana:1d10</c>, <c>cut</c>. <c>{D/2}</c> is worked out from the dungeon level
    /// (Angband's DUNGEON_LEVEL expressions).
    /// </summary>
    /// <summary>The verbs <see cref="ApplyTrapEffects"/> knows (traps, chest traps and curses use them).</summary>
    public static readonly IReadOnlySet<string> TrapEffectVerbs = new HashSet<string>(StringComparer.Ordinal)
    {
        "damage", "cut", "timed", "timed_nores", "drain", "element", "spot", "teleport", "summon", "aggravate", "wake",
        "project_los", "earthquake", "rubble", "granite", "drain_light", "drain_mana",
    };

    private void ApplyTrapEffects(string effects, string killer, Loc? at = null)
    {
        var depth = Math.Max(1, Level.Depth);
        var center = at ?? Player.Position;
        effects = Regex.Replace(effects, @"\{([^{}]*)\}", m =>
            Math.Max(0, Magic.SpellExpr.Eval(m.Groups[1].Value.Replace('D', 'L'), depth)).ToString(System.Globalization.CultureInfo.InvariantCulture));
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
                    if (Protected(e.Arg(0))) Publish(new MessageEvent("You are unaffected!"));
                    else IncreaseTimed(e.Arg(0), Amount(e.Arg(1)));
                    break;
                case "timed_nores":
                    IncreaseTimed(e.Arg(0), Amount(e.Arg(1)), check: false);
                    break;
                case "drain":
                    DrainStat(e.Arg(0));
                    break;
                case "element":
                    ElementalHit(e.Arg(0), Amount(e.Arg(1)), killer);
                    break;
                case "spot":
                    TrapBlast(e.Arg(0), e.Int(1), Amount(e.Arg(2)), killer, center);
                    break;
                case "teleport":
                    TeleportPlayer(TeleportDistance(e.Arg(0)));
                    break;
                case "summon":
                    SummonNearPlayer(Amount(e.Arg(0)), e.Arg(1), e.Int(2));
                    break;
                case "aggravate":
                    foreach (var m in Level.Monsters.All) m.Sleep = 0;
                    Publish(new MessageEvent("A high-pitched shriek fills the air!"));
                    break;
                case "wake":
                    WakeAround(center);
                    break;
                case "project_los":
                    AffectMonstersInView(e.Arg(0), Math.Max(1, e.Int(1)));
                    break;
                case "earthquake":
                    Earthquake(Math.Max(1, e.Int(0)), center);
                    break;
                case "rubble":
                    RubbleAround();
                    break;
                case "granite":
                    Level[center].Feature = Data.Terrain.Ids.Granite;
                    Level[center].Trap = 0;
                    Known.Forget(center);
                    UpdateView();
                    break;
                case "drain_light":
                    DrainLight(Amount(e.Arg(0)));
                    break;
                case "drain_mana":
                    DrainManaByTrap(Amount(e.Arg(0)));
                    break;
            }
        }
    }

    /// <summary>"10+1d20", "2d6", "1d3M2", "5" → a roll (Angband random values).</summary>
    private int Amount(string text) => Math.Max(0, RandomValue.Parse(text).Roll(Rng, Math.Max(1, Level.Depth)));

    /// <summary>
    /// A trap's blast (Angband SPOT/BALL from a trap): an element over a radius around the trap,
    /// weaker with distance, hurting you and the monsters near it; <c>kill_wall</c> turns the walls
    /// in reach to floor.
    /// </summary>
    private void TrapBlast(string element, int radius, int damage, string killer, Loc center)
    {
        var area = BallArea(center, radius);
        if (element == "kill_wall")
        {
            foreach (var p in area.Where(p => p != center && Level.InBoundsFully(p)))
            {
                var f = Level.FeatureAt(p);
                if (f.Has(TerrainFlags.Permanent) || f.Has(TerrainFlags.Passable) && !f.Has(TerrainFlags.Rubble)) continue;
                if (f.HasAny(TerrainFlags.DoorAny) || f.Has(TerrainFlags.Stair) || f.Shop is not null) continue;
                Level[p].Feature = Data.Terrain.Ids.Floor;
                Known.Forget(p);
            }
            UpdateView();
            return;
        }
        ShowProjection(center, [], area, element, ProjectionKind.Ball);
        foreach (var monster in Level.Monsters.All
                     .Where(m => m.Position.DistanceTo(center) <= radius && ProjectionPath.Projectable(Level, center, m.Position, radius + 1))
                     .OrderBy(m => m.Position.DistanceTo(center)).ThenBy(m => m.Id).ToList())
            ProjectileHitsMonster(monster, killer, element, BallDamage(damage, monster.Position.DistanceTo(center)), monster.Position.DistanceTo(center));
        if (Player.Position.DistanceTo(center) <= radius)
            ElementalHit(element, BallDamage(damage, Player.Position.DistanceTo(center)), killer);
        DestroyFloorObjects(area, element);
    }

    /// <summary>Angband effect_handler_WAKE: sleepers within twice the sight range stir.</summary>
    private void WakeAround(Loc origin)
    {
        var woken = false;
        foreach (var m in Level.Monsters.All.Where(m => m.IsAsleep && m.Position.DistanceTo(origin) < Data.Constants.MaxSight * 2))
        {
            m.Sleep = 0;
            woken = true;
        }
        if (woken) Publish(new MessageEvent("You hear a sudden stirring in the distance!"));
    }

    /// <summary>Angband effect_handler_RUBBLE: one to three of the empty squares around you fill with rubble.</summary>
    private void RubbleAround()
    {
        var wanted = Rng.RandInt1(3);
        for (var tries = 0; wanted > 0 && tries < 10; tries++)
        {
            foreach (var p in Level.Neighbors(Player.Position))
            {
                if (wanted == 0) break;
                if (!Level.InBoundsFully(p) || !Level.IsFloor(p) || Level[p].Monster != 0 || Level.Objects.Any(p) || !Rng.OneIn(3)) continue;
                Level[p].Feature = Rng.OneIn(2) ? Data.Terrain.Ids.PassableRubble : Data.Terrain.Ids.Rubble;
                wanted--;
            }
        }
        UpdateView();
    }

    /// <summary>Angband effect_handler_DRAIN_LIGHT: a light that burns fuel loses some (never all).</summary>
    private void DrainLight(int drain)
    {
        if (Player.Inventory.Light is not { UsesFuel: true, Fuel: > 0 } light) return;
        light.Fuel = Math.Max(1, light.Fuel - drain);
        if (!Player.IsBlind) Publish(new MessageEvent("Your light dims."));
        RecalculateBonuses();
    }

    /// <summary>Angband effect_handler_DRAIN_MANA from a trap: the mana goes, up to all of it.</summary>
    private void DrainManaByTrap(int drain)
    {
        if (Player.Mana <= 0)
        {
            Publish(new MessageEvent("The draining fails."));
            return;
        }
        Player.Mana = Math.Max(0, Player.Mana - drain);
    }

    /// <summary>Traps noticed by sight this game (the hint about hidden traps waits for the first).</summary>
    public int TrapsFound { get; private set; }

    /// <summary>Angband pick_trap: no trap doors on a quest level or the bottom one.</summary>
    private bool TrapDoorsAllowedHere => Level.Depth < Data.Constants.MaxDepth && QuestAt(Level.Depth) is null;

    /// <summary>Immune to traps (Angband player_is_trapsafe): safety from traps (TRAPSAFE), trap-immune gear, or the eagle's shape.</summary>
    private bool TrapSafe => Player.Timed.Has("trapsafe") || Player.HasGearFlag(ItemFlags.TrapImmune);

    /// <summary>
    /// Angband hit_trap: a trap goes off — unless you are safe from traps, or saved by your gear
    /// (feather falling), your armour (darts) or a saving throw (mind blasts). Its extra effect
    /// follows one time in two. A trap door drops you a level, a pit pulls you in, and the trap is
    /// gone if it only works once (and one time in three anyway). <paramref name="delayed"/>: true
    /// when leaving the square (only DELAY traps go off), false when arriving (all others), null
    /// for both (a failed disarm).
    /// </summary>
    private void HitTrap(Loc p, bool? delayed = false)
    {
        if (Level[p].Trap == 0 || Data.TrapByIndex(Level[p].Trap) is not { IsTrap: true } trap) return;
        if (delayed is { } d && d != trap.Has("DELAY")) return;
        if (TrapSafe)
        {
            if (Player.HasGearFlag(ItemFlags.TrapImmune)) LearnRune(RuneIds.Flag(ItemFlags.TrapImmune));
            Level[p].Flags |= SquareFlags.TrapVisible;
            return;
        }

        Disturb();
        Publish(new TrapSprungEvent(p, trap.Id));
        if (trap.Message is { } message) Publish(new MessageEvent(message));
        else if (trap.MessageBad is null) Publish(new MessageEvent($"You set off {Article(trap.Name)}!"));

        var saved = false;
        foreach (var flag in trap.Save.Where(Player.HasGearFlag))
        {
            saved = true;
            LearnRune(RuneIds.Flag(flag));
        }
        if (trap.Has("SAVE_ARMOR") && !CombatMath.TestHit(Rng, 125, Player.Armour, visible: true)) saved = true;
        if (trap.Has("SAVE_THROW") && Rng.RandInt0(100) < Player.SkillSave) saved = true;

        var killer = Article(trap.Name);
        if (saved)
        {
            if (trap.MessageGood is { } good) Publish(new MessageEvent(good));
        }
        else
        {
            if (trap.MessageBad is { } bad) Publish(new MessageEvent(bad));
            ApplyTrapEffects(trap.Effect, killer, p);
            if (Player.IsDead || Level[p].Trap == 0) return;
            if (trap.Extra is { } extra && Rng.OneIn(2))
            {
                if (trap.MessageExtra is { } more) Publish(new MessageEvent(more));
                ApplyTrapEffects(extra, killer, p);
                if (Player.IsDead || Level[p].Trap == 0) return;
            }
        }

        if (trap.IsTrapDoor && Player.Depth > 0 && Player.Depth < Data.Constants.MaxDepth && QuestAt(Player.Depth) is null)
        {
            Publish(new FellEvent(DeepDescent: false, Player.Depth + 1));
            ChangeLevel(Player.Depth + 1, StairArrival.None);
            return;
        }
        if (trap.Has("PIT") && Player.Position != p && Level.IsPassable(p) && Level[p].Monster == 0)
        {
            var from = Player.Position;
            Player.Position = p;
            Publish(new PlayerMovedEvent(from, p));
            UpdateView();
        }
        if (trap.Has("ONETIME") || Rng.OneIn(3))
        {
            Level[p].Trap = 0;
            Level[p].TrapPower = 0;
            Level[p].Flags &= ~SquareFlags.TrapVisible;
        }
        else Level[p].Flags |= SquareFlags.TrapVisible;
    }

    // --- Disarming -------------------------------------------------------------------------------

    /// <summary>The disarm skill, cut to a tenth when blind or in the dark, and again when confused or hallucinating (Angband).</summary>
    public int EffectiveDisarmSkill => ConditionedDisarm(Player.DisarmSkill);

    /// <summary>
    /// Angband do_cmd_disarm_aux's skill: the magical one for runes, the physical one for the rest,
    /// cut to a tenth when blind, in the dark, confused or hallucinating.
    /// </summary>
    public int TrapDisarmSkill(TrapDef trap)
    {
        var skill = trap.IsRune ? Player.DisarmMagicSkill : Player.DisarmSkill;
        var t = Player.Timed;
        if (Player.IsBlind || !Level[Player.Position].Has(SquareFlags.Seen) || t.Has(TimedIds.Confused) || t.Has(TimedIds.Image))
            skill /= 10;
        return skill;
    }

    private int ConditionedDisarm(int skill)
    {
        if (Player.IsBlind || !Level[Player.Position].Has(SquareFlags.Seen)) skill /= 10;
        if (Player.Timed.Has(TimedIds.Confused) || Player.Timed.Has(TimedIds.Image)) skill /= 10;
        return skill;
    }

    /// <summary>Disarm a chest or a known trap in a direction (or underfoot).</summary>
    private int Disarm(Direction dir)
    {
        var p = Player.Position.Step(dir);
        if (!Level.InBounds(p)) return 0;
        if (ChestAt(p, trapped: true) is { } chest) return DisarmChest(chest, p);
        if (IsWebbed(p))
        {
            ClearWeb(p);
            Publish(new MessageEvent("You clear the web."));
            return EnergyTable.MoveEnergy;
        }
        if (Level[p].Trap != 0 && Level[p].Has(SquareFlags.TrapVisible)) return DisarmTrap(p);
        if (ChestAt(p, trapped: false) is not null)
        {
            Publish(new MessageEvent("The chest is not trapped."));
            return 0;
        }
        Publish(new MessageEvent("You see nothing there to disarm."));
        return 0;
    }

    /// <summary>
    /// Angband do_cmd_disarm_aux: the skill less the level's trap power (a fifth of the depth), at
    /// least 2%. Success is worth 1 + that power in experience; failing twice sets it off.
    /// </summary>
    private int DisarmTrap(Loc p)
    {
        var trap = Data.TrapByIndex(Level[p].Trap)!;
        var power = Level.Depth / 5;
        var chance = Math.Max(2, TrapDisarmSkill(trap) - power);
        if (Rng.RandInt0(100) < chance)
        {
            Publish(new MessageEvent($"You have disarmed the {trap.Name}."));
            Publish(new TrapDisarmedEvent(p, trap.Id));
            Level[p].Trap = 0;
            Level[p].TrapPower = 0;
            Level[p].Flags &= ~SquareFlags.TrapVisible;
            GainExperience(1 + power);
        }
        else if (Rng.RandInt0(100) < chance)
        {
            Publish(new MessageEvent($"You failed to disarm the {trap.Name}."));
            _more = true;
        }
        else
        {
            Publish(new MessageEvent($"You set off the {trap.Name}!"));
            HitTrap(p, delayed: null);
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
        {
            Publish(new MessageEvent("You failed to disarm the chest."));
            _more = true;
        }
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
                _more = true;
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
