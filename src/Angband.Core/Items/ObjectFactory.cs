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

    /// <summary>Picks a kind for <paramref name="level"/>, weighted by commonness, occasionally deeper.</summary>
    public ObjectKindDef? PickKind(GameRandom rng, int level, Func<ObjectKindDef, bool>? filter = null)
    {
        if (level > 0 && rng.OneIn(GreatObjectChance))
            level = Math.Min(1 + level * MaxDepth / rng.RandInt1(MaxDepth), MaxDepth - 1);

        var eligible = data.Objects.Where(k =>
                k.Commonness > 0 && k.Level <= level && k.MinDepth <= level && k.MaxDepth >= level
                && data.ObjectBase(k.Base) is { } b && b.Id != "gold"
                && (filter?.Invoke(k) ?? true))
            .ToList();
        return rng.PickWeighted(eligible, k => k.Commonness);
    }

    /// <summary>A random object for the floor or a monster drop (Angband make_object).</summary>
    public Item? Make(GameRandom rng, int level, bool good = false, bool great = false)
    {
        Func<ObjectKindDef, bool>? filter = good || great
            ? k => data.ObjectBase(k.Base) is { IsWearable: true } || k.Level >= level / 2
            : null;
        var kind = PickKind(rng, level, filter);
        if (kind is null) return null;

        var item = Create(kind, Math.Max(1, kind.StackSize.Roll(rng)));
        item.OriginDepth = level;
        ApplyMagic(rng, item, level, good, great);
        return item;
    }

    /// <summary>
    /// Angband apply_magic: roll the object's power (-2 cursed ... +2 excellent) and apply bonuses,
    /// egos, artifacts or curses accordingly.
    /// </summary>
    public void ApplyMagic(GameRandom rng, Item item, int level, bool good = false, bool great = false, bool artifacts = true)
    {
        ApplyKindRolls(rng, item, level);
        if (item.IsChest)
        {
            item.ChestState = PickChestTraps(rng, item.Kind);
            return;
        }
        var goodChance = Math.Min(75, 10 + level);
        var greatChance = Math.Min(20, 5 + level / 5);

        var power = 0;
        if (good || great || rng.Percent(goodChance))
        {
            power = 1;
            if (great || rng.Percent(greatChance)) power = 2;
        }
        else if (rng.Percent(goodChance))
        {
            power = -1;
            if (rng.Percent(greatChance)) power = -2;
        }

        if (power == 2 && artifacts && item.IsWearable && TryMakeArtifact(rng, item, level)) return;

        var b = item.Base;
        if (b.IsWeapon || b.Slot == EquipSlot.Bow || b.IsAmmo)
        {
            if (power != 0)
            {
                var sign = Math.Sign(power);
                item.ToHit += sign * (rng.RandInt1(5) + MagicBonus(rng, 5, level));
                item.ToDam += sign * (rng.RandInt1(5) + MagicBonus(rng, 5, level));
                if (Math.Abs(power) == 2)
                {
                    item.ToHit += sign * MagicBonus(rng, 10, level);
                    item.ToDam += sign * MagicBonus(rng, 10, level);
                }
            }
        }
        else if (b.IsWearable && b.Slot is not (EquipSlot.Light or EquipSlot.Ring or EquipSlot.Amulet))
        {
            if (power != 0)
            {
                var sign = Math.Sign(power);
                item.ToAc += sign * (rng.RandInt1(5) + MagicBonus(rng, 5, level));
                if (Math.Abs(power) == 2) item.ToAc += sign * MagicBonus(rng, 10, level);
            }
        }
        else if (item.Kind.Has("RANDOM_TO_A"))
            item.ToAc += 5 + MagicBonus(rng, 10, level);

        if (power == 2 && b.IsWearable && b.Slot != EquipSlot.Light) TryMakeEgo(rng, item, level);
        if (power == -2 && b.IsWearable) AddRandomCurses(rng, item, rng.RandRange(1, 2));
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
    public bool TryMakeArtifact(GameRandom rng, Item item, int level)
    {
        if (!AllowArtifacts) return false;
        var candidates = Artifacts
            .Where(a => a.Kind == item.Kind.Id && a.Level <= level + 5 && !CreatedArtifacts.Contains(a.Id))
            .ToList();
        foreach (var art in candidates)
        {
            if (!rng.OneIn(art.Rarity)) continue;
            ApplyArtifact(item, art);
            return true;
        }
        return false;
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

    public bool TryMakeEgo(GameRandom rng, Item item, int level)
    {
        var egos = data.Egos.Where(e => e.Bases.Contains(item.Base.Id) && e.Level <= level && e.MaxDepth >= level).ToList();
        var ego = rng.PickWeighted(egos, e => e.Commonness);
        if (ego is null) return false;

        item.Ego = ego;
        item.ToHit += ego.ToHit.Roll(rng);
        item.ToDam += ego.ToDam.Roll(rng);
        item.ToAc += ego.ToAc.Roll(rng);
        foreach (var (mod, value) in ego.Modifiers) item.Modifiers[mod] = item.Modifier(mod) + value;
        foreach (var (mod, text) in ego.Rolls) item.Modifiers[mod] = item.Modifier(mod) + RandomValue.Parse(text).Roll(rng, level);
        foreach (var f in ego.Flags) item.Flags.Add(f);
        item.Slays.AddRange(ego.Slays);
        item.Brands.AddRange(ego.Brands);
        foreach (var r in ego.Resists) item.Resists.Add(r);
        foreach (var c in ego.Curses)
            if (!item.Curses.Contains(c)) item.Curses.Add(c);
        return true;
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

    /// <summary>Angband make_gold: value grows with depth, with rare large finds.</summary>
    public Item MakeGold(GameRandom rng, int level)
    {
        var avg = 18 * level / 10 + 18;
        var value = Math.Max(1, rng.Spread(avg, level + 10));
        while (rng.OneIn(100) && value * 10 <= short.MaxValue) value *= 10;

        var golds = data.Objects.Where(k => k.Base == "gold").OrderBy(k => k.Level).ToList();
        var kind = golds.LastOrDefault(k => k.Level <= level) ?? golds.FirstOrDefault()
                   ?? throw new GameDataException("No gold object kinds are defined.");
        var gold = Create(kind);
        gold.GoldValue = value;
        return gold;
    }
}
