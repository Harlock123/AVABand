using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Monsters;

namespace Angband.Core.Game;

// Angband project-mon.c project_m: what a projection of yours (or a trap's) does to a monster.
public sealed partial class GameSession
{
    /// <summary>What project_m works out for one monster (Angband project_monster_handler_context_t).</summary>
    private sealed class MonsterHit(Monster monster, int damage, int r, bool seen)
    {
        public Monster Monster { get; } = monster;
        public int Damage { get; set; } = damage;
        public int R { get; } = r;
        public bool Seen { get; } = seen;
        public bool Skipped { get; set; }
        public bool Poly { get; set; }
        public int Teleport { get; set; }
        /// <summary>A special note in place of the pain message ("resists a lot.").</summary>
        public string? Hurt { get; set; }
        /// <summary>How it dies of this ("disintegrates!"); null for the ordinary death.</summary>
        public string? Die { get; set; }
        public int Stun { get; set; }
        public int Confuse { get; set; }
        public int Disenchant { get; set; }
        public MonsterRaceDef Race => Monster.Race;

        /// <summary>Angband adjust_radius: a side effect weakens with distance from a ball's centre.</summary>
        public int AdjustRadius(int amount) => (amount + R) / (R + 1);
    }

    /// <summary>
    /// Angband project_m for a projection of the player's (or a trap's), at <paramref name="r"/> from
    /// the centre of its blast: the element's handler (immunities, vulnerabilities and breathers
    /// resisting what they breathe, special notes and deaths), then the hurt — a special note if
    /// there is one, otherwise the monster's pain — then, if it lives, polymorph, a teleport, or the
    /// stun, confusion or disenchantment the element brings.
    /// </summary>
    internal void ProjectileHitsMonster(Monster monster, string source, string? element, int damage, int r = 0)
    {
        var hit = new MonsterHit(monster, damage, r, monster.IsVisible);
        if (element is not null) MonsterElementHandler(hit, element);
        if (hit.Skipped) return;

        var name = Capitalize(MonsterName(monster));
        Publish(new PlayerAttackEvent(monster.Id, Hit: true, hit.Damage, CriticalGrade.None));
        var died = false;
        if (hit.Damage > 0)
        {
            // Angband project_m_player_attack: the death message is the element's (or a scream unseen).
            var death = hit.Damage > monster.Hp
                ? hit.Seen ? $"{name} {SingularVerb(hit.Die ?? (IsDestroyedRace(monster.Race) ? "[is|are] destroyed." : "die[s]."))}"
                : "You hear a scream of agony!"
                : null;
            if (hit.Hurt is not null && hit.Seen && death is null)
                Publish(new MessageEvent($"{name} {SingularVerb(hit.Hurt)}{DamageNote(hit.Damage)}"));
            died = DamageMonster(monster, hit.Damage, pain: hit.Hurt is null || !hit.Seen, deathNote: death);
        }
        else if (hit.Hurt is not null && hit.Seen)
            Publish(new MessageEvent($"{name} {SingularVerb(hit.Hurt)}"));
        if (died || !monster.IsActive) return;

        // Angband project_m_apply_side_effects.
        if (hit.Poly)
        {
            if (monster.Race.IsUnique)
            {
                if (hit.Seen) Publish(new MessageEvent($"{name} is unaffected!"));
                return;
            }
            if (monster.Race.Depth > Rng.RandInt1(90))
            {
                if (hit.Seen) Publish(new MessageEvent($"{name} is unaffected!"));
                return;
            }
            PolymorphMonster(monster);
        }
        else if (hit.Teleport > 0)
        {
            TeleportMonster(monster, hit.Teleport);
            WakeMonster(monster);
        }
        else
        {
            if (hit.Stun > 0) MonIncTimed(monster, MonsterCondition.Stun, hit.Stun);
            if (hit.Confuse > 0) MonIncTimed(monster, MonsterCondition.Confused, hit.Confuse);
            if (hit.Disenchant > 0) MonIncTimed(monster, MonsterCondition.Disenchanted, hit.Disenchant);
        }
    }

    /// <summary>Angband monster_is_destroyed: the undead and the lifeless are destroyed, not killed.</summary>
    private static bool IsDestroyedRace(MonsterRaceDef race) => race.Has(MonsterFlags.Undead) || race.Has("NONLIVING");

    /// <summary>Angband's project_monster_handler_* for the elements.</summary>
    private void MonsterElementHandler(MonsterHit h, string element)
    {
        switch (element)
        {
            case "acid": ResistElement(h, "IM_ACID", 9); break;
            case "elec": ResistElement(h, "IM_ELEC", 9); break;
            case "pois": ResistElement(h, "IM_POIS", 9); break;
            case "fire": HurtImmune(h, "HURT_FIRE", "IM_FIRE", 2, 9, "catch[es] fire!", "disintegrate[s]!"); break;
            case "cold": HurtImmune(h, "HURT_COLD", "IM_COLD", 2, 9, "[is|are] badly frozen.", "freeze[s] and shatter[s]!"); break;
            case "light":
                if (h.Seen) LearnMonsterResponse(h.Monster, "HURT_LIGHT");
                if (h.Race.Spells.Contains("BR_LIGHT"))
                {
                    if (h.Seen) LearnMonsterSpell(h.Race, "BR_LIGHT");
                    h.Hurt = "resist[s].";
                    h.Damage = h.Damage * 2 / (Rng.RandInt1(6) + 6);
                }
                else if (h.Race.Has("HURT_LIGHT"))
                {
                    h.Hurt = "cringe[s] from the light!";
                    h.Die = "shrivel[s] away in the light!";
                    h.Damage *= 2;
                }
                break;
            case "dark": Breath(h, "BR_DARK", 2); break;
            case "sound":
                if (Rng.OneIn(3)) h.Stun = h.AdjustRadius(5 + Rng.RandInt1(10));
                Breath(h, "BR_SOUN", 2);
                break;
            case "shards": Breath(h, "BR_SHAR", 3); break;
            case "nexus":
                ResistOther(h, "IM_NEXUS", 3, true, "resist[s].");
                if (Rng.OneIn(3)) h.Teleport = 10;
                else if (Rng.OneIn(4)) h.Teleport = 50;
                break;
            case "nether":
                if (h.Seen) LearnMonsterResponse(h.Monster, "IM_NETHER");
                if (h.Race.Has(MonsterFlags.Undead))
                {
                    h.Hurt = "[is|are] immune.";
                    h.Damage = 0;
                }
                else if (h.Race.Has("IM_NETHER"))
                {
                    h.Hurt = "resist[s].";
                    h.Damage = h.Damage * 3 / (Rng.RandInt1(6) + 6);
                }
                else if (h.Race.Has(MonsterFlags.Evil))
                {
                    h.Damage /= 2;
                    h.Hurt = "resist[s] somewhat.";
                }
                break;
            case "chaos":
                h.Poly = !h.Race.Spells.Contains("BR_CHAO");
                h.Confuse = h.AdjustRadius(10 + Rng.RandInt1(10));
                Breath(h, "BR_CHAO", 3);
                h.Hurt = null;
                break;
            case "disen":
                ResistOther(h, "IM_DISEN", 3, true, "resist[s].");
                if (!h.Race.Has("IM_DISEN") && h.Race.Spells.Any(id => Data.MonsterSpell(id) is { Innate: false }))
                    h.Disenchant = h.AdjustRadius(5 + Rng.RandInt1(10));
                break;
            case "water": ResistOther(h, "IM_WATER", 0, false, "[is|are] immune."); break;
            case "ice":
                if (Rng.OneIn(3)) h.Stun = h.AdjustRadius(5 + Rng.RandInt1(10));
                HurtImmune(h, "HURT_COLD", "IM_COLD", 2, 9, "[is|are] badly frozen.", "freeze[s] and shatter[s]!");
                break;
            case "gravity":
                if (Rng.RandInt1(127) > h.Race.Depth) h.Teleport = 10;
                if (h.Race.Spells.Contains("BR_GRAV")) h.Teleport = 0;
                Breath(h, "BR_GRAV", 3);
                break;
            case "inertia": Breath(h, "BR_INER", 3); break;
            case "force":
                if (Rng.OneIn(3)) h.Stun = h.AdjustRadius(5 + Rng.RandInt1(10));
                Breath(h, "BR_WALL", 3);
                break;
            case "time": Breath(h, "BR_TIME", 3); break;
            case "plasma": ResistOther(h, "IM_PLASMA", 3, true, "resist[s]."); break;
            case "holy_orb": ResistOther(h, MonsterFlags.Evil, 2, false, "[is|are] hit hard."); break;
            // meteor, missile, mana, arrow: plain damage.
        }
    }

    // Angband project_monster_resist_element.
    private void ResistElement(MonsterHit h, string flag, int factor)
    {
        if (h.Seen) LearnMonsterResponse(h.Monster, flag);
        if (!h.Race.Has(flag)) return;
        h.Hurt = "resist[s] a lot.";
        h.Damage /= factor;
    }

    // Angband project_monster_resist_other.
    private void ResistOther(MonsterHit h, string flag, int factor, bool reduce, string message)
    {
        if (h.Seen) LearnMonsterResponse(h.Monster, flag);
        if (!h.Race.Has(flag)) return;
        h.Hurt = message;
        h.Damage *= factor;
        if (reduce) h.Damage /= Rng.RandInt1(6) + 6;
    }

    // Angband project_monster_hurt_immune.
    private void HurtImmune(MonsterHit h, string hurtFlag, string immuneFlag, int hurtFactor, int immuneFactor, string hurt, string die)
    {
        if (h.Seen)
        {
            LearnMonsterResponse(h.Monster, immuneFlag);
            LearnMonsterResponse(h.Monster, hurtFlag);
        }
        if (h.Race.Has(immuneFlag))
        {
            h.Hurt = "resist[s] a lot.";
            h.Damage /= immuneFactor;
        }
        else if (h.Race.Has(hurtFlag))
        {
            h.Hurt = hurt;
            h.Die = die;
            h.Damage *= hurtFactor;
        }
    }

    // Angband project_monster_breath: what a monster breathes, it resists.
    private void Breath(MonsterHit h, string spell, int factor)
    {
        if (!h.Race.Spells.Contains(spell)) return;
        if (h.Seen) LearnMonsterSpell(h.Race, spell);
        h.Hurt = "resist[s].";
        h.Damage = h.Damage * factor / (Rng.RandInt1(6) + 6);
    }

    /// <summary>The monster conditions a projection can bring (Angband MON_TMD_*).</summary>
    private enum MonsterCondition { Stun, Confused, Disenchanted }

    /// <summary>
    /// Angband mon_inc_timed for stunning, confusion and disenchantment: at least two turns to begin
    /// with, the longer of what it has and what it gets (at most 50), and the race's NO_STUN, NO_CONF
    /// or IM_DISEN making it "unaffected!".
    /// </summary>
    private void MonIncTimed(Monster monster, MonsterCondition condition, int amount)
    {
        var (resist, begin, more) = condition switch
        {
            MonsterCondition.Stun => (MonsterFlags.NoStun, "[is|are] stunned.", "[is|are] even more stunned."),
            MonsterCondition.Confused => (MonsterFlags.NoConf, "look[s] confused.", "look[s] more confused."),
            _ => ("IM_DISEN", "seem[s] less magical!", (string?)null),
        };
        var old = condition switch
        {
            MonsterCondition.Stun => monster.Stun, MonsterCondition.Confused => monster.Confused, _ => monster.Disenchanted,
        };
        if (old == 0 && amount < 2) amount = 2;
        var now = Math.Min(50, Math.Max(old, amount));
        if (now == old) return;
        var name = Capitalize(MonsterName(monster));
        if (monster.Race.Has(resist))
        {
            if (monster.IsVisible)
            {
                LearnMonsterFlag(monster.Race, resist);
                Publish(new MessageEvent($"{name} is unaffected!"));
            }
            return;
        }
        switch (condition)
        {
            case MonsterCondition.Stun: monster.Stun = now; break;
            case MonsterCondition.Confused: monster.Confused = now; break;
            default: monster.Disenchanted = now; break;
        }
        var note = old == 0 ? begin : more;
        if (note is not null && monster.IsVisible) Publish(new MessageEvent($"{name} {SingularVerb(note)}"));
    }
}
