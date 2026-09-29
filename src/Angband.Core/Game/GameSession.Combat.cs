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
        // Angband py_attack_real: an attack frees a held monster.
        if (monster.Held > 0)
        {
            monster.Held = 0;
            if (monster.IsVisible) Publish(new MessageEvent($"{Capitalize(name)} is no longer held."));
        }
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

        // Angband improve_attack_modifier over the weapon and everything else worn but the bow (a
        // ring's or gloves' slay counts too); without a weapon, a punch.
        var gearSlays = Player.Inventory.Equipped.Where(i => i != weapon && i.Base.Slot != EquipSlot.Bow).SelectMany(i => i.Slays).ToList();
        var gearBrands = Player.Inventory.Equipped.Where(i => i != weapon && i.Base.Slot != EquipSlot.Bow).SelectMany(i => i.Brands).ToList();
        var (multiplier, verb, rune, oMultiplier) = weapon is null ? (1, "punch", null, 10) : BestMultiplier(weapon, monster);
        if ((gearSlays.Count > 0 || gearBrands.Count > 0) && BestMultiplier(gearSlays, gearBrands, monster, learn: true) is var worn
            && (PercentDamage ? worn.OMultiplier > oMultiplier : worn.Multiplier > multiplier))
            (multiplier, verb, rune, oMultiplier) = worn;
        // Temporary brands and slays (Angband player_timed.txt brand/slay: the poison coating,
        // Smite Evil, Demon Bane...) better the blow, weapon or not.
        var temporary = Player.Timed.Active.Select(kv => Data.Timed(kv.Key)).OfType<TimedEffectDef>().ToList();
        if (temporary.Any(t => t.Brand is not null || t.Slay is not null)
            && BestMultiplier([.. temporary.Select(t => t.Slay).OfType<SlayDef>()], [.. temporary.Select(t => t.Brand).OfType<BrandDef>()],
                monster, learn: false) is var temp
            && (PercentDamage ? temp.OMultiplier > oMultiplier : temp.Multiplier > multiplier))
            (multiplier, verb, rune, oMultiplier) = (temp.Multiplier, temp.Verb, null, temp.OMultiplier);
        int damage;
        CriticalGrade grade;
        if (PercentDamage)
            damage = OMeleeDamage(monster, weapon, oMultiplier, weaponToHit + Player.EffectiveToHit, out grade);
        else
        {
            // Angband melee_damage (a punch does 1) with the weapon's own to-dam, then critical_melee
            // (only with a weapon), then the rest of the to-dam.
            damage = (weapon is null ? 1 : dice.Roll(Rng)) * multiplier + weaponToDam;
            grade = CriticalGrade.None;
            if (weapon is not null)
                damage = CombatMath.CriticalMelee(Rng, weapon.Weight, weaponToHit + Player.EffectiveToHit,
                    Player.SkillMelee, damage, out grade, Player.Level, IsDebuffed(monster));
            damage += Player.EffectiveToDam;
        }
        if (damage <= 0)
        {
            damage = 0;
            verb = "fail to harm";
        }

        Publish(new PlayerAttackEvent(monster.Id, Hit: true, damage, grade));
        Publish(new MessageEvent($"You {ShapeBlowVerb() ?? verb} {name}{DamageNote(damage)}.{CriticalMessage(grade)}"));
        if (Player.Timed.Has("att_conf") && Data.Timed("att_conf") is { } glow)
        {
            // Angband blow_side_effects: the glowing hands fade, confusing the monster 10 turns and a
            // tenth of a roll up to your level.
            if (Player.Timed.Set(glow, 0) is { } faded) Publish(new MessageEvent(faded));
            MonIncTimed(monster, MonsterCondition.Confused, 10 + Rng.RandInt0(Player.Level) / 10);
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
    private (int Multiplier, string Verb, string? Rune, int OMultiplier) BestMultiplier(Item item, Monster monster) =>
        BestMultiplier(item.Slays, item.Brands, monster, learn: true);

    /// <summary>
    /// The best of these slays and brands against the monster (Angband improve_attack_modifier);
    /// <paramref name="learn"/>: an item's, whose runes it teaches.
    /// </summary>
    private (int Multiplier, string Verb, string? Rune, int OMultiplier) BestMultiplier(IEnumerable<SlayDef> slays,
        IEnumerable<BrandDef> brands, Monster monster, bool learn)
    {
        var race = monster.Race;
        (int Multiplier, string Verb, string? Rune, int OMultiplier) best = (1, "hit", null, 10);
        // Percentage damage ranks slays and brands by their O-multipliers, as 4.2 does.
        bool Better(int multiplier, int oMultiplier) =>
            PercentDamage ? oMultiplier > best.OMultiplier : multiplier > best.Multiplier;
        foreach (var slay in slays)
        {
            // A slay that bites tells you what the creature is (Angband learns the race flag).
            if (race.Has(slay.MonsterFlag)) Lore.For(race.Id).FlagsKnown.Add(slay.MonsterFlag);
            if (race.Has(slay.MonsterFlag) && OSlayMultiplier(slay.MonsterFlag, slay.Multiplier) is var oSlay
                && Better(slay.Multiplier, oSlay))
                best = (slay.Multiplier, slay.Verb, RuneIds.Slay(slay.MonsterFlag), oSlay);
        }
        foreach (var brand in brands)
        {
            var element = Data.Element(brand.Element);
            // A known brand shows whether the monster resists it, or is hurt by it (Angband learns either way).
            if (!learn || Knowledge.KnowsRune(RuneIds.Brand(brand.Element)))
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
            var oBrand = OBrandMultiplier(brand.Element, brand.Multiplier, vulnerable);
            if (Better(multiplier, oBrand)) best = (multiplier, brand.Verb, RuneIds.Brand(brand.Element), oBrand);
        }
        return best;
    }

    /// <summary>
    /// Damages a monster (Angband mon_take_hit): wakes it, may kill it (granting experience and
    /// dropping loot) or frighten it. Returns true if it died. With <paramref name="pain"/>, a
    /// survivor you can see shows how it was hurt (Angband message_pain, as missiles, spells and
    /// breath do; melee doesn't).
    /// </summary>
    /// <param name="deathNote">Said instead of "You have slain ..." if it dies (a spell's "The orc dies.").</param>
    public bool DamageMonster(Monster monster, int damage, bool pain = false, string? deathNote = null)
    {
        Publish(new MonsterDamagedEvent(monster.Id, monster.Position, damage));
        Reveal(monster);
        WakeMonster(monster);
        monster.Hp -= damage;

        if (monster.Hp < 0)
        {
            KillMonster(monster, deathNote);
            return true;
        }
        if (pain && monster.IsVisible && PainMessage(monster, damage) is { } hurt)
            Publish(new MessageEvent($"{hurt[..^1]}{DamageNote(damage)}{hurt[^1]}"));

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

    /// <summary>
    /// Angband message_pain: by the share of its health the hit left it, one of its base's seven pain
    /// messages ("The orc grunts with pain."); unharmed, it says so.
    /// </summary>
    public string? PainMessage(Monster monster, int damage)
    {
        var name = Capitalize(MonsterName(monster));
        if (damage <= 0) return $"{name} is unharmed.";
        var baseId = monster.Race.Base;
        if (Data.MonsterBases.FirstOrDefault(b => b.Id == baseId) is not { Pain: > 0 } monsterBase
            || Data.Pain.FirstOrDefault(p => p.Type == monsterBase.Pain) is not { Messages.Count: 7 } pain) return null;
        long now = Math.Max(0, monster.Hp);
        var percentage = (int)(now * 100 / (now + damage));
        var index = percentage switch { > 95 => 0, > 75 => 1, > 50 => 2, > 35 => 3, > 20 => 4, > 10 => 5, _ => 6 };
        return $"{name} {SingularVerb(pain.Messages[index])}";
    }

    /// <summary>Angband's message verbs for one subject: <c>grunt[s]</c> → grunts, <c>cr[ies|y]</c> → cries.</summary>
    private static string SingularVerb(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\[([^|\]]*)(\|[^\]]*)?\]", m => m.Groups[1].Value);

    private void KillMonster(Monster monster, string? deathNote = null)
    {
        // A shapechanged monster dies as what it really is (Sauron, not Wolf-Sauron).
        var race = monster.OriginalRace ?? monster.Race;
        var name = MonsterName(monster);
        var destroyed = race.Has(MonsterFlags.Undead) || race.Has("NONLIVING");
        Publish(new MessageEvent(deathNote ?? $"You have {(destroyed ? "destroyed" : "slain")} {name}."));

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
        monster.Aware = true; // (Angband monster_wake with an aware chance of 100)
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
