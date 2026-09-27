using Angband.Core.Definitions;
using Angband.Core.Randomness;

namespace Angband.Core.Items;

/// <summary>A real object (or stack of identical objects) in the game.</summary>
public sealed class Item
{
    public Item(long serial, ObjectKindDef kind, ObjectBaseDef baseDef, int number = 1)
    {
        Serial = serial;
        Kind = kind;
        Base = baseDef;
        Number = Math.Max(1, number);
        Damage = kind.Damage;
        Armour = kind.Armour;
        ToHit = kind.ToHit;
        ToDam = kind.ToDam;
        ToAc = kind.ToAc;
        Fuel = kind.Fuel;
        foreach (var (mod, value) in kind.Modifiers) Modifiers[mod] = value;
        foreach (var resist in kind.Resists) Resists.Add(resist);
        Slays.AddRange(kind.Slays);
        Brands.AddRange(kind.Brands);
        Curses.AddRange(kind.Curses);
        foreach (var flag in kind.Flags) Flags.Add(flag);
    }

    /// <summary>Unique id within a game (stable identity for commands and UI).</summary>
    public long Serial { get; }
    public ObjectKindDef Kind { get; }
    public ObjectBaseDef Base { get; }
    public int Number { get; set; }

    public Dice Damage { get; set; }
    public int Armour { get; set; }
    public int ToHit { get; set; }
    public int ToDam { get; set; }
    public int ToAc { get; set; }
    /// <summary>Remaining light fuel in player turns (0 for lights that need none).</summary>
    public int Fuel { get; set; }
    /// <summary>
    /// A chest's traps (Angband pval): 0 empty, 1 locked, above 1 a set of trap bits (and locked),
    /// below 0 unlocked/disarmed but still full.
    /// </summary>
    public int ChestState { get; set; }
    public bool IsChest => Base.Id == "chest";

    /// <summary>Charges left in a wand or staff.</summary>
    public int Charges { get; set; }
    /// <summary>Game turns (in tenths: world ticks) until a rod can be zapped again.</summary>
    public int Timeout { get; set; }
    /// <summary>For gold: its value.</summary>
    public int GoldValue { get; set; }

    public EgoItemDef? Ego { get; set; }
    public ArtifactDef? Artifact { get; set; }
    public Dictionary<string, int> Modifiers { get; } = new(StringComparer.Ordinal);
    public List<SlayDef> Slays { get; } = [];
    public List<BrandDef> Brands { get; } = [];
    public SortedSet<string> Resists { get; } = new(StringComparer.Ordinal);
    public List<string> Curses { get; } = [];
    /// <summary>Kind, ego and artifact flags (abilities such as <see cref="ItemFlags.Regen"/>, plus markers).</summary>
    public SortedSet<string> Flags { get; } = new(StringComparer.Ordinal);

    /// <summary>Depth it was found at, for character dumps.</summary>
    public int OriginDepth { get; set; }

    public bool IsGold => Base.Id == "gold";

    /// <summary>The effect this item has when activated (its artifact's, else its kind's), if any.</summary>
    public string? Activation => Artifact?.Activation ?? Kind.Activation;
    public string? ActivationText => Artifact?.Activation is not null ? Artifact.ActivationText : Kind.ActivationText;
    public bool CanActivate => !string.IsNullOrEmpty(Activation);
    public bool IsArtifact => Artifact is not null;
    public bool IsCursed => Curses.Count > 0;
    public bool IsWearable => Base.IsWearable;
    public bool IsAmmo => Base.IsAmmo;

    /// <summary>Made for throwing (Angband THROWING): more damage thrown, and a place in the quiver.</summary>
    public bool IsThrowing => Kind.Has("THROWING") || Flags.Contains("THROWING");

    /// <summary>
    /// How many missiles it counts as in the quiver: ammunition one each, throwing weapons five
    /// (Angband thrown_quiver_mult).
    /// </summary>
    public int QuiverWeight => (IsAmmo ? 1 : Inventory.ThrownQuiverMultiplier) * Number;
    /// <summary>Flavoured (unknown until learned) — except special artifact-only kinds, which never are.</summary>
    public bool IsFlavored => Base.Flavor is not null && !Kind.IsSpecialArtifactKind;
    /// <summary>Weight of one, in tenths of a pound.</summary>
    public int Weight => Kind.Weight;
    public int TotalWeight => Weight * Number;

    public int Modifier(string mod) => Modifiers.GetValueOrDefault(mod);

    /// <summary>
    /// Every learnable property (4.2 "rune") this object has. Weapons and armour always carry
    /// their combat-bonus runes, since even +0 must be learned to be shown.
    /// </summary>
    public IEnumerable<string> Runes()
    {
        var weaponLike = Base.IsWeapon || Base.Slot == EquipSlot.Bow || Base.IsAmmo;
        if (weaponLike || ToHit != 0) yield return RuneIds.ToHit;
        if (weaponLike || ToDam != 0) yield return RuneIds.ToDam;
        if ((Base.IsWearable && !weaponLike && Base.Slot is not (EquipSlot.Light or EquipSlot.Ring or EquipSlot.Amulet)) || ToAc != 0)
            yield return RuneIds.ToAc;
        foreach (var (mod, value) in Modifiers)
            if (value != 0) yield return RuneIds.Modifier(mod);
        foreach (var slay in Slays) yield return RuneIds.Slay(slay.MonsterFlag);
        foreach (var brand in Brands) yield return RuneIds.Brand(brand.Element);
        foreach (var resist in Resists) yield return RuneIds.Resist(resist);
        foreach (var curse in Curses) yield return RuneIds.Curse(curse);
        foreach (var flag in Flags)
            if (ItemFlags.Abilities.Contains(flag)) yield return RuneIds.Flag(flag);
    }

    /// <summary>Whether two stacks may merge (Angband object_similar, simplified).</summary>
    public bool CanStackWith(Item other) =>
        other != this
        && other.Kind == Kind
        && Base.MaxStack > 1
        && Ego is null && other.Ego is null && Artifact is null && other.Artifact is null
        && ToHit == other.ToHit && ToDam == other.ToDam && ToAc == other.ToAc
        && Damage == other.Damage && Armour == other.Armour
        && Curses.SequenceEqual(other.Curses)
        && Flags.SetEquals(other.Flags) && Charges == other.Charges && Timeout == other.Timeout && ChestState == other.ChestState
        && Fuel == other.Fuel
        // Angband object_similar: different inscriptions keep stacks apart; an uninscribed one may join.
        && (Note is null || other.Note is null || Note == other.Note)
        && Ignored == other.Ignored;

    /// <summary>The player's inscription (Angband's note), shown in braces; see <see cref="Inscription"/>.</summary>
    public string? Note { get; set; }

    /// <summary>Ignored by hand (Angband OBJ_NOTICE_IGNORE, "This item only").</summary>
    public bool Ignored { get; set; }

    /// <summary>
    /// The player has handled it — walked over, carried or bought it — so it's known not to be an
    /// artifact even with unknown runes (Angband OBJ_NOTICE_ASSESSED).
    /// </summary>
    public bool Assessed { get; set; }

    /// <summary>Takes <paramref name="count"/> off this stack as a new item with the same properties.</summary>
    public Item Split(long serial, int count)
    {
        if (count <= 0 || count >= Number) throw new ArgumentOutOfRangeException(nameof(count));
        var copy = Clone(serial, count);
        Number -= count;
        return copy;
    }

    public Item Clone(long serial, int number)
    {
        var copy = new Item(serial, Kind, Base, number)
        {
            Damage = Damage, Armour = Armour, ToHit = ToHit, ToDam = ToDam, ToAc = ToAc, Fuel = Fuel, Charges = Charges, Timeout = Timeout, ChestState = ChestState,
            GoldValue = GoldValue, Ego = Ego, Artifact = Artifact, OriginDepth = OriginDepth, Note = Note,
            Ignored = Ignored, Assessed = Assessed,
        };
        copy.Modifiers.Clear();
        foreach (var (k, v) in Modifiers) copy.Modifiers[k] = v;
        copy.Slays.Clear();
        copy.Slays.AddRange(Slays);
        copy.Brands.Clear();
        copy.Brands.AddRange(Brands);
        copy.Resists.Clear();
        foreach (var r in Resists) copy.Resists.Add(r);
        copy.Curses.Clear();
        copy.Curses.AddRange(Curses);
        copy.Flags.Clear();
        foreach (var f in Flags) copy.Flags.Add(f);
        return copy;
    }

    public override string ToString() => $"{Number}x{Kind.Id}#{Serial}";
}
