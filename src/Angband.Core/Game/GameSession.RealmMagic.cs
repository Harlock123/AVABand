using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Time;
using Angband.Core.World;

namespace Angband.Core.Game;

// The effects of Angband 4.2's mage, priest, druid and ranger books (effect-handler-attack.c and
// effect-handler-general.c): bolts that may be beams, short beams, arcs, strikes, meteor swarms,
// spheres, detection of traps and life, doors made and unmade, tapping devices for mana,
// teleporting to a spot, holding, tremors, nourishment, arrows from staves, branded ammunition
// and decoys.
public sealed partial class GameSession
{
    /// <summary>The effects of the four original realms' books. False if not one.</summary>
    private bool ApplyRealmSpellEffect(ItemEffect e)
    {
        switch (e.Name)
        {
            case "bolt_or_beam":
                BoltOrBeam(NullIfNone(e.Arg(0)), e.Dice(1).Roll(Rng), e.Int(2));
                return true;
            case "short_beam":
                ShortBeam(NullIfNone(e.Arg(0)), e.Dice(1).Roll(Rng), e.Int(2), e.Int(3));
                return true;
            case "arc":
                Arc(NullIfNone(e.Arg(0)), e.Dice(1).Roll(Rng), e.Int(2), Math.Max(20, e.Int(3)));
                return true;
            case "strike":
                Strike(NullIfNone(e.Arg(0)), e.Dice(1).Roll(Rng), e.Int(2));
                return true;
            case "swarm":
                for (var i = Math.Max(1, e.Int(3)); i > 0; i--) ProjectBall(_effectSource, NullIfNone(e.Arg(0)), e.Dice(1).Roll(Rng), Math.Max(0, e.Int(2)), _effectTarget);
                return true;
            case "sphere":
                Sphere(NullIfNone(e.Arg(0)), e.Dice(1).Roll(Rng), e.Int(2));
                return true;
            case "detect_traps":
                DetectTraps(e.Int(0));
                return true;
            case "detect_living":
                DetectMonstersWhere(e.Int(0), m => IsLiving(m.Race), "life");
                return true;
            case "destroy_traps":
                DestroyAdjacentTraps();
                return true;
            case "make_doors":
                MakeDoors();
                return true;
            case "tap_device":
                TapDevice();
                return true;
            case "teleport_to":
                TeleportTo(_effectTarget ?? TargetPosition() ?? AimPoint());
                return true;
            case "hold_adjacent":
                foreach (var m in Level.Neighbors(Player.Position).Select(Level.Monsters.At).OfType<Monster>().OrderBy(m => m.Id).ToList())
                    AffectMonster(m, "hold", e.Int(0));
                return true;
            case "earthquake_at":
                Earthquake(Math.Max(1, e.Int(0)), _effectTarget ?? AimPoint() ?? Player.Position);
                return true;
            case "nourish_to":
                // Angband NOURISH:INC_TO: fed up to this share of full, never beyond.
                var want = Data.Constants.FoodFull * e.Int(0) / 100;
                if (Player.Food < want) SetFood(want);
                Publish(new MessageEvent("You feel less hungry."));
                return true;
            case "create_arrows":
                CreateArrows();
                return true;
            case "brand_ammo":
                BrandAmmo();
                return true;
            case "decoy":
                PlaceDecoy();
                return true;
            default:
                return false;
        }
    }

    // --- Shapes of attack -----------------------------------------------------------------------------

    /// <summary>
    /// Angband BOLT_OR_BEAM: a beam one time in (level, or half that for non-mages, plus the spell's
    /// adjustment) out of 100, otherwise a bolt.
    /// </summary>
    private void BoltOrBeam(string? element, int damage, int adjust)
    {
        var chance = (ClassHas("BEAM") ? Player.Level : Player.Level / 2) + adjust;
        if (Rng.RandInt0(100) < chance) ProjectBeam(_effectSource, element, damage, _effectTarget);
        else ProjectBolt(_effectSource, element, damage, _effectTarget);
    }

    /// <summary>
    /// Angband SHORT_BEAM: a beam of a few squares — the given length plus a square per so many levels.
    /// </summary>
    private void ShortBeam(string? element, int damage, int length, int levelsPerSquare)
    {
        var range = Math.Max(1, length + (levelsPerSquare > 0 ? Player.Level / levelsPerSquare : 0));
        if (SpellTarget(_effectTarget) is not { } aim) return;
        var path = ProjectionPath.Compute(Level, Player.Position, aim, range, PathFlags.Through);
        Publish(new MissileFiredEvent(_effectSource, Player.Position, path));
        foreach (var p in path)
        {
            if (!Level.Has(p, TerrainFlags.Project)) break;
            if (Level.Monsters.At(p) is { } m) ProjectileHitsMonster(m, _effectSource, element, damage);
        }
    }

    /// <summary>
    /// Angband ARC: a cone of the given angle toward the target, out to the radius (0: any range).
    /// Full strength close by, falling away further out.
    /// </summary>
    private void Arc(string? element, int damage, int radius, int degrees)
    {
        if (SpellTarget(_effectTarget) is not { } aim) return;
        if (radius <= 0) radius = MaxRange;
        var source = Math.Min(25, degrees < 60 ? 4 * 60 / degrees : 4); // Angband's "diameter of source"
        var dir = Math.Atan2(aim.Y - Player.Position.Y, aim.X - Player.Position.X);
        Publish(new MessageEvent($"You unleash {Article(_effectSource.ToLowerInvariant())}."));
        foreach (var m in Level.Monsters.All.ToList())
        {
            var d = m.Position.DistanceTo(Player.Position);
            if (d == 0 || d > radius || !ProjectionPath.Projectable(Level, Player.Position, m.Position, radius + 1)) continue;
            var angle = Math.Atan2(m.Position.Y - Player.Position.Y, m.Position.X - Player.Position.X);
            var off = Math.Abs(Math.IEEERemainder(angle - dir, 2 * Math.PI)) * 180 / Math.PI;
            if (off > degrees / 2.0) continue;
            ProjectileHitsMonster(m, _effectSource, element, d <= source / 2 ? damage : damage * source / (2 * d));
        }
        DestroyFloorObjects(BreathArc(Player.Position, aim, degrees, radius), element);
    }

    /// <summary>Angband STRIKE: an explosion right at the target (which must be in line of sight).</summary>
    private void Strike(string? element, int damage, int radius)
    {
        var target = _effectTarget ?? AimPoint();
        if (target is not { } at || !ProjectionPath.Projectable(Level, Player.Position, at, MaxRange))
        {
            Publish(new MessageEvent("You have no target."));
            return;
        }
        foreach (var m in Level.Monsters.All
                     .Where(m => m.Position.DistanceTo(at) <= radius && ProjectionPath.Projectable(Level, at, m.Position, radius + 1))
                     .OrderBy(m => m.Position.DistanceTo(at)).ThenBy(m => m.Id).ToList())
            ProjectileHitsMonster(m, _effectSource, element, damage / (m.Position.DistanceTo(at) + 1));
    }

    /// <summary>Angband SPHERE: an explosion centred on the player that spares them.</summary>
    private void Sphere(string? element, int damage, int radius)
    {
        Publish(new MessageEvent($"{Capitalize(_effectSource)} erupts around you!"));
        foreach (var m in Level.Monsters.All
                     .Where(m => m.Position.DistanceTo(Player.Position) <= radius && ProjectionPath.Projectable(Level, Player.Position, m.Position, radius + 1))
                     .OrderBy(m => m.Position.DistanceTo(Player.Position)).ThenBy(m => m.Id).ToList())
            ProjectileHitsMonster(m, _effectSource, element, damage / (m.Position.DistanceTo(Player.Position) + 1));
        DestroyFloorObjects(BallArea(Player.Position, radius), element);
    }

    // --- Knowledge and the lay of the land ------------------------------------------------------------

    private void DetectTraps(int radius)
    {
        var found = 0;
        foreach (var p in Level.AllLocs().Where(p => p.ChebyshevTo(Player.Position) <= radius && Level[p].Trap != 0))
        {
            Level[p].Flags |= SquareFlags.TrapVisible;
            found++;
        }
        Publish(new MessageEvent(found > 0 ? "You sense the presence of traps!" : "You sense no traps."));
    }

    /// <summary>Angband TOUCH:KILL_TRAP: the traps next to you (and underfoot) vanish.</summary>
    private void DestroyAdjacentTraps()
    {
        var any = false;
        foreach (var p in Level.Neighbors(Player.Position).Append(Player.Position))
        {
            if (Level[p].Trap == 0 || Data.TrapByIndex(Level[p].Trap) is { Warding: true }) continue;
            Level[p].Trap = 0;
            Level[p].Flags &= ~SquareFlags.TrapVisible;
            any = true;
        }
        if (any) Publish(new MessageEvent("There is a bright flash of light!"));
    }

    /// <summary>Angband TOUCH:MAKE_DOOR: closed doors in the empty floor around you.</summary>
    private void MakeDoors()
    {
        var made = 0;
        foreach (var p in Level.Neighbors(Player.Position))
        {
            if (!Level.IsEmptyFloor(p) || Level.Objects.Any(p) || Level[p].Trap != 0) continue;
            Level[p].Feature = Data.Terrain.Ids.ClosedDoor;
            made++;
        }
        Publish(new MessageEvent(made > 0 ? "Doors appear around you." : "There is no room for a door."));
        UpdateView();
    }

    /// <summary>
    /// Angband TELEPORT_TO (Dimension Door): to the chosen spot, or the nearest free square to it.
    /// </summary>
    private void TeleportTo(Loc? target)
    {
        if (target is not { } aim)
        {
            Publish(new MessageEvent("You need a target to teleport to."));
            return;
        }
        var spot = Level.AllLocs()
            .Where(p => Level.IsPassable(p) && Level[p].Monster == 0 && !Level[p].Has(SquareFlags.Vault))
            .OrderBy(p => p.DistanceTo(aim)).ThenBy(p => p.Y).ThenBy(p => p.X).FirstOrDefault(Player.Position);
        var from = Player.Position;
        Player.Position = spot;
        Publish(new PlayerMovedEvent(from, spot));
        Publish(new PlayerTeleportedEvent(from, spot));
        UpdateView();
    }

    // --- Devices, arrows and ammunition -----------------------------------------------------------------

    /// <summary>
    /// Angband TAP_DEVICE: a wand's or staff's charges become mana — (5 + its level) × 3/2 energy a
    /// charge, a sixth of it mana — and the jolt stuns a little. (AVABand picks the fullest device.)
    /// </summary>
    private void TapDevice()
    {
        var device = Player.Inventory.Pack.Where(i => i.Base.Id is "wand" or "staff" && i.Charges > 0)
            .OrderByDescending(i => (5 + i.Kind.Level) * i.Charges).ThenBy(i => i.Serial).FirstOrDefault();
        if (device is null)
        {
            Publish(new MessageEvent("You have nothing to drain charges from."));
            return;
        }
        var energy = (5 + device.Kind.Level) * 3 * device.Charges / 2;
        if (energy < 36)
        {
            Publish(new MessageEvent($"That {device.Base.Name.Replace("~", "").ToLowerInvariant()} had no useable energy."));
            return;
        }
        if (Player.Mana >= Player.MaxMana)
        {
            Publish(new MessageEvent("Your mana is already full."));
            return;
        }
        device.Charges = 0;
        device.Flags.Add("EMPTY");
        Player.Mana = Math.Min(Player.MaxMana, Player.Mana + energy / 6);
        Player.ManaFraction = 0;
        Publish(new MessageEvent("You feel your head clear."));
        IncreaseTimed(TimedIds.Stun, Rng.RandInt1(2));
    }

    /// <summary>
    /// Angband CREATE_ARROWS: a staff becomes a stack of arrows of your level — good if the staff was
    /// deep enough (level > 25 on a d(level) roll), great if deeper still. (AVABand uses the shallowest staff.)
    /// </summary>
    private void CreateArrows()
    {
        var staff = Player.Inventory.Pack.Where(i => i.Base.Id == "staff").OrderBy(i => i.Kind.Level).ThenBy(i => i.Serial).FirstOrDefault()
                    ?? Level.Objects.At(Player.Position).FirstOrDefault(i => i.Base.Id == "staff");
        if (staff is null)
        {
            Publish(new MessageEvent("You have no staff to use."));
            return;
        }
        var level = staff.Kind.Level;
        var good = Rng.RandInt1(Math.Max(1, level)) > 25;
        var great = good && Rng.RandInt1(Math.Max(1, level)) > 50;
        if (Player.Inventory.Contains(staff)) Player.Inventory.Remove(staff, 1, () => Objects.NextSerial++);
        else if (staff.Number > 1) staff.Number--;
        else Level.Objects.Remove(Player.Position, staff);

        Item? arrows = null;
        for (var tries = 0; tries < 100 && arrows is null; tries++)
            if (Objects.Make(Rng, Player.Level, good, great) is { Base.Id: "arrow" } made) arrows = made;
        arrows ??= Objects.Create("arrow", Rng.RandRange(10, 20));
        Publish(new MessageEvent($"You make {Describe(arrows)}."));
        DropNear(arrows, Player.Position);
        RecalculateBonuses();
    }

    /// <summary>
    /// Angband BRAND_AMMO: a stack of ammunition takes a brand — flame (1 in 3), else frost or venom
    /// evenly — and some enchantment. (AVABand brands the largest unbranded stack in the quiver.)
    /// </summary>
    private void BrandAmmo()
    {
        var ammo = Player.Inventory.Quiver.Where(i => i.IsAmmo && i.Brands.Count == 0 && i.Slays.Count == 0 && i.Ego is null && !i.IsCursed)
            .OrderByDescending(i => i.Number).ThenBy(i => i.Serial).FirstOrDefault();
        if (ammo is null)
        {
            Publish(new MessageEvent("You have nothing to brand."));
            return;
        }
        var element = Rng.OneIn(3) ? "fire" : Rng.OneIn(2) ? "cold" : "pois";
        var ego = Data.Egos.FirstOrDefault(e => e.Bases.Contains(ammo.Base.Id) && e.Brands.Any(b => b.Element == element));
        if (ego is not null)
        {
            ammo.Ego = ego;
            ammo.Brands.AddRange(ego.Brands);
        }
        else ammo.Brands.Add(new BrandDef { Element = element, Multiplier = 3, Name = Data.Element(element)?.Name ?? element });
        ammo.ToHit += Rng.RandInt1(5);
        ammo.ToDam += Rng.RandInt1(5);
        foreach (var rune in ammo.Runes()) Knowledge.LearnRune(rune);
        Publish(new MessageEvent($"Your {Describe(ammo, withArticle: false)} {(ammo.Number == 1 ? "is" : "are")} covered in a glowing aura!"));
        Player.Inventory.SortQuiver();
    }

    // --- Decoys (rangers) ------------------------------------------------------------------------------

    /// <summary>Angband GLYPH:DECOY: a likeness of the player, underfoot, that fooled monsters go for.</summary>
    private void PlaceDecoy()
    {
        Level.Decoy = Player.Position;
        Publish(new MessageEvent("You leave a decoy behind you."));
    }

    /// <summary>Angband monster_is_decoyed: a monster with a line of sight to the decoy goes for it.</summary>
    private Loc? DecoyFor(Monster monster) =>
        Level.Decoy is { } decoy && ProjectionPath.Projectable(Level, monster.Position, decoy, Data.Constants.MaxSight) ? decoy : null;

    /// <summary>A monster reaching the decoy destroys it (unless it never attacks).</summary>
    private bool AttackDecoy(Monster monster, Loc target)
    {
        if (Level.Decoy != target || monster.Race.Has(MonsterFlags.NeverBlow)) return false;
        Level.Decoy = null;
        if (monster.IsVisible || Level[target].Has(SquareFlags.Seen)) Publish(new MessageEvent("The decoy is destroyed!"));
        return true;
    }
}
