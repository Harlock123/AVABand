using System.Text.Json;

namespace Angband.Audio;

/// <summary>
/// A folder with a <c>soundpack.json</c>: sound effects keyed by event name (Angband's message
/// names: HIT, MISS, OPENDOOR, LEVEL, BR_FIRE...) and music playlists keyed by mood (town,
/// town_night, dungeon, deep). Each event or mood lists files (one is picked at random). A pack may
/// provide effects, music, or both; missing entries are simply silent.
/// </summary>
public sealed class SoundPack
{
    public const string FileName = "soundpack.json";

    public required string Id { get; init; }
    public required string Directory { get; init; }
    public required string Name { get; init; }
    public string Author { get; init; } = "";
    public string License { get; init; } = "";
    public string Source { get; init; } = "";
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Sounds { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Music { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
    /// <summary>Per-event volume scale (default 1).</summary>
    public IReadOnlyDictionary<string, float> Volumes { get; init; } = new Dictionary<string, float>();

    public bool HasSounds => Sounds.Count > 0;
    public bool HasMusic => Music.Count > 0;

    public string Resolve(string relative) => Path.Combine(Directory, relative.Replace('/', Path.DirectorySeparatorChar));

    public IEnumerable<string> ReferencedFiles() =>
        Sounds.Values.Concat(Music.Values).SelectMany(f => f).Distinct(StringComparer.Ordinal);

    private sealed class Json
    {
        public string? Name { get; init; }
        public string? Author { get; init; }
        public string? License { get; init; }
        public string? Source { get; init; }
        public Dictionary<string, List<string>>? Sounds { get; init; }
        public Dictionary<string, List<string>>? Music { get; init; }
        public Dictionary<string, float>? Volumes { get; init; }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Loads and validates a pack; throws <see cref="InvalidDataException"/> listing every problem.</summary>
    public static SoundPack Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        Json json;
        try
        {
            json = JsonSerializer.Deserialize<Json>(File.ReadAllText(path), Options) ?? new Json();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            throw new InvalidDataException($"{path}: {ex.Message}", ex);
        }

        static IReadOnlyDictionary<string, IReadOnlyList<string>> Lists(Dictionary<string, List<string>>? d) =>
            (d ?? []).ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.OrdinalIgnoreCase);

        var pack = new SoundPack
        {
            Id = System.IO.Path.GetFileName(directory.TrimEnd(System.IO.Path.DirectorySeparatorChar)),
            Directory = directory,
            Name = json.Name ?? System.IO.Path.GetFileName(directory),
            Author = json.Author ?? "",
            License = json.License ?? "",
            Source = json.Source ?? "",
            Sounds = Lists(json.Sounds),
            Music = Lists(json.Music),
            Volumes = new Dictionary<string, float>(json.Volumes ?? [], StringComparer.OrdinalIgnoreCase),
        };

        var errors = new List<string>();
        foreach (var file in pack.ReferencedFiles())
        {
            if (file.Contains("..") || System.IO.Path.IsPathRooted(file)) errors.Add($"'{file}' must be a relative path inside the pack.");
            else if (!AudioDecoders.Extensions.Contains(System.IO.Path.GetExtension(file))) errors.Add($"'{file}' is not .wav, .ogg or .mp3.");
            else if (!File.Exists(pack.Resolve(file))) errors.Add($"missing file '{file}'.");
        }
        if (errors.Count > 0)
            throw new InvalidDataException($"Sound pack '{directory}' is invalid:{Environment.NewLine}" +
                                           string.Join(Environment.NewLine, errors.Select(e => "  - " + e)));
        return pack;
    }
}

/// <summary>Finds sound packs: subfolders with a <c>soundpack.json</c> under the given roots.</summary>
public static class SoundPackCatalog
{
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "soundpacks");

    public static string UserDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVABand", "soundpacks");

    /// <summary>Loads every pack; broken ones go to <paramref name="problems"/>. User packs override bundled ones by folder name.</summary>
    public static IReadOnlyList<SoundPack> Discover(IEnumerable<string> roots, List<string>? problems = null)
    {
        var found = new Dictionary<string, SoundPack>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        foreach (var dir in Directory.GetDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(Path.Combine(dir, SoundPack.FileName))) continue;
            try
            {
                var pack = SoundPack.Load(dir);
                found[pack.Id] = pack;
            }
            catch (InvalidDataException ex)
            {
                problems?.Add(ex.Message);
            }
        }
        return found.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<SoundPack> DiscoverDefault(List<string>? problems = null) =>
        Discover([DefaultDirectory, UserDirectory], problems);
}
