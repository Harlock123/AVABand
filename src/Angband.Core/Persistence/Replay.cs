using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;

namespace Angband.Core.Persistence;

/// <summary>How a recorded game ended (or stood when last written), to check a replay against.</summary>
public sealed record ReplayEnd(long GameTurn, int Depth, int X, int Y, int Hp, bool Dead);

/// <summary>
/// A replay (AVABand's own): the game as it stood when recording began (a save, inside), then every
/// command and every choice made outside commands (targets, options, ignoring...), in order. Every
/// game is deterministic, so playing the steps back from the start reproduces it exactly — and the
/// end state recorded with it says whether it did. Files are gzipped JSON (.avareplay).
/// </summary>
public sealed class ReplayFile
{
    public const int CurrentVersion = 1;
    public const string Extension = ".avareplay";

    public int Version { get; set; } = CurrentVersion;
    public DateTime WrittenUtc { get; set; }
    public string Name { get; set; } = "";
    public string Race { get; set; } = "";
    public string Class { get; set; } = "";
    public int Level { get; set; }
    public int MaxDepth { get; set; }
    public string? KilledBy { get; set; }
    public string StartSave { get; set; } = "";
    public List<JsonNode?> Steps { get; set; } = [];
    public ReplayEnd? End { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        using (var file = File.Create(temp))
        using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
            JsonSerializer.Serialize(gzip, this, Json);
        File.Move(temp, path, overwrite: true);
    }

    public static ReplayFile Read(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            var replay = JsonSerializer.Deserialize<ReplayFile>(gzip, Json) ?? throw new SaveGameException("The replay is empty.");
            if (replay.Version > CurrentVersion) throw new SaveGameException("The replay comes from a newer AVABand.");
            return replay;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or EndOfStreamException)
        {
            throw new SaveGameException($"The replay could not be read: {ex.Message}", ex);
        }
    }
}

/// <summary>Records a game as it is played (set as <see cref="GameSession.Recorder"/>).</summary>
public sealed class ReplayRecorder
{
    private readonly string _startSave;
    private readonly List<JsonNode?> _steps = [];

    /// <summary>Starts recording from the game as it stands now.</summary>
    public ReplayRecorder(GameSession game)
    {
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        _startSave = Convert.ToBase64String(stream.ToArray());
    }

    public int StepCount => _steps.Count;

    public void Command(GameCommand command) => _steps.Add(new JsonObject { ["op"] = "cmd", ["cmd"] = ReplayCodec.Encode(command) });

    /// <summary>A choice made outside a command: its name and arguments.</summary>
    public void Call(string op, params object?[] args)
    {
        var node = new JsonObject { ["op"] = op };
        var list = new JsonArray();
        foreach (var arg in args) list.Add(ReplayCodec.EncodeValue(arg));
        node["args"] = list;
        _steps.Add(node);
    }

    /// <summary>The replay so far, with the game's state now as its end.</summary>
    public ReplayFile ToFile(GameSession game)
    {
        var p = game.Player;
        return new ReplayFile
        {
            WrittenUtc = DateTime.UtcNow,
            Name = p.Name,
            Race = p.Race?.Name ?? "",
            Class = p.Class?.Name ?? "",
            Level = p.Level,
            MaxDepth = p.MaxDepth,
            KilledBy = p.IsDead ? p.KilledBy : null,
            StartSave = _startSave,
            Steps = [.. _steps.Select(s => s?.DeepClone())],
            End = ReplayCodec.EndOf(game),
        };
    }
}

/// <summary>Plays a replay back, a step at a time.</summary>
public sealed class ReplayPlayer
{
    private readonly ReplayFile _file;

    public ReplayPlayer(GameData data, ReplayFile file)
    {
        _file = file;
        using var stream = new MemoryStream(Convert.FromBase64String(file.StartSave));
        Game = SaveGame.Load(data, stream);
        Game.IsReplay = true;
    }

    public GameSession Game { get; }
    public ReplayFile File => _file;
    public int Position { get; private set; }
    public int Count => _file.Steps.Count;
    public bool Done => Position >= Count;

    /// <summary>Whether the game, played to the end, stands as recorded (null until done).</summary>
    public bool? Matches => !Done ? null : _file.End is null || _file.End == ReplayCodec.EndOf(Game);

    /// <summary>Plays the next step; false when there are none left.</summary>
    public bool Step()
    {
        if (Done) return false;
        var step = _file.Steps[Position++]!.AsObject();
        var op = step["op"]!.GetValue<string>();
        var args = step["args"]?.AsArray();
        var g = Game;
        string S(int i) => args![i]!.GetValue<string>();
        bool B(int i) => args![i]!.GetValue<bool>();
        switch (op)
        {
            case "cmd": g.Execute(ReplayCodec.DecodeCommand(g, step["cmd"]!)); break;
            case "target-monster":
                if (g.Level.Monsters.At(new Loc(args![0]!.GetValue<int>(), args[1]!.GetValue<int>())) is { } m) g.SetTarget(m);
                break;
            case "target-loc": g.SetTarget(new Loc(args![0]!.GetValue<int>(), args[1]!.GetValue<int>())); break;
            case "target-closest": g.TargetClosest(B(0)); break;
            case "option": g.SetOption(S(0), B(1)); break;
            case "interface-options":
                g.ApplyInterfaceOptions(args![0]!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<bool>()));
                break;
            case "debug": g.MarkDebugUsed(); break;
            case "ignore-kind": if (g.Data.Object(S(0)) is { } kind) g.SetKindIgnored(kind, B(1)); break;
            case "ignore-ego": if (g.Data.Egos.FirstOrDefault(e => e.Id == S(0)) is { } ego) g.SetEgoIgnored(ego, B(1)); break;
            case "ignore-quality": g.SetIgnoreQuality(S(0), Enum.Parse<IgnoreLevel>(S(1))); break;
            case "inscribe-kind": if (g.Data.Object(S(0)) is { } k) g.SetAutoInscription(k, args![1]?.GetValue<string>()); break;
            case "note": g.AddNote(S(0)); break;
            case "shops-pay": g.SetShopsPay(B(0)); break;
            case "aim-next": g.AimAtTargetNext = B(0); break;
            case "recall-sets-depth": g.RecallSetsDepth = args![0]?.GetValue<bool>(); break;
            case "recall-choice": g.RecallChoice = args![0]?.GetValue<int>(); break;
        }
        return true;
    }
}

/// <summary>Commands and their arguments to and from JSON (items by serial number, places as x,y).</summary>
public static class ReplayCodec
{
    public static ReplayEnd EndOf(GameSession g) =>
        new(g.GameTurn, g.Player.Depth, g.Player.Position.X, g.Player.Position.Y, g.Player.Hp, g.Player.IsDead);

    private static readonly Dictionary<string, Type> CommandTypes = typeof(GameCommand).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(GameCommand)) && !t.IsAbstract).ToDictionary(t => t.Name, StringComparer.Ordinal);

    /// <summary>The constructor a command is built with: its record's primary one (the most parameters).</summary>
    private static ConstructorInfo Constructor(Type t) => t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();

    public static JsonNode Encode(GameCommand command)
    {
        var type = command.GetType();
        var node = new JsonObject { ["$"] = type.Name };
        foreach (var p in Constructor(type).GetParameters())
            node[p.Name!] = EncodeValue(type.GetProperty(p.Name!)?.GetValue(command));
        return node;
    }

    public static JsonNode? EncodeValue(object? value) => value switch
    {
        null => null,
        GameCommand c => Encode(c),
        Item item => new JsonObject { ["item"] = item.Serial },
        CurseChoice choice => new JsonObject { ["item"] = choice.Item.Serial, ["curse"] = choice.Curse },
        Loc l => new JsonObject { ["x"] = l.X, ["y"] = l.Y },
        Enum e => JsonValue.Create(e.ToString()),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        char c => JsonValue.Create(c.ToString()),
        IReadOnlyDictionary<string, bool> d => new JsonObject(d.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)JsonValue.Create(kv.Value)))),
        _ => throw new NotSupportedException($"A replay can't record a {value.GetType().Name}."),
    };

    public static GameCommand DecodeCommand(GameSession game, JsonNode node)
    {
        var name = node["$"]!.GetValue<string>();
        if (!CommandTypes.TryGetValue(name, out var type)) throw new SaveGameException($"The replay has a command this AVABand doesn't know: {name}.");
        var ctor = Constructor(type);
        var args = ctor.GetParameters().Select(p => DecodeValue(game, node[p.Name!], p.ParameterType)).ToArray();
        return (GameCommand)ctor.Invoke(args);
    }

    private static object? DecodeValue(GameSession game, JsonNode? node, Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (node is null) return null;
        if (typeof(GameCommand).IsAssignableFrom(target)) return DecodeCommand(game, node);
        if (target == typeof(Item)) return FindItem(game, node["item"]!.GetValue<long>());
        if (target == typeof(CurseChoice)) return new CurseChoice(FindItem(game, node["item"]!.GetValue<long>()), node["curse"]!.GetValue<string>());
        if (target == typeof(Loc)) return new Loc(node["x"]!.GetValue<int>(), node["y"]!.GetValue<int>());
        if (target.IsEnum) return Enum.Parse(target, node.GetValue<string>());
        if (target == typeof(char)) return node.GetValue<string>()[0];
        return node.Deserialize(target);
    }

    /// <summary>An item by its serial number: carried, underfoot or on the level, or in a store.</summary>
    private static Item FindItem(GameSession game, long serial) =>
        game.Player.Inventory.All.FirstOrDefault(i => i.Serial == serial)
        ?? game.Level.Objects.All.Select(o => o.Item).FirstOrDefault(i => i.Serial == serial)
        ?? game.Stores.Values.SelectMany(s => s.Stock).FirstOrDefault(i => i.Serial == serial)
        ?? throw new SaveGameException($"The replay refers to an item that isn't there (#{serial}).");
}
