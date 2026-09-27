using Angband.Core.Definitions;
using Angband.Core.Randomness;

namespace Angband.Core.Items;

/// <summary>
/// Angband 4.2's random artifacts (obj-randart.c, with object power from obj-power.c), for the
/// birth option <c>birth_randarts</c>. The standard set is studied — how powerful each kind of item's
/// artifacts are, and which abilities they tend to have — and a new set of the same size is designed
/// to match: each gets a target power and a base item, then abilities until its power is within
/// 95–115% of the target. The One Ring and the quest artifacts (Grond, Morgoth's crown) stay as they
/// are. The same seed always makes the same set.
/// </summary>
public sealed class RandartGenerator
{
    // obj-randart.h / obj-randart.c constants.
    private const int MaxTries = 200, MinNameLength = 5, MaxNameLength = 9;
    private const int HitStart = 10, DamStart = 10, AcStart = 15, HitIncrement = 4, DamIncrement = 4, AcIncrement = 5;

    /// <summary>Artifacts that are never randomised (Angband: The One Ring and the QUEST_ART kinds).</summary>
    public static readonly IReadOnlySet<string> Fixed = new HashSet<string> { "the_one_ring", "grond", "of_morgoth" };

    private readonly GameData _data;
    private readonly GameRandom _rng;
    private readonly List<ArtifactDef> _standard;
    private readonly Dictionary<string, (int Count, int Min, int Avg, int Max)> _powerByBase = new();
    private readonly Dictionary<string, Dictionary<string, int>> _freqByGroup = new();
    private readonly Dictionary<string, int> _freqAll = new();
    private readonly int _maxPower;
    private readonly int _cursedCount;
    private readonly Dictionary<string, int> _superCounts = new();

    private RandartGenerator(GameData data, ulong seed)
    {
        _data = data;
        _rng = new GameRandom(GameRandom.DeriveSeed(seed, 0x52414E44)); // "RAND"
        _standard = data.Artifacts.Where(a => !Fixed.Contains(a.Id) && data.Object(a.Kind) is not null).ToList();

        // Study the standard set (Angband collect_artifact_data / store_base_power).
        var powers = _standard.ToDictionary(a => a.Id, a => ArtifactPower.Of(Draft.FromArtifact(a, data), data));
        _maxPower = Math.Max(1, powers.Values.Max());
        foreach (var group in _standard.GroupBy(a => data.Object(a.Kind)!.Base))
        {
            // Angband store_base_power: harmful (negative power) artifacts don't count towards the range.
            var p = group.Select(a => powers[a.Id]).Where(x => x > 0).DefaultIfEmpty(1).ToList();
            _powerByBase[group.Key] = (group.Count(), p.Min(), (int)p.Average(), p.Max());
        }
        _cursedCount = powers.Values.Count(p => p < 0); // Angband neg_power_total
        foreach (var art in _standard)
        {
            var draft = Draft.FromArtifact(art, data);
            var groupFreq = Freq(Group(draft.Base));
            foreach (var ability in Abilities(draft))
            {
                groupFreq[ability] = groupFreq.GetValueOrDefault(ability) + 1;
                _freqAll[ability] = _freqAll.GetValueOrDefault(ability) + 1;
            }
            if (draft.Base.IsWeapon && draft.Damage.Count >= draft.Kind.Damage.Count + 3) Count("melee_dice");
            if (draft.Base.IsWeapon && draft.Mod(ItemModifiers.Blows) >= 2) Count("melee_blows");
            if (draft.Mod(ItemModifiers.Speed) >= 5) Count("speed");
            if (draft.Base.Id == "boots" && draft.Mod(ItemModifiers.Speed) > 0) Count("boot_speed");
            if (draft.ToAc >= 20) Count("ac");
        }

        void Count(string what) => _superCounts[what] = _superCounts.GetValueOrDefault(what) + 1;
        Dictionary<string, int> Freq(string group) =>
            _freqByGroup.TryGetValue(group, out var f) ? f : _freqByGroup[group] = new Dictionary<string, int>();
    }

    /// <summary>Designs a random artifact set for a game (Angband do_randart).</summary>
    public static IReadOnlyList<ArtifactDef> Generate(GameData data, ulong seed) => new RandartGenerator(data, seed).CreateSet();

    // --- The set ----------------------------------------------------------------------------------

    /// <summary>
    /// Angband create_artifact_set: at least 80% as many of each kind of item as the standard set
    /// has, the rest by the standard set's proportions; the fixed artifacts are kept.
    /// </summary>
    private IReadOnlyList<ArtifactDef> CreateSet()
    {
        var result = new List<ArtifactDef>();
        var bases = _standard.Select(a => _data.Object(a.Kind)!.Base).ToList();
        var minimum = _powerByBase.ToDictionary(kv => kv.Key, kv => 4 * (kv.Value.Count + 1) / 5);
        var order = new List<string>();
        for (var pending = true; pending;)
        {
            pending = false;
            foreach (var b in minimum.Keys.Order(StringComparer.Ordinal).ToList())
                if (minimum[b] > 0)
                {
                    order.Add(b);
                    minimum[b]--;
                    pending = true;
                }
        }
        while (order.Count < _standard.Count) order.Add(_rng.Pick(bases));

        for (var i = 0; i < _standard.Count; i++) result.Add(Design(order[i], i + 1));
        result.AddRange(_data.Artifacts.Where(a => Fixed.Contains(a.Id)));
        return result;
    }

    /// <summary>Angband design_artifact.</summary>
    private ArtifactDef Design(string baseId, int number)
    {
        var stats = _powerByBase[baseId];
        // Angband Rand_sample(avg, max, min, 20, 20): a spread around the average, never past the extremes.
        var spread = (stats.Max - stats.Min) / 5 + 1;
        var power = Math.Clamp(_rng.Normal(stats.Avg, spread), Math.Max(1, stats.Min), Math.Max(stats.Min, stats.Max));
        var name = MakeName();

        // A base item not too powerful already.
        Draft draft = null!;
        for (var tries = 0; tries < MaxTries; tries++)
        {
            draft = Prepare(PickKind(baseId));
            var basePower = ArtifactPower.Of(draft, _data);
            if (basePower > power * 6 / 10 + 1 && power - basePower < 20) continue;
            break;
        }

        var freq = FrequencyTable(draft);
        var backup = draft.Clone();
        TrySupercharge(draft, power);
        if (ArtifactPower.Of(draft, _data) > power * 23 / 20 + 1) draft = backup;

        var cursed = _rng.OneIn(Math.Max(1, _standard.Count / Math.Max(2, _cursedCount)));
        var ap = ArtifactPower.Of(draft, _data);
        for (var tries = 0; tries < MaxTries; tries++)
        {
            backup = draft.Clone();
            AddAbility(draft, power, freq);
            ap = ArtifactPower.Of(draft, _data);
            if (cursed)
            {
                MakeBad(draft);
                if (_rng.OneIn(3)) cursed = false;
            }
            if (ap > power * 23 / 20 + 1)
            {
                draft = backup;
                continue;
            }
            if (ap >= power * 19 / 20) break;
        }
        ap = Math.Max(1, ArtifactPower.Of(draft, _data));

        // Rarity and depth from power (Angband: alloc_prob and alloc_min).
        var prob = Math.Clamp(4_000_000 / (ap * ap) / Math.Max(1, draft.Kind.Commonness), 1, 99);
        var minDepth = Math.Min(100, (ap + 100) * 100 / _maxPower);
        if (_rng.OneIn(5 + power / 20)) prob = Math.Min(99, prob + _rng.RandInt1(20));
        else if (_rng.OneIn(5 + power / 20)) minDepth = Math.Max(1, minDepth / 2);

        return draft.ToArtifact($"randart_{number}", name, Math.Max(1, minDepth), Math.Max(1, (int)Math.Round(100.0 / prob)),
            $"Random {ItemNaming.Plain(draft.Base.Name, false).ToLowerInvariant()} of power {ap}.");
    }

    /// <summary>Angband get_base_item: a random kind of the base — no elven rings, quest items or One Ring bases.</summary>
    private ObjectKindDef PickKind(string baseId)
    {
        var jewellery = baseId is "ring" or "amulet";
        var kinds = _data.Objects.Where(k => k.Base == baseId
                                             && !k.Name.Contains("Ring of", StringComparison.Ordinal)
                                             && k.Id is not ("ring_of_power" or "mighty_hammer" or "massive_iron_crown")
                                             && (!jewellery || k.Has("INSTA_ART"))).ToList();
        if (kinds.Count == 0) kinds = _data.Objects.Where(k => k.Base == baseId).ToList();
        return _rng.Pick(kinds);
    }

    /// <summary>Angband artifact_prep: the kind's own stats, plus starting bonuses for its type.</summary>
    private Draft Prepare(ObjectKindDef kind)
    {
        var draft = Draft.FromKind(kind, _data);
        var group = Group(draft.Base);
        if (group is "melee" or "bow")
        {
            draft.ToHit += HitStart / 2 + _rng.RandInt0(HitStart);
            draft.ToDam += DamStart / 2 + _rng.RandInt0(DamStart);
        }
        else if (group is not ("other"))
            draft.ToAc += AcStart / 2 + _rng.RandInt0(AcStart);
        if (draft.Base.Id == "light" && kind.Has("INSTA_ART")) draft.Mods[ItemModifiers.Light] = 3;
        return draft;
    }

    /// <summary>Angband try_supercharge: now and then huge dice, extra blows, big speed or big armour.</summary>
    private void TrySupercharge(Draft draft, int power)
    {
        var n = _standard.Count;
        bool Chance(string what) => _rng.RandInt0(n) < _superCounts.GetValueOrDefault(what);
        if (draft.Base.IsWeapon)
        {
            if (Chance("melee_dice")) draft.Damage = draft.Damage with { Count = draft.Damage.Count + 3 + _rng.RandInt0(4) };
            else if (Chance("melee_blows")) draft.Mods[ItemModifiers.Blows] = 2;
        }
        if (Chance("speed") || draft.Base.Id == "boots" && Chance("boot_speed"))
        {
            var speed = 5 + _rng.RandInt0(6);
            if (_rng.OneIn(2)) speed += _rng.RandInt1(3);
            if (_rng.OneIn(6)) speed += 1 + _rng.RandInt1(6);
            draft.Mods[ItemModifiers.Speed] = speed;
        }
        if (Group(draft.Base) != "bow" && Chance("ac"))
        {
            draft.ToAc += 19 + _rng.RandInt1(11);
            if (_rng.OneIn(2)) draft.ToAc += _rng.RandInt1(10);
            if (_rng.OneIn(6)) draft.ToAc += _rng.RandInt1(20);
        }
        _ = power;
    }

    // --- Abilities --------------------------------------------------------------------------------

    private static readonly string[] Stats = ["str", "int", "wis", "dex", "con"];
    private static readonly string[] LowResists = ["acid", "elec", "fire", "cold"];
    private static readonly string[] HighResists = ["pois", "fear", "light", "dark", "blind", "conf", "sound", "shards", "nexus", "nether", "chaos", "disen"];

    /// <summary>The artifact groups Angband counts abilities by.</summary>
    private static string Group(ObjectBaseDef b) => b.Id switch
    {
        "sword" or "hafted" or "polearm" or "digger" => "melee",
        "sling" or "bow" or "crossbow" => "bow",
        "boots" => "boots", "gloves" => "gloves", "helm" or "crown" => "helm", "shield" => "shield", "cloak" => "cloak",
        "soft_armour" or "hard_armour" or "dragon_armour" => "armour",
        _ => "other",
    };

    /// <summary>The abilities an artifact has, as Angband's count_abilities sees them.</summary>
    private static IEnumerable<string> Abilities(Draft d)
    {
        var group = Group(d.Base);
        var weapon = group is "melee" or "bow";
        for (var i = 0; i < Math.Max(0, (d.ToHit - d.Kind.ToHit - (weapon ? HitStart : 0)) / HitIncrement); i++) yield return "to_hit";
        for (var i = 0; i < Math.Max(0, (d.ToDam - d.Kind.ToDam - (weapon ? DamStart : 0)) / DamIncrement); i++) yield return "to_dam";
        for (var i = 0; i < Math.Max(0, (d.ToAc - d.Kind.ToAc - (weapon || group == "other" ? 0 : AcStart)) / AcIncrement); i++) yield return "to_ac";
        if (d.Damage.Count > d.Kind.Damage.Count) yield return "dice";
        foreach (var (mod, value) in d.Mods.Where(kv => kv.Value > 0))
            yield return mod switch
            {
                "str" or "int" or "wis" or "dex" or "con" => "stat",
                _ => mod,
            };
        foreach (var _ in d.Slays) yield return "slay";
        foreach (var _ in d.Brands) yield return "brand";
        foreach (var r in d.Resists)
            yield return r switch
            {
                "acid" or "elec" or "fire" or "cold" => "low_resist",
                "free_act" or "see_invis" => r,
                _ when r.StartsWith("sust_") => "sustain",
                _ => "high_resist",
            };
        foreach (var f in d.Flags.Where(ItemFlags.Abilities.Contains)) yield return f.ToLowerInvariant();
        if (d.Flags.Contains("BLESSED")) yield return "blessed";
        if (d.Activation is not null) yield return "activation";
    }

    /// <summary>
    /// Angband build_freq_table: how likely each ability is for this item — its group's frequencies
    /// in the standard set, plus a share of everyone's, only for abilities that suit it.
    /// </summary>
    private Dictionary<string, int> FrequencyTable(Draft d)
    {
        var group = Group(d.Base);
        string[] suitable = group switch
        {
            "melee" => ["to_hit", "to_dam", "to_ac", "dice", "blows", "slay", "brand", "blessed", "tunnel"],
            "bow" => ["to_hit", "to_dam", "shots"],
            _ => ["to_ac", "to_hit", "to_dam"],
        };
        string[] general =
        [
            "stat", "sustain", "stealth", "speed", "infra", "search", "light", "low_resist", "high_resist", "free_act",
            "see_invis", "telepathy", "hold_life", "regen", "slow_digest", "activation", "tunnel",
        ];
        var mine = _freqByGroup.GetValueOrDefault(group) ?? new Dictionary<string, int>();
        var table = new Dictionary<string, int>();
        foreach (var ability in suitable.Concat(general).Distinct())
        {
            if (ability == "light" && d.Base.Id == "light") continue;
            table[ability] = 3 * mine.GetValueOrDefault(ability) + _freqAll.GetValueOrDefault(ability) + 1;
        }
        return table;
    }

    /// <summary>Angband add_ability / add_ability_aux: one ability, chosen by the frequencies.</summary>
    private void AddAbility(Draft d, int targetPower, Dictionary<string, int> freq)
    {
        var abilities = freq.Keys.Order(StringComparer.Ordinal).ToList();
        var ability = _rng.PickWeighted(abilities, a => freq[a])!;
        switch (ability)
        {
            case "to_hit": AddBonus(ref d.ToHit, HitIncrement, 16, 26); break;
            case "to_dam": AddBonus(ref d.ToDam, DamIncrement, 16, 26); break;
            case "to_ac": AddBonus(ref d.ToAc, AcIncrement, 26, 36); break;
            case "dice": d.Damage = d.Damage with { Count = d.Damage.Count + _rng.RandInt1(2) }; break;
            case "blows": AddMod(d, ItemModifiers.Blows, powerful: true); break;
            case "shots": AddShots(d); break;
            case "slay": AddSlay(d); break;
            case "brand": AddBrand(d); break;
            case "blessed": d.Flags.Add("BLESSED"); break;
            case "stat": AddMod(d, _rng.Pick(Stats)); break;
            case "sustain":
                var unsustained = Stats.Where(s => !d.Resists.Contains("sust_" + s)).ToList();
                if (unsustained.Count > 0) d.Resists.Add("sust_" + _rng.Pick(unsustained));
                break;
            case "stealth" or "speed" or "infra" or "search" or "tunnel": AddMod(d, ability); break;
            case "light": if (d.Mod(ItemModifiers.Light) <= 0) d.Mods[ItemModifiers.Light] = 1; break;
            case "low_resist":
                var missing = LowResists.Where(r => !d.Resists.Contains(r)).ToList();
                if (missing.Count > 0) d.Resists.Add(_rng.Pick(missing));
                break;
            case "high_resist":
                var high = HighResists.Where(r => !d.Resists.Contains(r)).ToList();
                if (high.Count > 0) d.Resists.Add(_rng.Pick(high));
                break;
            case "free_act" or "see_invis": d.Resists.Add(ability); break;
            case "telepathy": d.Flags.Add(ItemFlags.Telepathy); break;
            case "hold_life": d.Flags.Add(ItemFlags.HoldLife); break;
            case "regen": d.Flags.Add(ItemFlags.Regen); break;
            case "slow_digest": d.Flags.Add(ItemFlags.SlowDigest); break;
            case "activation": if (d.Activation is null) AddActivation(d, targetPower); break;
        }
    }

    /// <summary>Angband add_to_hit/dam/AC: 1 + d(2 × increment), less likely past the high marks.</summary>
    private void AddBonus(ref int value, int increment, int high, int veryHigh)
    {
        if (value > veryHigh && !_rng.OneIn(6)) return;
        if (value > high && !_rng.OneIn(2)) return;
        value += 1 + _rng.RandInt0(2 * increment);
    }

    /// <summary>Angband add_mod: new modifiers average 3 (at most 6, except speed); blows are rarer.</summary>
    private void AddMod(Draft d, string mod, bool powerful = false)
    {
        var current = d.Mod(mod);
        if (current < 0)
        {
            if (_rng.OneIn(2)) d.Mods[mod] = current - 1;
            return;
        }
        if (powerful)
        {
            if (current == 0) d.Mods[mod] = _rng.RandInt1(2);
            else if (_rng.OneIn(20 * current)) d.Mods[mod] = current + 1;
            return;
        }
        if (mod != ItemModifiers.Speed && current >= 6) return;
        var value = current == 0 ? _rng.RandInt0(3) + _rng.RandInt1(3) : current + _rng.RandInt1(2);
        d.Mods[mod] = mod == ItemModifiers.Speed ? value : Math.Min(6, value);
    }

    /// <summary>Extra shots, in tenths as Angband counts them.</summary>
    private void AddShots(Draft d) => d.Mods[ItemModifiers.Shots] = Math.Min(20, d.Mod(ItemModifiers.Shots) + 2 + _rng.RandInt0(4));

    private void AddSlay(Draft d)
    {
        var slays = _data.Artifacts.Concat<object>(_data.Egos).SelectMany(x => x switch
        {
            ArtifactDef a => a.Slays,
            EgoItemDef e => e.Slays,
            _ => [],
        }).GroupBy(s => (s.MonsterFlag, s.Multiplier)).Select(g => g.First()).OrderBy(s => s.MonsterFlag).ThenBy(s => s.Multiplier).ToList();
        var options = slays.Where(s => d.Slays.All(t => t.MonsterFlag != s.MonsterFlag)).ToList();
        if (options.Count == 0) return;
        var slay = _rng.Pick(options);
        d.Slays.Add(slay);
        // Angband: often another if the first is weak.
        if (_rng.RandInt0(4) != 0 && ArtifactPower.SlayPower(slay) < 105) AddSlay(d);
    }

    private void AddBrand(Draft d)
    {
        if (d.Brands.Count > 0 && _rng.RandInt0(4) != 0) return;
        var brands = _data.Artifacts.SelectMany(a => a.Brands).Concat(_data.Egos.SelectMany(e => e.Brands))
            .GroupBy(b => (b.Element, b.Multiplier)).Select(g => g.First()).OrderBy(b => b.Element).ThenBy(b => b.Multiplier).ToList();
        var options = brands.Where(b => d.Brands.All(x => x.Element != b.Element)).ToList();
        if (options.Count == 0) return;
        var brand = _rng.Pick(options);
        d.Brands.Add(brand);
        // Angband: frequently the matching resistance too.
        if (_rng.RandInt0(4) != 0 && LowResists.Contains(brand.Element)) d.Resists.Add(brand.Element);
    }

    /// <summary>
    /// Angband add_activation: one of the standard set's activations whose artifact's power is roughly
    /// in proportion to this one's.
    /// </summary>
    private void AddActivation(Draft d, int targetPower)
    {
        var withActivations = _standard.Where(a => a.Activation is not null).ToList();
        for (var tries = 0; tries < MaxTries && withActivations.Count > 0; tries++)
        {
            var source = _rng.Pick(withActivations);
            var p = ArtifactPower.Of(Draft.FromArtifact(source, _data), _data);
            if (p * 2 < targetPower || p > targetPower * 2) continue;
            (d.Activation, d.ActivationText, d.Recharge, d.ActivationPower) = (source.Activation, source.ActivationText, source.Recharge, source.ActivationPower);
            return;
        }
    }

    /// <summary>Angband make_bad: some bonuses turn negative, and a curse or two.</summary>
    private void MakeBad(Draft d)
    {
        foreach (var mod in d.Mods.Keys.ToList())
            if (d.Mods[mod] > 0 && _rng.OneIn(2)) d.Mods[mod] = -d.Mods[mod];
        if (d.ToAc > 0 && _rng.OneIn(2)) d.ToAc = -d.ToAc;
        if (d.ToHit > 0 && _rng.OneIn(2)) d.ToHit = -d.ToHit;
        if (d.ToDam > 0 && _rng.OneIn(4)) d.ToDam = -d.ToDam;
        if (d.Flags.Contains("BLESSED")) return;
        var curses = _data.Curses.Select(c => c.Id).Order(StringComparer.Ordinal).ToList();
        for (var i = _rng.RandInt1(2); i > 0 && curses.Count > 0; i--)
        {
            var curse = _rng.Pick(curses);
            if (!d.Curses.Contains(curse)) d.Curses.Add(curse);
        }
    }

    // --- Names ------------------------------------------------------------------------------------

    /// <summary>
    /// Angband artifact_gen_name: a word from W. Sheldon Simms' generator (randname.c) — letter
    /// by letter, each from those that follow the previous two in Tolkien's names — as 'Name' or
    /// "of Name".
    /// </summary>
    private string MakeName()
    {
        var word = Capitalise(RandomName.Make(_rng, _data.NameWords, MinNameLength, MaxNameLength));
        return _rng.OneIn(3) ? $"'{word}'" : $"of {word}";
    }

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // --- The artifact being designed ----------------------------------------------------------------

    /// <summary>A mutable artifact under construction.</summary>
    internal sealed class Draft
    {
        public required ObjectKindDef Kind;
        public required ObjectBaseDef Base;
        public int ToHit, ToDam, ToAc, Armour, Weight;
        public Dice Damage;
        public Dictionary<string, int> Mods = new();
        public List<SlayDef> Slays = [];
        public List<BrandDef> Brands = [];
        public HashSet<string> Resists = new(StringComparer.Ordinal);
        public HashSet<string> Flags = new(StringComparer.Ordinal);
        public List<string> Curses = [];
        public string? Activation, ActivationText, Recharge;
        public int ActivationPower;

        public int Mod(string mod) => Mods.GetValueOrDefault(mod);

        /// <summary>What <see cref="ObjectPower"/> weighs: an artifact, with its activation's power.</summary>
        public PowerProfile Profile() => new()
        {
            Kind = Kind, Base = Base, ToHit = ToHit, ToDam = ToDam, ToAc = ToAc, Armour = Armour, Weight = Weight, Damage = Damage,
            Modifiers = Mods, Slays = Slays, Brands = Brands, Resists = Resists, Flags = Flags, Curses = Curses,
            IsArtifact = true, ActivationPower = Activation is not null ? ActivationPower : null,
        };

        public static Draft FromKind(ObjectKindDef kind, GameData data) => new()
        {
            Kind = kind, Base = data.ObjectBase(kind.Base)!, ToHit = kind.ToHit, ToDam = kind.ToDam, ToAc = kind.ToAc,
            Armour = kind.Armour, Weight = kind.Weight, Damage = kind.Damage,
            Mods = kind.Modifiers.ToDictionary(kv => kv.Key, kv => kv.Value),
            Slays = [.. kind.Slays], Brands = [.. kind.Brands],
            Resists = new HashSet<string>(kind.Resists, StringComparer.Ordinal),
            Flags = new HashSet<string>(kind.Flags.Where(f => f != "INSTA_ART"), StringComparer.Ordinal),
        };

        public static Draft FromArtifact(ArtifactDef art, GameData data)
        {
            var kind = data.Object(art.Kind)!;
            var d = FromKind(kind, data);
            (d.ToHit, d.ToDam, d.ToAc) = (art.ToHit, art.ToDam, art.ToAc);
            if (art.Armour > 0) d.Armour = art.Armour;
            if (art.Damage is { } dice) d.Damage = dice;
            foreach (var (mod, value) in art.Modifiers) d.Mods[mod] = value;
            d.Slays.AddRange(art.Slays);
            d.Brands.AddRange(art.Brands);
            d.Resists.UnionWith(art.Resists);
            d.Flags.UnionWith(art.Flags);
            d.Curses.AddRange(art.Curses);
            (d.Activation, d.ActivationText, d.Recharge, d.ActivationPower) = (art.Activation, art.ActivationText, art.Recharge, art.ActivationPower);
            return d;
        }

        public Draft Clone() => new()
        {
            Kind = Kind, Base = Base, ToHit = ToHit, ToDam = ToDam, ToAc = ToAc, Armour = Armour, Weight = Weight, Damage = Damage,
            Mods = new Dictionary<string, int>(Mods), Slays = [.. Slays], Brands = [.. Brands],
            Resists = new HashSet<string>(Resists, StringComparer.Ordinal), Flags = new HashSet<string>(Flags, StringComparer.Ordinal),
            Curses = [.. Curses], Activation = Activation, ActivationText = ActivationText, Recharge = Recharge,
            ActivationPower = ActivationPower,
        };

        public ArtifactDef ToArtifact(string id, string name, int level, int rarity, string description) => new()
        {
            Id = id, Name = name, Kind = Kind.Id, Level = level, Rarity = rarity,
            ToHit = ToHit, ToDam = ToDam, ToAc = ToAc, Armour = Armour == Kind.Armour ? 0 : Armour,
            Damage = Damage == Kind.Damage ? null : Damage,
            Modifiers = Mods.Where(kv => kv.Value != 0 && Kind.Modifiers.GetValueOrDefault(kv.Key) != kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
            Slays = [.. Slays.Except(Kind.Slays)], Brands = [.. Brands.Except(Kind.Brands)],
            Resists = [.. Resists.Except(Kind.Resists).Order(StringComparer.Ordinal)],
            Flags = [.. Flags.Except(Kind.Flags).Order(StringComparer.Ordinal)],
            Curses = [.. Curses], Activation = Activation, ActivationText = ActivationText, Recharge = Recharge,
            ActivationPower = ActivationPower, Description = description,
        };
    }
}

/// <summary>
/// Angband object_power (obj-power.c) for artifacts, standard or being designed (see <see cref="ObjectPower"/>).
/// </summary>
public static class ArtifactPower
{
    public static int SlayPower(SlayDef slay) => ObjectPower.SlayPower(slay);

    public static int Of(ArtifactDef art, GameData data) => Of(RandartGenerator.Draft.FromArtifact(art, data), data);

    internal static int Of(RandartGenerator.Draft d, GameData data) => ObjectPower.Of(d.Profile(), data);
}

/// <summary>
/// W. Sheldon Simms' random name generator (Angband randname.c): learns which letter follows each
/// pair of letters in a word list, then builds words letter by letter.
/// </summary>
public static class RandomName
{
    private const int WordMark = 26, Total = 27;

    public static string Make(GameRandom rng, IReadOnlyList<string> words, int min, int max)
    {
        if (words.Count == 0) return "Nameless";
        var probs = new int[WordMark + 1, WordMark + 1, Total + 1];
        foreach (var word in words)
        {
            int prev = WordMark, cur = WordMark;
            foreach (var ch in word.ToLowerInvariant())
            {
                if (ch is < 'a' or > 'z') continue;
                var next = ch - 'a';
                probs[prev, cur, next]++;
                probs[prev, cur, Total]++;
                (prev, cur) = (cur, next);
            }
            probs[prev, cur, WordMark]++;
            probs[prev, cur, Total]++;
        }

        while (true)
        {
            var letters = new System.Text.StringBuilder();
            int prev = WordMark, cur = WordMark, tries = 0;
            var vowel = false;
            while (tries < 10 && letters.Length <= max)
            {
                var total = probs[prev, cur, Total];
                if (total == 0) break;
                var r = rng.RandInt0(total);
                var next = 0;
                while (r >= probs[prev, cur, next])
                {
                    r -= probs[prev, cur, next];
                    next++;
                }
                if (next == WordMark)
                {
                    if (letters.Length >= min && vowel) return letters.ToString();
                    tries++;
                    continue;
                }
                var c = (char)('a' + next);
                vowel |= c is 'a' or 'e' or 'i' or 'o' or 'u';
                letters.Append(c);
                (prev, cur) = (cur, next);
            }
        }
    }
}
