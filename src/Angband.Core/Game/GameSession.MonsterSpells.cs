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
    /// <summary>
    /// Angband make_ranged_attack / monster_can_cast: first a roll against the race's spell
    /// frequency, then against its innate frequency — each a 100/N percent chance, halved while the
    /// player taunts and doubled at the monster's preferred range. Returns whether this turn is for a spell (false) or an innate attack (true),
    /// or null for neither.
    /// </summary>
    private bool? RangedAttackKind(Monster monster)
    {
        var race = monster.Race;
        // Angband: halved while the player taunts; doubled when the monster is at its preferred range.
        var atBest = monster.Position.DistanceTo(Player.Position) == CombatRange(monster).Best;
        int Chance(int oneIn) => oneIn <= 0 ? 0 : 100 / oneIn / (Player.Timed.Has("taunt") ? 2 : 1) * (atBest ? 2 : 1);
        if (Chance(race.SpellFrequency) is > 0 and var spell && Rng.RandInt0(100) < spell) return false;
        if (Chance(race.InnateFrequency) is > 0 and var innate && Rng.RandInt0(100) < innate) return true;
        return null;
    }

    /// <summary>
    /// Picks and casts a spell of the kind chosen (innate attacks or other spells, as Angband's
    /// choose_attack_spell); false if nothing suitable could be cast (the monster then moves).
    /// </summary>
    private bool TryCastSpell(Monster monster, bool innate)
    {
        var race = monster.Race;
        var here = monster.Position;
        var inView = CanSee(monster) && ProjectionPath.Projectable(Level, here, Player.Position, MaxRange);
        var clearShot = inView && ClearBolt(monster);

        var usable = race.Spells
            .Select(Data.MonsterSpell)
            .OfType<MonsterSpellDef>()
            .Where(s => s.Kind switch
            {
                MonsterSpellKind.Bolt => clearShot,
                MonsterSpellKind.Heal => monster.Hp < monster.MaxHp,
                MonsterSpellKind.Blink or MonsterSpellKind.Teleport => true,
                MonsterSpellKind.Haste => monster.Fast == 0,
                MonsterSpellKind.DrainMana => inView && Player.Mana > 0,
                MonsterSpellKind.HealKin => WoundedKin(monster) is not null,
                MonsterSpellKind.TeleportSelfTo => !inView || here.ChebyshevTo(Player.Position) > 1,
                MonsterSpellKind.Summon => inView || here.DistanceTo(Player.Position) <= 10,
                _ => inView,
            })
            .Where(s => s.Innate == innate)
            .Where(s => monster.Confused == 0 || s.Innate)
            .ToList();

        // Frightened monsters try to get away or patch themselves up.
        if (monster.IsAfraid)
        {
            var escapes = usable.Where(s => s.Escape).ToList();
            if (escapes.Count > 0) usable = escapes;
        }
        if (usable.Count == 0) return false;

        var spell = Rng.Pick(usable);
        var seen = monster.IsVisible;
        var name = Capitalize(MonsterName(monster));

        // Angband: non-innate spells fail 25 - (level + 3) / 4 percent of the time.
        if (!spell.Innate && !race.Has(MonsterFlags.Smart) && Rng.RandInt0(100) < Math.Max(0, 25 - (race.Depth + 3) / 4))
        {
            if (seen) Publish(new MessageEvent($"{name} tries to cast a spell, but fails."));
            return true;
        }
        return CastSpell(monster, spell);
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
        if (seen)
        {
            var lore = Lore.For(race.Id);
            lore.SpellsSeen.Add(spell.Id);
            lore.CastsSeen++;
            if (spell.Innate) lore.CastsInnate++;
            else lore.CastsSpell++;
        }
        Publish(new MonsterSpellEvent(monster.Id, spell.Id, seen));
        var killer = race.IsUnique ? race.Name : Article(race.Name);

        switch (spell.Kind)
        {
            case MonsterSpellKind.Bolt:
                ElementalHit(spell.Element, SpellDamage(spell, race), killer, race.Power, monster.Position);
                break;
            case MonsterSpellKind.Ball:
                ElementalHit(spell.Element, SpellDamage(spell, race), killer, race.Power, monster.Position);
                DestroyFloorObjects(BallArea(Player.Position, 2), spell.Element); // balls and breaths reach the floor too
                break;
            case MonsterSpellKind.Breath:
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
                    ElementalHit(element, basis + Rng.Damroll(dice, 5), killer, race.Power, monster.Position);
                    DestroyFloorObjects(BallArea(Player.Position, 3), element);
                }
                break;
            }
            case MonsterSpellKind.Web:
                SpinWebs(monster);
                break;
            case MonsterSpellKind.Status:
                ApplySpellStatus(spell);
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
        return true;
    }

    /// <summary>Whether a bolt from the monster would reach the player without hitting another monster.</summary>
    private bool ClearBolt(Monster monster)
    {
        var path = ProjectionPath.Compute(Level, monster.Position, Player.Position, MaxRange, PathFlags.StopAtCreature,
            p => p == Player.Position || Level.Monsters.At(p) is not null);
        return path.Count > 0 && path[^1] == Player.Position;
    }

    private int SpellDamage(MonsterSpellDef spell, MonsterRaceDef race) =>
        spell.Damage.Roll(Rng) + (spell.LevelDivisor > 0 ? race.Depth / spell.LevelDivisor : 0) + race.Depth * spell.LevelPercent / 100;

    /// <summary>
    /// Damage of an element (or plain damage when <paramref name="elementId"/> is null), reduced by the
    /// player's resistance. Taking elemental damage teaches the resistance runes of worn gear.
    /// </summary>
    public void ElementalHit(string? elementId, int damage, string killer, int power = 0, Loc? source = null)
    {
        if (elementId is not null && Data.Element(elementId) is { } element)
        {
            damage = CombatMath.ResistElement(Rng, element, damage, Player.Resists.GetValueOrDefault(elementId));
            if (Player.Inventory.Equipped.Any(i => i.Resists.Contains(elementId))) LearnRune(RuneIds.Resist(elementId));
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

    private void ApplySpellStatus(MonsterSpellDef spell)
    {
        if (spell.Timed is not { } timed) return;
        if (spell.PreventedBy is { } protection && Player.Resists.GetValueOrDefault(protection) > 0)
        {
            Publish(new MessageEvent("You are unaffected!"));
            if (Player.Inventory.Equipped.Any(i => i.Resists.Contains(protection))) LearnRune(RuneIds.Resist(protection));
            return;
        }
        if (spell.Save && Rng.RandInt0(100) < Player.SkillSave)
        {
            Publish(new MessageEvent("You resist the effects!"));
            return;
        }
        IncreaseTimed(timed, Math.Max(1, spell.Duration.Roll(Rng)));
    }

    /// <summary>Moves a monster to a random open square within <paramref name="range"/>.</summary>
    private void TeleportMonster(Monster monster, int range)
    {
        var from = monster.Position;
        var spots = Level.AllLocs()
            .Where(p => p.DistanceTo(from) <= range && p.DistanceTo(from) >= range / 3 && Level.IsPassable(p)
                        && Level[p].Monster == 0 && p != Player.Position)
            .ToList();
        if (spots.Count == 0) return;
        Level.Monsters.Move(monster, Rng.Pick(spots));
        monster.IsVisible = MonsterVisible(monster);
    }

    /// <summary>Pulls the player to a square next to <paramref name="caster"/> (Angband TELE_TO).</summary>
    private void TeleportPlayerTo(Loc caster)
    {
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
        var eligible = Data.Traps.Where(t => t.MinDepth <= Level.Depth && t.MaxDepth >= Level.Depth).ToList();
        var made = 0;
        foreach (var p in Level.Neighbors(center))
        {
            if (!Level.Has(p, TerrainFlags.Trap) || Level[p].Trap != 0 || Level.Objects.Any(p)) continue;
            if (Rng.PickWeighted(eligible, t => t.Weight) is not { } trap) break;
            Level[p].Trap = trap.Index;
            made++;
        }
        if (made > 0) Publish(new MessageEvent("You hear a clicking sound nearby."));
    }

    /// <summary>Sends the player to the next level up or down (Angband TELE_LEVEL).</summary>
    public void TeleportPlayerLevel()
    {
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

    /// <summary>Summons monsters of the level's depth (or the caster's kin) next to the player.</summary>
    private void SummonMonsters(Monster caster, MonsterSpellDef spell)
    {
        var unavailable = new HashSet<string>(KilledUniques);
        foreach (var m in Level.Monsters.All.Where(m => m.Race.IsUnique)) unavailable.Add(m.Race.Id);

        var count = Math.Max(1, spell.Count.Roll(Rng));
        var summoned = 0;
        for (var i = 0; i < count; i++)
        {
            Func<MonsterRaceDef, bool>? filter = spell.Kin ? r => r.Glyph == caster.Race.Glyph
                : spell.SummonGlyphs is { } glyphs ? r => glyphs.Contains(r.Glyph)
                : spell.SummonFlag is { } flag ? r => r.Has(flag)
                : null;
            // Angband summon.txt: some summon no uniques, some nothing but uniques.
            if (spell.SummonNoUniques) filter = Both(filter, r => !r.IsUnique);
            if (spell.SummonUniquesOnly) filter = Both(filter, r => r.IsUnique);
            var level = Math.Max(1, (Level.Depth + caster.Race.Depth) / 2 + 5); // Angband summon_specific

            var race = _spawner.PickRace(Rng, level, unavailable, filter);
            // ...falling back on another kind when there is none (the Ringwraiths → greater undead).
            if (race is null && spell.SummonFallbackGlyphs is { } fallback)
                race = _spawner.PickRace(Rng, level, unavailable, r => fallback.Contains(r.Glyph));
            var spot = Level.Neighbors(Player.Position)
                .Where(p => Level.IsPassable(p) && Level[p].Monster == 0)
                .OrderBy(_ => Rng.RandInt0(1000))
                .Cast<Loc?>()
                .FirstOrDefault();
            if (race is null || spot is not { } s) break;

            var m = _spawner.Place(Level, Rng, race, s, asleep: false);
            if (race.IsUnique) unavailable.Add(race.Id);
            Scheduler.Add(m);
            summoned++;
        }
        DisguiseMonsters();
        UpdateView();
        if (summoned == 0) Publish(new MessageEvent("Nothing appears."));
    }
}
