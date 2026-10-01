using Angband.Core.Effects;
using Angband.Core.Geometry;
using Angband.Core.Items;

namespace Angband.Core.Game;

/// <summary>
/// What each kind of breath, ball or bolt does besides its damage (Angband 4.2 project-player.c,
/// the project_player_handler_* functions), and fire, acid, lightning and cold destroying things in
/// the pack (project-obj.c inven_damage).
/// </summary>
public sealed partial class GameSession
{
    /// <summary>
    /// The side effects of being hit by an element (after the damage). <paramref name="damage"/> is
    /// what was taken; <paramref name="power"/> the caster's spell power (its level), which unlocks
    /// the nastier extras of fire, cold, poison, darkness and nether; <paramref name="source"/> where
    /// it came from (for nexus and force).
    /// </summary>
    private void ElementSideEffects(string element, int damage, int power, Loc? source)
    {
        switch (element)
        {
            case "acid" or "elec":
                if (!ImmuneTo(element)) InventoryDamage(element, Math.Min(damage * 5, 300));
                break;
            case "fire":
                if (ImmuneTo("fire")) break;
                InventoryDamage("fire", Math.Min(damage * 5, 300));
                if (power >= 80)
                {
                    if (Rng.RandInt0(Math.Max(1, damage)) > 500)
                    {
                        Publish(new MessageEvent("The intense heat saps you."));
                        DrainStat("str");
                    }
                    if (Rng.RandInt0(Math.Max(1, damage)) > 500 && Afflict(TimedIds.Blind, Rng.RandInt1(Math.Max(1, damage / 100))))
                        Publish(new MessageEvent("Your eyes fill with smoke!"));
                    if (Rng.RandInt0(Math.Max(1, damage)) > 500 && Afflict(TimedIds.Poisoned, Rng.RandInt1(Math.Max(1, damage / 10))))
                        Publish(new MessageEvent("You are assailed by poisonous fumes!"));
                }
                break;
            case "cold":
                if (ImmuneTo("cold")) break;
                InventoryDamage("cold", Math.Min(damage * 5, 300));
                if (power >= 80)
                {
                    if (Rng.RandInt0(Math.Max(1, damage)) > 500)
                    {
                        Publish(new MessageEvent("The cold seeps into your bones."));
                        DrainStat("dex");
                    }
                    if (Rng.RandInt0(Math.Max(1, damage)) > 500 && !HoldsLife)
                    {
                        Publish(new MessageEvent("The cold withers your life force!"));
                        LoseExperience(damage);
                    }
                }
                break;
            case "pois":
                if (!Afflict(TimedIds.Poisoned, 10 + Rng.RandInt1(Math.Max(1, damage))))
                    Publish(new MessageEvent("You resist the effect!"));
                if (power >= 60)
                {
                    if (Rng.RandInt0(Math.Max(1, damage)) > 200 && !ImmuneTo("acid"))
                    {
                        Publish(new MessageEvent("The venom stings your skin!"));
                        InventoryDamage("acid", damage / 5);
                        // The venom's acid is added to the hurt, resisted as acid (not a second attack).
                        var sting = Data.Element("acid") is { } acid
                            ? Combat.CombatMath.ResistElement(Rng, acid, damage / 5, Player.Resists.GetValueOrDefault("acid"))
                            : damage / 5;
                        TakeHit(sting, "venom");
                    }
                    if (Rng.RandInt0(Math.Max(1, damage)) > 200)
                    {
                        Publish(new MessageEvent("The stench sickens you."));
                        DrainStat("con");
                    }
                }
                break;
            case "light":
                if (Resisted("light")) break;
                Afflict(TimedIds.Blind, 2 + Rng.RandInt1(5));
                if (damage > 300)
                {
                    if (!Protected(TimedIds.Confused)) Publish(new MessageEvent("You are dazzled!"));
                    Afflict(TimedIds.Confused, 2 + Rng.RandInt1(damage / 100));
                }
                break;
            case "dark":
                if (Resisted("dark")) break;
                Afflict(TimedIds.Blind, 2 + Rng.RandInt1(5));
                if (power >= 70)
                {
                    if (Rng.RandInt0(Math.Max(1, damage)) > 100 && !HoldsLife)
                    {
                        Publish(new MessageEvent("The darkness steals your life force!"));
                        LoseExperience(damage);
                    }
                    if (Rng.RandInt0(Math.Max(1, damage)) > 200)
                    {
                        Publish(new MessageEvent("You feel unsure of yourself in the darkness."));
                        Afflict(TimedIds.Slow, damage / 100);
                    }
                    if (Rng.RandInt0(Math.Max(1, damage)) > 300)
                    {
                        Publish(new MessageEvent("Darkness penetrates your mind!"));
                        IncreaseTimed(TimedIds.Amnesia, damage / 100);
                    }
                }
                break;
            case "sound":
                if (Resisted("sound")) break;
                Stun(Math.Min(35, 5 + Rng.RandInt1(Math.Max(1, damage / 3))));
                if (damage > 300)
                {
                    if (!Protected(TimedIds.Confused)) Publish(new MessageEvent("The noise disorients you."));
                    Afflict(TimedIds.Confused, 2 + Rng.RandInt1(damage / 100));
                }
                break;
            case "shards":
                if (Resisted("shards")) break;
                Afflict(TimedIds.Cut, Rng.RandInt1(Math.Max(1, damage)));
                break;
            case "nexus":
                if (Resisted("nexus")) break;
                if (Rng.RandInt0(100) < Player.SkillSave) Publish(new MessageEvent("You avoid the effect!"));
                else IncreaseTimed("scrambled", Rng.RandInt0(20) + 20);
                if (Rng.OneIn(3) && source is { } from) TeleportPlayerNextTo(from);
                else if (Rng.OneIn(4))
                {
                    if (Rng.RandInt0(100) < Player.SkillSave) Publish(new MessageEvent("You avoid the effect!"));
                    else TeleportPlayerLevel();
                }
                else TeleportPlayer(200);
                break;
            case "nether":
                if (Player.Resists.GetValueOrDefault("nether") > 0 || HoldsLife)
                {
                    Publish(new MessageEvent("You resist the effect!"));
                    break;
                }
                Publish(new MessageEvent("You feel your life force draining away!"));
                LoseExperience(200 + Player.Experience / 100 * LifeDrainPercent);
                if (power >= 80)
                {
                    if (Rng.RandInt0(Math.Max(1, damage)) > 100 && Player.MaxMana > 0)
                    {
                        Publish(new MessageEvent("Your mind is dulled."));
                        Player.Mana -= Math.Min(Player.Mana, damage / 10);
                    }
                    if (Rng.RandInt0(Math.Max(1, damage)) > 200)
                    {
                        Publish(new MessageEvent("Your energy is sapped!"));
                        Player.Energy = 0;
                    }
                }
                break;
            case "chaos":
                ChaosSideEffects();
                break;
            case "disen":
                if (Resisted("disen")) break;
                Disenchant();
                break;
            case "water":
                Afflict(TimedIds.Confused, 5 + Rng.RandInt1(5));
                Stun(Rng.RandInt1(40));
                break;
            case "ice":
                if (!ImmuneTo("cold")) InventoryDamage("cold", Math.Min(damage * 5, 300));
                if (Player.Resists.GetValueOrDefault("shards") <= 0) Afflict(TimedIds.Cut, Rng.Damroll(5, 8));
                else Publish(new MessageEvent("You resist the effect!"));
                Stun(Rng.RandInt1(15));
                break;
            case "gravity":
                Publish(new MessageEvent("Gravity warps around you."));
                if (Rng.RandInt1(127) > Player.Level) TeleportPlayer(5);
                Afflict(TimedIds.Slow, 4 + Rng.RandInt0(4));
                Stun(Math.Min(35, 5 + Rng.RandInt1(Math.Max(1, damage / 3))));
                break;
            case "inertia":
                Afflict(TimedIds.Slow, 4 + Rng.RandInt0(4));
                break;
            case "force":
                Stun(Rng.RandInt1(20));
                if (source is { } centre) ThrustAway(centre, 3 + damage / 20);
                break;
            case "time":
                if (Rng.OneIn(2))
                {
                    Publish(new MessageEvent("You feel your life force draining away!"));
                    LoseExperience(100 + Player.Experience / 100 * LifeDrainPercent);
                }
                else if (!Rng.OneIn(5))
                {
                    for (var i = 0; i < 2; i++) LoseStatToTime(Rng.Pick(TimeStats));
                }
                else
                {
                    Publish(new MessageEvent("You're not as powerful as you used to be..."));
                    foreach (var (stat, _) in TimeStats) Player.StatDrain[stat] = Player.StatDrain.GetValueOrDefault(stat) + 1;
                    RecalculateAfterStatChange();
                }
                break;
            case "plasma":
                Stun(Math.Min(35, 5 + Rng.RandInt1(Math.Max(1, damage * 3 / 4))));
                break;
        }
    }

    private static readonly (string Stat, string Adjective)[] TimeStats =
        [("str", "strong"), ("int", "bright"), ("wis", "wise"), ("dex", "agile"), ("con", "hale")];

    /// <summary>Angband project_player_drain_stats: time takes a point off a stat, sustained or not.</summary>
    private void LoseStatToTime((string Stat, string Adjective) which)
    {
        Publish(new MessageEvent($"You're not as {which.Adjective} as you used to be..."));
        Player.StatDrain[which.Stat] = Player.StatDrain.GetValueOrDefault(which.Stat) + 1;
        RecalculateAfterStatChange();
    }

    /// <summary>Angband player_resists with its message: true (and "You resist the effect!") when resisted.</summary>
    private bool Resisted(string element)
    {
        if (Player.Resists.GetValueOrDefault(element) <= 0) return false;
        Publish(new MessageEvent("You resist the effect!"));
        return true;
    }

    /// <summary>Immunity (Angband player_is_immune): the highest level of resistance.</summary>
    private bool ImmuneTo(string element) => Player.Resists.GetValueOrDefault(element) >= 3;

    /// <summary>Afraid (Angband OF_AFRAID): frightened or in terror, or wearing something that frightens.</summary>
    public bool PlayerAfraid => Player.Timed.Has(TimedIds.Afraid) || Player.Timed.Has("terror") || Player.HasGearFlag(Definitions.ItemFlags.Afraid);

    private bool HoldsLife => Player.HasGearFlag(Definitions.ItemFlags.HoldLife) || Player.Resists.GetValueOrDefault("hold_life") > 0;

    /// <summary>Whether something keeps a status off (Angband player_inc_check, from player_timed.txt's fail lines).</summary>
    private bool Protected(string timed) => Data.Timed(timed) is { } def && !IncreaseCheck(def);

    /// <summary>Angband player_inc_timed with its check: adds a status unless the player is protected from it.</summary>
    private bool Afflict(string timed, int amount) => amount > 0 && IncreaseTimed(timed, amount);

    private void Stun(int amount) => Afflict(TimedIds.Stun, amount);

    /// <summary>Nexus: to a square next to the monster that cast it (Angband TELEPORT_TO).</summary>
    private void TeleportPlayerNextTo(Loc caster)
    {
        if (TeleportForbidden()) return;
        var spots = Level.Neighbors(caster).Where(p => Level.IsPassable(p) && Level[p].Monster == 0 && p != Player.Position).ToList();
        if (spots.Count == 0) return;
        var from = Player.Position;
        Player.Position = Rng.Pick(spots);
        Publish(new PlayerMovedEvent(from, Player.Position));
        Publish(new PlayerTeleportedEvent(from, Player.Position));
        UpdateView();
    }

    /// <summary>Force (Angband thrust_away): pushed straight away from where it came from, up to <paramref name="distance"/> squares.</summary>
    private void ThrustAway(Loc centre, int distance)
    {
        var dir = DirectionExtensions.FromOffset(Player.Position.X - centre.X, Player.Position.Y - centre.Y);
        if (dir == Direction.Here) return;
        var from = Player.Position;
        var at = from;
        for (var i = 0; i < distance; i++)
        {
            var next = at.Step(dir);
            if (!Level.InBounds(next) || !Level.IsPassable(next) || Level[next].Monster != 0) break;
            at = next;
        }
        if (at == from) return;
        Player.Position = at;
        Publish(new MessageEvent("You are thrown back!"));
        Publish(new PlayerMovedEvent(from, at));
        UpdateView();
    }

    /// <summary>
    /// Angband inven_damage: each vulnerable thing in the pack (not what is worn or wielded, and
    /// never artifacts) may be hurt — weapons and armour lose a point of enchantment, anything
    /// else is destroyed, one at a time with the chance <paramref name="chance"/> in 10,000
    /// (a quarter of that for rods). Returns how many were destroyed.
    /// </summary>
    public int InventoryDamage(string element, int chance)
    {
        if (chance <= 0) return 0;
        var destroyed = 0;
        // AVABand's bag egos: a Fireproof (Insulated...) bag carried keeps the pack safe from its element.
        var guarded = Player.Inventory.Pack.Any(b => b.Ego?.Guards.Contains(element) == true || b.Artifact?.Guards.Contains(element) == true);
        foreach (var item in Player.Inventory.Pack.Concat(Player.Inventory.Quiver).ToList())
        {
            if (!item.HarmedBy(element)) continue;
            if (guarded && Player.Inventory.Pack.Contains(item)) continue;
            // The name without its number ("Potions of Cure Light Wounds"), only once something is hurt.
            string Name() => System.Text.RegularExpressions.Regex.Replace(ItemNaming.Describe(item, Knowledge, withArticle: false, full: false), @"^\d+ ", "");
            var weapon = item.Base.Slot is Definitions.EquipSlot.Weapon or Definitions.EquipSlot.Bow && !item.Base.IsAmmo;
            var armour = item.Base.Slot is Definitions.EquipSlot.Body or Definitions.EquipSlot.Cloak or Definitions.EquipSlot.Shield
                or Definitions.EquipSlot.Head or Definitions.EquipSlot.Hands or Definitions.EquipSlot.Feet;
            if (weapon || armour)
            {
                if (Rng.RandInt0(10_000) >= chance) continue;
                if (weapon) { item.ToHit--; item.ToDam--; }
                else item.ToAc--;
                Publish(new MessageEvent($"{(item.Number > 1 ? "One of y" : "Y")}our {Name()} was damaged!"));
                continue;
            }
            var each = item.IsRod ? chance / 4 : chance;
            var lost = 0;
            for (var i = 0; i < item.Number; i++)
                if (Rng.RandInt0(10_000) < each) lost++;
            if (lost == 0) continue;
            var who = item.Number > 1 ? lost == item.Number ? "All of y" : lost > 1 ? "Some of y" : "One of y" : "Y";
            Publish(new MessageEvent($"{who}our {Name()} {(lost > 1 ? "were" : "was")} destroyed!"));
            Player.Inventory.Remove(item, lost, () => Objects.NextSerial++);
            destroyed += lost;
        }
        if (destroyed > 0) RecalculateBonuses();
        return destroyed;
    }

    // --- Things on the floor (Angband project-obj.c project_o) -------------------------------------

    /// <summary>Announces a projection for the UI to animate (Angband's bolt and ball graphics).</summary>
    private void ShowProjection(Loc from, IReadOnlyList<Loc> path, IEnumerable<Loc>? burst, string? element, ProjectionKind kind) =>
        Publish(new ProjectionEvent(from, [.. path], burst is null ? [] : [.. burst], element, kind));

    /// <summary>The squares a ball covers: within its radius of the centre, and in the blast's line of fire.</summary>
    public IEnumerable<Loc> BallArea(Loc centre, int radius)
    {
        for (var y = centre.Y - radius; y <= centre.Y + radius; y++)
        for (var x = centre.X - radius; x <= centre.X + radius; x++)
        {
            var p = new Loc(x, y);
            if (Level.InBounds(p) && p.DistanceTo(centre) <= radius && Combat.ProjectionPath.Projectable(Level, centre, p, radius + 1))
                yield return p;
        }
    }

    /// <summary>
    /// The squares a breath covers (Angband effect_handler_BREATH): a cone from the breather toward
    /// its target, <paramref name="degrees"/> wide, out to the full range, in the breath's line of fire.
    /// </summary>
    public IEnumerable<Loc> BreathArc(Loc source, Loc target, int degrees = 30, int radius = 0)
    {
        if (radius <= 0) radius = MaxRange;
        var aim = Math.Atan2(target.Y - source.Y, target.X - source.X);
        var half = degrees / 2.0 * Math.PI / 180;
        for (var y = source.Y - radius; y <= source.Y + radius; y++)
        for (var x = source.X - radius; x <= source.X + radius; x++)
        {
            var p = new Loc(x, y);
            if (p == source || !Level.InBounds(p) || p.DistanceTo(source) > radius) continue;
            var off = Math.Abs(Math.IEEERemainder(Math.Atan2(p.Y - source.Y, p.X - source.X) - aim, 2 * Math.PI));
            // Squares right beside the breather are always caught (Angband's diameter of source).
            if (off > half && p.DistanceTo(source) > 1) continue;
            if (Combat.ProjectionPath.Projectable(Level, source, p, radius + 1)) yield return p;
        }
    }

    /// <summary>What a kind of blast destroys on the floor: those that hate these (plasma is fire and lightning).</summary>
    private static string[] FloorHazards(string element) => element switch
    {
        "plasma" => ["fire", "elec"],
        "acid" or "elec" or "fire" or "cold" or "sound" or "shards" or "ice" or "force" => [element],
        _ => [],
    };

    /// <summary>
    /// Angband project_o: a ball or breath destroys every object in its area that hates its element —
    /// potions shatter, scrolls burn up — unless it is an artifact or proof against it ("The Mithril
    /// Arrows are unaffected!"); mana destroys anything but artifacts. What you see go is reported.
    /// Returns how many objects (stacks) were destroyed.
    /// </summary>
    public int DestroyFloorObjects(IEnumerable<Loc> area, string? element)
    {
        if (element is null) return 0;
        var mana = element == "mana";
        var hazards = FloorHazards(element);
        if (!mana && hazards.Length == 0) return 0;
        var destroyed = 0;
        foreach (var p in area.ToList())
        {
            foreach (var item in Level.Objects.At(p).ToList())
            {
                var hated = mana || hazards.Any(h => item.Base.Hates.Contains(h));
                if (!hated) continue;
                var seen = Level[p].Has(World.SquareFlags.Seen) && !IsIgnored(item);
                var name = System.Text.RegularExpressions.Regex.Replace(ItemNaming.Describe(item, Knowledge, withArticle: false, full: false), @"^\d+ ", "");
                var many = item.Number > 1;
                var proof = !mana && hazards.All(h => !item.Base.Hates.Contains(h) || !item.HarmedBy(h));
                if (item.Artifact is not null || proof)
                {
                    if (seen) Publish(new MessageEvent($"The {name} {(many ? "are" : "is")} unaffected!"));
                    continue;
                }
                var verb = mana ? (many ? "are destroyed" : "is destroyed") : FloorVerb(hazards.First(h => item.HarmedBy(h)), many);
                if (seen) Publish(new MessageEvent($"The {name} {verb}!"));
                Level.Objects.Remove(p, item);
                destroyed++;
            }
            if (Level[p].Has(World.SquareFlags.Seen)) Known.RememberObject(p, ObjectShownAt(p));
        }
        return destroyed;
    }

    /// <summary>Angband's words for each element's work on the floor.</summary>
    private static string FloorVerb(string element, bool many) => element switch
    {
        "acid" => many ? "melt" : "melts",
        "fire" => many ? "burn up" : "burns up",
        "elec" => many ? "are destroyed" : "is destroyed",
        _ => many ? "shatter" : "shatters", // cold, sound, shards, ice, force: potions and flasks
    };
}
