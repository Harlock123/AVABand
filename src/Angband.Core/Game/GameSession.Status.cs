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
        // Angband take_hit: damage reduction (DAM_RED gear or shape) comes off every hurt.
        damage -= DamageReduction;
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
        // Angband player_timed.txt fail:2:CHAOS — resisting chaos keeps the mind clear.
        if (id == TimedIds.Image && Player.Resists.GetValueOrDefault("chaos") > 0)
        {
            if (Player.Inventory.Equipped.Any(i => i.Resists.Contains("chaos"))) LearnRune(Definitions.RuneIds.Resist("chaos"));
            return false;
        }
        if (id == TimedIds.Afraid && (Player.Timed.Has(TimedIds.Hero) || Player.Timed.Has("berserk") || Player.Timed.Has("bold")))
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
        if (id is TimedIds.Hero or "berserk" or "bold" && Player.Timed.Has(TimedIds.Afraid) && Data.Timed(TimedIds.Afraid) is { } fear
            && Player.Timed.Set(fear, 0) is { } bold)
            Publish(new MessageEvent(bold));
        RecalculateBonuses();
        return true;
    }

    /// <summary>
    /// Angband process_world: the Black Breath (a Ringwraith's touch) may each turn sicken you (CON),
    /// sap your strength (STR) and dim your life force (experience) — sustains and hold life don't
    /// help — until it lifts or Herbal Curing drives it out.
    /// </summary>
    private void BlackBreathUpkeep()
    {
        if (!Player.Timed.Has("blackbreath")) return;
        if (Rng.OneIn(2)) LoseStatToBlackBreath("con", "The Black Breath sickens you.");
        if (Rng.OneIn(2)) LoseStatToBlackBreath("str", "The Black Breath saps your strength.");
        if (Rng.OneIn(2))
        {
            Publish(new MessageEvent("The Black Breath dims your life force."));
            LoseExperience(100 + Player.Experience / 100 * LifeDrainPercent);
        }
    }

    /// <summary>
    /// Angband process_world: gear that drains experience (DRAIN_EXP) takes a little one time in ten:
    /// a tenth of 10d6 plus the life-drain share of it.
    /// </summary>
    private void DrainExperienceUpkeep()
    {
        if (!Player.HasGearFlag(Definitions.ItemFlags.DrainExp)) return;
        if (Player.Experience > 0 && Rng.OneIn(10))
            LoseExperience((Rng.Damroll(10, 6) + Player.Experience / 100 * LifeDrainPercent) / 10);
        LearnRune(Definitions.RuneIds.Flag(Definitions.ItemFlags.DrainExp));
    }

    /// <summary>A body of stone (Angband player flag ROCK, from the Púkel-man's shape).</summary>
    private bool IsRock => PlayerShape?.Flags.Contains("ROCK") == true;

    /// <summary>Damage reduction from gear and shape (Angband state.dam_red).</summary>
    public int DamageReduction =>
        Player.Inventory.Equipped.Sum(i => i.Modifier(Definitions.ItemModifiers.DamRed))
        + (PlayerShape?.Modifiers.GetValueOrDefault(Definitions.ItemModifiers.DamRed) ?? 0);

    /// <summary>
    /// Energy for a step (Angband energy_per_move): extra moves from gear or shape (MOVES) make
    /// each step take a fraction of a turn — half with one, a third with two; fewer, longer.
    /// </summary>
    public int MoveEnergyPerStep
    {
        get
        {
            var moves = Player.Inventory.Equipped.Sum(i => i.Modifier(Definitions.ItemModifiers.Moves))
                        + (PlayerShape?.Modifiers.GetValueOrDefault(Definitions.ItemModifiers.Moves) ?? 0);
            return EnergyTable.MoveEnergy * (1 + Math.Abs(moves) - moves) / (1 + Math.Abs(moves));
        }
    }

    /// <summary>Angband player_stat_dec: a point off the stat, sustained or not.</summary>
    private void LoseStatToBlackBreath(string stat, string message)
    {
        Publish(new MessageEvent(message));
        if (Player.Stats.GetValueOrDefault(stat, 15) <= 3) return;
        Player.StatDrain[stat] = Player.StatDrain.GetValueOrDefault(stat) + 1;
        RecalculateAfterStatChange();
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
        // Angband PF_ROCK (the Púkel-man's shape): a stone body neither bleeds nor heals its cuts.
        if (timed.Has(TimedIds.Cut) && !IsRock)
            TakeHit(timed[TimedIds.Cut] switch { > 200 => 3, > 100 => 2, _ => 1 }, "a fatal wound");

        HungerTick(gameTurn);
        if (Player.IsDead) return;
        BloodlustUpkeep();
        if (Player.IsDead) return;
        // Angband TMD_HEAL (Rapid Regeneration): 30 hit points a turn.
        if (timed.Has("heal") && Player.Hp < Player.MaxHp) Player.Hp = Math.Min(Player.MaxHp, Player.Hp + 30);
        BlackBreathUpkeep();
        DrainExperienceUpkeep();
        RegenerateHp();
        RegenerateMana();
        ItemUpkeep();

        var ended = false;
        foreach (var (id, _) in timed.Active.ToList())
        {
            if (Data.Timed(id) is not { } def) continue;
            if (id == TimedIds.Cut && IsRock) continue;
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
        // Blackguards heal slowly, as does anyone wearing the Ring of Open Wounds (Angband IMPAIR_HP).
        if (ClassHas(Definitions.ClassFlags.ImpairHp) || Player.HasGearFlag(Definitions.ItemFlags.ImpairHp)) percent /= 2;
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
