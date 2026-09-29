namespace Angband.Core.Persistence;

// The save file format: plain records serialised as JSON (then gzipped). Game data is referred to by
// id (terrain, object kinds, races...), never by internal index, so saves survive data reordering.

/// <summary>Shown in load menus without restoring the game.</summary>
public sealed class SaveSummary
{
    public string Name { get; set; } = "";
    public string Race { get; set; } = "";
    public string Class { get; set; } = "";
    public int Level { get; set; }
    public int Depth { get; set; }
    public int MaxDepth { get; set; }
    public long Experience { get; set; }
    public long GameTurn { get; set; }
    public bool IsDead { get; set; }
    public bool IsWinner { get; set; }
    public DateTime SavedAtUtc { get; set; }
}

public sealed class SaveFile
{
    public int Version { get; set; }
    public SaveSummary Summary { get; set; } = new();
    public ulong Seed { get; set; }
    public ulong TownSeed { get; set; }
    public ulong[] Rng { get; set; } = [];
    public long GameTurn { get; set; }
    public List<int> PendingActors { get; set; } = [];
    public long NextSerial { get; set; }
    public List<string> CreatedArtifacts { get; set; } = [];
    /// <summary>Birth and cheat options (interface options are the player's settings, not the game's). Null in older saves.</summary>
    public Dictionary<string, bool>? Options { get; set; }
    public List<string> CheatsUsed { get; set; } = [];
    public IgnoreSave Ignore { get; set; } = new();
    /// <summary>With birth_randarts: the seed the game's artifact set is rebuilt from.</summary>
    public ulong? RandartSeed { get; set; }
    /// <summary>Store days owed (Angband daycount).</summary>
    public int StoreDays { get; set; }
    /// <summary>During Single Combat: the level outside the arena, and where the player stood on it.</summary>
    public LevelSave? ArenaReturn { get; set; }
    /// <summary>A persistent dungeon's other levels (birth_levels_persist); empty otherwise and in older saves.</summary>
    public List<LevelSave> StoredLevels { get; set; } = [];
    /// <summary>The player's history (Angband player-history.c); empty in older saves.</summary>
    public List<HistorySave> History { get; set; } = [];
    /// <summary>The hotbar's slots ("spell:id", "item:id" or null).</summary>
    public List<string?> Hotbar { get; set; } = [];
    public int ArenaReturnX { get; set; }
    public int ArenaReturnY { get; set; }
    /// <summary>The monster under the player's command (0 = none).</summary>
    public int CommandedMonster { get; set; }
    public List<string> KilledUniques { get; set; } = [];
    public Dictionary<string, int> QuestKills { get; set; } = [];
    public Dictionary<string, int> CharacterKills { get; set; } = [];
    /// <summary>The journey: levels arrived on and kills, in order (absent from older saves).</summary>
    public List<JourneyVisitSave> JourneyVisits { get; set; } = [];
    public List<JourneyKillSave> JourneyKills { get; set; } = [];
    public KnowledgeSave Knowledge { get; set; } = new();
    public PlayerSave Player { get; set; } = new();
    public LevelSave Level { get; set; } = new();
    public List<StoreSave> Stores { get; set; } = [];
}

public sealed class KnowledgeSave
{
    public List<string> Runes { get; set; } = [];
    public List<string> AwareKinds { get; set; } = [];
    public List<string> TriedKinds { get; set; } = [];
    /// <summary>Kind id → [flavour name, colour].</summary>
    public Dictionary<string, string[]> Flavors { get; set; } = [];
    public List<string> SeenKinds { get; set; } = [];
    public List<string> SeenEgos { get; set; } = [];
    public List<string> SeenArtifacts { get; set; } = [];
    /// <summary>Auto-inscriptions by kind.</summary>
    public Dictionary<string, string> KindNotes { get; set; } = [];
}

/// <summary>Ignore settings (Angband: kinds, egos per item type, and quality per type).</summary>
public sealed class IgnoreSave
{
    public List<string> KindsAware { get; set; } = [];
    public List<string> KindsUnaware { get; set; } = [];
    public List<string> Egos { get; set; } = [];
    public Dictionary<string, string> Quality { get; set; } = [];
}

public sealed class ItemSave
{
    public long Serial { get; set; }
    public string Kind { get; set; } = "";
    public int Number { get; set; }
    public string? Note { get; set; }
    public bool Ignored { get; set; }
    public bool Assessed { get; set; }
    public string Damage { get; set; } = "0";
    public int Armour { get; set; }
    public int ToHit { get; set; }
    public int ToDam { get; set; }
    public int ToAc { get; set; }
    public int Fuel { get; set; }
    public int Charges { get; set; }
    public int Timeout { get; set; }
    public int ChestState { get; set; }
    public int GoldValue { get; set; }
    public string? Ego { get; set; }
    public string? Artifact { get; set; }
    public Dictionary<string, int> Modifiers { get; set; } = [];
    public List<SlaySave> Slays { get; set; } = [];
    public List<BrandSave> Brands { get; set; } = [];
    public List<string> Resists { get; set; } = [];
    public List<string> Curses { get; set; } = [];
    /// <summary>Each curse's power and time to act (none in older saves: they get the default power).</summary>
    public Dictionary<string, int> CursePowers { get; set; } = [];
    public Dictionary<string, int> CurseTimeouts { get; set; } = [];
    /// <summary>Null in saves from before per-item flags (the kind's flags are used).</summary>
    public List<string>? Flags { get; set; }
    public int OriginDepth { get; set; }
}

public sealed class SlaySave
{
    public string Flag { get; set; } = "";
    public int Multiplier { get; set; }
    public string Verb { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class BrandSave
{
    public string Element { get; set; } = "";
    public int Multiplier { get; set; }
    public string Verb { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class PlayerSave
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public int Height { get; set; }
    public int Weight { get; set; }
    public string Background { get; set; } = "";
    public string? Race { get; set; }
    public string? Class { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Energy { get; set; }
    public int Depth { get; set; }
    public int MaxDepth { get; set; }
    public int Level { get; set; }
    public int MaxLevel { get; set; }
    public int ExpFactor { get; set; }
    public int HitDie { get; set; }
    public long Experience { get; set; }
    public int ExperienceFraction { get; set; }
    public long Gold { get; set; }
    public int Food { get; set; }
    public int MaxHp { get; set; }
    public int Hp { get; set; }
    public int HpFraction { get; set; }
    public bool IsDead { get; set; }
    public bool IsWinner { get; set; }
    public string? Shape { get; set; }
    public Dictionary<string, string> StatScramble { get; set; } = [];
    public string? KilledBy { get; set; }
    public int ConHpRemainder { get; set; }
    public bool Regenerates { get; set; }
    public int Infravision { get; set; }
    public int MaxMana { get; set; }
    public int Mana { get; set; }
    public int ManaFraction { get; set; }
    public Dictionary<string, int> Stats { get; set; } = [];
    public Dictionary<string, int> BaseStats { get; set; } = [];
    public Dictionary<string, int> NaturalStats { get; set; } = [];
    public Dictionary<string, int> StatDrain { get; set; } = [];
    public long MaxExperience { get; set; }
    public int RecallTimer { get; set; }
    public int RecallDepth { get; set; }
    public int DeepDescentTimer { get; set; }
    public List<int> HpGains { get; set; } = [];
    public List<string> LearnedSpells { get; set; } = [];
    public List<string> CastSpells { get; set; } = [];
    public int BaseSpeed { get; set; }
    public int BaseArmour { get; set; }
    public int BaseToHit { get; set; }
    public int BaseToDam { get; set; }
    public int BaseStealth { get; set; }
    public int BaseBlows { get; set; }
    public int BaseShots { get; set; }
    public Dictionary<string, int> IntrinsicResists { get; set; } = [];
    public Dictionary<string, int> Skills { get; set; } = [];
    public Dictionary<string, int> Timed { get; set; } = [];
    public List<ItemSave> Pack { get; set; } = [];
    public List<ItemSave> Quiver { get; set; } = [];
    /// <summary>One entry per equipment slot (null = empty).</summary>
    public List<ItemSave?> Equipment { get; set; } = [];
}

public sealed class MonsterSave
{
    public int Id { get; set; }
    public string Race { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Speed { get; set; }
    public int Energy { get; set; }
    public int Sleep { get; set; }
    public int Stun { get; set; }
    public int Disenchanted { get; set; }
    public int Confused { get; set; }
    public int Fear { get; set; }
    public int Held { get; set; }
    public int Fast { get; set; }
    public int Slow { get; set; }
    public bool Camouflaged { get; set; }
    /// <summary>The leader it guards (Angband bodyguards); null for everyone else and in older saves.</summary>
    public int? BodyguardOf { get; set; }
    /// <summary>What it has learned of the player (Angband known_pstate); null in older saves.</summary>
    public Dictionary<string, int>? KnownPlayer { get; set; }
    /// <summary>A shapechanged monster's own race (Race is its current form).</summary>
    public string? OriginalRace { get; set; }
    public ItemSave? MimicItem { get; set; }
    public List<ItemSave> Carried { get; set; } = [];
    public bool LootRolled { get; set; }
    public int[]? WanderTarget { get; set; }
    public int WanderStuck { get; set; }
}

public sealed class ObjectPileSave
{
    public int X { get; set; }
    public int Y { get; set; }
    public List<ItemSave> Items { get; set; } = [];
}

public sealed class JourneyVisitSave
{
    public long Turn { get; set; }
    public int Depth { get; set; }
    public string Profile { get; set; } = "";
}

public sealed class JourneyKillSave
{
    public long Turn { get; set; }
    public int Depth { get; set; }
    public string Race { get; set; } = "";
    public bool Unique { get; set; }
}

public sealed class HistorySave
{
    public long Turn { get; set; }
    public int Depth { get; set; }
    public string Text { get; set; } = "";
    public string? Artifact { get; set; }
    public bool Lost { get; set; }
}

public sealed class LevelSave
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int Depth { get; set; }
    public string ProfileId { get; set; } = "";
    public ulong Seed { get; set; }
    public bool IsLit { get; set; }
    public bool IsKnown { get; set; }
    public int Feeling { get; set; }
    public int FeelingSquaresSeen { get; set; }
    /// <summary>A ranger's decoy (square index), if any.</summary>
    public int? Decoy { get; set; }
    /// <summary>Hidden feeling squares not yet seen (square indices).</summary>
    public List<int> FeelSquares { get; set; } = [];
    /// <summary>Terrain ids used by <see cref="Features"/> and <see cref="Known"/> (index into this list).</summary>
    public List<string> Terrain { get; set; } = [];
    public ushort[] Features { get; set; } = [];
    public ushort[] Flags { get; set; } = [];
    /// <summary>Trap ids per square ("" = none), stored sparsely as index → id.</summary>
    public Dictionary<int, string> Traps { get; set; } = [];
    public Dictionary<int, int> Locks { get; set; } = [];
    /// <summary>How hard each trap is to notice (squares with a trap power; none in older saves, so seen at once).</summary>
    public Dictionary<int, int> TrapPowers { get; set; } = [];
    /// <summary>Remembered terrain (index into <see cref="Terrain"/>, -1 = unknown).</summary>
    public int[] Known { get; set; } = [];
    public Dictionary<int, ItemSave> RememberedObjects { get; set; } = [];
    public int[] Scent { get; set; } = [];
    public int ScentNow { get; set; }
    public int NextMonsterId { get; set; }
    public List<MonsterSave> Monsters { get; set; } = [];
    public List<ObjectPileSave> Objects { get; set; } = [];
    public int[] Population { get; set; } = [];
    /// <summary>For a stored level: the game turn it was left on.</summary>
    public long StoredTurn { get; set; }
}

public sealed class StoreSave
{
    public string Id { get; set; } = "";
    public string? Owner { get; set; }
    public List<ItemSave> Stock { get; set; } = [];
}
