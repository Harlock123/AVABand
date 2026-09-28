using System.Text.Json;
using System.Text.Json.Serialization;
using Angband.Core.Definitions;
using Angband.Core.Generation;

namespace Angband.Data;

/// <summary>
/// Loads game data from one or more directories. The first is the base game; each further
/// directory is a mod layered on top: list entries replace base entries with the same id and new
/// ids are appended, colours merge by name, and <c>town.json</c>/<c>constants.json</c> replace
/// wholesale. Every problem found is reported together in one <see cref="GameDataException"/>.
/// </summary>
public static class DataLoader
{
    public const string TerrainFile = "terrain.json";
    public const string TrapsFile = "traps.json";
    public const string ChestTrapsFile = "chest_traps.json";
    public const string QuestsFile = "quests.json";
    public const string ShapesFile = "shapes.json";
    /// <summary>Words the random artifact namer learns from (Angband names.txt, the Tolkien section).</summary>
    public const string NamesFile = "names.json";
    public const string ProfilesFile = "dungeon_profiles.json";
    public const string TownFile = "town.json";
    public const string ColorsFile = "colors.json";
    public const string ConstantsFile = "constants.json";
    public const string TemplatesFolder = "templates";
    public const string ElementsFile = "elements.json";
    public const string TimedEffectsFile = "timed_effects.json";
    public const string BlowMethodsFile = "blow_methods.json";
    public const string BlowEffectsFile = "blow_effects.json";
    public const string MonstersFile = "monsters.json";
    public const string ObjectBasesFile = "object_bases.json";
    public const string ObjectsFile = "objects.json";
    public const string EgosFile = "egos.json";
    public const string ArtifactsFile = "artifacts.json";
    public const string CursesFile = "curses.json";
    public const string FlavorsFile = "flavors.json";
    public const string StartingKitFile = "starting_kit.json";
    public const string MonsterSpellsFile = "monster_spells.json";
    public const string RealmsFile = "realms.json";
    public const string ClassesFile = "classes.json";
    public const string SpellsFile = "spells.json";
    public const string RacesFile = "races.json";
    public const string StoresFile = "stores.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower), new DiceJsonConverter() },
    };

    /// <summary>The <c>data</c> folder shipped next to the executable.</summary>
    public static string DefaultDataDirectory => Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>Per-user mod folders (each subfolder of <c>&lt;AppData&gt;/AVABand/mods</c>), in name order.</summary>
    public static IEnumerable<string> UserModDirectories()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVABand", "mods");
        return Directory.Exists(root)
            ? Directory.GetDirectories(root).Order(StringComparer.OrdinalIgnoreCase)
            : [];
    }

    public static GameData LoadDefault(bool includeUserMods = true) =>
        Load(includeUserMods ? [DefaultDataDirectory, .. UserModDirectories()] : [DefaultDataDirectory]);

    public static GameData Load(params IReadOnlyList<string> directories)
    {
        if (directories.Count == 0) throw new ArgumentException("At least one data directory is required.");
        if (!Directory.Exists(directories[0]))
            throw new GameDataException($"Data directory '{directories[0]}' does not exist.");

        var errors = new List<string>();
        var terrain = new Merged<TerrainJson>(t => t.Id);
        var traps = new Merged<TrapJson>(t => t.Id);
        var profiles = new Merged<DungeonProfileDef>(p => p.Id);
        var templates = new Merged<TemplateJson>(t => t.Id);
        var elements = new Merged<ElementDef>(e => e.Id);
        var timedEffects = new Merged<TimedEffectDef>(t => t.Id);
        var blowMethods = new Merged<BlowMethodDef>(m => m.Id);
        var blowEffects = new Merged<BlowEffectDef>(e => e.Id);
        var monsters = new Merged<MonsterJson>(m => m.Id);
        var monsterSpells = new Merged<MonsterSpellDef>(s => s.Id);
        var realms = new Merged<RealmDef>(r => r.Id);
        var classes = new Merged<ClassDef>(c => c.Id);
        var spells = new Merged<SpellDef>(s => s.Id);
        var races = new Merged<RaceDef>(r => r.Id);
        var stores = new Merged<StoreDef>(s => s.Id);
        var objectBases = new Merged<ObjectBaseDef>(b => b.Id);
        var objects = new Merged<ObjectKindDef>(o => o.Id);
        var egos = new Merged<EgoItemDef>(e => e.Id);
        var artifacts = new Merged<ArtifactDef>(a => a.Id);
        var curses = new Merged<CurseDef>(c => c.Id);
        var flavors = new Merged<FlavorGroupDef>(f => f.Id);
        var chestTraps = new Merged<ChestTrapDef>(t => t.Id);
        var quests = new Merged<QuestDef>(q => q.Id);
        var shapes = new Merged<ShapeDef>(s => s.Id);
        List<StartItemDef>? startingKit = null;
        var nameWords = new List<string>();
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        TownJson? town = null;
        GameConstants? constants = null;

        foreach (var dir in directories)
        {
            terrain.AddRange(Read<List<TerrainJson>>(dir, TerrainFile, errors));
            traps.AddRange(Read<List<TrapJson>>(dir, TrapsFile, errors));
            profiles.AddRange(Read<List<DungeonProfileDef>>(dir, ProfilesFile, errors));
            elements.AddRange(Read<List<ElementDef>>(dir, ElementsFile, errors));
            timedEffects.AddRange(Read<List<TimedEffectDef>>(dir, TimedEffectsFile, errors));
            blowMethods.AddRange(Read<List<BlowMethodDef>>(dir, BlowMethodsFile, errors));
            blowEffects.AddRange(Read<List<BlowEffectDef>>(dir, BlowEffectsFile, errors));
            monsters.AddRange(Read<List<MonsterJson>>(dir, MonstersFile, errors));
            monsterSpells.AddRange(Read<List<MonsterSpellDef>>(dir, MonsterSpellsFile, errors));
            realms.AddRange(Read<List<RealmDef>>(dir, RealmsFile, errors));
            classes.AddRange(Read<List<ClassDef>>(dir, ClassesFile, errors));
            spells.AddRange(Read<List<SpellDef>>(dir, SpellsFile, errors));
            races.AddRange(Read<List<RaceDef>>(dir, RacesFile, errors));
            stores.AddRange(Read<List<StoreDef>>(dir, StoresFile, errors));
            objectBases.AddRange(Read<List<ObjectBaseDef>>(dir, ObjectBasesFile, errors));
            objects.AddRange(Read<List<ObjectKindDef>>(dir, ObjectsFile, errors));
            egos.AddRange(Read<List<EgoItemDef>>(dir, EgosFile, errors));
            artifacts.AddRange(Read<List<ArtifactDef>>(dir, ArtifactsFile, errors));
            curses.AddRange(Read<List<CurseDef>>(dir, CursesFile, errors));
            flavors.AddRange(Read<List<FlavorGroupDef>>(dir, FlavorsFile, errors));
            chestTraps.AddRange(Read<List<ChestTrapDef>>(dir, ChestTrapsFile, errors));
            quests.AddRange(Read<List<QuestDef>>(dir, QuestsFile, errors));
            shapes.AddRange(Read<List<ShapeDef>>(dir, ShapesFile, errors));
            startingKit = Read<List<StartItemDef>>(dir, StartingKitFile, errors) ?? startingKit;
            nameWords.AddRange(Read<List<string>>(dir, NamesFile, errors) ?? []);
            town = Read<TownJson>(dir, TownFile, errors) ?? town;
            constants = Read<GameConstants>(dir, ConstantsFile, errors) ?? constants;
            foreach (var (name, hex) in Read<Dictionary<string, string>>(dir, ColorsFile, errors) ?? [])
                colors[name] = hex;

            var templateDir = Path.Combine(dir, TemplatesFolder);
            if (Directory.Exists(templateDir))
                foreach (var file in Directory.GetFiles(templateDir, "*.json").Order(StringComparer.Ordinal))
                    templates.AddRange(Read<List<TemplateJson>>(templateDir, Path.GetFileName(file), errors));
        }

        if (town is null) errors.Add($"{TownFile} is missing.");
        if (terrain.Items.Count == 0) errors.Add($"{TerrainFile} is missing or empty.");
        if (errors.Count > 0) throw Failure(errors);

        var terrainDefs = terrain.Items.Select(t => ToTerrain(t, colors, errors)).ToList();
        var trapDefs = traps.Items.Select(t => ToTrap(t, colors, errors)).ToList();
        var templateDefs = templates.Items.Select(t => ToTemplate(t, errors)).OfType<MapTemplateDef>().ToList();
        var monsterDefs = monsters.Items.Select(m => ToMonster(m, colors, errors)).ToList();
        CheckCombatReferences(elements.Items, timedEffects.Items, blowMethods.Items, blowEffects.Items, monsterDefs, errors);
        CheckSpellReferences(monsterSpells.Items, monsterDefs, elements.Items, timedEffects.Items, errors);
        CheckMagicReferences(realms.Items, classes.Items, spells.Items, objects.Items, objectBases.Items, timedEffects.Items, errors);
        var kindIds = objects.Items.Select(o => o.Id).ToHashSet();
        var baseIds = objectBases.Items.Select(b => b.Id).ToHashSet();
        foreach (var store in stores.Items)
        {
            if (town is not null && town.Shops.All(sh => sh.Id != store.Id)) errors.Add($"store '{store.Id}' has no shop in the town.");
            foreach (var k in store.Always.Concat(store.Normal).Where(k => !kindIds.Contains(k)))
                errors.Add($"store '{store.Id}' stocks unknown object '{k}'.");
            foreach (var b in store.Buys.Where(b => !baseIds.Contains(b)))
                errors.Add($"store '{store.Id}' buys unknown object base '{b}'.");
            if (store.MinItems > store.MaxItems) errors.Add($"store '{store.Id}' has minItems above maxItems.");
        }
        foreach (var q in quests.Items)
        {
            if (monsterDefs.All(m => m.Id != q.Race)) errors.Add($"quest '{q.Id}' names unknown monster '{q.Race}'.");
            if (q.Level < 1 || q.Level > (constants?.MaxDepth ?? 127)) errors.Add($"quest '{q.Id}' is on an impossible level {q.Level}.");
        }
        if (chestTraps.Items.Count > 30) errors.Add($"{ChestTrapsFile}: at most 30 chest traps (they are bits of one number).");
        foreach (var m in monsterDefs)
        foreach (var k in m.Mimics.Where(k => !kindIds.Contains(k)))
            errors.Add($"monster '{m.Id}' mimics unknown object '{k}'.");
        foreach (var race in races.Items)
            foreach (var stat in race.Stats.Keys.Concat(classes.Items.SelectMany(c => c.Stats.Keys)).Distinct()
                         .Where(s => !Angband.Core.Game.CharacterSpec.StatIds.Contains(s)))
                errors.Add($"race or class uses unknown stat '{stat}' (use {string.Join(", ", Angband.Core.Game.CharacterSpec.StatIds)}).");
        CheckItemReferences(objectBases.Items, objects.Items, egos.Items, artifacts.Items, curses.Items, flavors.Items,
            startingKit ?? [], elements.Items, timedEffects.Items, colors, errors);

        TerrainRegistry? registry = null;
        try
        {
            registry = new TerrainRegistry(terrainDefs);
        }
        catch (GameDataException ex)
        {
            errors.Add(ex.Message);
        }

        var shops = town!.Shops.Select(s => new ShopDef { Id = s.Id, Name = s.Name, Owner = s.Owner, IsHome = s.IsHome }).ToList();
        foreach (var shop in town.Shops)
            if (registry is not null && !registry.TryGet(shop.Terrain, out _))
                errors.Add($"{TownFile}: shop '{shop.Id}' uses unknown terrain '{shop.Terrain}'.");

        if (errors.Count > 0) throw Failure(errors);

        var townDef = new TownDef
        {
            Width = town.Width,
            Height = town.Height,
            LotColumns = town.LotColumns,
            LotRows = town.LotRows,
            Rubble = town.Rubble,
            Shops = town.Shops.Select(s => s.Terrain).ToList(),
        };

        var data = new GameData(registry!, trapDefs, profiles.Items, templateDefs, townDef, shops, colors,
            constants ?? new GameConstants())
        {
            Elements = elements.Items,
            TimedEffects = timedEffects.Items,
            BlowMethods = blowMethods.Items,
            BlowEffects = blowEffects.Items,
            Monsters = monsterDefs,
            MonsterSpells = monsterSpells.Items,
            Realms = realms.Items,
            Classes = classes.Items,
            Spells = spells.Items,
            Races = races.Items,
            Stores = stores.Items,
            ObjectBases = objectBases.Items,
            Objects = objects.Items,
            Egos = egos.Items,
            Artifacts = artifacts.Items,
            Curses = curses.Items,
            Flavors = flavors.Items,
            ChestTraps = chestTraps.Items,
            Quests = quests.Items.OrderBy(q => q.Level).ToList(),
            Shapes = shapes.Items,
            NameWords = nameWords.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StartingKit = startingKit ?? [],
        };

        try
        {
            _ = new DungeonGenerator(data); // validates generator and room-type references
        }
        catch (GameDataException ex)
        {
            throw Failure([ex.Message]);
        }
        return data;
    }

    private static GameDataException Failure(List<string> errors) =>
        new("Game data is invalid:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "  - " + e)));

    private static T? Read<T>(string dir, string file, List<string> errors) where T : class
    {
        var path = Path.Combine(dir, file);
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, Options);
        }
        catch (JsonException ex)
        {
            errors.Add($"{path}: {ex.Message}");
            return null;
        }
    }

    private static TerrainDef ToTerrain(TerrainJson t, IReadOnlyDictionary<string, string> colors, List<string> errors)
    {
        CheckColor(colors, t.Color, $"terrain '{t.Id}'", errors);
        return new TerrainDef
        {
            Id = t.Id,
            Name = t.Name,
            Glyph = ParseGlyph(t.Glyph, $"terrain '{t.Id}'", errors),
            Color = t.Color,
            Flags = ParseFlags(t.Flags, $"terrain '{t.Id}'", errors),
            Priority = t.Priority,
            Mimic = t.Mimic,
            Shop = t.Shop,
            DigDifficulty = t.DigDifficulty,
            Description = t.Description ?? "",
        };
    }

    private static TrapDef ToTrap(TrapJson t, IReadOnlyDictionary<string, string> colors, List<string> errors)
    {
        CheckColor(colors, t.Color, $"trap '{t.Id}'", errors);
        return new TrapDef
        {
            Id = t.Id,
            Name = t.Name,
            Glyph = ParseGlyph(t.Glyph, $"trap '{t.Id}'", errors),
            Color = t.Color,
            MinDepth = t.MinDepth,
            MaxDepth = t.MaxDepth,
            Weight = t.Weight,
            Effect = t.Effect ?? "",
            IsTrapDoor = t.IsTrapDoor,
            Warding = t.Warding,
            Web = t.Web,
            Description = t.Description ?? "",
        };
    }

    private static MapTemplateDef? ToTemplate(TemplateJson t, List<string> errors)
    {
        if (t.Rows is not { Count: > 0 })
        {
            errors.Add($"template '{t.Id}' has no rows.");
            return null;
        }

        var width = t.Rows.Max(r => r.Length);
        var rows = t.Rows.Select(r => r.PadRight(width)).ToList();
        foreach (var bad in rows.SelectMany(r => r).Where(c => !TemplateLegend.IsKnown(c)).Distinct())
            errors.Add($"template '{t.Id}' uses unknown symbol '{bad}'.");

        return new MapTemplateDef
        {
            Id = t.Id,
            Name = t.Name ?? t.Id,
            Kind = t.Kind,
            MinDepth = t.MinDepth,
            MaxDepth = t.MaxDepth,
            Weight = t.Weight,
            Rotatable = t.Rotatable,
            Rows = rows,
        };
    }

    private static MonsterRaceDef ToMonster(MonsterJson m, IReadOnlyDictionary<string, string> colors, List<string> errors)
    {
        CheckColor(colors, m.Color, $"monster '{m.Id}'", errors);
        return new MonsterRaceDef
        {
            Id = m.Id,
            Name = m.Name,
            Plural = m.Plural,
            Glyph = ParseGlyph(m.Glyph, $"monster '{m.Id}'", errors),
            Color = m.Color,
            Depth = m.Depth,
            Rarity = Math.Max(1, m.Rarity),
            Speed = m.Speed,
            HitPoints = Math.Max(1, m.HitPoints),
            Armour = m.Armour,
            Sleep = m.Sleep,
            Hearing = m.Hearing,
            Smell = m.Smell,
            SpellFrequency = m.SpellFrequency,
            InnateFrequency = m.InnateFrequency,
            SpellPower = m.SpellPower,
            Spells = m.Spells ?? [],
            Experience = m.Experience,
            Blows = m.Blows ?? [],
            Friends = m.Friends ?? [],
            Flags = new HashSet<string>(m.Flags ?? [], StringComparer.Ordinal),
            Mimics = m.Mimics ?? [],
            Shapes = m.Shapes ?? [],
            Description = m.Description ?? "",
        };
    }

    private static void CheckCombatReferences(IReadOnlyList<ElementDef> elements, IReadOnlyList<TimedEffectDef> timed,
        IReadOnlyList<BlowMethodDef> methods, IReadOnlyList<BlowEffectDef> effects, IReadOnlyList<MonsterRaceDef> monsters,
        List<string> errors)
    {
        var elementIds = elements.Select(e => e.Id).ToHashSet();
        var timedIds = timed.Select(t => t.Id).ToHashSet();
        var methodIds = methods.Select(m => m.Id).ToHashSet();
        var effectIds = effects.Select(e => e.Id).ToHashSet();

        foreach (var effect in effects)
        {
            if (effect.Element is { } el && !elementIds.Contains(el))
                errors.Add($"blow effect '{effect.Id}' uses unknown element '{el}'.");
            if (effect.Timed is { } t && !timedIds.Contains(t))
                errors.Add($"blow effect '{effect.Id}' uses unknown timed effect '{t}'.");
        }
        foreach (var monster in monsters)
        {
            foreach (var friend in monster.Friends)
            {
                if (friend.Race is { } r && !friend.IsSame && monsters.All(x => x.Id != r))
                    errors.Add($"monster '{monster.Id}' is escorted by unknown race '{r}'.");
                if (friend.Race is null && friend.Glyph is null)
                    errors.Add($"monster '{monster.Id}' has an escort with neither a race nor a base symbol.");
                if (friend.Chance is < 1 or > 100)
                    errors.Add($"monster '{monster.Id}' has an escort chance outside 1-100.");
            }
            foreach (var blow in monster.Blows)
            {
                if (!methodIds.Contains(blow.Method))
                    errors.Add($"monster '{monster.Id}' uses unknown blow method '{blow.Method}'.");
                if (!effectIds.Contains(blow.Effect))
                    errors.Add($"monster '{monster.Id}' uses unknown blow effect '{blow.Effect}'.");
            }
        }
    }

    private static void CheckSpellReferences(IReadOnlyList<MonsterSpellDef> spells, IReadOnlyList<MonsterRaceDef> monsters,
        IReadOnlyList<ElementDef> elements, IReadOnlyList<TimedEffectDef> timed, List<string> errors)
    {
        var ids = spells.Select(s => s.Id).ToHashSet();
        foreach (var s in spells)
        {
            if (s.Element is { } e && elements.All(x => x.Id != e) && !Projections.Unresistable.Contains(e))
                errors.Add($"monster spell '{s.Id}' uses unknown element '{e}'.");
            if (s.Timed is { } t && timed.All(x => x.Id != t)) errors.Add($"monster spell '{s.Id}' uses unknown timed effect '{t}'.");
        }
        foreach (var m in monsters)
        {
            foreach (var spell in m.Spells.Where(sp => !ids.Contains(sp)))
                errors.Add($"monster '{m.Id}' knows unknown spell '{spell}'.");
            var known = m.Spells.Select(sp => spells.FirstOrDefault(s => s.Id == sp)).OfType<MonsterSpellDef>().ToList();
            if (known.Any(s => !s.Innate) && m.SpellFrequency <= 0)
                errors.Add($"monster '{m.Id}' has spells but no spellFrequency.");
            if (known.Any(s => s.Innate) && m.InnateFrequency <= 0)
                errors.Add($"monster '{m.Id}' has innate attacks but no innateFrequency.");
        }
    }

    private static void CheckMagicReferences(IReadOnlyList<RealmDef> realms, IReadOnlyList<ClassDef> classes,
        IReadOnlyList<SpellDef> spells, IReadOnlyList<ObjectKindDef> kinds, IReadOnlyList<ObjectBaseDef> bases,
        IReadOnlyList<TimedEffectDef> timed, List<string> errors)
    {
        var realmIds = realms.ToDictionary(r => r.Id);
        var classIds = classes.Select(c => c.Id).ToHashSet();
        var kindById = kinds.ToDictionary(k => k.Id);
        var timedIds = timed.Select(t => t.Id).ToHashSet();

        foreach (var r in realms.Where(r => bases.All(b => b.Id != r.BookBase)))
            errors.Add($"realm '{r.Id}' uses unknown book base '{r.BookBase}'.");
        foreach (var c in classes)
        {
            if (c.Realm is { } realm && !realmIds.ContainsKey(realm)) errors.Add($"class '{c.Id}' uses unknown realm '{realm}'.");
            foreach (var k in c.StartingKit.Where(k => !kindById.ContainsKey(k.Kind)))
                errors.Add($"class '{c.Id}' kit names unknown object '{k.Kind}'.");
        }
        foreach (var s in spells)
        {
            var owner = $"spell '{s.Id}'";
            if (!realmIds.TryGetValue(s.Realm, out var realm)) errors.Add($"{owner} uses unknown realm '{s.Realm}'.");
            if (!kindById.TryGetValue(s.Book, out var book)) errors.Add($"{owner} is in unknown book '{s.Book}'.");
            else if (realm is not null && book.Base != realm.BookBase) errors.Add($"{owner}: book '{s.Book}' is not a {realm.Name} book.");
            foreach (var cls in s.Classes.Keys.Where(k => !classIds.Contains(k))) errors.Add($"{owner} names unknown class '{cls}'.");
            try
            {
                foreach (var e in Angband.Core.Game.SpellEffects.Parse(s.Effect, 1))
                {
                    if (!Angband.Core.Game.SpellEffects.IsKnown(e.Name)) errors.Add($"{owner}: unknown effect '{e.Name}'.");
                    else if (e.Name is "timed" or "cure" or "reduce" && !timedIds.Contains(e.Arg(0)))
                        errors.Add($"{owner}: unknown timed effect '{e.Arg(0)}'.");
                }
            }
            catch (Exception ex) when (ex is FormatException or DivideByZeroException)
            {
                errors.Add($"{owner}: bad expression in effect '{s.Effect}' ({ex.Message}).");
            }
        }
    }

    private static void CheckItemReferences(IReadOnlyList<ObjectBaseDef> bases, IReadOnlyList<ObjectKindDef> kinds,
        IReadOnlyList<EgoItemDef> egos, IReadOnlyList<ArtifactDef> artifacts, IReadOnlyList<CurseDef> curses,
        IReadOnlyList<FlavorGroupDef> flavors, IReadOnlyList<StartItemDef> kit, IReadOnlyList<ElementDef> elements,
        IReadOnlyList<TimedEffectDef> timed, IReadOnlyDictionary<string, string> colors, List<string> errors)
    {
        var baseIds = bases.ToDictionary(b => b.Id);
        var kindIds = kinds.Select(k => k.Id).ToHashSet();
        var curseIds = curses.Select(c => c.Id).ToHashSet();
        var elementIds = elements.Select(e => e.Id).ToHashSet();
        var timedIds = timed.Select(t => t.Id).ToHashSet();

        void CheckEffect(string? effect, string owner)
        {
            if (string.IsNullOrWhiteSpace(effect)) return;
            foreach (var e in Angband.Core.Game.ItemEffects.Parse(effect))
            {
                if (!Angband.Core.Game.ItemEffects.Known.Contains(e.Name))
                    errors.Add($"{owner}: unknown effect '{e.Name}'.");
                else if (e.Name is "timed" or "cure" or "reduce" && !timedIds.Contains(e.Arg(0)))
                    errors.Add($"{owner}: unknown timed effect '{e.Arg(0)}'.");
            }
        }
        void CheckBrands(IEnumerable<BrandDef> brands, string owner)
        {
            foreach (var b in brands)
                if (!elementIds.Contains(b.Element)) errors.Add($"{owner}: brand uses unknown element '{b.Element}'.");
        }
        void CheckCurses(IEnumerable<string> list, string owner)
        {
            foreach (var c in list)
                if (!curseIds.Contains(c)) errors.Add($"{owner}: unknown curse '{c}'.");
        }

        foreach (var b in bases) CheckColor(colors, b.Color, $"object base '{b.Id}'", errors);
        foreach (var k in kinds)
        {
            if (!baseIds.ContainsKey(k.Base)) errors.Add($"object '{k.Id}' has unknown base '{k.Base}'.");
            CheckEffect(k.Effect, $"object '{k.Id}'");
            CheckEffect(k.Activation, $"object '{k.Id}' activation");
            CheckBrands(k.Brands, $"object '{k.Id}'");
            CheckCurses(k.Curses, $"object '{k.Id}'");
        }
        foreach (var e in egos)
        {
            foreach (var b in e.Bases.Where(b => !baseIds.ContainsKey(b))) errors.Add($"ego '{e.Id}' names unknown base '{b}'.");
            CheckBrands(e.Brands, $"ego '{e.Id}'");
            CheckCurses(e.Curses, $"ego '{e.Id}'");
        }
        foreach (var a in artifacts)
        {
            if (!kindIds.Contains(a.Kind)) errors.Add($"artifact '{a.Id}' uses unknown object '{a.Kind}'.");
            CheckEffect(a.Activation, $"artifact '{a.Id}' activation");
            CheckBrands(a.Brands, $"artifact '{a.Id}'");
        }
        foreach (var c in curses) CheckEffect(c.Effect, $"curse '{c.Id}'");
        foreach (var s in kit.Where(s => !kindIds.Contains(s.Kind))) errors.Add($"starting kit names unknown object '{s.Kind}'.");

        foreach (var group in flavors)
        {
            foreach (var f in group.Flavors) CheckColor(colors, f.Color, $"flavor '{group.Id}/{f.Name}'", errors);
            var needed = kinds.Count(k => baseIds.TryGetValue(k.Base, out var b) && b.Flavor == group.Id && !k.IsSpecialArtifactKind);
            if (group.Syllables.Count == 0 && needed > group.Flavors.Count)
                errors.Add($"flavor group '{group.Id}' has {group.Flavors.Count} flavours for {needed} kinds.");
        }
        foreach (var b in bases.Where(b => b.Flavor is not null && flavors.All(f => f.Id != b.Flavor)))
            errors.Add($"object base '{b.Id}' uses unknown flavour group '{b.Flavor}'.");
    }

    private static char ParseGlyph(string? glyph, string owner, List<string> errors)
    {
        if (glyph is { Length: 1 }) return glyph[0];
        errors.Add($"{owner}: glyph must be exactly one character (got '{glyph}').");
        return '?';
    }

    private static TerrainFlags ParseFlags(IEnumerable<string>? names, string owner, List<string> errors)
    {
        var flags = TerrainFlags.None;
        foreach (var name in names ?? [])
        {
            if (Enum.TryParse<TerrainFlags>(name.Replace("_", ""), ignoreCase: true, out var flag)) flags |= flag;
            else errors.Add($"{owner}: unknown flag '{name}'.");
        }
        return flags;
    }

    private static void CheckColor(IReadOnlyDictionary<string, string> colors, string color, string owner, List<string> errors)
    {
        if (!colors.ContainsKey(color)) errors.Add($"{owner}: unknown colour '{color}'.");
    }

    /// <summary>Id-keyed list where later additions replace earlier ones in place.</summary>
    private sealed class Merged<T>(Func<T, string> key)
    {
        private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);
        public List<T> Items { get; } = [];

        public void AddRange(IEnumerable<T>? items)
        {
            foreach (var item in items ?? [])
            {
                var k = key(item);
                if (_index.TryGetValue(k, out var i)) Items[i] = item;
                else
                {
                    _index[k] = Items.Count;
                    Items.Add(item);
                }
            }
        }
    }

    // JSON shapes that differ from the engine definitions.

    private sealed class TerrainJson
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public string? Glyph { get; init; }
        public string Color { get; init; } = "White";
        public List<string>? Flags { get; init; }
        public int Priority { get; init; }
        public string? Mimic { get; init; }
        public string? Shop { get; init; }
        public int DigDifficulty { get; init; }
        public string? Description { get; init; }
    }

    private sealed class TrapJson
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public string? Glyph { get; init; } = "^";
        public string Color { get; init; } = "White";
        public int MinDepth { get; init; }
        public int MaxDepth { get; init; } = 127;
        public int Weight { get; init; } = 1;
        public string? Effect { get; init; }
        public bool Warding { get; init; }
        public bool Web { get; init; }
        public bool IsTrapDoor { get; init; }
        public string? Description { get; init; }
    }

    private sealed class TemplateJson
    {
        public required string Id { get; init; }
        public string? Name { get; init; }
        public MapTemplateKind Kind { get; init; }
        public int MinDepth { get; init; }
        public int MaxDepth { get; init; } = 127;
        public int Weight { get; init; } = 1;
        public bool Rotatable { get; init; } = true;
        public List<string>? Rows { get; init; }
    }

    private sealed class MonsterJson
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public string? Plural { get; init; }
        public string? Glyph { get; init; }
        public string Color { get; init; } = "White";
        public int Depth { get; init; }
        public int Rarity { get; init; } = 1;
        public int Speed { get; init; }
        public int HitPoints { get; init; } = 1;
        public int Armour { get; init; }
        public int Sleep { get; init; }
        public int Hearing { get; init; } = 20;
        public int Smell { get; init; }
        public int SpellFrequency { get; init; }
        public int InnateFrequency { get; init; }
        public int? SpellPower { get; init; }
        public List<string>? Spells { get; init; }
        public int Experience { get; init; }
        public List<MonsterBlowDef>? Blows { get; init; }
        public List<string>? Flags { get; init; }
        public List<MonsterFriendDef>? Friends { get; init; }
        public string? Description { get; init; }
        public List<string>? Shapes { get; init; }
        public List<string>? Mimics { get; init; }
    }

    private sealed class TownJson
    {
        public int Width { get; init; } = 66;
        public int Height { get; init; } = 22;
        public int LotColumns { get; init; } = 4;
        public int LotRows { get; init; } = 2;
        public string Rubble { get; init; } = "1d4";
        public List<ShopJson> Shops { get; init; } = [];
    }

    private sealed class ShopJson
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Terrain { get; init; }
        public string Owner { get; init; } = "";
        public bool IsHome { get; init; }
    }
}
