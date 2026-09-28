using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Randomness;
using Angband.Core.Time;

namespace Angband.Core.Game;

public sealed partial class GameSession
{
    /// <summary>Longest missile range (Angband max_range).</summary>
    public const int MaxRange = 20;

    private static readonly ObjectKindDef BareHands = new()
    {
        Id = "bare_hands", Name = "bare hands", Base = "none", Damage = new Dice(1, 1),
    };

    /// <summary>
    /// Melee (Angband py_attack): blows continue until the next one would exceed a full turn of
    /// energy, so fractional blows leave energy over. Stops early when the monster dies.
    /// </summary>
    private int PlayerMelee(Monster monster)
    {
        var name = MonsterName(monster);
        if (PlayerAfraid)
        {
            Publish(new MessageEvent($"You are too afraid to attack {name}!"));
            if (Player.HasGearFlag(ItemFlags.Afraid)) LearnRune(RuneIds.Flag(ItemFlags.Afraid));
            return EnergyTable.MoveEnergy;
        }

        var blowEnergy = CombatMath.BlowEnergy(Player.Blows, EnergyTable.MoveEnergy);
        var used = 0;
        WakeMonster(monster);
        CombatRegenOnAttack();
        if (ClassHas(ClassFlags.ShieldBash))
        {
            used = ShieldBash(monster);
            if (!monster.IsActive) return EnergyTable.MoveEnergy;
        }

        while (used + blowEnergy <= EnergyTable.MoveEnergy && monster.IsActive)
        {
            used += blowEnergy;
            // Angband py_attack_real / blow_after_effects: with IMPACT, a blow of more than 50 shakes
            // the earth around you, and the attack ends if the monster is no longer there.
            if (PlayerBlow(monster) > 50 && Player.HasGearFlag(ItemFlags.Impact))
            {
                LearnRune(RuneIds.Flag(ItemFlags.Impact));
                var where = monster.Position;
                Earthquake(10);
                if (!monster.IsActive || Level.Monsters.At(where) != monster) break;
            }
        }
        return used;
    }

    /// <summary>
    /// One melee blow (Angband py_attack_real): the to-hit test, slays and brands, criticals.
    /// Returns the damage done, or -1 for a miss.
    /// </summary>
    private int PlayerBlow(Monster monster)
    {
        var name = MonsterName(monster);
        TrackHealth(monster); // Angband py_attack_real: health_track
        var weapon = Player.Inventory.Weapon;
        var dice = weapon?.Damage ?? BareHands.Damage;
        var weaponToHit = weapon?.ToHit ?? 0;
        var weaponToDam = weapon?.ToDam ?? 0;
        var chance = CombatMath.MeleeChance(Player.SkillMelee, weaponToHit + Player.EffectiveToHit);
        if (!CombatMath.TestHit(Rng, chance, monster.Race.Armour, monster.IsVisible))
        {
            Publish(new PlayerAttackEvent(monster.Id, Hit: false, 0, CriticalGrade.None));
            Publish(new MessageEvent($"You miss {name}."));
            if (Bloodlust > 0 && Rng.OneIn(50))
            {
                Publish(new MessageEvent("You feel strange..."));
                OverExert(20, 20, scramble: true);
            }
            return -1;
        }

        var (multiplier, verb, rune, oMultiplier) = weapon is null ? (1, "hit", null, 10) : BestMultiplier(weapon, monster);
        if (Player.Timed.Has("att_pois") && PoisonCoating(monster.Race) is { } venom && venom > multiplier)
            (multiplier, verb, rune, oMultiplier) = (venom, "poison", null, OBrandMultiplier(3, venom > 3));
        // Smite Evil and Demon Bane (Angband ATT_EVIL: EVIL_2, ATT_DEMON: DEMON_5).
        if (Player.Timed.Has("att_evil") && monster.Race.Has(MonsterFlags.Evil) && multiplier < 2)
            (multiplier, verb, rune, oMultiplier) = (2, "smite", null, OSlayMultiplier(MonsterFlags.Evil, 2));
        if (Player.Timed.Has("att_demon") && monster.Race.Has("DEMON") && multiplier < 5)
            (multiplier, verb, rune, oMultiplier) = (5, "smite", null, OSlayMultiplier("DEMON", 5));
        int damage;
        CriticalGrade grade;
        if (PercentDamage)
            damage = OMeleeDamage(monster, weapon, oMultiplier, weaponToHit + Player.EffectiveToHit, out grade);
        else
        {
            damage = dice.Roll(Rng) * multiplier;
            damage = CombatMath.CriticalMelee(Rng, weapon?.Weight ?? 0, weaponToHit + Player.EffectiveToHit,
                Player.SkillMelee, damage, out grade);
            damage += weaponToDam + Player.EffectiveToDam;
        }
        damage = Math.Max(0, damage);

        Publish(new PlayerAttackEvent(monster.Id, Hit: true, damage, grade));
        Publish(new MessageEvent($"You {ShapeBlowVerb() ?? verb} {name}{DamageNote(damage)}.{CriticalMessage(grade)}"));
        if (Player.Timed.Has("att_conf") && Data.Timed("att_conf") is { } glow)
        {
            // Monster confusion (Angband ATT_CONF): the glowing hands confuse, then fade.
            AffectMonster(monster, "confuse", Player.Level + 20);
            if (Player.Timed.Set(glow, 0) is { } faded) Publish(new MessageEvent(faded));
        }
        if (weapon is not null)
        {
            // Hitting reveals the weapon's accuracy and damage bonuses; a slay or brand that
            // took effect reveals itself (4.2 rune learning).
            LearnRunesOf(weapon, RuneIds.ToHit, RuneIds.ToDam);
            if (rune is not null) LearnRune(rune);
        }
        var drained = Math.Min(Math.Max(0, monster.Hp), damage);
        var living = IsLiving(monster.Race);
        var killed = DamageMonster(monster, damage);
        if (Bloodlust > 0 && Rng.OneIn(50))
        {
            Publish(new MessageEvent("You feel something give way!"));
            OverExert(20, 0, con: true);
        }
        // Angband ATT_VAMP (vampire form): the bite heals by what it drains from the living.
        if (!killed && Player.Timed.Has("att_vamp") && living && drained > 0)
            Player.Hp = Math.Min(Player.MaxHp, Player.Hp + drained);
        return damage;
    }

    /// <summary>A poison-coated weapon (the Venom ritual) is a x3 poison brand, x6 against the vulnerable.</summary>
    private int? PoisonCoating(MonsterRaceDef race)
    {
        var element = Data.Element("pois");
        if (element?.ImmunityFlag is { } immune && race.Has(immune)) return null;
        return element?.VulnerabilityFlag is { } vuln && race.Has(vuln) ? 6 : 3;
    }

    /// <summary>
    /// Angband attempt_shield_bash: a fighter with a shield sometimes slams it into a worthy foe
    /// before the blows — more often unarmed or with a puny weapon. Damage grows with the shield's
    /// dice, its weight and the fighter's skill and level; the blow may stun or confuse, and the
    /// basher may stumble, losing part of the turn. Returns the energy lost to stumbling.
    /// </summary>
    private int ShieldBash(Monster monster)
    {
        var shield = Player.Inventory.Equipped.FirstOrDefault(i => i.Base.Id == "shield");
        if (shield is null || !monster.IsVisible) return 0;
        var race = monster.Race;
        if (race.Depth < Player.Level / 2) return 0; // too pathetic to bother with

        var dexToHit = Magic.StatTables.ToHit[Magic.StatTables.Index(Player.Stats.GetValueOrDefault("dex", 15))];
        var chance = Player.SkillMelee / 8 + dexToHit / 2;
        var weapon = Player.Inventory.Weapon;
        if (weapon is null) chance *= 4;
        else if (weapon.Damage.Count * weapon.Damage.Sides * Player.Blows / 100 < shield.Damage.Count * shield.Damage.Sides * 3) chance *= 2;
        if (chance <= Rng.RandInt0(200 + race.Depth)) return 0;

        // Momentum and accuracy (Angband also counts the character's own weight; AVABand assumes 150 lb).
        var quality = Player.SkillMelee / 4 + 150 / 8 + Player.Inventory.TotalWeight / 80 + shield.Weight / 2;
        var damage = shield.Damage.Roll(Rng) * (quality / 40 + Player.Level / 14);
        damage += Magic.StatTables.ToDamage[Magic.StatTables.Index(Player.Stats.GetValueOrDefault("str", 15))];
        damage = Math.Clamp(damage, 0, 125);
        Publish(new PlayerAttackEvent(monster.Id, Hit: true, damage, CriticalGrade.None));
        Publish(new MessageEvent("You get in a shield bash!"));
        if (Rng.RandInt1(Math.Max(1, damage)) > 30 + Rng.RandInt1(Math.Max(1, damage / 2))) Publish(new MessageEvent("WHAMM!"));
        if (DamageMonster(monster, damage)) return 0;

        if (quality + Player.Level > Rng.RandInt1(200 + race.Depth * 8)) StunMonster(monster, Rng.RandInt0(Player.Level / 5 + 1) + 4);
        if (quality + Player.Level > Rng.RandInt1(300 + race.Depth * 12) && !race.Has(MonsterFlags.NoConf))
        {
            monster.Confused = Math.Max(monster.Confused, Rng.RandInt0(Player.Level / 5 + 1) + 4);
            if (monster.IsVisible) Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} looks confused."));
        }
        if (35 + dexToHit < Rng.RandInt1(60))
        {
            Publish(new MessageEvent("You stumble!"));
            return Rng.RandInt1(50) + 25;
        }
        return 0;
    }

    /// <summary>
    /// Fire the launcher (Angband ranged_helper): the missile flies along the projection path past
    /// the target, testing each monster it meets and stopping at the first it hits.
    /// </summary>
    private int Fire(Loc? requestedTarget, Item? ammoChoice)
    {
        var bow = Player.Inventory.Bow;
        if (bow is null)
        {
            Publish(new MessageEvent("You have nothing to fire with."));
            return 0;
        }
        var ammo = ammoChoice ?? Player.Inventory.Quiver.FirstOrDefault(q => q.Base.AmmoClass == bow.Base.AmmoClass);
        if (ammo is null || ammo.Base.AmmoClass != bow.Base.AmmoClass || !Player.Inventory.Contains(ammo))
        {
            Publish(new MessageEvent("You have no ammunition to fire."));
            return 0;
        }

        // Angband calc_bonuses: the launcher's multiplier, plus extra might from anything worn.
        var multiplier = bow.Kind.Multiplier + Player.Inventory.Equipped.Sum(i => i.Modifier(ItemModifiers.Might));
        var range = Math.Min(6 + 2 * multiplier, MaxRange);
        var target = requestedTarget ?? AimPoint(range);
        if (target is not { } aim)
        {
            Publish(new MessageEvent("You have no target."));
            return 0;
        }

        var missile = Player.Inventory.Remove(ammo, 1, () => Objects.NextSerial++);
        var toHit = Player.EffectiveToHit + bow.ToHit + missile.ToHit;
        FlyMissile(missile, aim, range, Player.SkillBow, toHit, multiplier, bow);

        if (!Player.Inventory.Contains(ammo) || ammo.Number == 0)
            Publish(new MessageEvent("You have no more of that ammunition."));
        RecalculateBonuses();
        return CombatMath.ShotEnergy(Player.Shots, EnergyTable.MoveEnergy);
    }

    /// <summary>The closest visible monster the player has a clear shot at.</summary>
    public Monster? NearestTarget(int range = MaxRange) =>
        Level.Monsters.All
            .Where(m => (m.IsVisible || m.IsDetected) && Player.Position.DistanceTo(m.Position) <= range
                        && ProjectionPath.Projectable(Level, Player.Position, m.Position, range))
            .OrderBy(m => Player.Position.DistanceTo(m.Position))
            .ThenBy(m => m.Id)
            .FirstOrDefault();

    /// <summary>
    /// The best slay or brand of an object that applies to a race, with its attack verb and the rune
    /// it reveals (Angband improve_attack_modifier).
    /// </summary>
    private (int Multiplier, string Verb, string? Rune, int OMultiplier) BestMultiplier(Item item, Monster monster)
    {
        var race = monster.Race;
        (int Multiplier, string Verb, string? Rune, int OMultiplier) best = (1, "hit", null, 10);
        // Percentage damage ranks slays and brands by their O-multipliers, as 4.2 does.
        bool Better(int multiplier, int oMultiplier) =>
            PercentDamage ? oMultiplier > best.OMultiplier : multiplier > best.Multiplier;
        foreach (var slay in item.Slays)
        {
            // A slay that bites tells you what the creature is (Angband learns the race flag).
            if (race.Has(slay.MonsterFlag)) Lore.For(race.Id).FlagsKnown.Add(slay.MonsterFlag);
            if (race.Has(slay.MonsterFlag) && OSlayMultiplier(slay.MonsterFlag, slay.Multiplier) is var oSlay
                && Better(slay.Multiplier, oSlay))
                best = (slay.Multiplier, slay.Verb, RuneIds.Slay(slay.MonsterFlag), oSlay);
        }
        foreach (var brand in item.Brands)
        {
            var element = Data.Element(brand.Element);
            // A known brand shows whether the monster resists it, or is hurt by it (Angband learns either way).
            if (Knowledge.KnowsRune(RuneIds.Brand(brand.Element)))
            {
                if (element?.ImmunityFlag is { } resist) LearnMonsterResponse(monster, resist);
                if (element?.VulnerabilityFlag is { } hurt) LearnMonsterResponse(monster, hurt);
            }
            if (element?.ImmunityFlag is { } immune && race.Has(immune))
            {
                if (monster.IsVisible) Lore.For(race.Id).FlagsKnown.Add(immune);
                continue;
            }
            var vulnerable = element?.VulnerabilityFlag is { } vuln && race.Has(vuln);
            var multiplier = vulnerable ? brand.Multiplier * 2 : brand.Multiplier;
            var oBrand = OBrandMultiplier(brand.Multiplier, vulnerable);
            if (Better(multiplier, oBrand)) best = (multiplier, brand.Verb, RuneIds.Brand(brand.Element), oBrand);
        }
        return best;
    }

    /// <summary>
    /// Damages a monster (Angband mon_take_hit): wakes it, may kill it (granting experience and
    /// dropping loot) or frighten it. Returns true if it died.
    /// </summary>
    public bool DamageMonster(Monster monster, int damage)
    {
        Publish(new MonsterDamagedEvent(monster.Id, monster.Position, damage));
        Reveal(monster);
        WakeMonster(monster);
        monster.Hp -= damage;

        if (monster.Hp < 0)
        {
            KillMonster(monster);
            return true;
        }

        if (!monster.IsAfraid && !monster.Race.Has(MonsterFlags.NoFear))
        {
            var fear = CombatMath.MonsterFearOnHit(Rng, damage, monster.Hp, monster.MaxHp);
            if (fear > 0)
            {
                monster.Fear = fear;
                if (monster.IsVisible) Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} flees in terror!"));
            }
        }
        return false;
    }

    private void KillMonster(Monster monster)
    {
        // A shapechanged monster dies as what it really is (Sauron, not Wolf-Sauron).
        var race = monster.OriginalRace ?? monster.Race;
        var name = MonsterName(monster);
        var destroyed = race.Has(MonsterFlags.Undead) || race.Has("NONLIVING");
        Publish(new MessageEvent($"You have {(destroyed ? "destroyed" : "slain")} {name}."));

        var (whole, fraction) = CombatMath.KillExperience(race.Experience, race.Depth, Player.Level);
        Player.ExperienceFraction += fraction;
        if (Player.ExperienceFraction >= 0x10000)
        {
            Player.ExperienceFraction -= 0x10000;
            whole++;
        }
        GainExperience(whole);

        if (race.IsUnique)
        {
            KilledUniques.Add(race.Id);
            AddHistory($"Killed {race.Name}");
        }
        QuestKill(monster);
        Level.Monsters.Remove(monster);
        DropCarried(monster);
        var (items, gold) = DropMonsterLoot(monster);
        NoteKill(monster, items, gold);
        Publish(new MonsterKilledEvent(race.Id, monster.Position, whole, race.IsUnique));
        BloodlustOnKill();
        if (monster == Commanded) ReleaseCommand(announce: false);
        if (InArena && !Level.Monsters.All.Any()) LeaveArena();
    }

    private void WakeMonster(Monster monster)
    {
        if (monster.Sleep <= 0) return;
        monster.Sleep = 0;
        if (monster.IsVisible) Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} wakes up."));
    }

    /// <summary>"the jackal", "Fang, Farmer Maggot's Dog", or "it" when unseen.</summary>
    public string MonsterName(Monster monster) =>
        !monster.IsVisible ? "it" : monster.Race.IsUnique ? monster.Race.Name : $"the {monster.Race.Name}";

    private static string CriticalMessage(CriticalGrade grade) => grade switch
    {
        CriticalGrade.Good => " It was a good hit!",
        CriticalGrade.Great => " It was a great hit!",
        CriticalGrade.Superb => " It was a superb hit!",
        CriticalGrade.HighGreat => " It was a *GREAT* hit!",
        CriticalGrade.HighSuperb => " It was a *SUPERB* hit!",
        _ => "",
    };
}
