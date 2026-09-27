namespace Angband.Core.Definitions;

/// <summary>Everything loaded from the data files. Immutable once constructed.</summary>
public sealed class GameData
{
    public GameData(
        TerrainRegistry terrain,
        IReadOnlyList<TrapDef> traps,
        IReadOnlyList<DungeonProfileDef> profiles,
        IReadOnlyList<MapTemplateDef> templates,
        TownDef town,
        IReadOnlyList<ShopDef> shops,
        IReadOnlyDictionary<string, string> colors,
        GameConstants constants)
    {
        Terrain = terrain;
        Traps = traps;
        for (var i = 0; i < traps.Count; i++) traps[i].Index = (ushort)(i + 1);
        Profiles = profiles;
        Templates = templates;
        Town = town;
        Shops = shops;
        Colors = colors;
        Constants = constants;
    }

    public TerrainRegistry Terrain { get; }
    public IReadOnlyList<TrapDef> Traps { get; }
    public IReadOnlyList<DungeonProfileDef> Profiles { get; }
    public IReadOnlyList<MapTemplateDef> Templates { get; }
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
    public IReadOnlyList<FlavorGroupDef> Flavors { get; init; } = [];

    /// <summary>Forms the player can take (Angband shape.txt).</summary>
    public IReadOnlyList<ShapeDef> Shapes { get; init; } = [];
    /// <summary>Words for random names (random artifacts).</summary>
    public IReadOnlyList<string> NameWords { get; init; } = [];
    public ShapeDef? Shape(string id) => Shapes.FirstOrDefault(s => s.Id == id);

    /// <summary>Quests, shallowest first (Angband quest.txt).</summary>
    public IReadOnlyList<QuestDef> Quests { get; init; } = [];

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

    public ElementDef? Element(string id) => Elements.FirstOrDefault(e => e.Id == id);
    public TimedEffectDef? Timed(string id) => TimedEffects.FirstOrDefault(t => t.Id == id);
    public BlowMethodDef? BlowMethod(string id) => BlowMethods.FirstOrDefault(m => m.Id == id);
    public BlowEffectDef? BlowEffect(string id) => BlowEffects.FirstOrDefault(e => e.Id == id);
    public MonsterRaceDef? Monster(string id) => Monsters.FirstOrDefault(m => m.Id == id);

    /// <summary>Looks up a trap by its 1-based square index.</summary>
    public TrapDef? TrapByIndex(ushort index) => index == 0 || index > Traps.Count ? null : Traps[index - 1];
}

/// <summary>An item the player starts with.</summary>
public sealed class StartItemDef
{
    public required string Kind { get; init; }
    public int Count { get; init; } = 1;
    public bool Equip { get; init; }
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
    /// <summary>Quiver stacks.</summary>
    public int QuiverSize { get; init; } = 8;
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
    /// <summary>Starting gold, plus 50 per unspent point-buy point.</summary>
    public int StartGold { get; init; } = 200;
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
