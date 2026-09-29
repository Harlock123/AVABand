using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Items;
using Angband.Core.Magic;
using Angband.Core.Monsters;

namespace Angband.Core.Game;

// Angband mon-attack.c make_attack_normal and mon-blows.c: a monster's blows against the player.
public sealed partial class GameSession
{
    /// <summary>Angband STUN_HIT_REDUCTION and STUN_DAM_REDUCTION: a stunned monster hits a quarter less often, and a quarter less hard.</summary>
    private const int MonsterStunReduction = 25;

    /// <summary>What melee_effect_elemental says as the element strikes.</summary>
    private static readonly Dictionary<string, string> ElementBlowMessages = new()
    {
        ["acid"] = "You are covered in acid!", ["elec"] = "You are struck by electricity!",
        ["fire"] = "You are enveloped in flames!", ["cold"] = "You are covered with frost!",
    };

    /// <summary>
    /// Angband make_attack_normal: each blow (a NONE blow always lands) tests to hit; protection
    /// from evil may then turn it; it is described, rolled (a quarter less from a stunned monster)
    /// and handled by its effect; a hard one may cut or stun. A thief that got something blinks
    /// away once its blows are done, and the blows stop if you are moved.
    /// </summary>
    private void MonsterMelee(Monster monster)
    {
        var race = monster.Race;
        var name = Capitalize(MonsterName(monster));
        var killer = race.IsUnique ? race.Name : Article(race.Name);
        var rlev = Math.Max(race.Depth, 1);
        var lore = Lore.For(race.Id);
        var blinked = false;

        _attacker = race.Id;
        _actingMonster = monster;
        for (var index = 0; index < race.Blows.Count; index++)
        {
            if (Player.IsDead || !monster.IsActive) break;
            var blow = race.Blows[index];
            var method = Data.BlowMethod(blow.Method);
            var effect = Data.BlowEffect(blow.Effect);
            if (method is null || effect is null) continue;
            var where = Player.Position;
            var depth = Player.Depth;
            var visible = monster.IsVisible;
            var obvious = false;
            var damage = 0;

            var toHit = effect.Power + rlev * 3;
            if (monster.Stun > 0) toHit = toHit * (100 - MonsterStunReduction) / 100;
            if (effect.Id == "none" || CheckHit(toHit))
            {
                // Protection from evil: evil monsters no deeper than you are usually repelled.
                if (Player.Timed.Has("prot_evil"))
                {
                    if (visible) LearnMonsterFlag(race, MonsterFlags.Evil);
                    if (race.Has(MonsterFlags.Evil) && Player.Level >= rlev && Rng.RandInt0(100) + Player.Level > 50)
                    {
                        Publish(new MessageEvent($"{name} is repelled."));
                        continue;
                    }
                }

                var act = method.Act(Rng);
                obvious = true;
                damage = blow.Damage.Roll(Rng);
                if (monster.Stun > 0) damage = damage * (100 - MonsterStunReduction) / 100;
                Publish(new MessageEvent($"{name} {act}{(act.EndsWith('\'') || act.EndsWith('!') ? "" : ".")}"));

                var hit = new BlowContext(monster, method, rlev, killer, damage);
                BlowEffect(effect.Id, hit);
                damage = hit.Damage;
                blinked |= hit.Blinked;
                Publish(new MonsterAttackEvent(monster.Id, Hit: true, damage, blow.Method));

                // Only one of a cut and a stun, from a blow near its most.
                var doCut = method.Cut && !Player.IsDead;
                var doStun = method.Stun && !Player.IsDead;
                if (doCut && doStun)
                {
                    if (Rng.RandInt0(100) < 50) doCut = false;
                    else doStun = false;
                }
                if (doCut && CombatMath.CutAmount(Rng, CombatMath.MonsterCritical(Rng, blow.Damage, damage)) is > 0 and var cut)
                    IncreaseTimed(TimedIds.Cut, cut);
                if (doStun && CombatMath.StunAmount(Rng, CombatMath.MonsterCritical(Rng, blow.Damage, damage)) is > 0 and var stun)
                    IncreaseTimed(TimedIds.Stun, stun);
            }
            else
            {
                if (visible && method.Miss) Publish(new MessageEvent($"{name} misses you."));
                Publish(new MonsterAttackEvent(monster.Id, Hit: false, 0, blow.Method));
            }

            // Lore counts the blows seen to land (or all of them, once it has seen ten).
            if (visible && (obvious || damage > 0 || lore.BlowSeen(index) > 10)) lore.SeeBlow(index);
            if (Player.Position != where || Player.Depth != depth) break;
        }

        if (blinked && monster.IsActive)
        {
            if (!Player.IsDead && monster.IsVisible) Publish(new MessageEvent("There is a puff of smoke!"));
            TeleportMonster(monster, Data.Constants.MaxSight * 2 + 5);
        }
        _attacker = null;
        _actingMonster = null;
    }

    /// <summary>Angband check_hit: the blow tests against your armour, which shows its enchantment.</summary>
    private bool CheckHit(int toHit)
    {
        foreach (var item in Player.Inventory.Equipped.ToList()) LearnRunesOf(item, RuneIds.ToAc);
        return CombatMath.TestHit(Rng, toHit, Player.Armour, visible: true);
    }

    /// <summary>Angband melee_effect_handler_context_t, for the player as target.</summary>
    private sealed class BlowContext(Monster monster, BlowMethodDef method, int rlev, string killer, int damage)
    {
        public Monster Monster { get; } = monster;
        public BlowMethodDef Method { get; } = method;
        public int Rlev { get; } = rlev;
        public string Killer { get; } = killer;
        public int Damage { get; set; } = damage;
        public bool Blinked { get; set; }
    }

    /// <summary>Angband melee_handler_for_blow_effect: what each blow effect does.</summary>
    private void BlowEffect(string effect, BlowContext c)
    {
        switch (effect)
        {
            case "none": c.Damage = 0; break;
            case "hurt":
                c.Damage = CombatMath.ArmourReduce(c.Damage, Player.Armour);
                TakeHit(c.Damage, c.Killer);
                break;
            case "poison":
                BlowElemental(c, "pois", pure: false);
                if (Player.IsDead) return;
                IncreaseTimed(TimedIds.Poisoned, 5 + Rng.RandInt1(c.Rlev));
                LearnAboutPlayer(c.Monster, "pois");
                break;
            case "acid" or "elec" or "fire" or "cold": BlowElemental(c, effect, pure: true); break;
            case "disenchant":
                if (DamageTarget(c)) return;
                if (Player.Resists.GetValueOrDefault("disen") <= 0) Disenchant();
                LearnAboutPlayer(c.Monster, "disen");
                break;
            case "drain_charges": BlowDrainCharges(c); break;
            case "eat_gold": BlowEatGold(c); break;
            case "eat_item": BlowEatItem(c); break;
            case "eat_food": BlowEatFood(c); break;
            case "eat_light":
                if (DamageTarget(c)) return;
                DrainLight(250 + Rng.RandInt1(250));
                break;
            case "blind": BlowTimed(c, TimedIds.Blind, 10 + Rng.RandInt1(c.Rlev), "blind", null); break;
            case "confuse": BlowTimed(c, TimedIds.Confused, 3 + Rng.RandInt1(c.Rlev), "conf", null); break;
            case "terrify": BlowTimed(c, TimedIds.Afraid, 3 + Rng.RandInt1(c.Rlev), "fear", "You stand your ground!"); break;
            case "paralyze":
                // No paralysis without harm while you are already held.
                if (Player.Timed.Has(TimedIds.Paralyzed) && c.Damage < 1) c.Damage = 1;
                BlowTimed(c, TimedIds.Paralyzed, 3 + Rng.RandInt1(c.Rlev), "free_act", "You resist the effects!");
                break;
            case "lose_str" or "lose_int" or "lose_wis" or "lose_dex" or "lose_con":
                if (DamageTarget(c)) return;
                DrainStat(effect[5..]);
                break;
            case "lose_all":
                if (DamageTarget(c)) return;
                foreach (var stat in new[] { "str", "dex", "con", "int", "wis" }) DrainStat(stat);
                break;
            case "shatter":
                c.Damage = CombatMath.ArmourReduce(c.Damage, Player.Armour);
                if (DamageTarget(c)) return;
                if (c.Damage > 23) Earthquake(c.Damage / 12, c.Monster.Position);
                if (c.Damage > 100)
                {
                    var value = c.Damage - 100;
                    if (Rng.RandInt1(value) > 40) ThrustAway(c.Monster.Position, 1 + value / 40);
                }
                break;
            case "exp_10": BlowExperience(c, 95, Rng.Damroll(10, 6)); break;
            case "exp_20": BlowExperience(c, 90, Rng.Damroll(20, 6)); break;
            case "exp_40": BlowExperience(c, 75, Rng.Damroll(40, 6)); break;
            case "exp_80": BlowExperience(c, 50, Rng.Damroll(80, 6)); break;
            case "hallu":
                if (DamageTarget(c)) return;
                IncreaseTimed(TimedIds.Image, 3 + Rng.RandInt1(c.Rlev / 2));
                LearnAboutPlayer(c.Monster, "chaos");
                break;
            case "black_breath":
                if (DamageTarget(c)) return;
                if (Rng.OneIn(5)) IncreaseTimed("blackbreath", c.Damage / 10, check: false);
                break;
            default:
                TakeHit(c.Damage, c.Killer);
                break;
        }
    }

    /// <summary>Angband monster_damage_target: the blow's damage as it stands. True if it killed you.</summary>
    private bool DamageTarget(BlowContext c)
    {
        TakeHit(c.Damage, c.Killer);
        return Player.IsDead;
    }

    /// <summary>
    /// Angband melee_effect_elemental: the greater of the blow's physical harm (armour counting 50
    /// more; none for a blow that isn't physical) and its element's, resisted; the element harms the
    /// pack too.
    /// </summary>
    private void BlowElemental(BlowContext c, string elementId, bool pure)
    {
        if (ElementBlowMessages.TryGetValue(elementId, out var feel)) Publish(new MessageEvent(feel));
        var physical = c.Method.Phys ? CombatMath.ArmourReduce(c.Damage, Player.Armour + 50) : 0;
        var elemental = Data.Element(elementId) is { } element
            ? CombatMath.ResistElement(Rng, element, c.Damage, Player.Resists.GetValueOrDefault(elementId))
            : c.Damage;
        // Being hit by an element reveals gear that resists it.
        if (Player.Inventory.Equipped.Any(i => i.Resists.Contains(elementId))) LearnRune(RuneIds.Resist(elementId));
        c.Damage = Math.Max(physical, elemental);
        if (elemental > 0) InventoryDamage(elementId, Math.Min(elemental * 5, 300));
        if (c.Damage > 0) TakeHit(c.Damage, c.Killer);
        if (pure) LearnAboutPlayer(c.Monster, elementId);
    }

    /// <summary>
    /// Angband melee_effect_timed: the damage, then the effect — a saving throw first if it allows one
    /// (the effect's own protections are player_inc_timed's to check).
    /// </summary>
    private void BlowTimed(BlowContext c, string timed, int amount, string learn, string? saveMessage)
    {
        if (DamageTarget(c)) return;
        if (saveMessage is not null && Rng.RandInt0(100) < Player.SkillSave)
        {
            Publish(new MessageEvent(saveMessage));
            return;
        }
        IncreaseTimed(timed, amount);
        LearnAboutPlayer(c.Monster, learn);
    }

    /// <summary>
    /// Angband melee_effect_experience: the damage, then — unless hold life (resisting
    /// <paramref name="chance"/> times in 100) keeps it — the drain plus 2% of your experience
    /// (a tenth of it through hold life).
    /// </summary>
    private void BlowExperience(BlowContext c, int chance, int drain)
    {
        TakeHit(c.Damage, c.Killer);
        LearnAboutPlayer(c.Monster, "hold_life");
        if (Player.IsDead) return;
        var holdLife = Player.HasGearFlag(ItemFlags.HoldLife) || Player.Resists.GetValueOrDefault("hold_life") > 0;
        if (holdLife && Rng.RandInt0(100) < chance)
        {
            Publish(new MessageEvent("You keep hold of your life force!"));
            return;
        }
        var amount = drain + Player.Experience / 100 * LifeDrainPercent;
        Publish(new MessageEvent(holdLife ? "You feel your life slipping away!" : "You feel your life draining away!"));
        LoseExperience(holdLife ? amount / 10 : amount);
    }

    /// <summary>
    /// A random slot of the pack, as Angband picks one (<c>randint0(pack_size)</c>, so an empty slot
    /// is as likely as a full one).
    /// </summary>
    private Item? RandomPackSlot()
    {
        var index = Rng.RandInt0(Player.Inventory.PackSize);
        return index < Player.Inventory.Pack.Count ? Player.Inventory.Pack[index] : null;
    }

    /// <summary>The thief's saving throw: Dexterity and level (not while paralysed).</summary>
    private bool ProtectsBelongings() =>
        !Player.Timed.Has(TimedIds.Paralyzed)
        && Rng.RandInt0(100) < AdjDexSafe[StatTables.Index(Player.Stats.GetValueOrDefault("dex", 15))] + Player.Level;

    /// <summary>
    /// Angband melee_effect_handler_DRAIN_CHARGES: of ten tries at a pack slot, the first wand or
    /// staff with charges loses level / (its level + 2) + 1 of them, healing the monster by its level
    /// for each.
    /// </summary>
    private void BlowDrainCharges(BlowContext c)
    {
        if (DamageTarget(c)) return;
        for (var tries = 0; tries < 10; tries++)
        {
            if (RandomPackSlot() is not { } item || item.Base.Id is not ("wand" or "staff") || item.Charges <= 0) continue;
            var unpower = c.Rlev / (item.Kind.Level + 2) + 1;
            item.Charges = Math.Max(item.Charges - unpower, 0);
            Publish(new MessageEvent("Energy drains from your pack!"));
            var monster = c.Monster;
            monster.Hp += Math.Min(c.Rlev * unpower, monster.MaxHp - monster.Hp);
            return;
        }
    }

    /// <summary>Angband melee_effect_handler_EAT_GOLD.</summary>
    private void BlowEatGold(BlowContext c)
    {
        if (DamageTarget(c)) return;
        if (ProtectsBelongings())
        {
            Publish(new MessageEvent("You quickly protect your money pouch!"));
            if (Rng.RandInt0(3) != 0) c.Blinked = true;
            return;
        }
        var gold = Player.Gold / 10 + Rng.RandInt1(25);
        if (gold < 2) gold = 2;
        if (gold > 5000) gold = Player.Gold / 20 + Rng.RandInt1(3000);
        gold = Math.Min(gold, Player.Gold);
        Player.Gold -= gold;
        if (gold <= 0)
        {
            Publish(new MessageEvent("Nothing was stolen."));
            return;
        }
        Publish(new MessageEvent("Your purse feels lighter."));
        Publish(new MessageEvent(Player.Gold > 0 ? $"{gold} coins were stolen!" : "All of your coins were stolen!"));
        var coins = Objects.MakeGold(Rng, Level.Depth);
        coins.GoldValue = (int)Math.Min(int.MaxValue, gold);
        c.Monster.Carried.Add(coins);
        c.Blinked = true;
    }

    /// <summary>
    /// Angband melee_effect_handler_EAT_ITEM and steal_player_item: a saving throw, then ten tries at
    /// a pack slot for something not an artifact — which it can't take if it slays the thief.
    /// </summary>
    private void BlowEatItem(BlowContext c)
    {
        if (DamageTarget(c)) return;
        if (ProtectsBelongings())
        {
            Publish(new MessageEvent("You grab hold of your backpack!"));
            c.Blinked = true;
            return;
        }
        for (var tries = 0; tries < 10; tries++)
        {
            if (RandomPackSlot() is not { } item || item.IsArtifact) continue;
            var split = item.Number > 1;
            if (item.Slays.Any(s => c.Monster.Race.Has(s.MonsterFlag)))
                Publish(new MessageEvent($"{Capitalize(MonsterName(c.Monster))} tries to steal {(split ? "one of your" : "your")} "
                                         + $"{Describe(item, withArticle: false)}, but fails."));
            else
            {
                Publish(new MessageEvent($"{(split ? "One of your" : "Your")} {Describe(item, withArticle: false)} was stolen!"));
                c.Monster.Carried.Add(Player.Inventory.Remove(item, 1, () => Objects.NextSerial++));
                RecalculateBonuses();
            }
            c.Blinked = true;
            return;
        }
    }

    /// <summary>Angband melee_effect_handler_EAT_FOOD: of ten tries at a pack slot, the first food or mushroom.</summary>
    private void BlowEatFood(BlowContext c)
    {
        if (DamageTarget(c)) return;
        for (var tries = 0; tries < 10; tries++)
        {
            if (RandomPackSlot() is not { } item || item.Base.Id is not ("food" or "mushroom")) continue;
            var name = Describe(item.Clone(0, 1), withArticle: false);
            Publish(new MessageEvent(item.Number == 1 ? $"Your {name} was eaten!" : $"One of your {Describe(item, withArticle: false)} was eaten!"));
            Player.Inventory.Remove(item, 1, () => Objects.NextSerial++);
            return;
        }
    }

    /// <summary>
    /// Angband effect_handler_DISENCHANT: one of the eight slots that aren't jewellery or a light, at
    /// random; if it holds something enchanted, an artifact resists 60 times in 100, otherwise a
    /// weapon or launcher loses a point to-hit and to-dam, armour a point of armour (two, one time in
    /// five, above +5).
    /// </summary>
    private void Disenchant()
    {
        EquipSlot[] slots = [EquipSlot.Weapon, EquipSlot.Bow, EquipSlot.Body, EquipSlot.Cloak, EquipSlot.Shield,
            EquipSlot.Head, EquipSlot.Hands, EquipSlot.Feet];
        var count = slots.Length;
        var i = slots.Length - 1;
        for (; i >= 0; i--)
            if (Rng.OneIn(count--)) break;
        if (Player.Inventory.InSlot(slots[Math.Max(i, 0)]) is not { } item) return;
        if (item.ToHit <= 0 && item.ToDam <= 0 && item.ToAc <= 0) return;
        var name = Describe(item, withArticle: false);
        if (item.IsArtifact && Rng.RandInt0(100) < 60)
        {
            Publish(new MessageEvent($"Your {name} resist{(item.Number != 1 ? "" : "s")} disenchantment!"));
            return;
        }
        if (item.Base.Slot is EquipSlot.Weapon or EquipSlot.Bow)
        {
            if (item.ToHit > 0) item.ToHit--;
            if (item.ToHit > 5 && Rng.RandInt0(100) < 20) item.ToHit--;
            if (item.ToDam > 0) item.ToDam--;
            if (item.ToDam > 5 && Rng.RandInt0(100) < 20) item.ToDam--;
        }
        else
        {
            if (item.ToAc > 0) item.ToAc--;
            if (item.ToAc > 5 && Rng.RandInt0(100) < 20) item.ToAc--;
        }
        Publish(new MessageEvent($"Your {name} {(item.Number != 1 ? "were" : "was")} disenchanted!"));
        RecalculateBonuses();
    }
}
