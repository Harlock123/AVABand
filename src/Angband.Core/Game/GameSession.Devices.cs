using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Generation;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Time;
using Angband.Core.World;

namespace Angband.Core.Game;

// Magic devices (wands, staffs, rods) and the effects of the wider range of potions, scrolls and
// devices: aimed bolts and balls, monster status effects, area effects, stat and life restoration,
// enchanting, recharging, recall and descent (Angband obj-util.c / effects.c).
public sealed partial class GameSession
{
    /// <summary>Angband USE_DEVICE: the minimum chance a device works at all.</summary>
    private const int UseDevice = 3;

    // Aim of the effect being applied (for wands and rods); null aims at the nearest monster.
    private Loc? _effectTarget;
    private Direction? _effectDirection;
    private string _effectSource = "";

    public static bool IsDevice(Item item) => item.Base.Id is "wand" or "staff" or "rod";

    /// <summary>Whether a device's effect needs aiming (at a monster or in a direction).</summary>
    public static bool NeedsAim(ObjectKindDef kind) => NeedsAim(kind.Effect);

    /// <summary>Whether it must be aimed in a direction rather than at a monster (stone to mud, light beams).</summary>
    public static bool NeedsDirection(ObjectKindDef kind) => NeedsDirection(kind.Effect);

    public static bool NeedsAim(string? effect) =>
        effect is { } e && ItemEffects.Parse(e).Any(x => AimedEffects.Contains(x.Name));

    public static bool NeedsDirection(string? effect) =>
        effect is { } e && ItemEffects.Parse(e).Any(x => x.Name is "stone_to_mud" or "light_line" or "disarm");

    private static readonly HashSet<string> AimedEffects =
    [
        "bolt", "beam", "ball", "monster_status", "teleport_other", "stone_to_mud", "light_line", "drain_life",
        "heal_monster", "haste_monster", "clone_monster", "polymorph", "disarm", "wonder", "breath",
    ];

    /// <summary>
    /// The device skill, less a twentieth (rounded up) while afraid (Angband calc_bonuses,
    /// adjust_skill_scale for OF_AFRAID).
    /// </summary>
    public int DeviceSkill => PlayerAfraid ? Player.SkillDevice - (Math.Abs(Player.SkillDevice) + 19) / 20 : Player.SkillDevice;

    /// <summary>Angband's device skill check (3.x use_device): harder the deeper the device.</summary>
    public int DeviceFailChance(Item item)
    {
        var chance = DeviceSkill;
        if (Player.Timed.Has(TimedIds.Confused)) chance /= 2;
        if (Player.Timed.Has(TimedIds.Image)) chance -= chance / 5; // Angband: hallucination costs a fifth
        if (Player.Timed.Has(TimedIds.Amnesia)) chance -= chance / 5; // and so does amnesia
        chance -= Math.Min(50, item.Kind.Level);
        if (chance < UseDevice) return 100 - 100 / (UseDevice - chance + 1) + 1;
        return Math.Clamp(UseDevice * 100 / chance, 1, 99);
    }

    /// <summary>Aim a wand, use a staff or zap a rod (Angband do_cmd_use).</summary>
    private int UseDeviceItem(Item item, Loc? target, Direction? direction)
    {
        var verb = item.Base.Id switch { "wand" => "aim", "staff" => "use", _ => "zap" };
        if (item.IsRod && !item.RodReady)
        {
            Publish(new MessageEvent(item.Number > 1 ? "The rods are all still charging." : "The rod is still charging."));
            return 0;
        }
        if (item.Base.Id != "rod" && item.Charges <= 0)
        {
            Publish(new MessageEvent($"The {item.Base.Name.ToLowerInvariant()} has no charges left."));
            item.Flags.Add("EMPTY");
            return 0;
        }
        if (NeedsDirection(item.Kind) && direction is null)
        {
            Publish(new MessageEvent("You must choose a direction."));
            return 0;
        }
        if (NeedsGlyph(item.Kind.Effect) && _effectGlyph is null)
        {
            Publish(new MessageEvent("You must choose a monster symbol to banish."));
            return 0;
        }
        if (NeedsCurseChoice(item.Kind.Effect) && Knowledge.KnowsKind(item) && UncursableItems().Count == 0)
        {
            Publish(new MessageEvent("You have no curses to remove."));
            return 0;
        }
        // Don't waste a charge on nothing: aimed devices need something to aim at (once known).
        if (NeedsAim(item.Kind) && !NeedsDirection(item.Kind) && target is null && AimPoint() is null
            && Knowledge.KnowsKind(item))
        {
            Publish(new MessageEvent("You have no target."));
            return 0;
        }

        // Angband: chance = skill - level; below USE_DEVICE it rarely works.
        var chance = DeviceSkill;
        if (Player.Timed.Has(TimedIds.Confused)) chance /= 2;
        if (Player.Timed.Has(TimedIds.Image)) chance -= chance / 5; // Angband: hallucination costs a fifth
        if (Player.Timed.Has(TimedIds.Amnesia)) chance -= chance / 5; // and so does amnesia
        chance -= Math.Min(50, item.Kind.Level);
        if (chance < UseDevice && Rng.OneIn(UseDevice - chance + 1)) chance = UseDevice;
        if (chance < UseDevice || Rng.RandInt1(chance) < UseDevice)
        {
            Publish(new MessageEvent($"You failed to use the {item.Base.Name.ToLowerInvariant()} properly."));
            return EnergyTable.MoveEnergy;
        }

        var wasAware = Knowledge.KnowsKind(item);
        Publish(new ItemUsedEvent(item.Kind.Id, verb));
        _effectTarget = target;
        _effectDirection = direction;
        _effectSource = Knowledge.KnowsKind(item) ? item.Kind.Name.Replace("~", "") : "magic";
        bool obvious;
        try
        {
            obvious = ApplyEffects(item.Kind.Effect!);
        }
        finally
        {
            _effectTarget = null;
            _effectDirection = null;
            _effectSource = "";
        }

        // A zapped rod adds its recharge time to the stack's (Angband: the stack's rods recharge together).
        if (item.IsRod) item.Timeout += Math.Max(1, RandomValue.Parse(item.Kind.Recharge ?? "10").Roll(Rng, 0));
        else
        {
            item.Charges--;
            if (item.Charges <= 0) item.Flags.Add("EMPTY");
        }

        if (!wasAware)
        {
            if (obvious && Knowledge.LearnKind(item.Kind))
                Publish(new MessageEvent($"You have learned that it was {Describe(item)}."));
            else
            {
                Knowledge.MarkTried(item.Kind);
                Publish(new MessageEvent("You have no more idea what it does."));
            }
        }
        RecalculateBonuses();
        return EnergyTable.MoveEnergy;
    }

    /// <summary>
    /// Activate a worn item (Angband do_cmd_activate): like a device, it can fail (the harder the
    /// deeper the item), then recharges before it can be used again.
    /// </summary>
    private int Activate(Item item, Loc? target, Direction? direction)
    {
        if (!Player.Inventory.Equipped.Contains(item))
        {
            Publish(new MessageEvent("You must be wearing it to activate it."));
            return 0;
        }
        if (item.Activation is not { } effect)
        {
            Publish(new MessageEvent("That item has no activation."));
            return 0;
        }
        if (item.Timeout > 0)
        {
            Publish(new MessageEvent("It whines, glows and fades..."));
            return 0;
        }
        if (NeedsDirection(effect) && direction is null)
        {
            Publish(new MessageEvent("You must choose a direction."));
            return 0;
        }
        if (NeedsGlyph(effect) && _effectGlyph is null)
        {
            Publish(new MessageEvent("You must choose a monster symbol to banish."));
            return 0;
        }
        if (NeedsAim(effect) && !NeedsDirection(effect) && target is null && AimPoint() is null)
        {
            Publish(new MessageEvent("You have no target."));
            return 0;
        }

        // Angband: the activation's level makes it harder, as for other devices.
        var level = item.Artifact?.Level ?? item.Kind.Level;
        var chance = DeviceSkill;
        if (Player.Timed.Has(TimedIds.Confused)) chance /= 2;
        if (Player.Timed.Has(TimedIds.Image)) chance -= chance / 5; // Angband: hallucination costs a fifth
        if (Player.Timed.Has(TimedIds.Amnesia)) chance -= chance / 5; // and so does amnesia
        chance -= Math.Min(50, level);
        if (chance < UseDevice && Rng.OneIn(UseDevice - chance + 1)) chance = UseDevice;
        if (chance < UseDevice || Rng.RandInt1(chance) < UseDevice)
        {
            Publish(new MessageEvent("You failed to activate it properly."));
            return EnergyTable.MoveEnergy;
        }

        Publish(new MessageEvent($"You activate {Describe(item)}..."));
        Publish(new ItemUsedEvent(item.Kind.Id, "activate"));
        _effectTarget = target;
        _effectDirection = direction;
        _effectSource = item.Artifact?.Name.Trim('\'') ?? item.Kind.Name.Replace("~", "");
        try
        {
            ApplyEffects(effect);
        }
        finally
        {
            _effectTarget = null;
            _effectDirection = null;
            _effectSource = "";
        }
        var recharge = item.Artifact?.Activation is not null ? item.Artifact.Recharge : item.Kind.Recharge;
        item.Timeout = Math.Max(1, RandomValue.Parse(recharge ?? "10").Roll(Rng, 0));
        RecalculateBonuses();
        return EnergyTable.MoveEnergy;
    }

    /// <summary>Rods and activated gear recharge over time; recall and deep descent count down (every world tick).</summary>
    private void DeviceUpkeep()
    {
        foreach (var rod in Player.Inventory.Pack.Where(i => i.Timeout > 0))
            if (rod.Recharge() && Knowledge.KnowsKind(rod) && Options[OptionIds.NotifyRecharge])
                Publish(new MessageEvent(rod.Timeout > 0 && rod.Number > 1
                    ? $"One of your {Describe(rod, withArticle: false)} has recharged."
                    : $"Your {Describe(rod, withArticle: false)} {(rod.Number > 1 ? "have" : "has")} recharged."));
        foreach (var worn in Player.Inventory.Equipped.Where(i => i.Timeout > 0))
            if (--worn.Timeout == 0 && Options[OptionIds.NotifyRecharge])
                Publish(new MessageEvent($"Your {Describe(worn, withArticle: false)} has recharged."));

        if (Player.RecallTimer > 0 && --Player.RecallTimer == 0)
        {
            if (Player.Depth > 0)
            {
                Publish(new MessageEvent("You feel yourself yanked upwards!"));
                Publish(new RecalledEvent(Up: true));
                ChangeLevel(0, StairArrival.None);
            }
            else
            {
                Publish(new MessageEvent("You feel yourself yanked downwards!"));
                Publish(new RecalledEvent(Up: false));
                // Forced descent recalls one level below the deepest reached (Angband player_set_recall_depth).
                var depth = ForceDescend && QuestAt(Player.MaxDepth) is null ? DescentTarget(Player.MaxDepth)
                    : Player.RecallDepth > 0 ? Math.Min(Player.RecallDepth, Player.MaxDepth) : Player.MaxDepth;
                ChangeLevel(Math.Clamp(depth, 1, Data.Constants.MaxDepth), StairArrival.None);
            }
        }
        if (Player.DeepDescentTimer > 0 && --Player.DeepDescentTimer == 0)
        {
            var target = CapDepth(Player.Depth, Math.Min(Data.Constants.MaxDepth, Math.Max(Player.Depth, 1) + 5));
            Publish(new MessageEvent("The floor opens beneath you!"));
            Publish(new FellEvent(DeepDescent: true, target));
            ChangeLevel(target, StairArrival.None);
        }
    }

    // --- Effects beyond potions and scrolls ---------------------------------------------------

    /// <summary>The effects added for devices and the fuller item list. Returns true if noticeable; null if unknown.</summary>
    private bool? ApplyMoreEffects(ItemEffect e)
    {
        var name = _effectSource.Length > 0 ? _effectSource : "magic";
        switch (e.Name)
        {
            case "bolt":
                return ProjectBoltEffect(name, _effectTarget,
                    m => ProjectileHitsMonster(m, name, ElementArg(e.Arg(0)), e.Dice(1).Roll(Rng))) || true;
            case "beam":
                ProjectBeam(name, ElementArg(e.Arg(0)), e.Dice(1).Roll(Rng), _effectTarget);
                return true;
            case "ball":
                ProjectBall(name, ElementArg(e.Arg(0)), e.Dice(1).Roll(Rng), Math.Max(1, e.Int(2)), _effectTarget);
                return true;
            case "breath":
                // Dragon's Flame and friends: a big ball at the target.
                ProjectBall(name, ElementArg(e.Arg(0)), e.Dice(1).Roll(Rng), 2, _effectTarget);
                return true;
            case "monster_status":
                return ProjectBoltEffect(name, _effectTarget, m => AffectMonster(m, e.Arg(0), Math.Max(1, e.Int(1)))) || true;
            case "teleport_other":
                return ProjectBoltEffect(name, _effectTarget, m =>
                {
                    Publish(new MessageEvent($"{Capitalize(MonsterName(m))} disappears!"));
                    TeleportMonster(m, 100);
                    UpdateView();
                }) || true;
            case "drain_life":
                return ProjectBoltEffect(name, _effectTarget, m =>
                {
                    if (m.Race.Has(MonsterFlags.Undead) || m.Race.Has("NONLIVING"))
                        Publish(new MessageEvent($"{Capitalize(MonsterName(m))} is unaffected!"));
                    else ProjectileHitsMonster(m, name, null, e.Dice(0).Roll(Rng));
                }) || true;
            case "heal_monster":
                return ProjectBoltEffect(name, _effectTarget, m =>
                {
                    m.Hp = Math.Min(m.MaxHp, m.Hp + e.Dice(0).Roll(Rng));
                    Publish(new MessageEvent($"{Capitalize(MonsterName(m))} looks healthier."));
                }) || true;
            case "haste_monster":
                return ProjectBoltEffect(name, _effectTarget, m =>
                {
                    m.Fast = 50;
                    Publish(new MessageEvent($"{Capitalize(MonsterName(m))} starts moving faster."));
                }) || true;
            case "clone_monster":
                return ProjectBoltEffect(name, _effectTarget, CloneMonster) || true;
            case "polymorph":
                return ProjectBoltEffect(name, _effectTarget, PolymorphMonster) || true;
            case "stone_to_mud":
                StoneToMud(_effectDirection ?? Direction.Here);
                return true;
            case "light_line":
                LightLine(_effectDirection ?? Direction.Here, e.Dice(0));
                return true;
            case "disarm":
                return DisarmLine(_effectDirection ?? Direction.Here);
            case "wonder":
            {
                string[] wonders =
                [
                    "bolt:fire:6d8", "bolt:cold:5d8", "bolt:elec:3d8", "bolt:acid:5d8", "bolt:none:3d4",
                    "ball:pois:12:2", "ball:fire:72:2", "monster_status:slow:20", "monster_status:confuse:20",
                    "teleport_other", "heal_monster:4d6", "haste_monster", "clone_monster", "polymorph",
                ];
                return ApplyEffect(ItemEffects.Parse(Rng.Pick(wonders)).Single());
            }
            case "project_los":
                return AffectMonstersInView(e.Arg(0), Math.Max(1, e.Int(1)), NullIfNone(e.Arg(2)));
            case "dispel":
                return Dispel(NullIfNone(e.Arg(0)), e.Dice(1));
            case "detect_evil":
                return DetectMonsters(e.Int(0), "EVIL");
            case "detect_invisible":
                return DetectMonsters(e.Int(0), MonsterFlags.Invisible);
            case "detection":
            {
                var found = DetectMonsters(e.Int(0), null);
                found |= DetectObjects(e.Int(0));
                return found;
            }
            case "enlightenment":
                Known.RememberAll(Level);
                DetectObjects(999);
                Publish(new MessageEvent("An image of your surroundings forms in your mind..."));
                UpdateView();
                return true;
            case "darkness":
                Darken(Player.Position, 3);
                if (Player.Resists.GetValueOrDefault("dark") <= 0 && Player.Resists.GetValueOrDefault("blind") <= 0)
                    IncreaseTimed(TimedIds.Blind, 3 + Rng.RandInt1(5));
                return true;
            case "aggravate":
                foreach (var m in Level.Monsters.All) m.Sleep = 0;
                Publish(new MessageEvent("There is a high pitched humming noise."));
                return true;
            case "summon":
                return SummonNearPlayer(Math.Max(1, e.Dice(0).Roll(Rng)), e.Arg(1), e.Int(2));
            case "trap_creation":
                CreateTrapsAround(Player.Position);
                return true;
            case "restore_mana":
                if (Player.Mana >= Player.MaxMana) return false;
                Player.Mana = Player.MaxMana;
                Player.ManaFraction = 0;
                Publish(new MessageEvent("You feel your head clear."));
                return true;
            case "gain_stat":
                return GainStat(e.Arg(0));
            case "drain_stat":
                // "!str": a random stat other than strength (the price of a potion of Brawn).
                return DrainStat(e.Arg(0).StartsWith('!')
                    ? Rng.Pick(CharacterSpec.StatIds.Where(s => s != e.Arg(0)[1..]).ToList())
                    : e.Arg(0));
            case "set_food":
                SetFood(e.Int(0));
                return true;
            case "restore_stat":
            {
                var any = false;
                foreach (var stat in e.Arg(0) == "all" ? CharacterSpec.StatIds : [e.Arg(0)]) any |= RestoreStat(stat);
                return any;
            }
            case "restore_exp":
                return RestoreExperience();
            case "gain_exp":
            {
                var amount = Math.Max(Player.Experience / 2, e.Int(0));
                Publish(new MessageEvent("You feel more experienced."));
                GainExperience(Math.Min(amount, 100_000));
                return true;
            }
            case "recall":
                return ToggleRecall();
            case "deep_descent":
                if (ArenaForbids() || Player.DeepDescentTimer > 0) return false;
                Player.DeepDescentTimer = 5;
                Publish(new MessageEvent("The air around you starts to swirl..."));
                return true;
            case "teleport_level":
                TeleportPlayerLevel();
                return true;
            case "enchant":
                return Enchant(e.Arg(0), Math.Max(1, e.Dice(1).Roll(Rng)));
            case "recharge":
                return Recharge(Math.Max(1, e.Int(0)));
            case "acquirement":
            {
                for (var i = 0; i < Math.Max(1, e.Int(0)); i++)
                    if (Objects.Make(Rng, Math.Max(Level.Depth, 1), good: true, great: true, extraRoll: true) is { } item) DropNear(item, Player.Position);
                Publish(new MessageEvent("Something appears at your feet."));
                return true;
            }
            case "destruction":
                Destruction(Math.Max(1, e.Int(0)));
                return true;
            case "earthquake":
                Earthquake(Math.Max(1, e.Int(0)));
                return true;
            case "mass_banishment":
            {
                var count = 0;
                foreach (var m in Level.Monsters.All.Where(m => !m.Race.IsUnique && m.Position.DistanceTo(Player.Position) <= e.Int(0)).ToList())
                {
                    Level.Monsters.Remove(m);
                    count++;
                }
                if (count > 0) TakeHit(Rng.RandInt1(3) * count / 3 + 1, "the strain of casting Mass Banishment");
                UpdateView();
                return true;
            }
            case "stun":
                return IncreaseTimed(TimedIds.Stun, e.Dice(0).Roll(Rng));
            default:
                return ApplySpecialEffect(e);
        }
    }

    /// <summary>An element argument; "acid/fire/cold" picks one at random (multi-hued dragon breath).</summary>
    private string? ElementArg(string element) =>
        NullIfNone(element.Contains('/') ? Rng.Pick(element.Split('/')) : element);

    /// <summary>Starts or cancels Word of Recall (15 + 1d20 turns).</summary>
    public bool ToggleRecall()
    {
        if (ArenaForbids()) return true;
        if (IsTutorial)
        {
            // The lesson: what it would do, without leaving the tutorial's one level.
            TutorialDone.Add("recall");
            Publish(new MessageEvent("The air about you becomes charged... In a real game, in 15 to 35 turns you'd be carried up to "
                                     + "the town — or from the town, down to your deepest level. Here, the tutorial goes on."));
            return true;
        }
        // Angband birth_no_recall (until Morgoth is dead), and no recall from a quest level with forced descent.
        if (Options[OptionIds.NoRecall] && !Player.IsWinner || ForceDescend && QuestAt(Player.Depth) is not null)
        {
            Publish(new MessageEvent("Nothing happens."));
            return true;
        }
        if (Player.RecallTimer > 0)
        {
            Player.RecallTimer = 0;
            Publish(new MessageEvent("A tension leaves the air around you..."));
            return true;
        }
        // Angband effect_handler_RECALL: set where it will take you.
        var setHere = RecallSetsDepth;
        var choice = RecallChoice;
        RecallSetsDepth = null;
        RecallChoice = null;
        if (Player.Depth > 0)
        {
            if (Player.Depth != Player.MaxDepth)
            {
                if (setHere == true) Player.RecallDepth = Player.MaxDepth = Player.Depth;
            }
            else Player.RecallDepth = 0; // the deepest level
        }
        else if (PersistentLevels && choice is { } level)
        {
            // Persistent dungeons: a level visited before (4.2 player_get_recall_depth).
            if (!RecallChoices.Contains(level))
            {
                Publish(new MessageEvent("You must choose a level you have previously visited."));
                return false;
            }
            Player.RecallDepth = level;
        }
        Player.RecallTimer = 15 + Rng.RandInt1(20);
        Publish(new MessageEvent("The air about you becomes charged..."));
        return true;
    }

    /// <summary>The answer to "Set recall depth to current depth?", for the next recall started below the deepest level.</summary>
    public bool? RecallSetsDepth
    {
        get => _recallSetsDepth;
        set
        {
            using var _ = Recorded("recall-sets-depth", value);
            _recallSetsDepth = value;
        }
    }
    private bool? _recallSetsDepth;

    /// <summary>The level chosen to return to, for the next recall started from town in a persistent dungeon.</summary>
    public int? RecallChoice
    {
        get => _recallChoice;
        set
        {
            using var _ = Recorded("recall-choice", value);
            _recallChoice = value;
        }
    }
    private int? _recallChoice;

    /// <summary>The levels a persistent dungeon's recall can take you to: those kept.</summary>
    public IReadOnlyList<int> RecallChoices => [.. _storedLevels.Keys.Where(d => d > 0).Order()];

    /// <summary>Whether a command would start (not cancel) Word of Recall: a recall scroll, rod, spell or activation.</summary>
    public bool StartsRecall(GameCommand command)
    {
        if (Player.RecallTimer > 0 || InArena) return false;
        var effect = command switch
        {
            UseCommand use => use.Item.Kind.Effect,
            ActivateCommand activate => activate.Item.Activation,
            CastCommand cast => Data.Spell(cast.SpellId)?.Effect,
            _ => null,
        };
        return effect is not null && effect.Split(';').Any(e => e.Trim().Split(':')[0] == "recall");
    }

    /// <summary>
    /// A monster status from a wand, rod or staff (slow, confuse, sleep, scare, stun, hold). Monsters
    /// resist according to their level (uniques more so) and their NO_* flags.
    /// </summary>
    private void AffectMonster(Monster m, string status, int power)
    {
        // Angband CHARM (druids): half as much again against animals.
        if (ClassHas("CHARM") && m.Race.Has("ANIMAL")) power += power / 2;
        var name = Capitalize(MonsterName(m));
        var race = m.Race;
        var immune = status switch
        {
            "confuse" => race.Has(MonsterFlags.NoConf),
            "sleep" or "hold" => race.Has(MonsterFlags.NoSleep) || race.Has("NO_HOLD"),
            "scare" => race.Has(MonsterFlags.NoFear),
            "stun" => race.Has(MonsterFlags.NoStun),
            "slow" => race.Has("NO_SLOW"),
            _ => false,
        };
        var resistChance = Math.Clamp(race.Depth + (race.IsUnique ? 20 : 0) + 25 - power, 5, 95);
        WakeMonster(m);
        if (immune)
            LearnMonsterFlag(race, status switch
            {
                "confuse" => MonsterFlags.NoConf, "sleep" or "hold" => MonsterFlags.NoSleep,
                "scare" => MonsterFlags.NoFear, "stun" => MonsterFlags.NoStun, _ => "NO_SLOW",
            });
        if (immune || Rng.Percent(resistChance))
        {
            Publish(new MessageEvent($"{name} is unaffected."));
            return;
        }
        switch (status)
        {
            case "slow":
                m.Slow = Math.Max(m.Slow, 10 + Rng.RandInt1(10));
                Publish(new MessageEvent($"{name} starts moving slower."));
                break;
            case "confuse":
                m.Confused = Math.Max(m.Confused, 10 + Rng.RandInt1(10));
                Publish(new MessageEvent($"{name} looks confused."));
                break;
            case "sleep":
                m.Sleep = Math.Max(m.Sleep, 50 + Rng.RandInt1(50));
                Publish(new MessageEvent($"{name} falls asleep!"));
                break;
            case "hold":
                m.Held = Math.Max(m.Held, 3 + Rng.RandInt1(5));
                Publish(new MessageEvent($"{name} is frozen to the spot!"));
                break;
            case "scare":
                m.Fear = Math.Max(m.Fear, 10 + Rng.RandInt1(10));
                Publish(new MessageEvent($"{name} flees in terror!"));
                break;
            case "stun":
                m.Stun = Math.Max(m.Stun, 5 + Rng.RandInt1(5));
                Publish(new MessageEvent($"{name} is dazed."));
                break;
        }
    }

    /// <summary>
    /// Effects on every monster in view (slow, sleep, confuse, scare, haste; <c>away</c> teleports
    /// them off), optionally only those with a flag (see <see cref="MatchesKind"/>).
    /// </summary>
    private bool AffectMonstersInView(string status, int power, string? kind = null)
    {
        var any = false;
        foreach (var m in Level.Monsters.All.Where(m => Level[m.Position].Has(SquareFlags.View) && MatchesKind(m, kind)).ToList())
        {
            any = true;
            if (status == "haste")
            {
                m.Fast = 50;
                if (m.IsVisible) Publish(new MessageEvent($"{Capitalize(MonsterName(m))} starts moving faster."));
            }
            else if (status == "away")
            {
                if (m.IsVisible) Publish(new MessageEvent($"{Capitalize(MonsterName(m))} disappears!"));
                TeleportMonster(m, power);
            }
            else AffectMonster(m, status, power);
        }
        if (status == "away") UpdateView();
        return any;
    }

    /// <summary>
    /// Whether a monster is of a kind: a race flag (EVIL, UNDEAD...), <c>LIVING</c> (neither undead
    /// nor nonliving) or <c>SPIRIT</c> (anything with a mind); null matches everything.
    /// </summary>
    private static bool MatchesKind(Monster m, string? kind) => kind switch
    {
        null => true,
        "LIVING" => !m.Race.Has(MonsterFlags.Undead) && !m.Race.Has("NONLIVING"),
        "SPIRIT" => !m.Race.Has("EMPTY_MIND"),
        _ => m.Race.Has(kind),
    };

    /// <summary>Damages every monster in view of a kind (null = all): Dispel Evil, Dispel Undead, Dispel Life...</summary>
    private bool Dispel(string? flag, Randomness.Dice dice)
    {
        var any = false;
        foreach (var m in Level.Monsters.All
                     .Where(m => Level[m.Position].Has(SquareFlags.View) && MatchesKind(m, flag))
                     .OrderBy(m => m.Id).ToList())
        {
            any = true;
            if (m.IsVisible) Publish(new MessageEvent($"{Capitalize(MonsterName(m))} shudders."));
            DamageMonster(m, dice.Roll(Rng), pain: true);
        }
        return any;
    }

    private void CloneMonster(Monster m)
    {
        var spot = Level.Neighbors(m.Position).Where(p => Level.IsPassable(p) && Level[p].Monster == 0 && p != Player.Position).ToList();
        if (m.Race.IsUnique || spot.Count == 0)
        {
            Publish(new MessageEvent($"{Capitalize(MonsterName(m))} is unaffected."));
            return;
        }
        var clone = _spawner.Place(Level, Rng, m.Race, Rng.Pick(spot), asleep: false);
        clone.Camouflaged = false;
        Scheduler.Add(clone);
        m.Hp = m.MaxHp;
        Publish(new MessageEvent($"{Capitalize(MonsterName(m))} spawns!"));
        UpdateView();
    }

    private void PolymorphMonster(Monster m)
    {
        if (m.Race.IsUnique || Rng.Percent(Math.Clamp(m.Race.Depth, 5, 90)))
        {
            Publish(new MessageEvent($"{Capitalize(MonsterName(m))} is unaffected."));
            return;
        }
        var race = _spawner.PickRace(Rng, Math.Max(1, m.Race.Depth + Rng.RandRange(-5, 5)), new HashSet<string>(KilledUniques),
            r => !r.IsUnique && r != m.Race);
        if (race is null) return;
        var at = m.Position;
        Level.Monsters.Remove(m);
        var changed = _spawner.Place(Level, Rng, race, at, asleep: false);
        Scheduler.Add(changed);
        changed.Camouflaged = false; // it changed before your eyes
        UpdateView();
        Publish(new MessageEvent($"The creature changes into {Article(race.Name)}!"));
    }

    /// <summary>A beam of light: lights the squares it crosses and burns light-sensitive monsters.</summary>
    private void LightLine(Direction dir, Randomness.Dice damage)
    {
        var p = Player.Position;
        for (var i = 0; i < MaxRange; i++)
        {
            p = p.Step(dir);
            if (!Level.InBounds(p) || !Level.Has(p, TerrainFlags.Project)) break;
            Level[p].Flags |= SquareFlags.Glow;
            if (Level.Monsters.At(p) is { } lit) LearnMonsterResponse(lit, "HURT_LIGHT");
            if (Level.Monsters.At(p) is { } m && m.Race.Has("HURT_LIGHT"))
            {
                Publish(new MessageEvent($"{Capitalize(MonsterName(m))} cringes from the light!"));
                DamageMonster(m, damage.Roll(Rng), pain: true);
            }
        }
        Publish(new MessageEvent("A line of light appears."));
        UpdateView();
    }

    private bool DisarmLine(Direction dir)
    {
        var p = Player.Position;
        var any = false;
        for (var i = 0; i < MaxRange; i++)
        {
            p = p.Step(dir);
            if (!Level.InBounds(p) || !Level.IsPassable(p)) break;
            if (Level[p].Trap == 0) continue;
            Level[p].Trap = 0;
            any = true;
        }
        if (any) Publish(new MessageEvent("You hear a click."));
        return any;
    }

    /// <summary>
    /// Enchant weapon to-hit/to-dam or armour (Angband enchant): the wielded weapon or a random worn
    /// armour piece; the higher the bonus, the likelier the enchantment fails.
    /// </summary>
    private bool Enchant(string what, int times)
    {
        var item = what is "to_h" or "to_d"
            ? Player.Inventory.Weapon
            : Player.Inventory.Equipped.Where(i => i.Base.Slot is not (EquipSlot.Weapon or EquipSlot.Bow or EquipSlot.Ring
                    or EquipSlot.Amulet or EquipSlot.Light)).OrderBy(_ => Rng.RandInt0(1000)).FirstOrDefault();
        if (item is null)
        {
            Publish(new MessageEvent("You have nothing to enchant."));
            return false;
        }
        var worked = false;
        for (var i = 0; i < times; i++)
        {
            var current = what switch { "to_h" => item.ToHit, "to_d" => item.ToDam, _ => item.ToAc };
            // Angband enchant_table: chance of failure grows with the bonus; artifacts resist more.
            var fail = current < 0 ? 0 : Math.Min(990, current * current * 7);
            if (item.IsArtifact) fail = Math.Max(fail, 500);
            if (Rng.RandInt0(1000) < fail) continue;
            switch (what)
            {
                case "to_h": item.ToHit++; break;
                case "to_d": item.ToDam++; break;
                default: item.ToAc++; break;
            }
            worked = true;
        }
        Publish(new MessageEvent(worked
            ? $"Your {Describe(item, withArticle: false)} glows brightly!"
            : "The enchantment failed."));
        RecalculateBonuses();
        return true;
    }

    /// <summary>
    /// Recharging (Angband recharge): adds charges to the emptiest wand or staff in the pack; may
    /// destroy it, likelier for deep devices and ones with charges left.
    /// </summary>
    private bool Recharge(int strength)
    {
        var device = Player.Inventory.Pack.Where(i => i.Base.Id is "wand" or "staff")
            .OrderBy(i => i.Charges).ThenBy(i => i.Serial).FirstOrDefault();
        if (device is null)
        {
            Publish(new MessageEvent("You have nothing to recharge."));
            return false;
        }
        var chance = Math.Max(1, (strength + 100 - device.Kind.Level - 10 * device.Charges / Math.Max(1, device.Number)) / 15);
        if (Rng.OneIn(RaceRechargeChance(chance)))
        {
            Publish(new MessageEvent($"The recharge backfires! Your {Describe(device, withArticle: false)} is destroyed."));
            Player.Inventory.Remove(device, device.Number, () => Objects.NextSerial++);
            return true;
        }
        var added = 2 + Rng.RandInt1(Math.Max(1, strength / 5));
        device.Charges += added;
        device.Flags.Remove("EMPTY");
        Publish(new MessageEvent($"Your {Describe(device, withArticle: false)} glows."));
        return true;
    }

    /// <summary>*Destruction*: the area around the player is reduced to rubble; monsters vanish.</summary>
    private void Destruction(int radius)
    {
        foreach (var p in Level.AllLocs().Where(p => p.DistanceTo(Player.Position) <= radius && p != Player.Position))
        {
            if (!Level.InBoundsFully(p) || Level.FeatureAt(p).Has(TerrainFlags.Permanent) || Level.FeatureAt(p).Has(TerrainFlags.Stair)) continue;
            if (Level.Monsters.At(p) is { } m && !m.Race.Has("QUESTOR")) Level.Monsters.Remove(m);
            foreach (var item in Level.Objects.At(p).Where(i => !i.IsArtifact).ToList()) Level.Objects.Remove(p, item);
            ref var sq = ref Level[p];
            sq.Trap = 0;
            sq.Flags &= ~SquareFlags.Glow;
            var roll = Rng.RandInt0(200);
            sq.Feature = roll < 20 ? Data.Terrain.Ids.Granite
                : roll < 70 ? Data.Terrain.Ids.Quartz
                : roll < 100 ? Data.Terrain.Ids.Magma
                : Data.Terrain.Ids.Floor;
            Known.Forget(p);
        }
        Publish(new MessageEvent("There is a searing blast of light!"));
        if (Player.Resists.GetValueOrDefault("light") <= 0 && Player.Resists.GetValueOrDefault("blind") <= 0)
            IncreaseTimed(TimedIds.Blind, 10 + Rng.RandInt1(10));
        UpdateView();
    }

    /// <summary>An earthquake: nearby walls and floors shift at random; monsters caught are hurt.</summary>
    private void Earthquake(int radius, Loc? center = null)
    {
        var at = center ?? Player.Position;
        foreach (var p in Level.AllLocs().Where(p => p.DistanceTo(at) <= radius && p != Player.Position && p != at))
        {
            if (!Level.InBoundsFully(p) || Level.FeatureAt(p).Has(TerrainFlags.Permanent) || Level.FeatureAt(p).Has(TerrainFlags.Stair)) continue;
            if (!Rng.OneIn(4)) continue;
            if (Level.Monsters.At(p) is { } m)
            {
                DamageMonster(m, Rng.Damroll(4, 8));
                continue;
            }
            ref var sq = ref Level[p];
            sq.Feature = Rng.OneIn(2) ? Data.Terrain.Ids.Rubble : Data.Terrain.Ids.Floor;
            sq.Trap = 0;
            Known.Forget(p);
        }
        Publish(new MessageEvent("The ground shakes!"));
        UpdateView();
    }
}
