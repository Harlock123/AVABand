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
    /// <summary>Picks and casts a spell; false if nothing suitable could be cast (the monster then moves).</summary>
    private bool TryCastSpell(Monster monster)
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

        Publish(new MessageEvent(seen ? spell.Message.Replace("{name}", name) : spell.UnseenMessage));
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
            case MonsterSpellKind.Ball:
                ElementalHit(spell.Element, SpellDamage(spell, race), killer);
                break;
            case MonsterSpellKind.Breath:
                ElementalHit(spell.Element, Math.Min(spell.BreathCap, Math.Max(1, monster.Hp / Math.Max(1, spell.BreathDivisor))), killer);
                break;
            case MonsterSpellKind.Wound:
                if (Rng.RandInt0(100) < Player.SkillSave) Publish(new MessageEvent("You resist the effects!"));
                else TakeHit(SpellDamage(spell, race), killer);
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
    public void ElementalHit(string? elementId, int damage, string killer)
    {
        if (elementId is not null && Data.Element(elementId) is { } element)
        {
            damage = CombatMath.ResistElement(Rng, element, damage, Player.Resists.GetValueOrDefault(elementId));
            if (Player.Inventory.Equipped.Any(i => i.Resists.Contains(elementId))) LearnRune(RuneIds.Resist(elementId));
        }
        TakeHit(damage, killer);
    }

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
            var race = _spawner.PickRace(Rng, Math.Max(1, (Level.Depth + caster.Race.Depth) / 2), unavailable, filter);
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
