using Angband.Core.Definitions;
using Angband.Core.Randomness;

namespace Angband.Core.Items;

/// <summary>
/// The properties <see cref="ObjectPower"/> weighs: an object as it is (or as far as the player
/// knows it), a random artifact being designed, or the object a curse stands for.
/// </summary>
public sealed class PowerProfile
{
    /// <summary>Null for a curse's object, which has no kind (Angband's curse objects).</summary>
    public ObjectKindDef? Kind { get; init; }
    public ObjectBaseDef? Base { get; init; }
    public int ToHit { get; init; }
    public int ToDam { get; init; }
    public int ToAc { get; init; }
    public int Armour { get; init; }
    public int Weight { get; init; }
    public Dice Damage { get; init; }
    public IReadOnlyDictionary<string, int> Modifiers { get; init; } = new Dictionary<string, int>();
    public IReadOnlyCollection<SlayDef> Slays { get; init; } = [];
    public IReadOnlyCollection<BrandDef> Brands { get; init; } = [];
    /// <summary>AVABand's resist list: elements, and protections, sustains and abilities (fear, free_act, sust_str...).</summary>
    public IReadOnlyCollection<string> Resists { get; init; } = [];
    public IReadOnlyCollection<string> Flags { get; init; } = [];
    /// <summary>Elements it ignores (its kind's or ego's IGNORE_x; artifacts ignore the four base ones).</summary>
    public IReadOnlyCollection<string> Ignores { get; init; } = [];
    /// <summary>Elements the object is vulnerable to (from curses).</summary>
    public IReadOnlyCollection<string> Vulnerabilities { get; init; } = [];
    public IReadOnlyCollection<string> Curses { get; init; } = [];
    public bool IsEgo { get; init; }
    public bool IsArtifact { get; init; }
    /// <summary>The power of its activation (Angband activation.txt), when it has one of its own.</summary>
    public int? ActivationPower { get; init; }

    /// <summary>
    /// An object's profile: everything about it, or (given the player's knowledge) only what the
    /// player has learned — Angband's obj->known, as object_value uses.
    /// </summary>
    public static PowerProfile Of(Item item, PlayerKnowledge? known = null)
    {
        bool Knows(string rune) => known is null || known.KnowsRune(rune);
        return new PowerProfile
        {
            Kind = item.Kind, Base = item.Base,
            ToHit = Knows(RuneIds.ToHit) ? item.ToHit : 0,
            ToDam = Knows(RuneIds.ToDam) ? item.ToDam : 0,
            ToAc = Knows(RuneIds.ToAc) ? item.ToAc : 0,
            Armour = item.Armour, Weight = item.Weight, Damage = item.Damage,
            Modifiers = item.Modifiers.Where(kv => Knows(RuneIds.Modifier(kv.Key))).ToDictionary(kv => kv.Key, kv => kv.Value),
            Slays = [.. item.Slays.Where(s => Knows(RuneIds.Slay(s.MonsterFlag)))],
            Brands = [.. item.Brands.Where(b => Knows(RuneIds.Brand(b.Element)))],
            Resists = [.. item.Resists.Where(r => Knows(RuneIds.Resist(r)))],
            Flags = [.. item.Flags.Where(f => !ItemFlags.Abilities.Contains(f) || Knows(RuneIds.Flag(f)))],
            Curses = [.. item.Curses.Where(c => Knows(RuneIds.Curse(c)))],
            // (What an object ignores is its kind's: unknown until the kind is.)
            Ignores = known is null || known.KnowsKind(item) ? [.. item.Kind.Ignore.Concat(item.Ego?.Ignore ?? []).Distinct()] : [],
            IsEgo = item.Ego is not null, IsArtifact = item.Artifact is not null,
            ActivationPower = item.Artifact?.Activation is not null ? item.Artifact.ActivationPower : null,
        };
    }
}

/// <summary>
/// Angband 4.2 object_power (obj-power.c): what an object's attack, armour, modifiers, abilities,
/// resistances, activation and curses are worth, in the power units that price wearables and ammo
/// (<see cref="ItemValue"/>) and balance random artifacts.
/// </summary>
public static class ObjectPower
{
    // obj-power.h
    private const int NonweapDamage = 15, WeapDamage = 12, BaseJewelryPower = 4, BaseArmourPower = 1, DamagePower = 5,
        ToHitPower = 3, BaseAcPower = 2, ToAcPower = 2, MaxBlows = 5, InhibitPower = 20000, InhibitBlows = 3,
        InhibitMight = 4, InhibitShots = 21, HighToAc = 26, VeryHighToAc = 36, InhibitAc = 56;

    private static readonly int[] AbilityPower = [0, 0, 0, 0, 0, 0, 0, 2, 4, 6, 8, 12, 16, 20, 24, 30, 36, 42, 48, 56, 64, 74, 84, 96, 110];

    // obj-power.c archery[]: ammo damage and launcher damage/multiplier, by ammo.
    private static (int AmmoDam, int LaunchDam, int LaunchMult) Archery(string? ammo) => ammo switch
    {
        "arrow" => (12, 9, 5), "bolt" => (14, 9, 7), _ => (10, 9, 4),
    };

    // obj-power.c flag_sets, by object_property.txt subtype.
    private enum FlagSet { None, Sustain, Protection, Misc }

    private static FlagSet SetOf(string? subtype) => subtype switch
    {
        "sustain" => FlagSet.Sustain, "protection" => FlagSet.Protection, "misc ability" => FlagSet.Misc, _ => FlagSet.None,
    };

    // obj-power.c flag_sets: extra power for several of a kind, and a bonus for the full set.
    private static (int Factor, int Bonus, int Size) SetInfo(FlagSet set) => set switch
    {
        FlagSet.Sustain => (1, 10, 5), FlagSet.Protection => (3, 15, 4), _ => (1, 25, 8),
    };

    // obj-power.c el_powers: low (acid..cold) and high resistances.
    private static readonly (string Id, bool Low, int Ignore, int Vuln, int Res, int Im)[] Elements =
    [
        ("acid", true, 3, -6, 5, 38), ("elec", true, 1, -6, 6, 35), ("fire", true, 3, -6, 6, 40), ("cold", true, 1, -6, 6, 37),
        ("pois", false, 0, 0, 28, 0), ("light", false, 0, 0, 6, 0), ("dark", false, 0, 0, 16, 0), ("sound", false, 0, 0, 14, 0),
        ("shards", false, 0, 0, 8, 0), ("nexus", false, 0, 0, 15, 0), ("nether", false, 0, 0, 20, 0), ("chaos", false, 0, 0, 20, 0),
        ("disen", false, 0, 0, 20, 0),
    ];

    /// <summary>slay.txt power (100 = no better than nothing).</summary>
    public static int SlayPower(SlayDef slay, GameData data) => data.SlayType(slay.MonsterFlag, slay.Multiplier)?.Power ?? 100;

    /// <summary>brand.txt power.</summary>
    public static int BrandPower(BrandDef brand, GameData data) => data.BrandType(brand.Element, brand.Multiplier)?.Power ?? 100;

    public static int Of(Item item, GameData data, PlayerKnowledge? known = null) => Of(PowerProfile.Of(item, known), data);

    /// <summary>Angband object_power: the sum of its parts, in the order Angband adds them.</summary>
    public static int Of(PowerProfile o, GameData data)
    {
        var b = o.Base;
        var melee = b?.IsWeapon == true;
        var bow = b?.Slot == EquipSlot.Bow;
        var ammo = b?.IsAmmo == true;
        var tval = b?.Id ?? "";
        int Mod(string id) => o.Modifiers.GetValueOrDefault(id);

        // Attack power (to_damage_power, damage_dice_power, ammo and launcher power).
        var p = o.ToDam * DamagePower / 2;
        if (!bow && !melee && !ammo) p += o.ToDam * DamagePower;
        var dicePower = 0;
        if (melee || ammo) dicePower = o.Damage.Count * (o.Damage.Sides + 1) * DamagePower / 4;
        else if (!bow && (o.Brands.Count > 0 || o.Slays.Count > 0 || Mod("blows") > 0 || Mod("shots") > 0 || Mod("might") > 0))
            dicePower = WeapDamage * DamagePower;
        p += dicePower;
        if (bow) p += Archery(b!.AmmoClass).AmmoDam * DamagePower / 2;
        var mult = bow ? Math.Max(1, o.Kind?.Multiplier ?? 1) : 1;
        if (ammo)
        {
            var archery = Archery(tval);
            if (o.IsEgo) p += archery.LaunchDam * DamagePower / 2;
            p = p * archery.LaunchMult / (2 * MaxBlows);
        }

        // Extra blows, shots and might.
        var blows = Mod("blows");
        if (blows >= InhibitBlows) return p + InhibitPower;
        if (blows != 0) p = p * (MaxBlows + blows) / MaxBlows + NonweapDamage * blows * DamagePower / 2;
        var shots = Mod("shots");
        if (shots >= InhibitShots) return p + InhibitPower;
        if (shots > 0) p = p * (10 + shots) / 10;
        if (Mod("might") >= InhibitMight) return p + InhibitPower;
        p *= mult + Mod("might");

        // Slays and brands.
        p = SlayPowerOf(o, p, dicePower, data);
        if (bow) p /= MaxBlows;
        p += o.ToHit * ToHitPower / 2;

        // Armour class.
        if (o.Armour != 0)
        {
            p += BaseArmourPower;
            var q = o.Armour * BaseAcPower / 2;
            if (o.Weight > 0) q = q * Math.Min(450, 750 * (o.Armour + o.ToAc) / o.Weight) / 100;
            else q *= 5;
            p += q;
        }
        if (o.ToAc != 0)
        {
            p += o.ToAc * ToAcPower / 2;
            if (o.ToAc > HighToAc) p += (o.ToAc - (HighToAc - 1)) * ToAcPower;
            if (o.ToAc > VeryHighToAc) p += (o.ToAc - (VeryHighToAc - 1)) * ToAcPower * 2;
            if (o.ToAc >= InhibitAc) p += InhibitPower;
        }
        if (tval is "ring" or "amulet") p += BaseJewelryPower;

        p = ModifierPower(o, p, tval, data);
        p = FlagsPower(o, p, tval, data);
        p = ElementPower(o, p);

        // Activation, or the kind's own effect.
        if (o.ActivationPower is { } act) p += act;
        else if (o.Kind is { Power: not 0 } k) p += k.Power;

        // Curses: each is worth what its object is (Angband curse_power; AVABand's curses have no
        // individual strength to subtract).
        foreach (var id in o.Curses)
            if (data.Curse(id) is { } curse) p += Of(CurseProfile(curse), data);
        return p;
    }

    private static int SlayPowerOf(PowerProfile o, int p, int dicePower, GameData data)
    {
        int brands = o.Brands.Count, slays = o.Slays.Count(s => s.Multiplier <= 3), kills = o.Slays.Count(s => s.Multiplier > 3);
        if (brands + slays + kills == 0) return p;
        var best = 1;
        foreach (var br in o.Brands) best = Math.Max(best, BrandPower(br, data));
        foreach (var s in o.Slays) best = Math.Max(best, SlayPower(s, data));
        p += dicePower * dicePower * (best - 100) / 2500;
        if (slays > 1) p += slays * slays * dicePower / (DamagePower * 5);
        if (brands > 1) p += 2 * brands * brands * dicePower / (DamagePower * 5);
        if (slays > 0 && brands > 0) p += slays * brands * dicePower / (DamagePower * 5);
        if (kills > 1) p += 3 * kills * kills * dicePower / (DamagePower * 5);
        if (slays == 8) p += 10;
        if (brands == 5) p += 20;
        if (kills == 3) p += 20;
        return p;
    }

    /// <summary>Angband modifier_power: each modifier's power (times its type-mult), plus a term for many at once.</summary>
    private static int ModifierPower(PowerProfile o, int p, string tval, GameData data)
    {
        var statBonus = 0;
        foreach (var (mod, k) in o.Modifiers)
        {
            if ((data.ObjectProperty("stat", mod) ?? data.ObjectProperty("mod", mod)) is not { } m) continue;
            statBonus += k * m.Mult;
            p += k * m.Power * m.TypeMultFor(tval);
        }
        if (statBonus > 249) p += InhibitPower;
        else if (statBonus > 0) p += AbilityPower[statBonus / 10];
        return p;
    }

    /// <summary>Angband flags_power: sustains, protections, abilities and bad flags, with extra for sets.</summary>
    private static int FlagsPower(PowerProfile o, int p, string tval, GameData data)
    {
        var counts = new Dictionary<FlagSet, int>();
        var flags = o.Resists.Concat(o.Flags).Distinct(StringComparer.Ordinal).ToList();
        // A light that needs no fuel (the Phial, the Star...) has Angband's NO_FUEL.
        if (o.Base?.Slot == EquipSlot.Light && o.Kind is { Fuel: 0 } && !flags.Contains("NO_FUEL")) flags.Add("NO_FUEL");
        foreach (var flag in flags)
        {
            if (data.ObjectProperty("flag", flag) is not { } f) continue;
            p += f.Power * f.TypeMultFor(tval);
            if (SetOf(f.Subtype) is var set and not FlagSet.None) counts[set] = counts.GetValueOrDefault(set) + 1;
        }
        foreach (var (set, count) in counts)
        {
            var s = SetInfo(set);
            if (count > 1) p += s.Factor * count * count;
            if (count == s.Size) p += s.Bonus;
        }
        return p;
    }

    /// <summary>
    /// Angband element_power: ignoring, vulnerability, resistance or immunity to each element, with
    /// extra for several and a bonus for a full set. Anything that ignores an element (an artifact
    /// ignores the four base ones) is worth that much more.
    /// </summary>
    private static int ElementPower(PowerProfile o, int p)
    {
        int immunities = 0, low = 0, high = 0;
        foreach (var el in Elements)
        {
            if (el.Low && (o.IsArtifact || o.Ignores.Contains(el.Id))) p += el.Ignore;
            var level = o.Vulnerabilities.Contains(el.Id) && !o.Resists.Contains(el.Id) ? -1
                : o.Resists.Contains("im_" + el.Id) ? 3
                : o.Resists.Contains(el.Id) ? 1 : 0;
            p += level switch { -1 => el.Vuln, 1 => el.Res, 3 => el.Im + el.Res, _ => 0 };
            if (level >= 3 && el.Low) immunities++;
            if (level >= 1 && el.Low) low++;
            if (level >= 1 && !el.Low) high++;
        }
        if (immunities > 1) p += 6 * immunities * immunities;
        if (immunities == 4) p += InhibitPower;
        if (low > 1) p += low * low;
        if (low == 4) p += 10;
        if (high > 1) p += 2 * high * high;
        if (high == 9) p += 10;
        return p;
    }

    /// <summary>A curse's own object (Angband curse.txt "obj"): its bonuses, modifiers, flags, resists and vulnerabilities.</summary>
    private static PowerProfile CurseProfile(CurseDef curse) => new()
    {
        ToHit = curse.ToHit, ToDam = curse.ToDam, ToAc = curse.ToAc,
        Modifiers = curse.Modifiers, Flags = [.. curse.Flags], Resists = [.. curse.Resists],
        Vulnerabilities = [.. curse.Vulnerabilities],
    };
}
