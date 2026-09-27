namespace Angband.Core.Definitions;

/// <summary>All terrain features, indexed densely for compact square storage.</summary>
public sealed class TerrainRegistry
{
    private readonly List<TerrainDef> _byIndex = [];
    private readonly Dictionary<string, TerrainDef> _byId = new(StringComparer.Ordinal);

    public TerrainRegistry(IEnumerable<TerrainDef> defs)
    {
        foreach (var def in defs)
        {
            if (!_byId.TryAdd(def.Id, def))
                throw new GameDataException($"Duplicate terrain id '{def.Id}'.");
            def.Index = (ushort)_byIndex.Count;
            _byIndex.Add(def);
        }
        Ids = new WellKnownTerrain(this);
    }

    /// <summary>Indices of the features the engine itself needs to reference.</summary>
    public WellKnownTerrain Ids { get; }

    public int Count => _byIndex.Count;
    public IReadOnlyList<TerrainDef> All => _byIndex;

    public TerrainDef this[ushort index] => _byIndex[index];
    public TerrainDef this[string id] => _byId.TryGetValue(id, out var def)
        ? def
        : throw new GameDataException($"Unknown terrain id '{id}'.");

    public bool TryGet(string id, out TerrainDef def) => _byId.TryGetValue(id, out def!);

    public IEnumerable<TerrainDef> Shops => _byIndex.Where(t => t.Has(TerrainFlags.Shop));
}

/// <summary>
/// Terrain ids the engine hard-wires. Mods may restyle these (glyph, colour, name) but must keep the ids.
/// </summary>
public sealed class WellKnownTerrain
{
    public WellKnownTerrain(TerrainRegistry registry)
    {
        ushort Get(string id) => registry.TryGet(id, out var def)
            ? def.Index
            : throw new GameDataException($"Required terrain '{id}' is missing from terrain data.");

        None = Get("none");
        Floor = Get("floor");
        Granite = Get("granite_wall");
        Permanent = Get("permanent_wall");
        Magma = Get("magma_vein");
        Quartz = Get("quartz_vein");
        MagmaTreasure = Get("magma_with_treasure");
        QuartzTreasure = Get("quartz_with_treasure");
        Rubble = Get("rubble");
        PassableRubble = Get("passable_rubble");
        ClosedDoor = Get("closed_door");
        OpenDoor = Get("open_door");
        BrokenDoor = Get("broken_door");
        SecretDoor = Get("secret_door");
        UpStair = Get("up_staircase");
        DownStair = Get("down_staircase");
        Lava = Get("lava");
    }

    public ushort None { get; }
    public ushort Floor { get; }
    public ushort Granite { get; }
    public ushort Permanent { get; }
    public ushort Magma { get; }
    public ushort Quartz { get; }
    public ushort MagmaTreasure { get; }
    public ushort QuartzTreasure { get; }
    public ushort Rubble { get; }
    public ushort PassableRubble { get; }
    public ushort ClosedDoor { get; }
    public ushort OpenDoor { get; }
    public ushort BrokenDoor { get; }
    public ushort SecretDoor { get; }
    public ushort UpStair { get; }
    public ushort DownStair { get; }
    public ushort Lava { get; }
}
