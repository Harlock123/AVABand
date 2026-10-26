using System.Text.Json;

namespace Angband.Data;

/// <summary>What became of a mod folder when the game's data was loaded.</summary>
public enum ModLoadState
{
    /// <summary>Layered on the game's data.</summary>
    Loaded,
    /// <summary>Switched off in Settings → Mods, so not read.</summary>
    Disabled,
    /// <summary>Tried, but it broke the data, so the game started without it.</summary>
    Failed,
}

/// <summary>One mod folder's fate at startup (<see cref="DataLoader.LoadWithMods"/>); <see cref="Error"/> says why it failed.</summary>
public sealed record ModLoadResult(string Name, string Directory, ModLoadState State, string? Error = null);

/// <summary>
/// A mod's optional <c>mod.json</c> (AVABand's own): who made it and what it does, shown in Settings → Mods.
/// Every field may be left out; a mod without the file goes by its folder's name.
/// </summary>
public sealed class ModManifest
{
    public string? Name { get; set; }
    public string? Author { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
}

/// <summary>One data file in a mod, and what it does to the game's own data.</summary>
/// <param name="File">The file, as the loader names it (<c>ava_monsters.json</c>, <c>templates/vaults.json</c>).</param>
/// <param name="Added">Entries with ids the game doesn't have.</param>
/// <param name="Replaced">Entries replacing the game's own of the same id.</param>
/// <param name="ReplacesWhole">A file read whole (town, constants, starting kit), so it replaces the game's.</param>
public sealed record ModFileChange(string File, int Added, int Replaced, bool ReplacesWhole = false)
{
    /// <summary>"ava_monsters.json: adds 1, replaces 0" — as Settings → Mods lists it.</summary>
    public string Summary => ReplacesWhole
        ? $"{File}: replaces the game's"
        : $"{File}: adds {Added}, replaces {Replaced}";
}

/// <summary>A mod folder as Settings → Mods shows it: its manifest, its files and their changes, and any problems.</summary>
public sealed class ModInfo
{
    /// <summary>The folder's name: how the mod is switched off and how a modded character names it.</summary>
    public required string Name { get; init; }
    public required string Directory { get; init; }
    public ModManifest? Manifest { get; init; }
    public List<ModFileChange> Files { get; } = [];
    /// <summary>Files that can't be read (bad JSON, the wrong shape) — the loader will refuse the mod.</summary>
    public List<string> Errors { get; } = [];
    /// <summary>Things worth knowing that don't stop it loading (a file the game never reads, say).</summary>
    public List<string> Notes { get; } = [];

    /// <summary>The manifest's name, else the folder's.</summary>
    public string Title => string.IsNullOrWhiteSpace(Manifest?.Name) ? Name : Manifest.Name!;
}

/// <summary>
/// Looks into mod folders without loading them (AVABand's own): reads each one's <c>mod.json</c>, finds the
/// data files the loader would read, and counts the entries each adds or replaces by comparing ids with the
/// game's own data. Also writes the example mod (Settings → Mods, "Make an example mod").
/// </summary>
public static class ModCatalog
{
    public const string ManifestFile = "mod.json";
    /// <summary>The folder name of the example mod.</summary>
    public const string ExampleName = "example-mod";

    private enum Shape { Keyed, Whole, Colors, Lines }

    private sealed record FileKind(string File, string Group, Shape Shape, Func<JsonElement, string?>? Key = null);

    private static string? Id(JsonElement e) => Prop(e, "id");

    // Every file the loader reads (DataLoader.Load), with what its entries are keyed by. Files in the same
    // group share one registry (monsters.json and ava_monsters.json both hold monsters).
    private static readonly FileKind[] Kinds =
    [
        new(DataLoader.TerrainFile, "terrain", Shape.Keyed, Id),
        new(DataLoader.AvaTerrainFile, "terrain", Shape.Keyed, Id),
        new(DataLoader.TrapsFile, "traps", Shape.Keyed, Id),
        new(DataLoader.ProfilesFile, "profiles", Shape.Keyed, Id),
        new(DataLoader.ElementsFile, "elements", Shape.Keyed, Id),
        new(DataLoader.TimedEffectsFile, "timed", Shape.Keyed, Id),
        new(DataLoader.BlowMethodsFile, "blow_methods", Shape.Keyed, Id),
        new(DataLoader.BlowEffectsFile, "blow_effects", Shape.Keyed, Id),
        new(DataLoader.MonstersFile, "monsters", Shape.Keyed, Id),
        new(DataLoader.AvaMonstersFile, "monsters", Shape.Keyed, Id),
        new(DataLoader.AvaSeasonalMonstersFile, "seasonal_monsters", Shape.Keyed, Id),
        new(DataLoader.MonsterSpellsFile, "monster_spells", Shape.Keyed, Id),
        new(DataLoader.RealmsFile, "realms", Shape.Keyed, Id),
        new(DataLoader.ClassesFile, "classes", Shape.Keyed, Id),
        new(DataLoader.AvaClassesFile, "ava_classes", Shape.Keyed, Id),
        new(DataLoader.SpellsFile, "spells", Shape.Keyed, Id),
        new(DataLoader.RacesFile, "races", Shape.Keyed, Id),
        new(DataLoader.AvaRacesFile, "ava_races", Shape.Keyed, Id),
        new(DataLoader.StoresFile, "stores", Shape.Keyed, Id),
        new(DataLoader.ObjectBasesFile, "object_bases", Shape.Keyed, Id),
        new(DataLoader.AvaObjectBasesFile, "object_bases", Shape.Keyed, Id),
        new(DataLoader.ObjectsFile, "objects", Shape.Keyed, Id),
        new(DataLoader.AvaObjectsFile, "objects", Shape.Keyed, Id),
        new(DataLoader.EgosFile, "egos", Shape.Keyed, Id),
        new(DataLoader.AvaEgosFile, "egos", Shape.Keyed, Id),
        new(DataLoader.ArtifactsFile, "artifacts", Shape.Keyed, Id),
        new(DataLoader.AvaArtifactsFile, "artifacts", Shape.Keyed, Id),
        new(DataLoader.CursesFile, "curses", Shape.Keyed, Id),
        new(DataLoader.AvaCursesFile, "curses", Shape.Keyed, Id),
        new(DataLoader.SummonsFile, "summons", Shape.Keyed, Id),
        new(DataLoader.FlavorsFile, "flavors", Shape.Keyed, Id),
        new(DataLoader.ChestTrapsFile, "chest_traps", Shape.Keyed, Id),
        new(DataLoader.QuestsFile, "quests", Shape.Keyed, Id),
        new(DataLoader.AvaQuestsFile, "ava_quests", Shape.Keyed, Id),
        new(DataLoader.HistoriesFile, "histories", Shape.Keyed, e => Prop(e, "chart")),
        new(DataLoader.ShapesFile, "shapes", Shape.Keyed, Id),
        new(DataLoader.MonsterBasesFile, "monster_bases", Shape.Keyed, Id),
        new(DataLoader.PainFile, "pain", Shape.Keyed, e => Prop(e, "type")),
        new(DataLoader.ObjectPropertiesFile, "object_properties", Shape.Keyed, e => Prop(e, "type") + ":" + Prop(e, "code")),
        new(DataLoader.SlaysFile, "slays", Shape.Keyed, e => Prop(e, "code")),
        new(DataLoader.BrandsFile, "brands", Shape.Keyed, e => Prop(e, "code")),
        new(DataLoader.PlayerPropertiesFile, "player_properties", Shape.Keyed, e => Prop(e, "type") + ":" + (Prop(e, "code") ?? Prop(e, "name"))),
        new(DataLoader.PitsFile, "pits", Shape.Keyed, Id),
        new(DataLoader.TemplatesFolder + "/" + DataLoader.VaultsFile, "vaults", Shape.Keyed, Id),
        new(DataLoader.TemplatesFolder + "/" + DataLoader.AvaVaultsFile, "vaults", Shape.Keyed, Id),
        new(DataLoader.TemplatesFolder + "/" + DataLoader.RoomTemplatesFile, "room_templates", Shape.Keyed, Id),
        new(DataLoader.TownFile, "town", Shape.Whole),
        new(DataLoader.ConstantsFile, "constants", Shape.Whole),
        new(DataLoader.StartingKitFile, "starting_kit", Shape.Whole),
        new(DataLoader.ColorsFile, "colors", Shape.Colors),
        new(DataLoader.HintsFile, "hints", Shape.Lines),
        new(DataLoader.NamesFile, "names", Shape.Lines),
    ];

    /// <summary>The data files a mod may hold (the same names as the game's own), for the help and the checks.</summary>
    public static IReadOnlyList<string> ModdableFiles { get; } = [.. Kinds.Select(k => k.File)];

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Every mod folder under <paramref name="root"/>, in load order, each compared with <paramref name="baseDirectory"/>.</summary>
    public static IReadOnlyList<ModInfo> Discover(string root, string? baseDirectory = null)
    {
        var dirs = DataLoader.UserModDirectories(root).ToList();
        if (dirs.Count == 0) return []; // (no need to read the game's data)
        var baseIds = BaseIds(baseDirectory ?? DataLoader.DefaultDataDirectory);
        return [.. dirs.Select(d => Inspect(d, baseIds))];
    }

    /// <summary>One mod folder, compared with the game's data in <paramref name="baseDirectory"/>.</summary>
    public static ModInfo Inspect(string modDirectory, string? baseDirectory = null) =>
        Inspect(modDirectory, BaseIds(baseDirectory ?? DataLoader.DefaultDataDirectory));

    /// <summary>Reads a folder's <c>mod.json</c>: null without one; a problem goes in <paramref name="errors"/>.</summary>
    public static ModManifest? ReadManifest(string modDirectory, List<string>? errors = null)
    {
        var path = Path.Combine(modDirectory, ManifestFile);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(path), ManifestOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            errors?.Add($"{ManifestFile}: {ex.Message}");
            return null;
        }
    }

    private static ModInfo Inspect(string modDirectory, Dictionary<string, HashSet<string>> baseIds)
    {
        var errors = new List<string>();
        var info = new ModInfo
        {
            Name = DataLoader.ModName(modDirectory),
            Directory = modDirectory,
            Manifest = ReadManifest(modDirectory, errors),
        };
        info.Errors.AddRange(errors);
        foreach (var kind in Kinds)
        {
            var path = Path.Combine(modDirectory, kind.File);
            if (!File.Exists(path)) continue;
            if (Parse(path, kind.File, info.Errors) is not { } doc) continue;
            using (doc)
            {
                var root = doc.RootElement;
                switch (kind.Shape)
                {
                    case Shape.Whole:
                        info.Files.Add(new ModFileChange(kind.File, 0, 0, ReplacesWhole: true));
                        break;
                    case Shape.Lines when root.ValueKind == JsonValueKind.Array:
                        info.Files.Add(new ModFileChange(kind.File, root.GetArrayLength(), 0));
                        break;
                    case Shape.Colors when root.ValueKind == JsonValueKind.Object:
                    {
                        var known = baseIds.GetValueOrDefault(kind.Group) ?? [];
                        var names = root.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        var replaced = names.Count(n => known.Contains(n));
                        info.Files.Add(new ModFileChange(kind.File, names.Count - replaced, replaced));
                        break;
                    }
                    case Shape.Keyed when root.ValueKind == JsonValueKind.Array:
                    {
                        var known = baseIds.GetValueOrDefault(kind.Group) ?? [];
                        var keys = root.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Object ? kind.Key!(e) : null).ToList();
                        var missing = keys.Count(k => k is null);
                        if (missing > 0) info.Errors.Add($"{kind.File}: {missing} {(missing == 1 ? "entry has" : "entries have")} no id.");
                        var unique = keys.OfType<string>().Distinct(StringComparer.Ordinal).ToList();
                        var replaced = unique.Count(known.Contains);
                        info.Files.Add(new ModFileChange(kind.File, unique.Count - replaced, replaced));
                        break;
                    }
                    default:
                        info.Errors.Add($"{kind.File}: expected {(kind.Shape == Shape.Colors ? "an object of colour names" : "a list [ ... ]")}.");
                        break;
                }
            }
        }

        // JSON the loader never reads: most likely a misspelt name, so say so.
        var readable = Kinds.Select(k => k.File).Append(ManifestFile).ToHashSet(StringComparer.Ordinal);
        foreach (var file in JsonFiles(modDirectory, ""))
            if (!readable.Contains(file))
                info.Notes.Add($"{file} is not a file AVABand reads (check its name), so it is ignored.");
        if (info.Files.Count == 0 && info.Errors.Count == 0)
            info.Notes.Add("No data files: this mod changes nothing.");
        return info;
    }

    private static IEnumerable<string> JsonFiles(string directory, string prefix)
    {
        if (!System.IO.Directory.Exists(directory)) yield break;
        foreach (var file in System.IO.Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal))
            yield return prefix + Path.GetFileName(file);
        if (prefix.Length == 0)
            foreach (var file in JsonFiles(Path.Combine(directory, DataLoader.TemplatesFolder), DataLoader.TemplatesFolder + "/"))
                yield return file;
    }

    private static JsonDocument? Parse(string path, string file, List<string>? errors)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonDocument.Parse(stream, DocumentOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            errors?.Add($"{file}: {ex.Message}");
            return null;
        }
    }

    /// <summary>The ids the game's own data has, by group (one read of the base folder for a whole Discover).</summary>
    private static Dictionary<string, HashSet<string>> BaseIds(string baseDirectory)
    {
        var ids = new Dictionary<string, HashSet<string>>();
        foreach (var kind in Kinds.Where(k => k.Shape is Shape.Keyed or Shape.Colors))
        {
            var set = ids.TryGetValue(kind.Group, out var s) ? s
                : ids[kind.Group] = new HashSet<string>(kind.Shape == Shape.Colors ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var path = Path.Combine(baseDirectory, kind.File);
            if (!File.Exists(path) || Parse(path, kind.File, null) is not { } doc) continue;
            using (doc)
            {
                if (kind.Shape == Shape.Colors && doc.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var p in doc.RootElement.EnumerateObject()) set.Add(p.Name);
                else if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var e in doc.RootElement.EnumerateArray())
                        if (e.ValueKind == JsonValueKind.Object && kind.Key!(e) is { } key) set.Add(key);
            }
        }
        return ids;
    }

    /// <summary>A property as text, its name matched ignoring case (as the loader reads them); null if absent.</summary>
    private static string? Prop(JsonElement e, string name)
    {
        foreach (var p in e.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    _ => p.Value.GetRawText(),
                };
        return null;
    }

    /// <summary>
    /// Writes the example mod into <paramref name="root"/> (<c>example-mod/</c>: a <c>mod.json</c>, a new monster
    /// in <c>ava_monsters.json</c> and a new item in <c>ava_objects.json</c>) and returns its folder; null if a
    /// folder of that name is already there (it is never overwritten).
    /// </summary>
    public static string? WriteExample(string root)
    {
        var dir = Path.Combine(root, ExampleName);
        if (System.IO.Directory.Exists(dir)) return null;
        System.IO.Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ManifestFile), ExampleManifest);
        File.WriteAllText(Path.Combine(dir, DataLoader.AvaMonstersFile), ExampleMonsters);
        File.WriteAllText(Path.Combine(dir, DataLoader.AvaObjectsFile), ExampleObjects);
        return dir;
    }

    // The example mod's files — the same JSON as the worked example in the help (Help → Mods).

    internal const string ExampleManifest = """
        {
          "name": "Example mod",
          "author": "AVABand",
          "version": "1.0",
          "description": "A small working mod to copy from: adds the mud newt (a monster near the surface) and the miner's pick-knife (a dagger). Switch it off in Settings → Mods, or delete its folder."
        }
        """;

    internal const string ExampleMonsters = """
        [
          {
            "id": "example_mud_newt",
            "name": "mud newt",
            "glyph": "R",
            "color": "Umber",
            "base": "reptile",
            "depth": 2,
            "rarity": 1,
            "speed": 0,
            "hitPoints": 6,
            "armour": 6,
            "sleep": 20,
            "hearing": 12,
            "smell": 20,
            "experience": 3,
            "blows": [ { "method": "bite", "effect": "hurt", "damage": "1d3" } ],
            "flags": [ "ANIMAL", "HURT_COLD", "CLEAR_WEB" ],
            "description": "A slick brown newt as long as your arm, at home in the wet mud of the upper caves."
          }
        ]
        """;

    internal const string ExampleObjects = """
        [
          {
            "id": "example_pick_knife",
            "name": "Miner's Pick-knife~",
            "base": "sword",
            "level": 2,
            "commonness": 20,
            "minDepth": 1,
            "cost": 40,
            "weight": 14,
            "damage": "1d5",
            "flags": [ "THROWING" ],
            "color": "Slate",
            "description": "A short, heavy blade with a spike on its back, made for prising ore from the rock."
          }
        ]
        """;
}
