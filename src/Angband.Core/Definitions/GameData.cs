namespace Angband.Core.Definitions;

/// <summary>Everything loaded from the data files. Immutable once constructed.</summary>
public sealed class GameData
{
    public GameData(
        TerrainRegistry terrain,
        IReadOnlyList<TrapDef> traps,
        IReadOnlyList<DungeonProfileDef> profiles,
        TownDef town,
        IReadOnlyList<ShopDef> shops,
        IReadOnlyDictionary<string, string> colors,
        GameConstants constants)
    {
        Terrain = terrain;
        Traps = traps;
        for (var i = 0; i < traps.Count; i++) traps[i].Index = (ushort)(i + 1);
        Profiles = profiles;
        Town = town;
        Shops = shops;
        Colors = colors;
        Constants = constants;
    }

    public TerrainRegistry Terrain { get; }
    public IReadOnlyList<TrapDef> Traps { get; }
    public IReadOnlyList<DungeonProfileDef> Profiles { get; }
    /// <summary>Angband vault.txt: vaults and interesting rooms.</summary>
    public IReadOnlyList<VaultDef> Vaults { get; init; } = [];
    /// <summary>Angband room_template.txt.</summary>
    public IReadOnlyList<RoomTemplateDef> RoomTemplates { get; init; } = [];
    /// <summary>Angband pit.txt: themes for pits, nests, chambers, lairs and gauntlets.</summary>
    public IReadOnlyList<PitProfileDef> Pits { get; init; } = [];
    public TownDef Town { get; }
    public IReadOnlyList<ShopDef> Shops { get; }
    /// <summary>Colour name to <c>#RRGGBB</c>.</summary>
    public IReadOnlyDictionary<string, string> Colors { get; }
    public GameConstants Constants { get; }

    public IReadOnlyList<ElementDef> Elements { get; init; } = [];
    public IReadOnlyList<TimedEffectDef> TimedEffects { get; init; } = [];
    public IReadOnlyList<BlowMethodDef> BlowMethods { get; init; } = [];
    public IReadOnlyList<BlowEffectDef> BlowEffects { get; init; } = [];
    public IReadOnlyList<MonsterRaceDef> Monsters { get; init; } = [];
    public IReadOnlyList<MonsterSpellDef> MonsterSpells { get; init; } = [];
    public IReadOnlyList<RealmDef> Realms { get; init; } = [];
    public IReadOnlyList<ClassDef> Classes { get; init; } = [];
    public IReadOnlyList<RaceDef> Races { get; init; } = [];
    /// <summary>Race and class abilities as the birth screen names them (Angband player_property.txt).</summary>
    public IReadOnlyList<PlayerPropertyDef> PlayerProperties { get; init; } = [];
    /// <summary>What shopkeepers may tell you as you come in (Angband hints.txt).</summary>
    public IReadOnlyList<string> Hints { get; init; } = [];
    /// <summary>The background charts (Angband history.txt).</summary>
    public IReadOnlyList<HistoryChartDef> Histories { get; init; } = [];
    public IReadOnlyList<StoreDef> Stores { get; init; } = [];
    public StoreDef? Store(string id) => Stores.FirstOrDefault(s => s.Id == id);
    public RaceDef? Race(string id) => Races.FirstOrDefault(r => r.Id == id);
    public IReadOnlyList<SpellDef> Spells { get; init; } = [];
    public RealmDef? Realm(string id) => Realms.FirstOrDefault(r => r.Id == id);
    public ClassDef? Class(string id) => Classes.FirstOrDefault(c => c.Id == id);
    public SpellDef? Spell(string id) => Spells.FirstOrDefault(s => s.Id == id);
    public MonsterSpellDef? MonsterSpell(string id) => MonsterSpells.FirstOrDefault(s => s.Id == id);

    public IReadOnlyList<ObjectBaseDef> ObjectBases { get; init; } = [];
    public IReadOnlyList<ObjectKindDef> Objects { get; init; } = [];
    public IReadOnlyList<EgoItemDef> Egos { get; init; } = [];
    public IReadOnlyList<ArtifactDef> Artifacts { get; init; } = [];
    public IReadOnlyList<CurseDef> Curses { get; init; } = [];
    public IReadOnlyList<SummonDef> Summons { get; init; } = [];
    public IReadOnlyList<FlavorGroupDef> Flavors { get; init; } = [];

    /// <summary>Forms the player can take (Angband shape.txt).</summary>
    public IReadOnlyList<ShapeDef> Shapes { get; init; } = [];
    /// <summary>The kinds of monster and their symbols (Angband monster_base.txt), for identifying a symbol.</summary>
    public IReadOnlyList<MonsterBaseDef> MonsterBases { get; init; } = [];
    /// <summary>What object properties are worth (Angband object_property.txt).</summary>
    public IReadOnlyList<ObjectPropertyDef> ObjectProperties { get; init; } = [];
    /// <summary>Slays' and brands' worth (Angband slay.txt, brand.txt).</summary>
    public IReadOnlyList<SlayTypeDef> SlayTypes { get; init; } = [];
    public IReadOnlyList<BrandTypeDef> BrandTypes { get; init; } = [];

    private Dictionary<(string, string), ObjectPropertyDef>? _properties;

    /// <summary>The property of that type with AVABand's id (or 4.2.5's code), if there is one.</summary>
    public ObjectPropertyDef? ObjectProperty(string type, string id)
    {
        _properties ??= ObjectProperties.GroupBy(p => (p.Type, p.Id)).ToDictionary(g => g.Key, g => g.First());
        return _properties.GetValueOrDefault((type, id)) ?? _properties.GetValueOrDefault((type, id.ToUpperInvariant()));
    }

    public SlayTypeDef? SlayType(string flag, int multiplier) =>
        SlayTypes.FirstOrDefault(s => s.Flag == flag && s.Multiplier == multiplier);

    public BrandTypeDef? BrandType(string element, int multiplier) =>
        BrandTypes.FirstOrDefault(b => b.Element == element && b.Multiplier == multiplier);
    /// <summary>Pain messages by type (Angband pain.txt).</summary>
    public IReadOnlyList<PainDef> Pain { get; init; } = [];
    /// <summary>Words for random names (random artifacts).</summary>
    public IReadOnlyList<string> NameWords { get; init; } = [];
    public ShapeDef? Shape(string id) => Shapes.FirstOrDefault(s => s.Id == id);

    /// <summary>Quests, shallowest first (Angband quest.txt).</summary>
    public IReadOnlyList<QuestDef> Quests { get; init; } = [];
    /// <summary>AVABand's own quests (ava_quests.json): separate from Angband's, which decide the win.</summary>
    public IReadOnlyList<AvaQuestDef> AvaQuests { get; init; } = [];
    public AvaQuestDef? AvaQuest(string id) => AvaQuests.FirstOrDefault(q => q.Id == id);

    /// <summary>Chest traps; each gets its bit (1, 2, 4...) from its position in the list.</summary>
    public IReadOnlyList<ChestTrapDef> ChestTraps
    {
        get => _chestTraps;
        init
        {
            _chestTraps = value;
            for (var i = 0; i < value.Count; i++) value[i].Bit = 1 << i;
        }
    }
    private readonly IReadOnlyList<ChestTrapDef> _chestTraps = [];
    /// <summary>The player's starting kit: object kind ids with counts (until character creation).</summary>
    public IReadOnlyList<StartItemDef> StartingKit { get; init; } = [];

    public ObjectBaseDef? ObjectBase(string id) => ObjectBases.FirstOrDefault(b => b.Id == id);
    public ObjectKindDef? Object(string id) => Objects.FirstOrDefault(o => o.Id == id);
    public CurseDef? Curse(string id) => Curses.FirstOrDefault(c => c.Id == id);
    public SummonDef? Summon(string id) => Summons.FirstOrDefault(s => s.Id == id);

    public ElementDef? Element(string id) => Elements.FirstOrDefault(e => e.Id == id);
    public TimedEffectDef? Timed(string id) => TimedEffects.FirstOrDefault(t => t.Id == id);
    public BlowMethodDef? BlowMethod(string id) => BlowMethods.FirstOrDefault(m => m.Id == id);
    public BlowEffectDef? BlowEffect(string id) => BlowEffects.FirstOrDefault(e => e.Id == id);
    public MonsterRaceDef? Monster(string id) => Monsters.FirstOrDefault(m => m.Id == id);

    /// <summary>Looks up a trap by its 1-based square index.</summary>
    public TrapDef? TrapByIndex(ushort index) => index == 0 || index > Traps.Count ? null : Traps[index - 1];

    /// <summary>
    /// Angband pick_trap: a player trap allowed at <paramref name="depth"/>, weighted 100 / rarity —
    /// trap doors only where you could fall (<paramref name="trapDoors"/>).
    /// </summary>
    public TrapDef? PickTrap(Randomness.GameRandom rng, int depth, bool trapDoors)
    {
        if (depth <= 0) return null; // no traps in the town
        var eligible = Traps.Where(t => t.Weight > 0 && t.MinDepth <= depth && t.MaxDepth >= depth && (trapDoors || !t.IsTrapDoor)).ToList();
        return rng.PickWeighted(eligible, t => t.Weight);
    }
}

/// <summary>An item the player starts with.</summary>
public sealed class StartItemDef
{
    public required string Kind { get; init; }
    /// <summary>How many (Angband equip min); a number up to <see cref="CountMax"/> when that is more.</summary>
    public int Count { get; init; } = 1;
    /// <summary>The most (Angband equip max); none means exactly <see cref="Count"/>.</summary>
    public int? CountMax { get; init; }
    public bool Equip { get; init; }
    /// <summary>A birth option that leaves it out (Angband eopts: the Word of Recall with birth_no_recall).</summary>
    public string? UnlessOption { get; init; }
}

public sealed class GameConstants
{
    /// <summary>Deepest dungeon level (Angband: 127).</summary>
    public int MaxDepth { get; init; } = 127;
    /// <summary>Feet per dungeon level, for display.</summary>
    public int FeetPerLevel { get; init; } = 50;
    /// <summary>Angband "birth_connect_stairs": arrive on a staircase.</summary>
    public bool ConnectedStairs { get; init; } = true;
    /// <summary>How far the player can see, in grids (Angband: z_info->max_sight).</summary>
    public int MaxSight { get; init; } = 20;
    /// <summary>Game turns in a full day; the first half is daytime (Angband: z_info->day_length).</summary>
    public int DayLength { get; init; } = 10_000;
    /// <summary>Distinct items the pack holds (Angband pack_size); the quiver uses up pack slots.</summary>
    public int PackSize { get; init; } = 23;
    /// <summary>Missiles per pack slot the quiver uses (Angband: 40).</summary>
    public int QuiverSlotSize { get; init; } = 40;
    /// <summary>Quiver stacks (Angband carry-cap:quiver-size).</summary>
    public int QuiverSize { get; init; } = 10;
    /// <summary>One chance in this many, each world turn, of a new monster somewhere out of sight (Angband mon-gen:chance).</summary>
    public int AllocMonsterChance { get; init; } = 500;
    /// <summary>A new lantern's fuel (Angband obj-make:default-lamp); it holds up to its kind's (fuel-lamp).</summary>
    public int DefaultLampFuel { get; init; } = 7500;
    /// <summary>Monsters in the town by day and by night (Angband mon-gen:town-day, town-night).</summary>
    public int TownMonstersDay { get; init; } = 4;
    public int TownMonstersNight { get; init; } = 8;
    /// <summary>Most breeders allowed on a level (Angband repro_monster_max).</summary>
    public int MaxBreeders { get; init; } = 100;
    /// <summary>Breeding chance scale (Angband repro_monster_rate).</summary>
    public int BreedRate { get; init; } = 8;
    /// <summary>Starting value of each stat before class adjustments (character creation will roll these).</summary>
    public int BaseStat { get; init; } = 15;
    /// <summary>Hit die used when no race is given.</summary>
    public int RaceHitDie { get; init; } = 10;
    /// <summary>Point-buy budget (Angband: 20).</summary>
    public int BirthPoints { get; init; } = 20;
    /// <summary>Starting gold, plus 50 per unspent point-buy point, less the starting kit's worth (Angband player:start-gold).</summary>
    public int StartGold { get; init; } = 600;
    /// <summary>
    /// Angband's birth_no_selling (on by default): stores give nothing for items, and gold found in
    /// the dungeon is multiplied to compensate. Turn off for classic selling.
    /// </summary>
    public bool NoSelling { get; init; } = true;
    /// <summary>Angband store:turns: the stores are maintained once per 10 × this many game turns spent in the dungeon.</summary>
    public int StoreTurns { get; init; } = 1000;
    /// <summary>Angband store:inven-max: the most piles a store holds.</summary>
    public int StoreInvenMax { get; init; } = 24;
    /// <summary>Angband store:shuffle: each store day, one chance in this many that a shopkeeper retires.</summary>
    public int StoreShuffle { get; init; } = 25;
    /// <summary>Angband store:magic-level: the deepest level stores (other than the black market) make stock at.</summary>
    public int StoreMagicLevel { get; init; } = 5;

    // Hunger (Angband 4.1 PY_FOOD_*): food counter thresholds.
    /// <summary>The most food a character can hold.</summary>
    public int FoodUpper { get; init; } = 20_000;
    /// <summary>At or above: gorged (slowed by 10).</summary>
    public int FoodMax { get; init; } = 15_000;
    /// <summary>At or above: full. New characters start just below.</summary>
    public int FoodFull { get; init; } = 10_000;
    /// <summary>Below: hungry.</summary>
    public int FoodHungry { get; init; } = 2_000;
    /// <summary>Below: weak (slower healing).</summary>
    public int FoodWeak { get; init; } = 1_000;
    /// <summary>Below: faint (may pass out; healing slower still).</summary>
    public int FoodFaint { get; init; } = 500;
    /// <summary>Below: starving (takes damage, no healing).</summary>
    public int FoodStarve { get; init; } = 100;
    /// <summary>Game turns between digestions.</summary>
    public int DigestInterval { get; init; } = 100;
}
