using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Time;
using Angband.Core.World;

namespace Angband.Core.Game;

// Angband's remaining special effects: banishment, probing, door destruction, glyphs of warding,
// monster confusion, curse weapon/armour, random outcomes, stat scrambling, and shapechanges for
// both the player (shape.txt) and monsters (the SHAPECHANGE spell).
public sealed partial class GameSession
{
    /// <summary>Angband z_info->glyph_hardness: a monster breaks a glyph if d550 is below its level.</summary>
    public const int GlyphHardness = 550;

    // The monster letter chosen for banishment (set while a banishing item's effects run).
    private char? _effectGlyph;

    public static bool NeedsGlyph(string? effect) =>
        effect is { } e && ItemEffects.Parse(e).Any(x => x.Name == "banish");

    /// <summary>Runs a command with a banishment letter chosen; one that needs a letter and has none is refused.</summary>
    private int WithGlyph(char? glyph, Func<int> run)
    {
        _effectGlyph = glyph;
        try
        {
            return run();
        }
        finally
        {
            _effectGlyph = null;
        }
    }

    // --- Effects -------------------------------------------------------------------------------

    /// <summary>Special effects (called from the device/item effect handler). Null if not one of these.</summary>
    private bool? ApplySpecialEffect(ItemEffect e)
    {
        switch (e.Name)
        {
            case "damage":
                TakeHit(Amount(e.Arg(0)), "a strange effect");
                return true;
            case "lose_hp_fraction":
                // Taking vampire form costs a quarter of your hit points (Angband shape.txt).
                TakeHit(Player.Hp / Math.Max(1, e.Int(0)), "taking vampire form");
                return true;
            case "banish":
                return Banish(_effectGlyph);
            case "probe":
                return Probe();
            case "destroy_doors":
                return DestroyDoors();
            case "glyph":
                return PlaceGlyph();
            case "curse_weapon":
                return CurseEquipment(weapon: true);
            case "curse_armour":
                return CurseEquipment(weapon: false);
            case "shapechange":
                return Shapechange(e.Arg(0));
            default:
                return null;
        }
    }

    /// <summary>
    /// Angband BANISH: every non-unique monster shown with the chosen letter leaves the level; the
    /// effort costs 1d4 hit points each.
    /// </summary>
    private bool Banish(char? glyph)
    {
        if (glyph is not { } g)
        {
            Publish(new MessageEvent("You must choose a monster symbol to banish."));
            return false;
        }
        var victims = Level.Monsters.All.Where(m => m.Race.Glyph == g && !m.Race.IsUnique && !m.Race.Has(MonsterFlags.Questor)).ToList();
        foreach (var m in victims)
        {
            Level.Monsters.Remove(m);
            TakeHit(Rng.RandInt1(4), "the strain of casting Banishment");
            if (Player.IsDead) break;
        }
        Publish(new MessageEvent(victims.Count > 0 ? $"You banish {victims.Count} creature{(victims.Count == 1 ? "" : "s")}." : "Nothing happens."));
        UpdateView();
        return true;
    }

    /// <summary>Angband PROBE: learn everything about the monsters in view, and how hurt they are.</summary>
    private bool Probe()
    {
        var seen = Level.Monsters.All.Where(m => m.IsVisible && Level[m.Position].Has(SquareFlags.View)).ToList();
        if (seen.Count == 0)
        {
            Publish(new MessageEvent("You sense no monsters to probe."));
            return false;
        }
        Publish(new MessageEvent("Probing..."));
        foreach (var m in seen)
        {
            Publish(new MessageEvent($"{Capitalize(MonsterName(m))} has {Math.Max(0, m.Hp)} hit points."));
            Lore.For(m.Race.Id).Probed = true;
        }
        Publish(new MessageEvent("That's all."));
        return true;
    }

    /// <summary>Angband door destruction: the doors around the player crumble.</summary>
    private bool DestroyDoors()
    {
        var any = false;
        foreach (var p in Level.Neighbors(Player.Position).Append(Player.Position))
        {
            if (!Level.FeatureAt(p).Has(TerrainFlags.DoorAny)) continue;
            Level[p].Feature = Data.Terrain.Ids.Floor;
            Level[p].LockPower = 0;
            any = true;
        }
        if (any)
        {
            Publish(new MessageEvent("There is a high-pitched humming noise."));
            UpdateView();
        }
        return any;
    }

    /// <summary>Angband GLYPH: a glyph of warding under the player, which monsters must break to pass.</summary>
    private bool PlaceGlyph()
    {
        var p = Player.Position;
        var glyph = Data.Traps.FirstOrDefault(t => t.Warding);
        if (glyph is null || !Level.Has(p, TerrainFlags.Trap) || Level.Objects.Any(p) || Level[p].Trap != 0)
        {
            Publish(new MessageEvent("There is no clear floor here."));
            return false;
        }
        Level[p].Trap = glyph.Index;
        Level[p].Flags |= SquareFlags.TrapVisible;
        Publish(new MessageEvent("You inscribe a mystic symbol on the floor."));
        return true;
    }

    /// <summary>Whether a monster may step onto (or attack into) a square: a glyph there must be broken first.</summary>
    private bool PassesWarding(Monster monster, Loc p)
    {
        if (Level[p].Trap == 0 || Data.TrapByIndex(Level[p].Trap) is not { Warding: true }) return true;
        if (Rng.RandInt1(GlyphHardness) >= monster.Race.Depth) return false;
        Level[p].Trap = 0;
        if (Level[p].Has(SquareFlags.Seen)) Publish(new MessageEvent("The rune of protection is broken!"));
        return true;
    }

    /// <summary>Angband CURSE_WEAPON / CURSE_ARMOR: a random curse on the weapon or a worn armour piece.</summary>
    private bool CurseEquipment(bool weapon)
    {
        var item = weapon
            ? Player.Inventory.Weapon
            : Player.Inventory.Equipped.Where(i => i.Base.Slot is not (EquipSlot.Weapon or EquipSlot.Bow or EquipSlot.Ring
                or EquipSlot.Amulet or EquipSlot.Light)).OrderBy(i => i.Serial).FirstOrDefault();
        if (item is null) return false;
        if (item.IsArtifact && Rng.OneIn(2))
        {
            Publish(new MessageEvent($"Your {Describe(item, withArticle: false)} resists the curse!"));
            return true;
        }
        var count = item.Curses.Count;
        Objects.AddRandomCurses(Rng, item, 1, Math.Max(1, Level.Depth));
        Publish(new MessageEvent(item.Curses.Count > count
            ? $"A terrible black aura blasts your {Describe(item, withArticle: false)}!"
            : "You feel a malevolent presence, but it passes."));
        RecalculateBonuses();
        return true;
    }

    // --- Timed effects with side effects -----------------------------------------------------------

    /// <summary>When a timed effect starts: stat scrambling shuffles the stats.</summary>
    private void OnTimedStarted(string id)
    {
        if (id != "scrambled") return;
        var stats = CharacterSpec.StatIds.ToList();
        var shuffled = stats.OrderBy(_ => Rng.RandInt0(1000)).ToList();
        Player.StatScramble.Clear();
        for (var i = 0; i < stats.Count; i++) Player.StatScramble[stats[i]] = shuffled[i];
        RecalculateAfterStatChange();
    }

    /// <summary>When a timed effect ends: sprinting leaves you slow, scrambled stats return.</summary>
    private void OnTimedEnded(string id)
    {
        Player.OilGrades.Remove(id); // (an oil's grade goes with its brand)
        switch (id)
        {
            case "command":
                Commanded = null;
                break;
            case "sprint":
                if (Data.Timed(TimedIds.Slow) is { } slow && Player.Timed.Increase(slow, 100) is { } message)
                    Publish(new MessageEvent(message));
                break;
            case "scrambled":
                Player.StatScramble.Clear();
                RecalculateAfterStatChange();
                break;
        }
    }

    // --- Player shapechanges ----------------------------------------------------------------------

    public ShapeDef? PlayerShape => Player.Shape is { } id ? Data.Shape(id) : null;

    /// <summary>Takes a shape (Angband player_shape_change): its change effect applies, then its bonuses.</summary>
    public bool Shapechange(string shapeId)
    {
        if (Data.Shape(shapeId) is not { } shape) return false;
        Player.Shape = shape.Id;
        Publish(new MessageEvent($"You assume the shape of {Article(shape.Name)}!"));
        Publish(new PlayerShapeChangedEvent(shape.Id));
        // Its change effect, worked out at the player's level (the werewolf scares at level strength).
        if (shape.Effect.Length > 0)
            ApplyEffects(string.Join("; ", SpellEffects.Parse(shape.Effect, Player.Level).Select(e => e.ToEffectString())));
        ApplySkills();
        RecalculateBonuses();
        UpdateView();
        return true;
    }

    /// <summary>Back to the usual shape (Angband player_resume_normal_shape).</summary>
    private int ResumeNormalShape()
    {
        if (Player.Shape is null) return 0;
        Player.Shape = null;
        Publish(new MessageEvent("You resume your usual shape."));
        Publish(new PlayerShapeChangedEvent(null));
        ApplySkills();
        RecalculateBonuses();
        return EnergyTable.MoveEnergy;
    }

    /// <summary>Things a changed shape can't do (Angband: no items or spells without hands).</summary>
    private bool RefusedByShape(GameCommand command)
    {
        if (PlayerShape is not { } shape) return false;
        if (command is not (UseCommand or ActivateCommand or WieldCommand or TakeOffCommand or ThrowCommand
            or FireCommand or CastCommand or StudyCommand or RefuelCommand or BuyCommand or SellCommand)) return false;
        Publish(new MessageEvent($"You cannot do this while in {shape.Name} form."));
        return true;
    }

    /// <summary>The attack verb for a blow in the current shape ("bite", "claw"...), or null for the usual ones.</summary>
    private string? ShapeBlowVerb() => PlayerShape is { Blows.Count: > 0 } shape ? Rng.Pick(shape.Blows) : null;

    // --- Monster shapechanges ---------------------------------------------------------------------

    /// <summary>Angband SHAPECHANGE: the monster becomes one of its shapes until it changes back.</summary>
    internal void MonsterShapechange(Monster monster)
    {
        var original = monster.OriginalRace ?? monster.Race;
        var shapes = original.Shapes.Select(Data.Monster).OfType<MonsterRaceDef>().Where(r => r != monster.Race).ToList();
        if (shapes.Count == 0) return;
        var shape = Rng.Pick(shapes);
        var seen = monster.IsVisible;
        var before = MonsterName(monster);
        monster.OriginalRace = original;
        monster.Race = shape;
        monster.IsVisible = MonsterVisible(monster);
        if (seen) Publish(new MessageEvent($"{Capitalize(before)} changes into {Article(shape.Name)}!"));
        UpdateView();
    }

    /// <summary>A shapechanged monster sometimes returns to its own form.</summary>
    private void MaybeRevertMonsterShape(Monster monster)
    {
        if (monster.OriginalRace is not { } original || !Rng.OneIn(10)) return;
        monster.Race = original;
        monster.OriginalRace = null;
        monster.IsVisible = MonsterVisible(monster);
        if (monster.IsVisible) Publish(new MessageEvent($"{Capitalize(MonsterName(monster))} returns to its own shape."));
    }
}
