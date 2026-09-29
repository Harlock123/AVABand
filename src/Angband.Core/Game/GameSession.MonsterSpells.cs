using Angband.Core.Randomness;
using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Generation;
using Angband.Core.Geometry;
using Angband.Core.Monsters;

namespace Angband.Core.Game;

/// <summary>Monster spells and innate ranged attacks (Angband make_ranged_attack / mon-spell.c).</summary>
public sealed partial class GameSession
{
    /// <summary>Angband's spell types (list-mon-spells.h), by spell id: what the choosing filters go by.</summary>
    [Flags]
    private enum SpellType { None = 0, Annoy = 1, Bolt = 2, Ball = 4, Breath = 8, Direct = 16, Summon = 32, Haste = 64, Heal = 128, HealOther = 256, Tactic = 512, Escape = 1024, Innate = 2048 }

    private const SpellType DamageSpells = SpellType.Bolt | SpellType.Ball | SpellType.Breath | SpellType.Direct; // RST_DAMAGE

    private static SpellType TypeOf(string id) => id switch
    {
        "SHRIEK" or "WEAVE" => SpellType.Annoy | SpellType.Innate,
        "WHIP" or "SPIT" or "SHOT" or "ARROW" or "BOLT" or "BOULDER" => SpellType.Bolt | SpellType.Innate,
        _ when id.StartsWith("BR_", StringComparison.Ordinal) => SpellType.Breath | SpellType.Innate,
        _ when id.StartsWith("BA_", StringComparison.Ordinal) || id.StartsWith("BE_", StringComparison.Ordinal) || id == "STORM" => SpellType.Ball,
        _ when id.StartsWith("BO_", StringComparison.Ordinal) || id == "MISSILE" => SpellType.Bolt,
        "MIND_BLAST" or "BRAIN_SMASH" => SpellType.Direct | SpellType.Annoy,
        "WOUND" => SpellType.Direct,
        "SLOW" or "HOLD" => SpellType.Annoy | SpellType.Haste,
        "HASTE" => SpellType.Haste,
        "HEAL" => SpellType.Heal,
        "HEAL_KIN" => SpellType.HealOther,
        "BLINK" => SpellType.Tactic | SpellType.Escape,
        "TPORT" or "TELE_AWAY" or "TELE_LEVEL" => SpellType.Escape,
        "SHAPECHANGE" => SpellType.Tactic,
        _ when id.StartsWith("S_", StringComparison.Ordinal) => SpellType.Summon,
        _ => SpellType.Annoy,
    };

    /// <summary>
    /// Angband monster_can_cast: a roll against the race's frequency (innate or not) — halved while
    /// you taunt, doubled at its favourite range — then you must be in range and in its line of fire.
    /// </summary>
    private bool MonsterCanCast(Monster monster, bool innate)
    {
        var oneIn = innate ? monster.Race.InnateFrequency : monster.Race.SpellFrequency;
        if (oneIn <= 0) return false;
        var chance = 100 / oneIn;
        if (Player.Timed.Has("taunt")) chance /= 2;
        var distance = monster.Position.DistanceTo(Player.Position);
        if (distance == CombatRange(monster).Best) chance *= 2;
        if (Rng.RandInt0(100) >= chance) return false;
        if (distance > MaxRange) return false;
        return ProjectionPath.Projectable(Level, monster.Position, Player.Position, MaxRange);
    }

    /// <summary>
    /// Angband make_ranged_attack: a spell if the frequency allows (or else an innate attack); a smart
    /// monster near death leaves damaging spells aside half the time; one that isn't stupid drops
    /// what can't help (a heal at full health, haste while hasted, teleport-to beside you, a lash out
    /// of reach, what it has learned you resist, a bolt without a clear shot, a summons with no room);
    /// then one of the rest at random. A spell (not an innate attack) fails 25% of the time less a
    /// little — 4.2.5's formula takes the smaller of the race's spell power and 1, so it is 24 or 25%
    /// — plus 20 afraid and 50 confused or disenchanted; stupid monsters never fail.
    /// </summary>
    private bool MakeRangedAttack(Monster monster)
    {
        var innate = false;
        if (!MonsterCanCast(monster, false))
        {
            if (!MonsterCanCast(monster, true)) return false;
            innate = true;
        }
        var race = monster.Race;
        var spells = race.Spells.Select(Data.MonsterSpell).OfType<MonsterSpellDef>()
            .OrderBy(sp => Data.MonsterSpells.ToList().IndexOf(sp)).ToList();
        if (race.Has(MonsterFlags.Smart) && monster.Hp < monster.MaxHp / 10 && Rng.OneIn(2))
            spells = [.. spells.Where(sp => (TypeOf(sp.Id) & DamageSpells) == 0)];
        var stupid = race.Has(MonsterFlags.Stupid);
        if (!stupid)
        {
            var distance = monster.Position.DistanceTo(Player.Position);
            spells = [.. spells.Where(sp => sp.Id switch
            {
                "HEAL" => monster.Hp < monster.MaxHp,
                "HEAL_KIN" => WoundedKin(monster) is not null,
                "HASTE" => monster.Fast <= 10,
                "TELE_TO" or "TELE_SELF_TO" => distance != 1,
                "WHIP" => distance <= 2,
                "SPIT" => distance <= 3,
                _ => true,
            })];
            spells = WithoutKnownFailures(monster, spells);
            if (!ClearBolt(monster)) spells = [.. spells.Where(sp => (TypeOf(sp.Id) & SpellType.Bolt) == 0)];
            if (!SummonPossible(monster.Position)) spells = [.. spells.Where(sp => (TypeOf(sp.Id) & SpellType.Summon) == 0)];
        }
        // Angband choose_attack_spell: only innate attacks, or only spells, as was rolled for.
        var choice = spells.Where(sp => ((TypeOf(sp.Id) & SpellType.Innate) != 0) == innate).ToList();
        if (choice.Count == 0) return false;
        var spell = choice[Rng.RandInt0(choice.Count)];

        if (monster.Camouflaged) Reveal(monster);
        if (!innate && !stupid)
        {
            var power = Math.Min(race.Power, 1);
            var failRate = 25 - (power + 3) / 4;
            if (monster.Fear > 0) failRate += 20;
            if (monster.Confused > 0 || monster.Disenchanted > 0) failRate += 50;
            if (Rng.RandInt0(100) < failRate)
            {
                Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} tries to cast a spell, but fails."));
                return true;
            }
        }
        return CastSpell(monster, spell);
    }

    /// <summary>Angband summon_possible: an empty, unwarded floor square within 2 of the grid, in its line of sight.</summary>
    private bool SummonPossible(Loc grid)
    {
        for (var y = grid.Y - 2; y <= grid.Y + 2; y++)
        for (var x = grid.X - 2; x <= grid.X + 2; x++)
        {
            var near = new Loc(x, y);
            if (!Level.InBounds(near) || grid.DistanceTo(near) > 2) continue;
            if (Level[near].Trap != 0 && Data.TrapByIndex(Level[near].Trap) is { Warding: true }) continue;
            if (Level.IsEmptyFloor(near) && near != Player.Position && ProjectionPath.Projectable(Level, grid, near, 3)) return true;
        }
        return false;
    }

    /// <summary>A monster casts this spell (never failing): for tests.</summary>
    internal bool CastSpellForTest(Monster monster, string spellId) => CastSpell(monster, Data.MonsterSpell(spellId)!);

    /// <summary>The spell takes effect: its message, the monster's lore, then what it does.</summary>
    private bool CastSpell(Monster monster, MonsterSpellDef spell)
    {
        var race = monster.Race;
        var here = monster.Position;
        var seen = monster.IsVisible;
        var name = Capitalize(MonsterName(monster));

        // Some spells say something different at higher power (Angband's power-cutoff message levels).
        var tier = spell.LoreFor(race.Power);
        Publish(new MessageEvent(seen
            ? SpellText(tier?.Message ?? spell.Message, name, race)
            : tier?.UnseenMessage ?? spell.UnseenMessage));
        _attacker = race.Id;
        _actingMonster = monster;
        if (seen)
        {
            var lore = Lore.For(race.Id);
            lore.SpellsSeen.Add(spell.Id);
            lore.CastsSeen++;
            if (spell.Innate) lore.CastsInnate++;
            else lore.CastsSpell++;
        }
        Publish(new MonsterSpellEvent(monster.Id, spell.Id, seen, spell.Sound));
        var killer = race.IsUnique ? race.Name : Article(race.Name);

        switch (spell.Kind)
        {
            case MonsterSpellKind.Bolt:
                ShowProjection(here, ProjectionPath.Compute(Level, here, Player.Position, MaxRange, PathFlags.None), null,
                    spell.Element, ProjectionKind.Bolt);
                ElementalHit(spell.Element, SpellDamage(spell, race), killer, race.Power, monster.Position);
                break;
            case MonsterSpellKind.Ball:
                ShowProjection(here, ProjectionPath.Compute(Level, here, Player.Position, MaxRange, PathFlags.None),
                    BallArea(Player.Position, 2), spell.Element, ProjectionKind.Ball);
                ElementalHit(spell.Element, SpellDamage(spell, race), killer, race.Power, monster.Position);
                DestroyFloorObjects(BallArea(Player.Position, 2), spell.Element); // balls and breaths reach the floor too
                break;
            case MonsterSpellKind.Breath:
                ShowProjection(here, [], BreathArc(here, Player.Position), spell.Element, ProjectionKind.Breath);
                ElementalHit(spell.Element, Math.Min(spell.BreathCap, Math.Max(1, monster.Hp / Math.Max(1, spell.BreathDivisor))), killer,
                    race.Power, monster.Position);
                DestroyFloorObjects(BreathArc(monster.Position, Player.Position), spell.Element);
                break;
            case MonsterSpellKind.Wound when spell.PowerScaled:
            {
                // Angband 4.2 WOUND: (power/3*2)d5 damage and (power/5-10)d10 cuts, unless saved against.
                if (Rng.RandInt0(100) < Player.SkillSave)
                {
                    Publish(new MessageEvent(tier?.SaveMessage ?? "You resist the effects!"));
                    break;
                }
                TakeHit(Rng.Damroll(Math.Max(1, race.Power / 3 * 2), 5), killer);
                if (race.Power / 5 - 10 is > 0 and var cutDice && !Player.IsDead)
                    IncreaseTimed(TimedIds.Cut, Rng.Damroll(cutDice, 10));
                break;
            }
            case MonsterSpellKind.Wound:
                if (Rng.RandInt0(100) < Player.SkillSave) Publish(new MessageEvent("You resist the effects!"));
                else TakeHit(SpellDamage(spell, race), killer);
                break;
            case MonsterSpellKind.Storm:
            {
                // Angband STORM: balls of water, lightning and ice, each (power/3)d5 on a base.
                var dice = Math.Max(1, race.Power / 3);
                foreach (var (element, basis) in new[] { ("water", 30), ("elec", 20), ("ice", 20) })
                {
                    if (Player.IsDead) break;
                    ShowProjection(Player.Position, [], BallArea(Player.Position, 3), element, ProjectionKind.Ball);
                    ElementalHit(element, basis + Rng.Damroll(dice, 5), killer, race.Power, monster.Position);
                    DestroyFloorObjects(BallArea(Player.Position, 3), element);
                }
                break;
            }
            case MonsterSpellKind.Web:
                SpinWebs(monster);
                break;
            case MonsterSpellKind.Status:
                ApplySpellStatus(spell, tier?.SaveMessage);
                break;
            case MonsterSpellKind.Blink:
                TeleportMonster(monster, 10);
                break;
            case MonsterSpellKind.Teleport:
                TeleportMonster(monster, 100);
                break;
            case MonsterSpellKind.TeleportTo:
                TeleportPlayerTo(here);
                break;
            case MonsterSpellKind.Heal:
                monster.Hp = Math.Min(monster.MaxHp, monster.Hp + race.Depth * spell.HealPerLevel);
                if (monster.Fear > 0)
                {
                    monster.Fear = 0;
                    if (seen) Publish(new MessageEvent($"{name} recovers its courage."));
                }
                break;
            case MonsterSpellKind.Summon:
                SummonMonsters(monster, spell);
                break;
            case MonsterSpellKind.Shriek:
                foreach (var m in Level.Monsters.All.Where(m => m.Position.DistanceTo(here) <= 20))
                    m.Sleep = 0;
                break;
            case MonsterSpellKind.DrainMana:
            {
                // Angband DRAIN_MANA: up to d(level/2)+1 points, healing the caster six per point.
                var drained = Math.Min(Player.Mana, Rng.RandInt1(Math.Max(1, race.Depth / 2)) + 1);
                Player.Mana -= drained;
                Publish(new MessageEvent("You feel your mind drain away."));
                if (monster.Hp < monster.MaxHp)
                {
                    monster.Hp = Math.Min(monster.MaxHp, monster.Hp + 6 * drained);
                    if (seen) Publish(new MessageEvent($"{name} appears healthier."));
                }
                break;
            }
            case MonsterSpellKind.Mind:
                if (Rng.RandInt0(100) < Player.SkillSave) Publish(new MessageEvent("You resist the effects!"));
                else
                {
                    TakeHit(SpellDamage(spell, race), killer);
                    if (spell.Timed is not null && !Player.IsDead) ApplySpellStatus(spell);
                }
                break;
            case MonsterSpellKind.Haste:
                monster.Fast = 50;
                if (seen) Publish(new MessageEvent($"{name} starts moving faster."));
                break;
            case MonsterSpellKind.Forget:
                if (Rng.RandInt0(100) < Player.SkillSave) Publish(new MessageEvent("You resist the effects!"));
                else
                {
                    foreach (var p in Level.AllLocs().Where(p => !Level[p].Has(World.SquareFlags.View))) Known.Forget(p);
                    Publish(new MessageEvent("Your memories fade away."));
                }
                break;
            case MonsterSpellKind.Traps:
                CreateTrapsAround(Player.Position);
                break;
            case MonsterSpellKind.TeleportAway:
                TeleportPlayer(100);
                break;
            case MonsterSpellKind.TeleportLevel:
                TeleportPlayerLevel();
                break;
            case MonsterSpellKind.TeleportSelfTo:
            {
                var spot = Level.Neighbors(Player.Position)
                    .Where(p => Level.IsPassable(p) && Level[p].Monster == 0)
                    .OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
                if (spot.Count > 0)
                {
                    Level.Monsters.Move(monster, Rng.Pick(spot));
                    monster.IsVisible = MonsterVisible(monster);
                }
                break;
            }
            case MonsterSpellKind.HealKin:
                if (WoundedKin(monster) is { } kin)
                {
                    kin.Hp = Math.Min(kin.MaxHp, kin.Hp + race.Depth * spell.HealPerLevel);
                    if (kin.IsVisible) Publish(new MessageEvent($"{Capitalize(MonsterName(kin))} looks healthier."));
                }
                break;
            case MonsterSpellKind.Shapechange:
                MonsterShapechange(monster);
                break;
            case MonsterSpellKind.Darkness:
                Darken(Player.Position, 3);
                if (Player.Resists.GetValueOrDefault("dark") <= 0 && Player.Resists.GetValueOrDefault("blind") <= 0
                    && Data.Timed(TimedIds.Blind) is not null)
                    IncreaseTimed(TimedIds.Blind, 3 + Rng.RandInt1(5));
                break;
        }
        _attacker = null;
        _actingMonster = null;
        return true;
    }

    /// <summary>Whether a bolt from the monster would reach the player without hitting another monster.</summary>
    private bool ClearBolt(Monster monster)
    {
        var path = ProjectionPath.Compute(Level, monster.Position, Player.Position, MaxRange, PathFlags.StopAtCreature,
            p => p == Player.Position || Level.Monsters.At(p) is not null);
        return path.Count > 0 && path[^1] == Player.Position;
    }

    private int SpellDamage(MonsterSpellDef spell, MonsterRaceDef race) => spell.DamageFormula is { } formula
        ? DiceFormula.Roll(formula, spell.FormulaTerms, race.Power, Rng) // Angband 4.2: from the spell power
        : spell.Damage.Roll(Rng) + (spell.LevelDivisor > 0 ? race.Depth / spell.LevelDivisor : 0) + race.Depth * spell.LevelPercent / 100;

    /// <summary>
    /// Damage of an element (or plain damage when <paramref name="elementId"/> is null), reduced by the
    /// player's resistance. Taking elemental damage teaches the resistance runes of worn gear.
    /// </summary>
    public void ElementalHit(string? elementId, int damage, string killer, int power = 0, Loc? source = null)
    {
        // Angband project_p: blind, or struck by something unseen, you're told only what hit you.
        if (elementId is not null && Data.Element(elementId) is { BlindDescription.Length: > 0 } felt
            && (Player.IsBlind || _actingMonster is { IsVisible: false }))
            Publish(new MessageEvent($"You are hit by {felt.BlindDescription}!"));
        if (elementId is not null && Data.Element(elementId) is { } element)
        {
            // Angband adjust_dam: ice is resisted as cold is; the evil are vulnerable to holy orbs.
            var resistedAs = element.ResistedAs ?? elementId;
            damage = CombatMath.ResistElement(Rng, element, damage, Player.Resists.GetValueOrDefault(resistedAs));
            if (Player.Inventory.Equipped.Any(i => i.Resists.Contains(resistedAs))) LearnRune(RuneIds.Resist(resistedAs));
            LearnAboutPlayer(_actingMonster, resistedAs); // the caster sees how well it worked
        }
        TakeHit(damage, killer);
        // Then what the element does besides (Angband project_player's handlers).
        if (elementId is not null && !Player.IsDead) ElementSideEffects(elementId, damage, power, source);
    }

    /// <summary>
    /// Angband project_player_handler_CHAOS: unless resisted, chaos makes you hallucinate and
    /// confuses you, and drains experience (hold life protects).
    /// </summary>
    private void ChaosSideEffects()
    {
        if (Player.Resists.GetValueOrDefault("chaos") > 0)
        {
            Publish(new MessageEvent("You resist the effect!"));
            return;
        }
        IncreaseTimed(TimedIds.Image, Rng.RandInt1(10));
        if (Player.Resists.GetValueOrDefault("conf") <= 0)
            IncreaseTimed(TimedIds.Confused, 10 + Rng.RandInt0(20));
        if (Player.HasGearFlag(ItemFlags.HoldLife) || Player.Resists.GetValueOrDefault("hold_life") > 0) return;
        var drain = Player.Experience * 3 / (100 * 2) * LifeDrainPercent;
        Publish(new MessageEvent("You feel your life force draining away!"));
        LoseExperience(drain);
    }

    /// <summary>Angband mon-play:life-drain (percent).</summary>
    private const int LifeDrainPercent = 2;

    private void ApplySpellStatus(MonsterSpellDef spell, string? saveMessage = null)
    {
        if (spell.Timed is not { } timed) return;
        LearnAboutPlayer(_actingMonster, spell.PreventedBy);
        if (spell.PreventedBy is { } protection && Player.Resists.GetValueOrDefault(protection) > 0)
        {
            Publish(new MessageEvent("You are unaffected!"));
            if (Player.Inventory.Equipped.Any(i => i.Resists.Contains(protection))) LearnRune(RuneIds.Resist(protection));
            return;
        }
        if (spell.Save && Rng.RandInt0(100) < Player.SkillSave)
        {
            Publish(new MessageEvent(saveMessage ?? "You resist the effects!"));
            return;
        }
        IncreaseTimed(timed, Math.Max(1, spell.Duration.Roll(Rng)));
    }

    /// <summary>Moves a monster to a random open square within <paramref name="range"/>.</summary>
    private void TeleportMonster(Monster monster, int range)
    {
        var from = monster.Position;
        // One of the squares in range, picked uniformly: count them, roll, then walk to that one (in
        // the level's own order) — the same choice as picking from a list of them, without the list.
        bool Fits(Loc p) => p.DistanceTo(from) <= range && p.DistanceTo(from) >= range / 3 && Level.IsPassable(p)
                            && Level[p].Monster == 0 && p != Player.Position;
        IEnumerable<Loc> InRange()
        {
            for (var y = Math.Max(0, from.Y - range); y <= Math.Min(Level.Height - 1, from.Y + range); y++)
            for (var x = Math.Max(0, from.X - range); x <= Math.Min(Level.Width - 1, from.X + range); x++)
                if (Fits(new Loc(x, y))) yield return new Loc(x, y);
        }
        var count = InRange().Count();
        if (count == 0) return;
        Level.Monsters.Move(monster, InRange().ElementAt(Rng.RandInt0(count)));
        monster.IsVisible = MonsterVisible(monster);
    }

    /// <summary>Pulls the player to a square next to <paramref name="caster"/> (Angband TELE_TO).</summary>
    private void TeleportPlayerTo(Loc caster)
    {
        if (TeleportForbidden()) return;
        var spots = Level.Neighbors(caster)
            .Where(p => Level.IsPassable(p) && Level[p].Monster == 0)
            .OrderBy(p => p.Y).ThenBy(p => p.X)
            .ToList();
        if (spots.Count == 0) return;
        var from = Player.Position;
        Player.Position = Rng.Pick(spots);
        Publish(new PlayerMovedEvent(from, Player.Position));
        UpdateView();
    }

    /// <summary>The most wounded visible-to-it monster of the caster's kind nearby, if any.</summary>
    private Monster? WoundedKin(Monster caster) => Level.Monsters.All
        .Where(m => m != caster && m.Race.Glyph == caster.Race.Glyph && m.Hp < m.MaxHp
                    && m.Position.DistanceTo(caster.Position) <= 10)
        .OrderBy(m => m.Hp * 100 / Math.Max(1, m.MaxHp)).ThenBy(m => m.Id)
        .FirstOrDefault();

    /// <summary>Places traps on the empty floor around a square (Angband TRAPS / effect_handler_TOUCH).</summary>
    private void CreateTrapsAround(Loc center)
    {
        var made = 0;
        foreach (var p in Level.Neighbors(center))
        {
            if (!Level.Has(p, TerrainFlags.Trap) || Level[p].Trap != 0 || Level.Objects.Any(p)) continue;
            if (Data.PickTrap(Rng, Level.Depth, TrapDoorsAllowedHere) is not { } trap) break;
            Level[p].Trap = trap.Index;
            Level[p].TrapPower = trap.RollPower(Rng, Level.Depth);
            made++;
        }
        if (made > 0) Publish(new MessageEvent("You hear a clicking sound nearby."));
    }

    /// <summary>Sends the player to the next level up or down (Angband TELE_LEVEL).</summary>
    public void TeleportPlayerLevel()
    {
        if (TeleportForbidden()) return;
        if (ArenaForbids()) return;
        // Angband TELEPORT_LEVEL: up or down at random; never up in the town or with forced descent,
        // never down from a quest level or the bottom of the dungeon.
        var canUp = Player.Depth > 0 && !ForceDescend;
        var canDown = QuestAt(Player.Depth) is null && Player.Depth < Data.Constants.MaxDepth;
        if (!canUp && !canDown)
        {
            Publish(new MessageEvent("Nothing happens."));
            return;
        }
        var up = canUp && (!canDown || Rng.OneIn(2));
        Publish(new MessageEvent(up ? "You rise up through the ceiling." : "You sink through the floor."));
        ChangeLevel(up ? Player.Depth - 1 : DescentTarget(Player.Depth), StairArrival.None);
    }

    /// <summary>Unlights and forgets the area around a square (Angband DARKNESS / unlight).</summary>
    private void Darken(Loc center, int radius)
    {
        foreach (var p in Level.AllLocs().Where(p => p.DistanceTo(center) <= radius))
        {
            Level[p].Flags &= ~World.SquareFlags.Glow;
            if (Level.FeatureAt(p).Has(TerrainFlags.Floor)) Known.Forget(p);
        }
        Publish(new MessageEvent("Darkness surrounds you."));
        UpdateView();
    }

    private static Func<MonsterRaceDef, bool> Both(Func<MonsterRaceDef, bool>? a, Func<MonsterRaceDef, bool> b) =>
        a is null ? b : r => a(r) && b(r);

    /// <summary>A spell's message with the caster's name, "you" as its target and its pronoun (his / her / its).</summary>
    private static string SpellText(string message, string name, MonsterRaceDef race) =>
        message.Replace("{name}", name).Replace("{target}", "you")
            .Replace("{pronoun}", race.Has("MALE") ? "his" : race.Has("FEMALE") ? "her" : "its");

    /// <summary>
    /// Angband effect_handler_WEB: webs on every open floor square around the weaver without a trap
    /// already — within 1 square, 2 for a spell power over 40, 3 over 80.
    /// </summary>
    private void SpinWebs(Monster weaver)
    {
        if (Data.Traps.FirstOrDefault(t => t.Web) is not { } web) return;
        var radius = 1 + (weaver.Race.Power > 40 ? 1 : 0) + (weaver.Race.Power > 80 ? 1 : 0);
        foreach (var p in Level.AllLocs().Where(p => p.DistanceTo(weaver.Position) <= radius))
        {
            ref var sq = ref Level[p];
            if (sq.Trap != 0 || !Level.FeatureAt(p).Has(TerrainFlags.Floor)) continue;
            sq.Trap = web.Index;
            sq.Flags |= World.SquareFlags.TrapVisible;
        }
        UpdateView();
    }

    /// <summary>Whether a spider web covers the square.</summary>
    public bool IsWebbed(Loc p) => Level[p].Trap != 0 && Data.TrapByIndex(Level[p].Trap) is { Web: true };

    /// <summary>Clears a web (Angband square_destroy_trap).</summary>
    private void ClearWeb(Loc p)
    {
        Level[p].Trap = 0;
        Level[p].Flags &= ~World.SquareFlags.TrapVisible;
    }
}
