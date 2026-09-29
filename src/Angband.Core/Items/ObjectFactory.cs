using Angband.Core.Definitions;
using Angband.Core.Randomness;

namespace Angband.Core.Items;

/// <summary>
/// Creates objects (Angband obj-make.c): picks kinds by depth, rolls magic (good/great/bad), egos,
/// artifacts and curses, and gold. Serial numbers make every object identifiable.
/// </summary>
public sealed class ObjectFactory(GameData data)
{
    /// <summary>Angband z_info->max_depth, used by <see cref="MagicBonus"/>.</summary>
    private const int MaxDepth = 128;
    /// <summary>1-in-N chance of a deeper-than-normal kind (Angband great_obj).</summary>
    public const int GreatObjectChance = 20;
    /// <summary>Angband obj-make:great-ego: one in this many egos are picked from much deeper.</summary>
    public const int GreatEgoChance = 20;

    public long NextSerial { get; set; } = 1;

    /// <summary>Artifacts generated this game; each exists at most once.</summary>
    public HashSet<string> CreatedArtifacts { get; } = new(StringComparer.Ordinal);

    /// <summary>The artifacts this game can make: the standard set, or a random one (birth_randarts).</summary>
    public IReadOnlyList<ArtifactDef> Artifacts { get; set; } = data.Artifacts;

    /// <summary>False with Angband's birth_no_artifacts: no artifacts are generated.</summary>
    public bool AllowArtifacts { get; set; } = true;

    public Item Create(ObjectKindDef kind, int number = 1) =>
        new(NextSerial++, kind, data.ObjectBase(kind.Base) ?? throw new GameDataException($"Object '{kind.Id}' has unknown base '{kind.Base}'."), number);

    public Item Create(string kindId, int number = 1) =>
        Create(data.Object(kindId) ?? throw new ArgumentException($"Unknown object '{kindId}'."), number);

    /// <summary>
    /// Angband m_bonus: a magic bonus up to <paramref name="max"/>, normally distributed around a
    /// value that grows with depth.
    /// </summary>
    public static int MagicBonus(GameRandom rng, int max, int level)
    {
        level = Math.Min(level, MaxDepth - 1);
        var bonus = max * level / MaxDepth;
        if (rng.RandInt0(MaxDepth) < max * level % MaxDepth) bonus++;
        var stand = max / 4;
        if (rng.RandInt0(4) < max % 4) stand++;
        return Math.Clamp(rng.Normal(bonus, stand), 0, max);
    }

    /// <summary>Angband obj-make:max-depth: object allocation goes no deeper than this.</summary>
    public const int MaxObjectDepth = 100;

    /// <summary>
    /// Angband get_obj_num: a kind for <paramref name="level"/>, weighted by commonness — now and then
    /// from far deeper, and never from past the deepest allocation level (100).
    /// </summary>
    public ObjectKindDef? PickKind(GameRandom rng, int level, Func<ObjectKindDef, bool>? filter = null)
    {
        if (level > 0 && rng.OneIn(GreatObjectChance))
            level = 1 + level * MaxObjectDepth / rng.RandInt1(MaxObjectDepth);
        level = Math.Clamp(level, 0, MaxObjectDepth);

        var eligible = data.Objects.Where(k =>
                k.Commonness > 0 && (k.MinDepth ?? k.Level) <= level && k.MaxDepth >= level
                && data.ObjectBase(k.Base) is { } b && b.Id != "gold"
                && (filter?.Invoke(k) ?? true))
            .ToList();
        return rng.PickWeighted(eligible, k => k.Commonness);
    }

    /// <summary>
    /// A random object for the floor or a monster drop (Angband make_object). <paramref name="extraRoll"/>
    /// gives two more chances at an artifact (a unique's drop, acquirement).
    /// </summary>
    public Item? Make(GameRandom rng, int level, bool good = false, bool great = false, bool extraRoll = false)
    {
        // Angband make_object: now and then a special artifact (the Phial, the Star, a ring of
        // power...) — one time in ten for a good object; failing that, the object is good.
        if (rng.OneIn(good ? 10 : 1000))
        {
            if (MakeSpecialArtifact(rng, level) is { } special) return special;
            good = true;
        }
        Func<ObjectKindDef, bool>? filter = good || great ? IsGoodKind : null;
        var kind = PickKind(rng, good ? level + 10 : level, filter);
        if (kind is null) return null;

        var item = Create(kind, 1);
        item.OriginDepth = level;
        ApplyMagic(rng, item, level, good, great, extraRoll: extraRoll);
        // Angband make_object: then, unless it became an artifact, perhaps a pile of them.
        if (!item.IsArtifact && kind.PileChance >= rng.RandInt1(100))
            item.Number = Math.Max(1, kind.StackSize.Roll(rng));
        return item;
    }

    /// <summary>
    /// Angband kind_is_good: what a good drop may be — armour and weapons that don't start damaged,
    /// arrows and bolts, and kinds marked GOOD (the Ring of Speed, the great amulets).
    /// </summary>
    public bool IsGoodKind(ObjectKindDef kind)
    {
        int Least(string roll, int fixedValue) =>
            kind.Rolls.TryGetValue(roll, out var text) ? RandomValue.Parse(text).Min : fixedValue;
        var b = data.ObjectBase(kind.Base);
        if (b is { IsWearable: true } && b.Slot is EquipSlot.Body or EquipSlot.Cloak or EquipSlot.Shield or EquipSlot.Head
                or EquipSlot.Hands or EquipSlot.Feet)
            return Least("to_a", kind.ToAc) >= 0;
        if (kind.Base is "sword" or "hafted" or "polearm" or "digger" or "sling" or "bow" or "crossbow")
            return Least("to_h", kind.ToHit) >= 0 && Least("to_d", kind.ToDam) >= 0;
        if (kind.Base is "arrow" or "bolt") return true;
        return kind.Has("GOOD");
    }

    /// <summary>
    /// Angband apply_magic: roll the object's power (good one time in 33 + level, great 30% of those),
    /// give an excellent one its chances at an artifact (two if it must be great, two more for a
    /// unique's drop), make it an ego if great, curse a wearable one time in 20, then its bonuses.
    /// </summary>
    public void ApplyMagic(GameRandom rng, Item item, int level, bool good = false, bool great = false, bool artifacts = true,
        bool extraRoll = false)
    {
        ApplyKindRolls(rng, item, level);
        if (item.IsChest)
        {
            item.ChestState = PickChestTraps(rng, item.Kind);
            return;
        }

        var power = 0;
        if (good || rng.RandInt0(100) < 33 + level)
        {
            power = 1;
            if (great || rng.RandInt0(100) < 30) power = 2;
        }

        if (artifacts)
        {
            var rolls = (great ? 2 : power >= 2 ? 1 : 0) + (extraRoll ? 2 : 0);
            for (var i = 0; i < rolls; i++)
                if (TryMakeArtifact(rng, item, level)) return;
        }

        if (power == 2) TryMakeEgo(rng, item, level);
        if (rng.OneIn(20) && item.IsWearable) level = ApplyCurse(rng, item, level);

        var b = item.Base;
        if (b.IsWeapon || b.Slot == EquipSlot.Bow || b.IsAmmo) ApplyWeaponMagic(rng, item, level, power);
        else if (b.IsWearable && b.Slot is not (EquipSlot.Light or EquipSlot.Ring or EquipSlot.Amulet))
        {
            // Angband apply_magic_armour.
            if (power > 0)
            {
                item.ToAc += rng.RandInt1(5) + MagicBonus(rng, 5, level);
                if (power > 1) item.ToAc += MagicBonus(rng, 10, level);
            }
        }
        else if (item.Kind.Id == "ring_of_speed")
            while (rng.OneIn(2)) item.Modifiers[ItemModifiers.Speed] = item.Modifier(ItemModifiers.Speed) + 1; // super-charged

        ApplyEgoMinimums(item);
    }

    /// <summary>
    /// Angband apply_magic_weapon: a good weapon gains 1d5 + m_bonus(5) to hit and to dam, a great
    /// one m_bonus(10) more — and a great melee weapon may have its dice super-charged, great
    /// ammunition an extra side or two.
    /// </summary>
    private static void ApplyWeaponMagic(GameRandom rng, Item item, int level, int power)
    {
        if (power <= 0) return;
        item.ToHit += rng.RandInt1(5) + MagicBonus(rng, 5, level);
        item.ToDam += rng.RandInt1(5) + MagicBonus(rng, 5, level);
        if (power < 2) return;
        item.ToHit += MagicBonus(rng, 10, level);
        item.ToDam += MagicBonus(rng, 10, level);

        var (dd, ds) = (item.Damage.Count, item.Damage.Sides);
        if (item.Base.IsWeapon && !item.IsAmmo && item.Base.Slot == EquipSlot.Weapon)
        {
            while (dd * ds > 0 && rng.OneIn(4 * dd * ds))
            {
                // More dice or sides make still more likely.
                if (rng.RandInt0(dd + ds) < dd)
                {
                    for (var more = rng.RandInt1(2 + dd / ds); (dd + 1) * ds <= 40 && more > 0; more--)
                        if (!rng.OneIn(3)) dd++;
                }
                else
                {
                    for (var more = rng.RandInt1(2 + ds / dd); dd * (ds + 1) <= 40 && more > 0; more--)
                        if (!rng.OneIn(3)) ds++;
                }
            }
        }
        else if (item.IsAmmo && rng.OneIn(6))
        {
            ds++;
            if (rng.OneIn(10)) ds++;
        }
        item.Damage = item.Damage with { Count = dd, Sides = ds };
    }

    /// <summary>
    /// Angband apply_curse: up to four tries at a curse that fits the item (never a blessed one);
    /// each curse taken makes the object count as a little deeper.
    /// </summary>
    public int ApplyCurse(GameRandom rng, Item item, int level)
    {
        if (item.Flags.Contains("BLESSED")) return level;
        var power = rng.RandInt1(9) + 10 * MagicBonus(rng, 9, level);
        var newLevel = level;
        for (var n = rng.RandInt1(4); n > 0; n--)
        {
            for (var tries = 3; tries > 0; tries--)
            {
                var curse = rng.Pick(data.Curses);
                if (curse.Bases.Count > 0 && !curse.Bases.Contains(item.Base.Id)) continue;
                if (!item.Curses.Contains(curse.Id))
                {
                    item.Curses.Add(curse.Id);
                    newLevel += rng.RandInt1(1 + power / 10);
                }
                break;
            }
        }
        return newLevel;
    }

    /// <summary>Rolls the kind's random values: modifiers and bonuses (rings, amulets...) and device charges.</summary>
    public static void ApplyKindRolls(GameRandom rng, Item item, int level)
    {
        foreach (var (key, text) in item.Kind.Rolls)
        {
            var value = RandomValue.Parse(text).Roll(rng, level);
            switch (key)
            {
                case "to_h": item.ToHit += value; break;
                case "to_d": item.ToDam += value; break;
                case "to_a": item.ToAc += value; break;
                default: item.Modifiers[key] = item.Modifier(key) + value; break;
            }
        }
        if (item.Kind.Charges is { } charges) item.Charges = Math.Max(0, RandomValue.Parse(charges).Roll(rng, level));
    }

    /// <summary>
    /// Angband pick_chest_traps: one chest in ten is merely locked; otherwise a trap for the chest's
    /// level, maybe a second (likelier for deeper chests), and for very deep ones a third or fourth.
    /// </summary>
    public int PickChestTraps(GameRandom rng, ObjectKindDef kind)
    {
        var level = kind.Level;
        if (data.ChestTraps.Count < 2 || rng.OneIn(10)) return 1;
        int One()
        {
            var eligible = data.ChestTraps.Skip(1).Where(t => t.Level <= level).ToList();
            return eligible.Count == 0 ? 1 : rng.Pick(eligible).Bit;
        }
        var traps = One();
        if (level > 5 && rng.OneIn(1 + (65 - level) / 10)) traps |= One();
        if (level > 45 && rng.OneIn(Math.Max(1, 65 - level)))
        {
            traps |= One();
            if (rng.OneIn(40)) traps |= One();
        }
        return traps;
    }

    /// <summary>Turns the object into an artifact of the same kind, if one is eligible and rolls its rarity.</summary>
    /// <summary>
    /// Angband make_artifact: the first artifact of the item's kind (not a special one) not yet made
    /// that passes its rolls — made deeper than its minimum only by luck (one in twice the shortfall),
    /// never below its maximum, and then with its alloc chance.
    /// </summary>
    public bool TryMakeArtifact(GameRandom rng, Item item, int level)
    {
        if (!AllowArtifacts || level <= 0 || item.Number != 1 || item.Kind.IsSpecialArtifactKind) return false;
        foreach (var art in Artifacts.Where(a => a.Kind == item.Kind.Id && !CreatedArtifacts.Contains(a.Id)))
        {
            if (!PassesArtifactRolls(rng, art, level)) continue;
            ApplyArtifact(item, art);
            return true;
        }
        return false;
    }

    /// <summary>Angband make_artifact_special: one of the artifacts only ever made as themselves.</summary>
    public Item? MakeSpecialArtifact(GameRandom rng, int level)
    {
        if (!AllowArtifacts || level <= 0) return null;
        foreach (var art in Artifacts.Where(a => !CreatedArtifacts.Contains(a.Id)))
        {
            if (data.Object(art.Kind) is not { IsSpecialArtifactKind: true } kind) continue;
            if (!PassesArtifactRolls(rng, art, level)) continue;
            var item = Create(kind);
            item.OriginDepth = level;
            ApplyArtifact(item, art);
            return item;
        }
        return null;
    }

    private static bool PassesArtifactRolls(GameRandom rng, ArtifactDef art, int level)
    {
        if (art.Level > level && rng.RandInt0((art.Level - level) * 2) != 0) return false;
        if (art.MaxDepth < level) return false;
        return rng.RandInt1(100) <= art.AllocChance;
    }

    /// <summary>Makes an artifact directly (e.g. for tests or quest rewards).</summary>
    public Item CreateArtifact(ArtifactDef art)
    {
        var item = Create(art.Kind);
        ApplyArtifact(item, art);
        return item;
    }

    private void ApplyArtifact(Item item, ArtifactDef art)
    {
        item.Artifact = art;
        item.Number = 1;
        item.ToHit = art.ToHit;
        item.ToDam = art.ToDam;
        item.ToAc = art.ToAc;
        if (art.Armour > 0) item.Armour = art.Armour;
        if (art.Damage is { } dmg) item.Damage = dmg;
        foreach (var (mod, value) in art.Modifiers) item.Modifiers[mod] = value;
        item.Slays.AddRange(art.Slays);
        item.Brands.AddRange(art.Brands);
        foreach (var r in art.Resists) item.Resists.Add(r);
        foreach (var f in art.Flags) item.Flags.Add(f);
        foreach (var c in art.Curses)
            if (!item.Curses.Contains(c)) item.Curses.Add(c);
        item.Fuel = 0; // artifact lights never run out
        CreatedArtifacts.Add(art.Id);
    }

    /// <summary>
    /// Angband make_ego_item / ego_find_random: now and then from much deeper; an ego is allowed up to
    /// its deepest, and below its shallowest only by luck (one in a third of the shortfall, at least 2).
    /// </summary>
    public bool TryMakeEgo(GameRandom rng, Item item, int level)
    {
        if (item.IsArtifact || item.Ego is not null) return false;
        if (level > 0 && rng.OneIn(GreatEgoChance))
            level = Math.Min(1 + level * MaxDepth / rng.RandInt1(MaxDepth), MaxDepth - 1);
        var egos = new List<EgoItemDef>();
        foreach (var e in data.Egos)
        {
            if (level > e.MaxDepth || !e.Fits(item.Kind)) continue;
            if (level >= e.Level || rng.OneIn(Math.Max(2, (e.Level - level) / 3))) egos.Add(e);
        }
        var ego = rng.PickWeighted(egos, e => e.Commonness);
        if (ego is null) return false;
        ApplyEgo(rng, item, ego, level);
        return true;
    }

    /// <summary>Angband ego_apply_magic, then ego_apply_minima: makes <paramref name="item"/> <paramref name="ego"/>.</summary>
    public static void ApplyEgo(GameRandom rng, Item item, EgoItemDef ego, int level)
    {
        item.Ego = ego;
        AddRandomPower(rng, item, ego.RandomPower);
        item.ToHit += ego.ToHit.Roll(rng);
        item.ToDam += ego.ToDam.Roll(rng);
        item.ToAc += ego.ToAc.Roll(rng);
        foreach (var (mod, value) in ego.Modifiers) item.Modifiers[mod] = item.Modifier(mod) + value;
        foreach (var (key, text) in ego.Rolls)
        {
            var roll = RandomValue.Parse(text).Roll(rng, level);
            switch (key)
            {
                case "to_h": item.ToHit += roll; break;
                case "to_d": item.ToDam += roll; break;
                case "to_a": item.ToAc += roll; break;
                default: item.Modifiers[key] = item.Modifier(key) + roll; break;
            }
        }
        foreach (var f in ego.Flags) item.Flags.Add(f);
        foreach (var f in ego.FlagsOff) item.Flags.Remove(f);
        item.Slays.AddRange(ego.Slays);
        item.Brands.AddRange(ego.Brands);
        foreach (var r in ego.Resists) item.Resists.Add(r);
        foreach (var c in ego.Curses)
            if (!item.Curses.Contains(c)) item.Curses.Add(c);
        ApplyEgoMinimums(item);
    }

    /// <summary>Angband ego_apply_minima: the ego's least values, once all the magic is in.</summary>
    public static void ApplyEgoMinimums(Item item)
    {
        if (item.Ego is not { } ego) return;
        foreach (var (key, least) in ego.Minimums)
        {
            switch (key)
            {
                case "to_h": item.ToHit = Math.Max(item.ToHit, least); break;
                case "to_d": item.ToDam = Math.Max(item.ToDam, least); break;
                case "to_a": item.ToAc = Math.Max(item.ToAc, least); break;
                default: if (item.Modifier(key) < least) item.Modifiers[key] = least; break;
            }
        }
    }

    private static readonly string[] Sustains = ["sust_str", "sust_int", "sust_wis", "sust_dex", "sust_con"];
    // Angband's protections and miscellaneous abilities (object_property.txt), in AVABand's spelling:
    // protections and see invisible / free action are protections, the rest ability flags.
    private static readonly string[] PowerResists = ["fear", "blind", "conf", "stun", "see_invis", "free_act"];
    private static readonly string[] PowerFlags =
        [ItemFlags.SlowDigest, ItemFlags.Feather, ItemFlags.Regen, ItemFlags.Telepathy, ItemFlags.HoldLife, ItemFlags.TrapImmune];
    private static readonly string[] BaseResists = ["acid", "elec", "fire", "cold"];
    private static readonly string[] HighResists = ["pois", "light", "dark", "sound", "shards", "nexus", "nether", "chaos", "disen"];

    /// <summary>
    /// Angband ego_apply_magic's random extras: a sustain, a power (protection or ability), a base or
    /// high resist — one the item doesn't already have.
    /// </summary>
    private static void AddRandomPower(GameRandom rng, Item item, string? kind)
    {
        if (kind is null) return;
        var pick = kind == "resist_or_power" ? rng.RandInt1(3) : 0;
        string[] resists = [], flags = [];
        if (kind == "sustain") resists = Sustains;
        else if (kind == "power" || pick == 1) (resists, flags) = (PowerResists, PowerFlags);
        else if (kind == "base_resist" || pick > 1) resists = BaseResists;
        else if (kind == "high_resist") resists = HighResists;
        var choices = resists.Where(r => !item.Resists.Contains(r)).Select(r => (Resist: true, Id: r))
            .Concat(flags.Where(f => !item.Flags.Contains(f) && !item.Kind.Has(f)).Select(f => (Resist: false, Id: f))).ToList();
        if (choices.Count == 0) return;
        var (isResist, id) = rng.Pick(choices);
        if (isResist) item.Resists.Add(id);
        else item.Flags.Add(id);
    }

    public void AddRandomCurses(GameRandom rng, Item item, int count)
    {
        var eligible = data.Curses.Where(c => c.Bases.Count == 0 || c.Bases.Contains(item.Base.Id)).ToList();
        for (var i = 0; i < count && eligible.Count > 0; i++)
        {
            var curse = rng.Pick(eligible);
            if (!item.Curses.Contains(curse.Id)) item.Curses.Add(curse.Id);
        }
    }

    /// <summary>
    /// Angband make_gold: value grows with depth (16 at the surface, 80 at 2000 ft), with rare large
    /// finds; the treasure is picked by the value (money_kind): the kinds in order, copper to
    /// adamantite, spread over values up to the largest drop at the greatest depth.
    /// </summary>
    public Item MakeGold(GameRandom rng, int level)
    {
        var avg = 16 * level / 10 + 16;
        var value = Math.Max(1, rng.Spread(avg, level + 10));
        while (rng.OneIn(100) && value * 10 <= short.MaxValue) value *= 10;

        var golds = data.Objects.Where(k => k.Base == "gold").ToList();
        if (golds.Count == 0) throw new GameDataException("No gold object kinds are defined.");
        var maxGoldDrop = 3 * (data.Constants.MaxDepth + 1) + 30;
        var kind = golds[Math.Min(value * 100 / maxGoldDrop * golds.Count / 100, golds.Count - 1)];
        var gold = Create(kind);
        gold.GoldValue = value;
        return gold;
    }
}
