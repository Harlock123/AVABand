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
        foreach (var c in kind.Curses) CursePowers[c] = kind.CursePowers.GetValueOrDefault(c, DefaultCursePower);
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

    /// <summary>
    /// AVABand's quests: which quest this belongs to (and which piece of it — "broken_blade",
    /// "thief:page:2"), for quest items and the items they lead to. Saved.
    /// </summary>
    public string? QuestTag { get; set; }

    /// <summary>A quest item (QUEST_ITEM): can't be dropped, thrown, sold, ignored, stolen or burnt.</summary>
    public bool IsQuestItem => Kind.Has("QUEST_ITEM");
    /// <summary>Power of a curse when none is recorded (older saves, data without one).</summary>
    public const int DefaultCursePower = 20;

    /// <summary>
    /// The power of each curse (Angband curse_data.power): how hard it is to remove; 100 or more
    /// is permanent.
    /// </summary>
    public Dictionary<string, int> CursePowers { get; } = new(StringComparer.Ordinal);

    /// <summary>Player turns until each curse next acts (Angband curse_data.timeout).</summary>
    public Dictionary<string, int> CurseTimeouts { get; } = new(StringComparer.Ordinal);

    public int CursePower(string curse) => CursePowers.GetValueOrDefault(curse, DefaultCursePower);

    /// <summary>A curse broken or gone: its power and timer go with it.</summary>
    public void RemoveCurse(string curse)
    {
        Curses.Remove(curse);
        CursePowers.Remove(curse);
        CurseTimeouts.Remove(curse);
    }

    public bool IsCursed => Curses.Count > 0;
    public bool IsWearable => Base.IsWearable;
    public bool IsAmmo => Base.IsAmmo;

    /// <summary>Made for throwing (Angband THROWING): more damage thrown, and a place in the quiver.</summary>
    public bool IsThrowing => Kind.Has("THROWING") || Flags.Contains("THROWING");

    /// <summary>Bursts when thrown (Angband EXPLODE, flasks of oil): three times the damage, and it always breaks.</summary>
    public bool Explodes => Kind.Has("EXPLODE");

    /// <summary>A launcher's multiplier: its own, plus any extra might (Angband pval + MIGHT).</summary>
    public int Multiplier => Kind.Multiplier + Modifier(Definitions.ItemModifiers.Might);

    /// <summary>A light that burns fuel: one with fuel to burn, unless it needs none (Angband NO_FUEL, Everburning).</summary>
    public bool UsesFuel => Kind.Fuel > 0 && !Flags.Contains("NO_FUEL");

    /// <summary>Fuel for a lantern (Angband tval_is_fuel): a flask.</summary>
    public bool IsFuel => Base.Id == "flask";

    /// <summary>
    /// How many missiles it counts as in the quiver: ammunition one each, throwing weapons five
    /// (Angband thrown_quiver_mult).
    /// </summary>
    public int QuiverWeight => (IsAmmo ? 1 : Inventory.ThrownQuiverMultiplier) * Number;
    /// <summary>Flavoured (unknown until learned) — except special artifact-only kinds, which never are.</summary>
    public bool IsFlavored => Base.Flavor is not null && !Kind.IsSpecialArtifactKind;
    /// <summary>Weight of one, in tenths of a pound.</summary>
    public int Weight => Artifact?.Weight ?? Kind.Weight;

    /// <summary>Elements it makes you immune to: an artifact's (Angband RES_x[3]).</summary>
    public IReadOnlyList<string> Immunities => Artifact?.Immunities ?? [];

    /// <summary>Stuck on once worn (Angband STICKY: the One Ring, the Iron Crown of Morgoth).</summary>
    public bool IsSticky => Flags.Contains("STICKY");
    public int TotalWeight => Weight * Number;

    public int Modifier(string mod) => Modifiers.GetValueOrDefault(mod);

    /// <summary>
    /// Every learnable property (4.2 "rune") this object has. Weapons and armour always carry
    /// their combat-bonus runes, since even +0 must be learned to be shown.
    /// </summary>
    /// <summary>AVABand's socketed bracers: how many gems they take.</summary>
    public int Sockets => Kind.Sockets;

    /// <summary>The gems set in it (their properties are merged into its own; see GameSession.Gems.cs).</summary>
    public List<Item> Gems { get; } = [];

    /// <summary>On a set gem: the resistances, flags and curses it brought that its host didn't have (taken away with it).</summary>
    public List<string> AddedResists { get; } = [];
    public List<string> AddedFlags { get; } = [];
    public List<string> AddedCurses { get; } = [];

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
        foreach (var resist in Resists.Concat(Immunities).Distinct()) yield return RuneIds.Resist(resist);
        foreach (var curse in Curses) yield return RuneIds.Curse(curse);
        foreach (var flag in Flags)
            if (ItemFlags.Abilities.Contains(flag)) yield return RuneIds.Flag(flag);
    }

    /// <summary>Whether two stacks may merge (Angband object_similar, simplified).</summary>
    /// <summary>
    /// Angband 4.2 object_stackable: the same kind with the same properties — enchantments, dice,
    /// modifiers, resistances, flags, curses and ego — and never artifacts or chests. Wands and
    /// staves stack whatever their charges (a stack shares them) and rods whatever their recharging;
    /// other things that are recharging an activation don't stack.
    /// </summary>
    public bool CanStackWith(Item other) =>
        other != this
        && other.Kind == Kind
        && Base.MaxStack > 1
        && Artifact is null && other.Artifact is null
        && Ego == other.Ego
        && ToHit == other.ToHit && ToDam == other.ToDam && ToAc == other.ToAc
        && Damage == other.Damage && Armour == other.Armour
        && Curses.SequenceEqual(other.Curses) && Curses.All(c => CursePower(c) == other.CursePower(c))
        && Flags.SetEquals(other.Flags) && ChestState == other.ChestState
        && Modifiers.Count == other.Modifiers.Count && Modifiers.All(m => other.Modifier(m.Key) == m.Value)
        && Resists.SetEquals(other.Resists)
        && (HasCharges || Charges == other.Charges)
        && (IsRod || (Timeout == 0 && other.Timeout == 0))
        && Fuel == other.Fuel
        // Angband object_similar: different inscriptions keep stacks apart; an uninscribed one may join.
        && (Note is null || other.Note is null || Note == other.Note)
        && Ignored == other.Ignored
        && QuestTag == other.QuestTag
        && Gems.Count == 0 && other.Gems.Count == 0;

    /// <summary>
    /// Whether an element can harm it in the pack (Angband EL_INFO_HATES without EL_INFO_IGNORE):
    /// its base hates the element and neither its kind nor its ego is proof against it. Artifacts
    /// are never harmed.
    /// </summary>
    public bool HarmedBy(string element) =>
        Artifact is null && Base.Hates.Contains(element) && !Kind.Ignore.Contains(element) && Ego?.Ignore.Contains(element) != true;

    /// <summary>Wands and staves: a stack's charges are shared (Angband tval_can_have_charges).</summary>
    public bool HasCharges => Base.Id is "wand" or "staff";

    /// <summary>Rods: a stack's recharge time is shared (Angband tval_can_have_timeout).</summary>
    public bool IsRod => Base.Id == "rod";

    /// <summary>The average turns one rod of this kind takes to recharge (Angband randcalc(time, AVERAGE)).</summary>
    public int RechargeTime => Kind.Recharge is { } r ? Math.Max(1, RandomValue.Parse(r).Average) : 0;

    /// <summary>
    /// Angband number_charging: how many rods of the stack are still recharging (the shared timeout
    /// counts one rod per recharge time); a single rod or activatable item is just charging or not.
    /// </summary>
    public int NumberCharging
    {
        get
        {
            if (Timeout <= 0) return 0;
            if (!IsRod || RechargeTime <= 0) return 1;
            return Math.Min(Number, (Timeout + RechargeTime - 1) / RechargeTime);
        }
    }

    /// <summary>Whether a rod of the stack is ready to zap (Angband: some aren't charging).</summary>
    public bool RodReady => NumberCharging < Number;

    /// <summary>
    /// Angband recharge_timeout: every rod still charging recharges at once. Returns true when at
    /// least one finished.
    /// </summary>
    public bool Recharge()
    {
        var before = NumberCharging;
        if (before == 0) return false;
        Timeout -= IsRod ? Math.Min(before, Timeout) : 1;
        return NumberCharging < before;
    }

    /// <summary>Angband object_absorb_merge: joins another stack to this one, pooling charges and recharge time.</summary>
    public void Absorb(Item other)
    {
        Number += other.Number;
        Note ??= other.Note;
        if (IsRod) Timeout += other.Timeout;
        if (HasCharges) Charges = (int)Math.Min(MaxCharges, (long)Charges + other.Charges);
    }

    /// <summary>Angband MAX_PVAL.</summary>
    public const int MaxCharges = 32767;

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
        // Angband distribute_charges: the charges go in proportion; the part taken off gets up to
        // its share of the recharge time.
        if (HasCharges)
        {
            copy.Charges = Charges * count / Number;
            Charges -= copy.Charges;
        }
        if (IsRod)
        {
            copy.Timeout = Math.Min(Timeout, RechargeTime * count);
            Timeout -= copy.Timeout;
        }
        Number -= count;
        return copy;
    }

    public Item Clone(long serial, int number)
    {
        var copy = new Item(serial, Kind, Base, number)
        {
            Damage = Damage, Armour = Armour, ToHit = ToHit, ToDam = ToDam, ToAc = ToAc, Fuel = Fuel, Charges = Charges, Timeout = Timeout, ChestState = ChestState,
            GoldValue = GoldValue, Ego = Ego, Artifact = Artifact, OriginDepth = OriginDepth, Note = Note,
            Ignored = Ignored, Assessed = Assessed, QuestTag = QuestTag,
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
        copy.CursePowers.Clear();
        foreach (var (k, v) in CursePowers) copy.CursePowers[k] = v;
        copy.CurseTimeouts.Clear();
        foreach (var (k, v) in CurseTimeouts) copy.CurseTimeouts[k] = v;
        copy.Flags.Clear();
        foreach (var f in Flags) copy.Flags.Add(f);
        copy.Gems.AddRange(Gems.Select(g => g.Clone(g.Serial, g.Number)));
        copy.AddedResists.AddRange(AddedResists);
        copy.AddedFlags.AddRange(AddedFlags);
        copy.AddedCurses.AddRange(AddedCurses);
        return copy;
    }

    public override string ToString() => $"{Number}x{Kind.Id}#{Serial}";
}
