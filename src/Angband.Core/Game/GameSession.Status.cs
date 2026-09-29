using Angband.Core.Geometry;
using Angband.Core.Effects;
using Angband.Core.Magic;
using Angband.Core.Time;

namespace Angband.Core.Game;

public sealed partial class GameSession
{
    /// <summary>Angband adj_con_fix: how much faster Constitution heals cuts, poison and stunning.</summary>
    private static readonly int[] ConFix =
        [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 4, 4, 5, 6, 6, 7, 7, 8, 8, 8, 9, 9, 9];

    /// <summary>Angband PY_REGEN_NORMAL and PY_REGEN_HPBASE (regeneration in 1/65536 hit points).</summary>
    private const int RegenNormal = 197;
    private const int RegenBase = 1442;

    /// <summary>
    /// Hurts the player (Angband take_hit). Death happens below zero hit points. With
    /// show_damage_taken the amount is noted on the message that said what hit (or, if nothing
    /// was said this turn, a message of its own) — unless <paramref name="note"/> is off, as for the
    /// ticks of poison, bleeding and hunger.
    /// </summary>
    public void TakeHit(int damage, string killer, bool note = true)
    {
        // Angband take_hit: invulnerability shrugs off all but the mightiest blows; damage
        // reduction (DAM_RED gear or shape) comes off every other hurt.
        if (Player.Timed.Has("invuln") && damage < 9000) return;
        damage -= DamageReduction;
        if (Player.IsDead || damage <= 0) return;
        Player.Hp -= damage;
        Disturb();
        CombatRegenOnWound(damage, killer);
        Publish(new PlayerHurtEvent(damage, Player.Hp, Player.MaxHp));
        if (note && Options[OptionIds.ShowDamageTaken]) NoteDamage(damage);

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

    /// <summary>
    /// Adds to a timed effect, printing its message (Angband player_inc_timed). With
    /// <paramref name="check"/> (Angband's TIMED_INC, as against TIMED_INC_NO_RES) the effect's
    /// <c>fail</c> lines can stop it: a protection, a resistance, a vulnerability, being stone, or
    /// another effect. Returns false for unknown or prevented effects.
    /// </summary>
    public bool IncreaseTimed(string id, int amount, bool check = true)
    {
        if (Data.Timed(id) is not { } def) return false;
        if (id == TimedIds.Afraid && (Player.Timed.Has(TimedIds.Hero) || Player.Timed.Has("berserk") || Player.Timed.Has("bold")))
        {
            Publish(new MessageEvent("You feel bold."));
            return true;
        }
        if (check && !IncreaseCheck(def)) return false;
        var wasActive = Player.Timed.Has(id);
        var message = Player.Timed.Increase(def, amount);
        if (!wasActive && Player.Timed.Has(id)) OnTimedStarted(id);
        if (message is not null)
        {
            SayTimed(message);
            Publish(new StatusChangedEvent(id, Player.Timed[id]));
        }
        if (id is TimedIds.Hero or "berserk" or "bold" && Player.Timed.Has(TimedIds.Afraid) && Data.Timed(TimedIds.Afraid) is { } fear
            && Player.Timed.Set(fear, 0) is { } bold)
            SayTimed(bold);
        RecalculateBonuses();
        return true;
    }

    /// <summary>
    /// Whether nothing keeps the effect from taking hold (Angband player_inc_check): each of its
    /// <c>fail</c> lines — worn protections and resistances show themselves as they do.
    /// </summary>
    private bool IncreaseCheck(Definitions.TimedEffectDef def)
    {
        foreach (var f in def.Fail)
        {
            switch (f.Kind)
            {
                case "protection" or "resist":
                    if (Player.Inventory.Equipped.Any(i => i.Resists.Contains(f.Id))) LearnRune(Definitions.RuneIds.Resist(f.Id));
                    if (Player.Resists.GetValueOrDefault(f.Id) > 0) return false;
                    break;
                case "vulnerable":
                    if (Player.Resists.GetValueOrDefault(f.Id) < 0) return false;
                    break;
                case "player":
                    if (f.Id == "ROCK" && IsRock) return false;
                    break;
                case "timed":
                    if (Player.Timed.Has(f.Id)) return false;
                    break;
            }
        }
        return true;
    }

    /// <summary>
    /// A timed effect's message, with Angband's tags for the wielded weapon filled in (Angband
    /// print_custom_message): <c>{kind}</c> its kind ("hands" bare-handed), <c>{s}</c> a verb's
    /// "s" for one weapon, <c>{is}</c> "is" or "are".
    /// </summary>
    private void SayTimed(string message)
    {
        if (message.Contains('{'))
        {
            var weapon = Player.Inventory.Weapon;
            message = message.Replace("{kind}", weapon?.Kind.Name ?? "hands")
                .Replace("{s}", weapon is { Number: 1 } ? "s" : "")
                .Replace("{is}", weapon is { Number: 1 } ? "is" : "are");
        }
        Publish(new MessageEvent(message));
    }

    /// <summary>Whether a timed effect is at the grade with this label (Angband player_timed_grade_eq).</summary>
    public bool TimedGradeIs(string id, string label) =>
        Player.Timed.Has(id) && Data.Timed(id)?.GradeAt(Player.Timed[id]) is { } g && g.Label == label;

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
    /// Angband process_world: one chance in 500 each world turn of a new monster, asleep, somewhere
    /// more than the sight range and five squares away.
    /// </summary>
    private void WanderingMonster()
    {
        if (InArena || !Rng.OneIn(Data.Constants.AllocMonsterChance)) return;
        var unavailable = new HashSet<string>(KilledUniques);
        foreach (var m in Level.Monsters.All.Where(m => m.Race.IsUnique)) unavailable.Add(m.Race.Id);
        if (PersistentLevels) unavailable.UnionWith(UniquesOnStoredLevels());
        if (_spawner.PlaceDistant(Level, Rng, Player.Position, Data.Constants.MaxSight + 5, Level.Depth, unavailable) is not { } placed) return;
        foreach (var m in placed) Scheduler.Add(m);
        DisguiseMonsters();
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

    /// <summary>
    /// Angband player_check_terrain_damage, not rolled: what a turn in lava would do to you — 150 fire,
    /// as your resistance takes it, halved by feather falling.
    /// </summary>
    public int ExpectedTerrainDamage(Loc p)
    {
        if (!Level.InBounds(p) || !Level.Has(p, Definitions.TerrainFlags.Fiery)) return 0;
        var damage = Player.Resists.GetValueOrDefault("fire") switch
        {
            >= 3 => 0,
            2 => 150 / 9,
            1 => 150 / 3,
            < 0 => 150 * 4 / 3,
            _ => 150,
        };
        return Player.HasGearFlag(Definitions.ItemFlags.Feather) ? damage / 2 : damage;
    }

    /// <summary>
    /// Angband move_player's question before damaging terrain: the warning to ask about, when (not
    /// confused) this step would cost more than a third of your hit points; null when no need.
    /// </summary>
    public string? DangerousStepWarning(GameCommand command)
    {
        var dir = command switch { WalkCommand w => w.Direction, JumpCommand j => j.Direction, _ => (Direction?)null };
        if (dir is not { } d || Player.Timed.Has(TimedIds.Confused)) return null;
        var to = Player.Position.Step(d);
        if (Level.Monsters.At(to) is not null) return null;
        return ExpectedTerrainDamage(to) > Player.Hp / 3 ? "The lava will scald you!  Really step in?" : null;
    }

    /// <summary>
    /// Angband player_take_terrain_damage: standing in lava burns — 100 + 1d100 fire, resisted as
    /// fire and halved by feather falling (lightfooted) — and may burn what you carry.
    /// </summary>
    private void TerrainDamage()
    {
        if (Player.IsDead || !Level.Has(Player.Position, Definitions.TerrainFlags.Fiery)) return;
        var damage = 100 + Rng.RandInt1(100);
        if (Player.HasGearFlag(Definitions.ItemFlags.Feather))
        {
            damage /= 2;
            LearnRune(Definitions.RuneIds.Flag(Definitions.ItemFlags.Feather));
        }
        Publish(new MessageEvent("The lava burns you!"));
        ElementalHit("fire", damage, "burning to a cinder in lava");
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
        QuestUpkeep();
        if (gameTurn % (Data.Constants.DayLength / 2) == 0 && Player.Depth == 0)
        {
            ApplyTownLighting();
            Publish(new DayNightChangedEvent(IsDaytime));
            Publish(new MessageEvent(IsDaytime ? "The sun has risen." : "The sun has fallen."));
        }
        if (Player.IsDead) return;

        var timed = Player.Timed;
        if (timed.Has(TimedIds.Poisoned)) TakeHit(1, "poison", note: false);
        // Angband PF_ROCK (the Púkel-man's shape): a stone body neither bleeds nor heals its cuts.
        if (timed.Has(TimedIds.Cut) && !IsRock)
            TakeHit(timed[TimedIds.Cut] switch { > 200 => 3, > 100 => 2, _ => 1 }, "a fatal wound", note: false);

        HungerTick(gameTurn);
        if (Player.IsDead) return;
        BloodlustUpkeep();
        if (Player.IsDead) return;
        // Angband TMD_HEAL (Rapid Regeneration): 30 hit points a turn.
        if (timed.Has("heal") && Player.Hp < Player.MaxHp) Player.Hp = Math.Min(Player.MaxHp, Player.Hp + 30);
        BlackBreathUpkeep();
        WanderingMonster();
        DrainExperienceUpkeep();
        RegenerateHp();
        RegenerateMana();
        ItemUpkeep();

        // Angband decrease_timeouts: most effects wear off a turn at a time; cuts, poison and
        // stunning as fast as Constitution heals them (a mortal wound, or a stone body, not at all).
        var ended = false;
        var adjust = ConFix[StatTables.Index(Player.Stats.GetValueOrDefault("con", 15))] + 1;
        foreach (var (id, _) in timed.Active.ToList())
        {
            if (Data.Timed(id) is not { } def) continue;
            var decrease = id switch
            {
                TimedIds.Cut => IsRock || TimedGradeIs(id, "Mortal Wound") ? 0 : adjust,
                TimedIds.Poisoned or TimedIds.Stun => adjust,
                _ => 1,
            };
            if (decrease == 0) continue;
            if (id == TimedIds.Stun) ended = true; // its grade sets the penalties
            if (timed.Decrease(def, decrease) is { } message)
            {
                SayTimed(message);
                Publish(new StatusChangedEvent(id, 0));
                if (!timed.Has(id)) OnTimedEnded(id);
                ended = true;
            }
        }
        if (ended) RecalculateBonuses();

        MonsterUpkeep(gameTurn);
        // Angband process_world: noise and scent, once a turn (not while resting).
        if (!Player.IsResting) UpdateNoiseAndScent();
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

    /// <summary>Angband make_noise and update_scent.</summary>
    private void UpdateNoiseAndScent()
    {
        var covertracks = Player.Timed.Has("covertracks");
        Noise.Update(Level, Player.Position, covertracks ? 4 : 1);
        Scent.Lay(Level, Player.Position, covertracks);
    }

    // Messages said this turn, for noting damage on them (show_damage_taken).
    private long _messagesSaid, _turnMessageMark, _notedMessage = -1;

    /// <summary>A new turn (the player's command, or a monster's move): messages before it aren't this hit's.</summary>
    private void MarkTurnForDamageNotes() => _turnMessageMark = _messagesSaid;

    private void NoteDamage(int damage)
    {
        if (_messagesSaid > _turnMessageMark && _notedMessage != _messagesSaid)
            Publish(new DamageNoteEvent(damage));
        else
            Publish(new MessageEvent($"You take {damage} damage."));
        _notedMessage = _messagesSaid;
    }
}
