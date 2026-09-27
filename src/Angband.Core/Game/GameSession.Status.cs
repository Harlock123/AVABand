using Angband.Core.Effects;
using Angband.Core.Time;

namespace Angband.Core.Game;

public sealed partial class GameSession
{
    /// <summary>Angband PY_REGEN_NORMAL and PY_REGEN_HPBASE (regeneration in 1/65536 hit points).</summary>
    private const int RegenNormal = 197;
    private const int RegenBase = 1442;

    /// <summary>Hurts the player (Angband take_hit). Death happens below zero hit points.</summary>
    public void TakeHit(int damage, string killer)
    {
        if (Player.IsDead || damage <= 0) return;
        Player.Hp -= damage;
        Disturb();
        CombatRegenOnWound(damage, killer);
        Publish(new PlayerHurtEvent(damage, Player.Hp, Player.MaxHp));

        if (Player.Hp < 0)
        {
            // Angband take_hit: bloodlust can keep you fighting past death's door.
            if (Bloodlust > 0 && Player.Hp + Bloodlust + Player.Level >= 0)
            {
                Publish(new MessageEvent(Rng.RandInt0(10) != 0 ? "Your lust for blood keeps you alive!"
                    : "So great was his prowess and skill in warfare, the Elves said: 'The Mormegil cannot be slain, save by mischance.'"));
                return;
            }
            if (CheatDeath()) return;
            Player.IsDead = true;
            Player.KilledBy = killer;
            NoteDeath();
            Publish(new MessageEvent("You die."));
            Publish(new PlayerDiedEvent(killer, Player.Depth));
        }
    }

    /// <summary>Adds to a timed effect, printing its message. Returns false for unknown effects.</summary>
    public bool IncreaseTimed(string id, int amount)
    {
        if (Data.Timed(id) is not { } def) return false;
        if (id == TimedIds.Afraid && (Player.Timed.Has(TimedIds.Hero) || Player.Timed.Has("berserk")))
        {
            Publish(new MessageEvent("You feel bold."));
            return true;
        }
        var wasActive = Player.Timed.Has(id);
        var message = Player.Timed.Increase(def, amount);
        if (!wasActive && Player.Timed.Has(id)) OnTimedStarted(id);
        if (message is not null)
        {
            Publish(new MessageEvent(message));
            Publish(new StatusChangedEvent(id, Player.Timed[id]));
        }
        if (id is TimedIds.Hero or "berserk" && Player.Timed.Has(TimedIds.Afraid) && Data.Timed(TimedIds.Afraid) is { } fear
            && Player.Timed.Set(fear, 0) is { } bold)
            Publish(new MessageEvent(bold));
        RecalculateBonuses();
        return true;
    }

    /// <summary>World upkeep every 10 game turns (Angband process_world).</summary>
    private void WorldTick(long gameTurn)
    {
        Publish(new WorldTickEvent(gameTurn, Player.Depth, IsDaytime));
        CountStoreDay(gameTurn);
        if (gameTurn % (Data.Constants.DayLength / 2) == 0 && Player.Depth == 0)
        {
            ApplyTownLighting();
            Publish(new DayNightChangedEvent(IsDaytime));
            Publish(new MessageEvent(IsDaytime ? "The sun has risen." : "The sun has fallen."));
        }
        if (Player.IsDead) return;

        var timed = Player.Timed;
        if (timed.Has(TimedIds.Poisoned)) TakeHit(1, "poison");
        if (timed.Has(TimedIds.Cut))
            TakeHit(timed[TimedIds.Cut] switch { > 200 => 3, > 100 => 2, _ => 1 }, "a fatal wound");

        HungerTick(gameTurn);
        if (Player.IsDead) return;
        BloodlustUpkeep();
        if (Player.IsDead) return;
        // Angband TMD_HEAL (Rapid Regeneration): 30 hit points a turn.
        if (timed.Has("heal") && Player.Hp < Player.MaxHp) Player.Hp = Math.Min(Player.MaxHp, Player.Hp + 30);
        RegenerateHp();
        RegenerateMana();
        ItemUpkeep();

        var ended = false;
        foreach (var (id, _) in timed.Active.ToList())
        {
            if (Data.Timed(id) is not { } def) continue;
            if (timed.Decrease(def, 1) is { } message)
            {
                Publish(new MessageEvent(message));
                Publish(new StatusChangedEvent(id, 0));
                if (!timed.Has(id)) OnTimedEnded(id);
                ended = true;
            }
        }
        if (ended) RecalculateBonuses();

        MonsterUpkeep(gameTurn);
    }

    /// <summary>Angband player_regen_hp: a fixed-point trickle, doubled while resting.</summary>
    private void RegenerateHp()
    {
        if (Player.Hp >= Player.MaxHp)
        {
            Player.HpFraction = 0;
            return;
        }

        var percent = Hunger.RegenPercent(HungerLevel, RegenNormal);
        if (Player.IsResting) percent *= 2;
        if (Player.Timed.Has(TimedIds.Regen)) percent *= 2;
        if (Player.Regenerates || Player.HasGearFlag(Definitions.ItemFlags.Regen)) percent *= 2;
        if (ClassHas(Definitions.ClassFlags.ImpairHp)) percent /= 2; // blackguards heal slowly
        var t = Player.Timed;
        if (t.Has(TimedIds.Paralyzed) || t.Has(TimedIds.Poisoned) || t.Has(TimedIds.Stun) || t.Has(TimedIds.Cut))
            percent = 0;

        var gain = (long)Player.MaxHp * percent + RegenBase;
        Player.Hp += (int)(gain >> 16);
        Player.HpFraction += (int)(gain & 0xFFFF);
        if (Player.HpFraction >= 0x10000)
        {
            Player.HpFraction -= 0x10000;
            Player.Hp++;
        }
        if (Player.Hp >= Player.MaxHp)
        {
            Player.Hp = Player.MaxHp;
            Player.HpFraction = 0;
        }
    }

    /// <summary>
    /// Rest until healed and free of harmful effects, a monster comes into view, or the player is
    /// hurt (Angband 'R&amp;').
    /// </summary>
    private bool Rest()
    {
        if (Level.Monsters.All.Any(m => m.IsVisible))
        {
            Publish(new MessageEvent("You cannot rest with monsters nearby."));
            return false;
        }

        var rested = false;
        Player.IsResting = true;
        for (var i = 0; i < 5000 && Player.IsResting && !Player.IsDead; i++)
        {
            var harmful = Player.Timed.Active.Any(kv => Data.Timed(kv.Key)?.Harmful == true);
            // Angband: rest until hit points and mana are full (a blackguard's mana comes from fighting).
            var manaFull = Player.Mana >= Player.MaxMana || ClassHas(Definitions.ClassFlags.CombatRegen);
            if (Player.Hp >= Player.MaxHp && manaFull && !harmful) break;

            SpendAndAdvance(EnergyTable.MoveEnergy);
            rested = true;
            if (Level.Monsters.All.Any(m => m.IsVisible))
            {
                Publish(new MessageEvent("You stop resting: something comes into view."));
                break;
            }
        }
        Player.IsResting = false;
        return rested;
    }
}
