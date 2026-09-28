using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Randomness;
using Angband.Core.Time;
using Angband.Core.World;

namespace Angband.Core.Game;

/// <summary>Objects: carrying, wielding, using, throwing, the floor, identification and generation.</summary>
public sealed partial class GameSession
{
    /// <summary>Makes every object; also tracks artifacts already created.</summary>
    public ObjectFactory Objects { get; private set; } = null!;

    /// <summary>What the player has learned about objects (runes and flavours).</summary>
    public PlayerKnowledge Knowledge { get; private set; } = null!;

    public string Describe(Item item, bool withArticle = true) => ItemNaming.Describe(item, Knowledge, withArticle);

    private void InitItems()
    {
        Objects = new ObjectFactory(Data);
        Knowledge = new PlayerKnowledge(Data, Seed) { IgnoredCheck = IsMarkedIgnored };
        Player.Inventory = new Inventory(Data.Constants.PackSize, Data.Constants.QuiverSlotSize, Data.Constants.QuiverSize);

        foreach (var start in Data.StartingKit)
        {
            var kind = Data.Object(start.Kind) ?? throw new GameDataException($"Starting kit names unknown object '{start.Kind}'.");
            if (KitCount(start, kind) is var count && count <= 0) continue;
            var item = Objects.Create(kind, count);
            // The starting kit is fully known, as in Angband.
            Knowledge.LearnKind(kind);
            foreach (var rune in item.Runes()) Knowledge.LearnRune(rune);
            if (start.Equip && item.IsWearable) Player.Inventory.Wield(item, () => Objects.NextSerial++);
            else Player.Inventory.Add(item);
        }
        ApplyBirthKnowledge();
        RecalculateBonuses();
    }

    /// <summary>
    /// Recomputes everything equipment affects (Angband calc_bonuses): armour, to-hit/dam, speed
    /// (including being over-burdened), stealth, blows, shots, light radius and resistances.
    /// </summary>
    public void RecalculateBonuses()
    {
        var p = Player;
        foreach (var carried in p.Inventory.All)
        {
            Knowledge.See(carried); // what you carry, you have seen
            carried.Assessed = true;
        }
        var gear = p.Inventory.Equipped.ToList();
        var weapon = p.Inventory.Weapon;
        var bow = p.Inventory.Bow;

        // Current stats: natural, less drain, plus equipment (Angband: 3 to 18/220).
        foreach (var stat in Game.CharacterSpec.StatIds)
        {
            if (!p.NaturalStats.TryGetValue(stat, out var natural)) natural = p.NaturalStats[stat] = p.Stats.GetValueOrDefault(stat, 15);
            // Scrambled stats read from another stat's natural value (Angband SCRAMBLE).
            if (p.StatScramble.TryGetValue(stat, out var source)) natural = p.NaturalStats.GetValueOrDefault(source, natural);
            var bonus = gear.Sum(i => i.Modifier(stat)) + (PlayerShape?.Modifiers.GetValueOrDefault(stat) ?? 0);
            p.Stats[stat] = Math.Clamp(natural - p.StatDrain.GetValueOrDefault(stat) + bonus, 3, 40);
        }

        // Stats (Angband adj_dex_ta, adj_dex_th, adj_str_td, adj_str_wgt).
        int Adj(int[] table, string stat) => table[Magic.StatTables.Index(p.Stats.GetValueOrDefault(stat, 15))];
        p.Armour = p.BaseArmour + Adj(Magic.StatTables.ToArmour, "dex");
        p.ToHit = p.BaseToHit + Adj(Magic.StatTables.ToHit, "dex");
        p.ToDam = p.BaseToDam + Adj(Magic.StatTables.ToDamage, "str");
        p.WeightLimit = Adj(Magic.StatTables.CarryLimit, "str") * 10;
        p.Stealth = p.BaseStealth;
        p.Blows = p.BaseBlows;
        p.Shots = p.BaseShots;
        var speed = 0;
        var light = 0;
        var resists = new Dictionary<string, int>(p.IntrinsicResists);
        p.GearFlags.Clear();
        var infravision = p.Infravision;
        var vulnerable = new HashSet<string>();

        foreach (var item in gear)
        {
            p.Armour += item.Armour + item.ToAc;
            // A wielded weapon's or bow's own to-hit/dam only count for attacks made with it.
            if (item != weapon && item != bow)
            {
                p.ToHit += item.ToHit;
                p.ToDam += item.ToDam;
            }
            speed += item.Modifier(ItemModifiers.Speed);
            p.Stealth += item.Modifier(ItemModifiers.Stealth);
            p.Blows += 100 * item.Modifier(ItemModifiers.Blows);
            p.Shots += item.Modifier(ItemModifiers.Shots); // tenths of a shot, as in Angband (SHOTS[10] = +1 shot)
            var itemLight = item.Modifier(ItemModifiers.Light);
            if (itemLight > 0 && ClassHas(ClassFlags.Unlight)) itemLight--; // Angband: lights are dimmer to the unlit
            if (item.Kind.Fuel > 0 && item.Fuel <= 0) itemLight = 0; // burnt out
            else if (item.Kind.Has("BURNS_OUT") && item.Fuel < 100) itemLight--;
            light += itemLight;
            foreach (var r in item.Resists) resists[r] = Math.Max(resists.GetValueOrDefault(r), 1);
            foreach (var flag in item.Flags)
                if (ItemFlags.Abilities.Contains(flag)) p.GearFlags.Add(flag);
            infravision += item.Modifier(ItemModifiers.Infravision);

            foreach (var curseId in item.Curses)
            {
                if (Data.Curse(curseId) is not { } curse) continue;
                p.Armour += curse.ToAc;
                p.ToHit += curse.ToHit;
                p.ToDam += curse.ToDam;
                speed += curse.Modifiers.GetValueOrDefault(ItemModifiers.Speed);
                p.Stealth += curse.Modifiers.GetValueOrDefault(ItemModifiers.Stealth);
                foreach (var v in curse.Vulnerabilities) vulnerable.Add(v);
            }
        }

        foreach (var v in vulnerable)
            if (resists.GetValueOrDefault(v) <= 0) resists[v] = -1;

        // Temporary effects (Angband: blessing, heroism, opposition to elements).
        var timed = p.Timed;
        if (timed.Has(TimedIds.Blessed)) { p.Armour += 5; p.ToHit += 10; }
        if (timed.Has(TimedIds.Hero)) p.ToHit += 12;
        foreach (var element in Data.Elements)
        {
            if (!timed.Has("oppose_" + element.Id)) continue;
            var current = resists.GetValueOrDefault(element.Id);
            // A temporary resist stacks with a permanent one (double resist), but not beyond.
            resists[element.Id] = current >= 3 ? current : Math.Min(2, Math.Max(current, 0) + 1);
        }

        // Angband: -1 speed per tenth of the weight limit carried beyond half of it.
        var weight = p.Inventory.TotalWeight;
        if (weight > p.WeightLimit / 2) speed -= (weight - p.WeightLimit / 2) / Math.Max(1, p.WeightLimit / 10);
        if (Hunger.LevelOf(p.Food, Data.Constants) == HungerLevel.Gorged) speed -= 10;

        if (timed.Has("infravision")) infravision += 5;
        if (timed.Has("berserk")) { p.ToHit += 12; p.Armour -= 10; }
        if (timed.Has("stoneskin")) { p.Armour += 40; speed -= 5; }
        if (timed.Has("sprint") || timed.Has("terror")) speed += 10;
        if (timed.Has("stealth")) p.Stealth += 10;
        // Angband BRAVERY_30: warriors fear nothing from level 30.
        if (ClassHas("BRAVERY_30") && p.Level >= 30) resists["fear"] = Math.Max(resists.GetValueOrDefault("fear"), 1);
        // Angband FAST_SHOT: rangers shoot a tenth of a shot faster per three levels.
        if (ClassHas("FAST_SHOT") && bow is not null) p.Shots += p.Level / 3;
        if (timed["bloodlust"] is var lust and > 0)
        {
            // Angband calc_bonuses: +1 to-dam per 2 points of bloodlust, +1 blow per 20.
            p.ToDam += lust / 2;
            p.Blows += 100 * (lust / 20);
        }
        if (timed.Has("grim_purpose"))
        {
            // (An older AVABand status for Grim Purpose, kept so saves with it still load.)
            resists["free_act"] = Math.Max(resists.GetValueOrDefault("free_act"), 1);
            resists["conf"] = Math.Max(resists.GetValueOrDefault("conf"), 1);
        }
        // Angband player_timed.txt flag-synonyms: while these last they are the protection itself.
        if (timed.Has("free_act")) resists["free_act"] = Math.Max(resists.GetValueOrDefault("free_act"), 1);
        if (timed.Has("oppose_conf")) resists["conf"] = Math.Max(resists.GetValueOrDefault("conf"), 1);
        if (timed.Has("bold")) resists["fear"] = Math.Max(resists.GetValueOrDefault("fear"), 1);
        if (ClassHas(ClassFlags.Unlight)) resists["dark"] = Math.Max(resists.GetValueOrDefault("dark"), 1);
        if (ClassHas(ClassFlags.Evil))
        {
            // Angband calc_bonuses: the evil resist nether and are vulnerable to holy orbs.
            resists["nether"] = Math.Max(resists.GetValueOrDefault("nether"), 1);
            resists["holy_orb"] = -1;
        }
        if (ClassHas(ClassFlags.BlessWeapon) && weapon is not null && (weapon.Base.Id == "hafted" || weapon.Flags.Contains("BLESSED")))
        {
            // Angband: a paladin fights better with a blessed or hafted weapon.
            p.ToHit += 2;
            p.ToDam += 2;
        }
        if (PlayerShape is { } shape)
        {
            // A shape's bonuses and protections (Angband shape.txt).
            p.ToHit += shape.ToHit;
            p.ToDam += shape.ToDam;
            p.Armour += shape.ToAc;
            speed += shape.Modifiers.GetValueOrDefault(ItemModifiers.Speed);
            p.Stealth += shape.Modifiers.GetValueOrDefault(ItemModifiers.Stealth);
            p.Blows += 100 * shape.Modifiers.GetValueOrDefault(ItemModifiers.Blows);
            infravision += shape.Modifiers.GetValueOrDefault(ItemModifiers.Infravision);
            light += shape.Modifiers.GetValueOrDefault(ItemModifiers.Light);
            foreach (var r in shape.Resists) resists[r] = Math.Max(resists.GetValueOrDefault(r), 1);
            foreach (var f in shape.Flags) p.GearFlags.Add(f);
        }
        if (PercentDamage) p.Blows = Math.Max(p.Blows, 200); // Angband calc_blows: two blows at least in O-combat
        p.TotalInfravision = Math.Max(0, infravision);
        p.EquipmentSpeed = speed;
        p.LightRadius = Math.Max(0, light);
        p.Resists.Clear();
        foreach (var (k, v) in resists) p.Resists[k] = v;
    }

    // --- Rune learning ---------------------------------------------------------------------

    /// <summary>Learns a rune (Angband player_learn_rune), announcing it and newly identified objects.</summary>
    public bool LearnRune(string rune)
    {
        if (!Knowledge.LearnRune(rune)) return false;
        Publish(new MessageEvent($"You have learned the rune of {RuneName(rune)}."));
        Publish(new RuneLearnedEvent(rune));
        foreach (var item in Player.Inventory.All.Where(i => i.Runes().Contains(rune) && Knowledge.IsFullyKnown(i)))
            Publish(new MessageEvent($"You have {Describe(item)}."));
        return true;
    }

    /// <summary>Learns the listed runes of <paramref name="item"/> that it actually has.</summary>
    private void LearnRunesOf(Item item, params string[] runes)
    {
        var has = item.Runes().ToHashSet();
        foreach (var rune in runes)
            if (has.Contains(rune)) LearnRune(rune);
    }

    /// <summary>The player-facing name of a rune ("resist fire", "slay orcs", "+speed"...).</summary>
    public string RuneName(string rune)
    {
        var (kind, arg) = rune.IndexOf(':') is var i and > 0 ? (rune[..i], rune[(i + 1)..]) : (rune, "");
        return kind switch
        {
            RuneIds.ToHit => "accuracy",
            RuneIds.ToDam => "damage",
            RuneIds.ToAc => "protection",
            "mod" => arg,
            "slay" => $"slay {arg.ToLowerInvariant()}",
            "brand" => $"{Data.Element(arg)?.Name ?? arg} brand",
            "resist" => $"resist {Data.Element(arg)?.Name ?? arg}",
            "curse" => Data.Curse(arg)?.Name ?? arg,
            "flag" => ItemFlags.Name(arg),
            _ => rune,
        };
    }

    // --- Commands --------------------------------------------------------------------------

    /// <summary>Pick up objects underfoot (all, or one). Costs half a turn.</summary>
    private int Pickup(Item? which)
    {
        // Ignored objects are left alone unless picked up by name (Angband's pickup menu hides them).
        var here = Level.Objects.At(Player.Position).Where(i => which is not null || !IsIgnored(i)).ToList();
        if (here.Count == 0)
        {
            Publish(new MessageEvent("There is nothing here to pick up."));
            return 0;
        }

        var picked = false;
        foreach (var item in which is null ? here : here.Where(i => i == which))
        {
            if (item.IsGold) { TakeGold(item); picked = true; continue; }
            if (!Player.Inventory.CanCarry(item))
            {
                Publish(new MessageEvent($"You have no room for {Describe(item)}."));
                continue;
            }
            Level.Objects.Remove(Player.Position, item);
            var stack = Player.Inventory.Add(item)!;
            NoticeKnacks(stack);
            Publish(new MessageEvent($"You have {Describe(stack)}."));
            Publish(new ItemPickedUpEvent(item.Kind.Id, item.Number));
            picked = true;
        }
        if (!picked) return 0;
        RecalculateBonuses();
        return EnergyTable.MoveEnergy / 2;
    }

    private void TakeGold(Item gold)
    {
        Level.Objects.Remove(Player.Position, gold);
        Player.Gold += gold.GoldValue;
        Publish(new MessageEvent($"You have found {gold.GoldValue} gold pieces worth of {ItemNaming.Plain(gold.Kind.Name, false)}."));
        Publish(new ItemPickedUpEvent(gold.Kind.Id, gold.GoldValue));
    }

    /// <summary>
    /// Stepping onto objects: gold is always taken, others picked up by the pickup options
    /// (<see cref="AutoPickupOkay"/>) or noticed. Returns how many objects were picked up.
    /// </summary>
    private int NoticeFloorObjects()
    {
        var picked = 0;
        var here = Level.Objects.At(Player.Position).ToList();
        foreach (var item in here)
        {
            if (item.IsGold) { TakeGold(item); continue; }
            item.Assessed = true; // walking over an object looks it over (Angband object_touch)
            if (IsIgnored(item)) continue;
            if (AutoPickupCount(item) is var count and > 0)
            {
                var taken = item;
                if (count < item.Number) taken = item.Split(Objects.NextSerial++, count);
                else Level.Objects.Remove(Player.Position, item);
                var stack = Player.Inventory.Add(taken)!;
                NoticeKnacks(stack);
                Publish(new MessageEvent($"You have {Describe(stack)}."));
                picked++;
                continue;
            }
            Publish(new MessageEvent($"You see {Describe(item)}."));
        }
        RecalculateBonuses();
        return picked;
    }

    private int Drop(Item item, int count)
    {
        if (!Player.Inventory.Contains(item)) return 0;
        var dropped = Player.Inventory.Remove(item, Math.Clamp(count, 1, item.Number), () => Objects.NextSerial++);
        Level.Objects.Add(Player.Position, dropped);
        Publish(new MessageEvent($"You drop {Describe(dropped)}."));
        Publish(new ItemDroppedEvent(dropped.Kind.Id));
        RecalculateBonuses();
        return EnergyTable.MoveEnergy / 2;
    }

    private int Wield(Item item)
    {
        var onFloor = Level.Objects.At(Player.Position).Contains(item);
        if (!item.IsWearable || (!onFloor && !Player.Inventory.Contains(item)) || Player.Inventory.Equipped.Contains(item))
        {
            Publish(new MessageEvent("You cannot wield that."));
            return 0;
        }

        if (onFloor)
        {
            if (item.Number > 1) item = item.Split(Objects.NextSerial++, 1);
            else Level.Objects.Remove(Player.Position, item);
        }

        var previous = Player.Inventory.Wield(item, () => Objects.NextSerial++);
        if (previous is not null)
        {
            if (Player.Inventory.Add(previous) is null)
            {
                Level.Objects.Add(Player.Position, previous);
                Publish(new MessageEvent($"You have no room for {Describe(previous)}, so it drops to the floor."));
            }
            else Publish(new MessageEvent($"You were wearing {Describe(previous)}."));
        }

        // Modifiers and the like are obvious as soon as you put something on (4.2 "obvious runes").
        foreach (var rune in item.Runes().Where(r => r.StartsWith("mod:") || r.StartsWith("flag:")))
            LearnRune(rune);
        foreach (var curse in item.Curses)
            if (Data.Curse(curse) is { EffectChance: 0 }) LearnRune(RuneIds.Curse(curse)); // passive curses show at once

        Publish(new MessageEvent($"You are {(item.Base.Slot == EquipSlot.Weapon ? "wielding" : "wearing")} {Describe(item)}."));
        Publish(new ItemWieldedEvent(item.Kind.Id));
        RecalculateBonuses();
        return EnergyTable.MoveEnergy;
    }

    private int TakeOff(Item item)
    {
        if (!Player.Inventory.Equipped.Contains(item)) return 0;
        if (!Player.Inventory.TakeOff(item))
        {
            Publish(new MessageEvent("You have no room in your pack."));
            return 0;
        }
        Publish(new MessageEvent($"You were wearing {Describe(item)}."));
        RecalculateBonuses();
        return EnergyTable.MoveEnergy / 2;
    }

    /// <summary>Quaff, read or eat (Angband use_aux). Flavoured kinds become known when their effect is noticed.</summary>
    private int Use(Item item, Loc? target = null, Direction? direction = null)
    {
        var onFloor = Level.Objects.At(Player.Position).Contains(item);
        if (IsDevice(item) && !string.IsNullOrEmpty(item.Kind.Effect))
        {
            if (onFloor)
            {
                Publish(new MessageEvent("You must pick it up first."));
                return 0;
            }
            return Player.Inventory.Contains(item) ? UseDeviceItem(item, target, direction) : 0;
        }
        if ((!onFloor && !Player.Inventory.Contains(item)) || string.IsNullOrEmpty(item.Kind.Effect))
        {
            Publish(new MessageEvent("You cannot use that."));
            return 0;
        }
        if (NeedsGlyph(item.Kind.Effect) && _effectGlyph is null)
        {
            Publish(new MessageEvent("You must choose a monster symbol to banish."));
            return 0;
        }
        if (item.Base.Id == "scroll" && (Player.IsBlind || !Level[Player.Position].Has(SquareFlags.Seen)))
        {
            Publish(new MessageEvent(Player.IsBlind ? "You can't see anything." : "You have no light to read by."));
            return 0;
        }
        if (item.Base.Id == "scroll" && Player.Timed.Has(TimedIds.Amnesia)) // Angband player_can_read
        {
            Publish(new MessageEvent("You can't remember how to read!"));
            return 0;
        }

        var verb = item.Base.Id switch { "potion" => "quaff", "scroll" => "read", "food" => "eat", _ => "use" };
        var wasAware = Knowledge.KnowsKind(item);

        // Use one of the stack first, so effects that look at the pack see the right count.
        if (onFloor)
        {
            if (item.Number > 1) item.Number--;
            else Level.Objects.Remove(Player.Position, item);
        }
        else
        {
            item.Number--;
            Player.Inventory.Prune();
        }

        Publish(new ItemUsedEvent(item.Kind.Id, verb));
        var obvious = ApplyEffects(item.Kind.Effect!);

        if (!wasAware)
        {
            if (obvious && Knowledge.LearnKind(item.Kind))
            {
                var sample = item.Clone(0, 1);
                Publish(new MessageEvent($"You have learned that it was {Describe(sample)}."));
            }
            else
            {
                Knowledge.MarkTried(item.Kind);
                Publish(new MessageEvent("You have no more idea what it does."));
            }
        }
        RecalculateBonuses();
        return EnergyTable.MoveEnergy;
    }

    /// <summary>Throws an object at a target (or the nearest visible monster).</summary>
    private int Throw(Item item, Loc? requestedTarget)
    {
        var onFloor = Level.Objects.At(Player.Position).Contains(item);
        if (!onFloor && !Player.Inventory.Contains(item)) return 0;
        var wielded = Player.Inventory.Equipped.Contains(item);
        // Angband obj_can_throw: of worn things, only a melee weapon (it comes off to be thrown).
        if (wielded && !(item.Base.IsWeapon && item.Base.Slot == EquipSlot.Weapon))
        {
            Publish(new MessageEvent("You must take it off first."));
            return 0;
        }

        var range = ThrowRange(item);
        var target = requestedTarget ?? AimPoint(range);
        if (target is not { } aim)
        {
            Publish(new MessageEvent("You have no target."));
            return 0;
        }

        Item missile;
        if (onFloor)
        {
            missile = item.Number > 1 ? item.Split(Objects.NextSerial++, 1) : item;
            if (missile == item) Level.Objects.Remove(Player.Position, item);
        }
        else missile = Player.Inventory.Remove(item, 1, () => Objects.NextSerial++);

        var toHit = Player.EffectiveToHit + missile.ToHit;
        var chanceBase = Player.SkillThrow;
        FlyMissile(missile, aim, range, chanceBase, toHit, multiplier: 1, launcher: null);
        RecalculateBonuses();
        return EnergyTable.MoveEnergy;
    }

    /// <summary>Angband do_cmd_throw: ((adj_str_blow + 20) × 10) / weight, at most 10 squares.</summary>
    public int ThrowRange(Item item)
    {
        var str = Magic.StatTables.StrBlow[Magic.StatTables.Index(Player.Stats.GetValueOrDefault("str", 15))];
        return Math.Clamp((str + 20) * 10 / Math.Max(10, item.Weight), 1, 10);
    }

    /// <summary>
    /// Angband ranged_damage: a throwing weapon thrown by hand does (2 + weight/12) times its damage
    /// ("to keep throwing weapons competitive"); anything else, once.
    /// </summary>
    public static int ThrowMultiplier(Item item) => item.IsThrowing ? 2 + item.Weight / 12 : 1;

    /// <summary>
    /// Moves a thrown or fired missile along its path, attacking monsters it meets. It stops at the
    /// first monster it hits, may break, and otherwise lands near where it stopped.
    /// </summary>
    private void FlyMissile(Item missile, Loc aim, int range, int skill, int toHit, int multiplier, Item? launcher)
    {
        var path = ProjectionPath.Compute(Level, Player.Position, aim, range, PathFlags.Through);
        Publish(new MissileFiredEvent(Describe(missile, withArticle: false), Player.Position, path));

        var landing = Player.Position;
        var hitSomething = false;
        var flown = 0;
        foreach (var grid in path)
        {
            if (!Level.Has(grid, TerrainFlags.Project)) break;
            landing = grid;
            flown++;
            if (Level.Monsters.At(grid) is not { } monster) continue;

            var chance = CombatMath.MissileChance(skill, toHit, Player.Position.DistanceTo(grid));
            if (!CombatMath.TestHit(Rng, chance, monster.Race.Armour, monster.IsVisible)) continue;

            var (bestMult, verb, rune, oMultiplier) = BestMultiplier(missile, monster);
            int damage;
            CriticalGrade grade;
            if (PercentDamage)
                damage = ORangedDamage(monster, missile, launcher, multiplier, oMultiplier, skill, toHit, out grade);
            else
            {
                damage = missile.Damage.Roll(Rng) + missile.ToDam + (launcher?.ToDam ?? 0);
                if (launcher is null) damage *= ThrowMultiplier(missile);
                damage *= Math.Max(1, multiplier) * bestMult;
                damage = CombatMath.CriticalShot(Rng, missile.Weight, toHit, Player.Level, damage, out grade);
            }

            var name = MonsterName(monster);
            Publish(new PlayerAttackEvent(monster.Id, Hit: true, damage, grade, Ranged: true));
            Publish(new MessageEvent($"The {Describe(missile, withArticle: false)} {(verb == "hit" ? "hits" : verb + "s")} {name}{DamageNote(damage)}.{CriticalMessage(grade)}"));
            LearnRunesOf(missile, RuneIds.ToHit, RuneIds.ToDam);
            if (launcher is not null) LearnRunesOf(launcher, RuneIds.ToHit, RuneIds.ToDam);
            if (rune is not null) LearnRune(rune);
            DamageMonster(monster, damage);
            hitSomething = true;
            break;
        }

        ShowProjection(Player.Position, [.. path.Take(flown)], null, null, ProjectionKind.Missile);

        // Angband breakage_chance: the base's chance on a hit, its square (as a fraction) on a miss —
        // so flasks and potions (100%) always shatter.
        var breakChance = hitSomething ? missile.Base.BreakChance : missile.Base.BreakChance * missile.Base.BreakChance / 100;
        if (!missile.IsArtifact && Rng.Percent(breakChance))
        {
            if (Level[landing].Has(SquareFlags.Seen))
                Publish(new MessageEvent($"The {Describe(missile, withArticle: false)} breaks."));
            return;
        }
        DropNear(missile, landing);
    }

    /// <summary>Refuels the wielded lantern from a flask of oil.</summary>
    private int Refuel(Item flask)
    {
        var light = Player.Inventory.Light;
        if (light is null || !light.Kind.Has("REFUELABLE"))
        {
            Publish(new MessageEvent("Your light cannot be refilled."));
            return 0;
        }
        if (!Player.Inventory.Contains(flask) || !flask.Kind.Has("FUEL"))
        {
            Publish(new MessageEvent("That is not fuel."));
            return 0;
        }
        light.Fuel = Math.Min(light.Kind.Fuel, light.Fuel + flask.Kind.Fuel);
        flask.Number--;
        Player.Inventory.Prune();
        Publish(new MessageEvent(light.Fuel >= light.Kind.Fuel ? "Your lamp is full." : "You fuel your lamp."));
        RecalculateBonuses();
        return EnergyTable.MoveEnergy / 2;
    }

    /// <summary>Drops an object on or near a square, like Angband drop_near.</summary>
    public void DropNear(Item item, Loc near)
    {
        for (var r = 0; r <= 3; r++)
        for (var dy = -r; dy <= r; dy++)
        for (var dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
            var p = new Loc(near.X + dx, near.Y + dy);
            if (!Level.InBounds(p) || !Level.Has(p, TerrainFlags.Object)) continue;
            if (r > 0 && !ProjectionPath.Projectable(Level, near, p, 5)) continue;
            Level.Objects.Add(p, item);
            return;
        }
        if (Level[near].Has(SquareFlags.Seen))
            Publish(new MessageEvent($"The {Describe(item, withArticle: false)} disappears."));
    }

    // --- Effects of potions, scrolls and food -------------------------------------------------

    /// <summary>
    /// Runs an effect string such as <c>heal:15; cure:blind</c> (see <see cref="ItemEffects"/>).
    /// Returns true if anything noticeable happened (which identifies the object).
    /// </summary>
    public bool ApplyEffects(string effects)
    {
        var obvious = false;
        var list = ItemEffects.Parse(effects).ToList();
        for (var i = 0; i < list.Count; i++)
        {
            // "random:N" (Angband RANDOM): one of the next N effects happens, chosen at random.
            if (list[i].Name == "random" && list[i].Int(0) > 0)
            {
                var n = Math.Min(list[i].Int(0), list.Count - i - 1);
                if (n > 0) obvious |= ApplyEffect(list[i + 1 + Rng.RandInt0(n)]);
                i += n;
                continue;
            }
            obvious |= ApplyEffect(list[i]);
        }
        return obvious;
    }

    private bool ApplyEffect(ItemEffect e)
    {
        switch (e.Name)
        {
            case "heal":
            {
                var amount = Math.Max(e.Dice(0).Roll(Rng), Player.MaxHp * e.Int(1) / 100);
                if (Player.Hp >= Player.MaxHp) return false;
                Player.Hp = Math.Min(Player.MaxHp, Player.Hp + amount);
                Publish(new MessageEvent(amount >= 15 ? "You feel much better." : "You feel a little better."));
                return true;
            }
            case "cure":
            {
                if (Data.Timed(e.Arg(0)) is not { } def || !Player.Timed.Has(def.Id)) return false;
                if (Player.Timed.Set(def, 0) is { } message) Publish(new MessageEvent(message));
                OnTimedEnded(def.Id);
                return true;
            }
            case "reduce":
            {
                if (Data.Timed(e.Arg(0)) is not { } def || !Player.Timed.Has(def.Id)) return false;
                var current = Player.Timed[def.Id];
                if (Player.Timed.Set(def, current / 2) is { } message) Publish(new MessageEvent(message));
                return true;
            }
            case "timed":
                return e.Dice(1).Roll(Rng) is var turns and > 0 && IncreaseTimed(e.Arg(0), turns);
            case "nourish":
                Publish(new MessageEvent("That tastes good."));
                SetFood(Player.Food + e.Int(0));
                return true;
            case "satisfy":
                // Angband 4.1 Satisfy Hunger: to just below gorged, never lower.
                if (Player.Food >= Data.Constants.FoodMax - 1) return false;
                SetFood(Data.Constants.FoodMax - 1);
                return true;
            case "teleport":
                return TeleportPlayer(e.Int(0));
            case "light_area":
                LightArea();
                return true;
            case "detect_monsters":
                return DetectMonsters(e.Int(0), null);
            case "map_area":
                MapArea(e.Int(0));
                return true;
            case "detect_objects":
                return DetectObjects(e.Int(0));
            case "identify":
                return IdentifyRune();
            case "remove_curse":
                return RemoveCurse();
            case "fire_damage":
                TakeHit(e.Dice(0).Roll(Rng), "a burning flask of oil");
                return true;
            default:
                return ApplyMoreEffects(e) ?? false;
        }
    }

    /// <summary>Teleports the player to a random open square within <paramref name="range"/> (at least half as far if possible).</summary>
    public bool TeleportPlayer(int range)
    {
        var from = Player.Position;
        var spots = Level.AllLocs()
            .Where(p => Level.IsPassable(p) && Level[p].Monster == 0 && p != from && p.DistanceTo(from) <= range
                        && !Level[p].Has(SquareFlags.Vault))
            .ToList();
        var far = spots.Where(p => p.DistanceTo(from) >= range / 2).ToList();
        var pool = far.Count > 0 ? far : spots;
        if (pool.Count == 0) return false;

        Player.Position = Rng.Pick(pool);
        Publish(new PlayerMovedEvent(from, Player.Position));
        Publish(new PlayerTeleportedEvent(from, Player.Position));
        Publish(new MessageEvent("You feel yourself yanked sideways!"));
        UpdateView();
        return true;
    }

    /// <summary>Lights the room the player is in (and nearby squares), permanently.</summary>
    private void LightArea()
    {
        Publish(new MessageEvent("You are surrounded by a white light."));
        foreach (var p in Level.AllLocs().Where(p => p.DistanceTo(Player.Position) <= 2))
            Level[p].Flags |= SquareFlags.Glow;

        if (!Level[Player.Position].Has(SquareFlags.Room)) return;
        var queue = new Queue<Loc>([Player.Position]);
        var seen = new HashSet<Loc> { Player.Position };
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            Level[p].Flags |= SquareFlags.Glow;
            if (!Level.IsPassable(p) && p != Player.Position) continue; // walls get lit but don't spread
            foreach (var n in Level.Neighbors(p))
                if (Level[n].Has(SquareFlags.Room) && seen.Add(n)) queue.Enqueue(n);
        }
    }

    private bool DetectMonsters(int radius, string? flag)
    {
        var found = 0;
        // Mimics and lurkers keep their cover: detection doesn't see through it (Angband).
        foreach (var m in Level.Monsters.All.Where(m => !m.Camouflaged && m.Position.ChebyshevTo(Player.Position) <= radius
                                                        && (flag is null || m.Race.Has(flag))))
        {
            m.IsDetected = true;
            if (flag is not null) LearnMonsterFlag(m.Race, flag); // detect evil tells you it is evil
            found++;
        }
        var what = flag is null ? "monsters" : $"{flag.ToLowerInvariant()} creatures";
        Publish(new MessageEvent(found > 0 ? $"You sense the presence of {what}!" : $"You sense no {what}."));
        return true;
    }

    private bool DetectObjects(int radius)
    {
        var found = 0;
        foreach (var (loc, item) in Level.Objects.All.Where(o => o.Loc.ChebyshevTo(Player.Position) <= radius).ToList())
        {
            Known.RememberObject(loc, Level.Objects.At(loc)[0]);
            found++;
        }
        // A mimic's disguise looks like any other object to magic that senses objects.
        foreach (var m in Level.Monsters.All.Where(m => m.MimicItem is not null && m.Position.ChebyshevTo(Player.Position) <= radius))
        {
            Known.RememberObject(m.Position, m.MimicItem);
            found++;
        }
        Publish(new MessageEvent(found > 0 ? "You sense the presence of objects!" : "You sense no objects."));
        return true;
    }

    /// <summary>Magic mapping: remembers floors and the walls next to them within a radius.</summary>
    private void MapArea(int radius)
    {
        foreach (var p in Level.AllLocs().Where(p => p.ChebyshevTo(Player.Position) <= radius))
        {
            var f = Level.FeatureAt(p);
            if (f.Has(TerrainFlags.Passable) || f.HasAny(TerrainFlags.DoorAny | TerrainFlags.Stair | TerrainFlags.Rubble)
                || Level.Neighbors(p).Any(Level.IsPassable))
                Known.Remember(Level, p);
        }
        Publish(new MessageEvent("You sense your surroundings."));
    }

    /// <summary>Scroll of Identify Rune: learns one unknown rune on the first unknown equipped (or carried) item.</summary>
    private bool IdentifyRune()
    {
        var item = Player.Inventory.Equipped.Concat(Player.Inventory.Pack).Concat(Player.Inventory.Quiver)
            .FirstOrDefault(i => Knowledge.UnknownRunes(i).Any());
        if (item is null)
        {
            Publish(new MessageEvent("You have nothing to identify."));
            return true; // the scroll itself is still learned
        }
        var unknown = Knowledge.UnknownRunes(item).ToList();
        LearnRune(Rng.Pick(unknown));
        return true;
    }

    private bool RemoveCurse()
    {
        var item = Player.Inventory.Equipped.FirstOrDefault(i => i.IsCursed);
        if (item is null)
        {
            Publish(new MessageEvent("You feel as if someone is watching over you."));
            return true;
        }
        var curse = item.Curses[0];
        item.Curses.RemoveAt(0);
        Publish(new MessageEvent($"The {Data.Curse(curse)?.Name ?? curse} curse on your {Describe(item, withArticle: false)} is broken!"));
        RecalculateBonuses();
        return true;
    }

    // --- Upkeep --------------------------------------------------------------------------------

    /// <summary>Light fuel burns down; curses occasionally act (every player turn at normal speed).</summary>
    private void ItemUpkeep()
    {
        DeviceUpkeep();
        if (Player.Inventory.Light is { } light && light.Kind.Fuel > 0 && light.Fuel > 0)
        {
            light.Fuel--;
            if (light.Fuel == 100) Publish(new MessageEvent("Your light is growing faint."));
            if (light.Fuel == 0)
            {
                Publish(new MessageEvent("Your light has gone out!"));
                Publish(new LightOutEvent());
            }
            if (light.Fuel is 0 or 99) { RecalculateBonuses(); UpdateView(); }
        }

        foreach (var item in Player.Inventory.Equipped.ToList())
        foreach (var curseId in item.Curses.ToList())
        {
            if (Data.Curse(curseId) is not { EffectChance: > 0, Effect: { } effect } curse) continue;
            if (!Rng.OneIn(curse.EffectChance)) continue;
            if (curse.EffectMessage.Length > 0) Publish(new MessageEvent(curse.EffectMessage));
            ApplyEffects(effect);
            LearnRune(RuneIds.Curse(curseId));
        }
    }

    // --- Level population and monster drops ------------------------------------------------------

    /// <summary>Places the generator's object hints and the level's object/gold budget (Angband alloc_object).</summary>
    private void PopulateObjects()
    {
        var depth = Level.Depth;
        foreach (var hint in Level.SpawnHints)
        {
            if (!Level.InBounds(hint.Loc) || !Level.Has(hint.Loc, TerrainFlags.Object)) continue;
            var item = hint.Kind switch
            {
                SpawnKind.Object => Objects.Make(Rng, depth + hint.DepthBonus),
                SpawnKind.GoodObject => Objects.Make(Rng, depth + hint.DepthBonus, good: true),
                SpawnKind.GreatObject => Objects.Make(Rng, depth + hint.DepthBonus, good: true, great: true),
                SpawnKind.Gold => MakeLevelGold(depth + hint.DepthBonus),
                SpawnKind.MonsterOrObject when Level[hint.Loc].Monster == 0 => Objects.Make(Rng, depth + hint.DepthBonus),
                _ => null,
            };
            if (item is not null) Level.Objects.Add(hint.Loc, item);
        }

        var budget = Level.Population;
        Scatter(budget.RoomObjects, p => Level[p].Has(SquareFlags.Room), () => Objects.Make(Rng, depth));
        Scatter(budget.AnywhereObjects, _ => true, () => Objects.Make(Rng, depth));
        Scatter(budget.Gold, _ => true, () => MakeLevelGold(depth));

        void Scatter(int count, Func<Loc, bool> where, Func<Item?> make)
        {
            for (var i = 0; i < count; i++)
            {
                Loc? spot = null;
                for (var t = 0; t < 500 && spot is null; t++)
                {
                    var p = new Loc(Rng.RandRange(1, Level.Width - 2), Rng.RandRange(1, Level.Height - 2));
                    if (Level.IsEmptyFloor(p) && !Level.Objects.Any(p) && !Level[p].Has(SquareFlags.Vault)
                        && p != Player.Position && where(p))
                        spot = p;
                }
                if (spot is { } s && make() is { } item) Level.Objects.Add(s, item);
            }
        }
    }

    /// <summary>Angband monster_death drops: DROP_60/90/1/2/3/4, DROP_GOOD/GREAT, ONLY_GOLD/ONLY_ITEM.</summary>
    /// <summary>Drops a monster's treasure; returns how many objects and piles of gold fell.</summary>
    private (int Items, int Gold) DropMonsterLoot(Monster monster)
    {
        // Loot rolled for a thief (and perhaps stolen) has already gone with the monster's carried items.
        if (monster.LootRolled) return (0, 0);
        int items = 0, golds = 0;
        foreach (var item in RollMonsterLoot(monster))
        {
            DropNear(item, monster.Position);
            if (item.IsGold) golds++;
            else items++;
        }
        return (items, golds);
    }

    /// <summary>A monster's treasure (Angband DROP_* flags), at its depth or the level's.</summary>
    private List<Item> RollMonsterLoot(Monster monster)
    {
        var loot = new List<Item>();
        var race = monster.Race;
        var number = 0;
        if (race.Has("DROP_20") && Rng.Percent(20)) number++;
        if (race.Has("DROP_40") && Rng.Percent(40)) number++;
        if (race.Has("DROP_60") && Rng.Percent(60)) number++;
        if (race.Has("DROP_90") && Rng.Percent(90)) number++;
        if (race.Has("DROP_1")) number++;
        if (race.Has("DROP_2")) number += 2;
        if (race.Has("DROP_3")) number += 3;
        if (race.Has("DROP_4")) number += 4;
        if (number == 0) return loot;

        var level = Math.Max((race.Depth + Level.Depth) / 2, race.Depth);
        var good = race.Has("DROP_GOOD") || race.Has("DROP_GREAT");
        var great = race.Has("DROP_GREAT");
        for (var i = 0; i < number; i++)
        {
            var gold = race.Has("ONLY_GOLD") || (!race.Has("ONLY_ITEM") && Rng.OneIn(2));
            var item = gold ? MakeLevelGold(level) : Objects.Make(Rng, level, good, great);
            if (item is not null) loot.Add(item);
        }
        return loot;
    }
}

/// <summary>One parsed effect, e.g. <c>timed:fast:20+1d20</c> → name "timed", args ["fast", "20+1d20"].</summary>
public sealed record ItemEffect(string Name, IReadOnlyList<string> Args)
{
    public string ToEffectString() => Args.Count == 0 ? Name : Name + ":" + string.Join(":", Args);

    public string Arg(int i) => i < Args.Count ? Args[i] : "";
    public int Int(int i) => int.TryParse(Arg(i), out var v) ? v : 0;

    /// <summary>Dice argument; also accepts <c>base+NdS</c> like Angband's <c>10+1d20</c>.</summary>
    public Dice Dice(int i)
    {
        var text = Arg(i);
        if (Randomness.Dice.TryParse(text, out var d)) return d;
        var plus = text.IndexOf('+');
        if (plus > 0 && int.TryParse(text[..plus], out var b) && Randomness.Dice.TryParse(text[(plus + 1)..], out var rest))
            return rest with { Bonus = rest.Bonus + b };
        return Randomness.Dice.Zero;
    }
}

/// <summary>Parses effect strings: <c>effect:arg:arg; effect:arg</c>.</summary>
public static class ItemEffects
{
    public static readonly IReadOnlySet<string> Known = new HashSet<string>
    {
        "heal", "cure", "reduce", "timed", "nourish", "satisfy", "teleport", "light_area", "detect_monsters", "map_area",
        "detect_objects", "identify", "remove_curse", "fire_damage",
        // Devices and the fuller item list (GameSession.Devices.cs).
        "bolt", "beam", "ball", "breath", "monster_status", "teleport_other", "drain_life", "heal_monster", "haste_monster",
        "clone_monster", "polymorph", "stone_to_mud", "light_line", "disarm", "wonder", "project_los", "dispel",
        "detect_evil", "detect_invisible", "detection", "enlightenment", "darkness", "aggravate", "summon",
        "trap_creation", "restore_mana", "gain_stat", "drain_stat", "set_food", "restore_stat", "restore_exp", "gain_exp", "recall",
        "deep_descent", "teleport_level", "enchant", "recharge", "acquirement", "destruction", "earthquake",
        "mass_banishment", "stun",
        // Special effects (GameSession.Special.cs).
        "damage", "banish", "probe", "destroy_doors", "glyph", "curse_weapon", "curse_armour", "shapechange", "random",
        "lose_hp_fraction",
    };

    public static IEnumerable<ItemEffect> Parse(string effects) =>
        effects.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split(':', StringSplitOptions.TrimEntries))
            .Select(bits => new ItemEffect(bits[0], bits.Skip(1).ToList()));
}
