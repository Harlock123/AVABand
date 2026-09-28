using Angband.Core.Definitions;
using Angband.Core.Generation.Generators;
using Angband.Core.Generation.Rooms;
using Angband.Core.Geometry;
using Angband.Core.Randomness;
using Angband.Core.World;

namespace Angband.Core.Generation;

/// <summary>How the player arrived, which decides the connected staircase under them.</summary>
public enum StairArrival
{
    None,
    Descended,
    Ascended,
}

/// <param name="Depth">Dungeon level; 0 is the town.</param>
/// <param name="Seed">Seed for this level. The same request always yields the same level.</param>
/// <param name="Arrival">How the player got here.</param>
/// <param name="ProfileId">Force a specific profile (testing, debug commands); null picks by weight.</param>
/// <param name="ConnectStairs">Arrive on a staircase leading back the way you came (Angband birth_connect_stairs); null uses the data default.</param>
/// <param name="Joins">
/// Persistent levels (Angband birth_levels_persist): staircases this level must have, where the
/// stored levels above and below have theirs — an up staircase under each down staircase of the
/// level above, a down staircase over each up staircase below. Stairs of a direction that has
/// joins are only the joins. Labyrinths aren't generated for persistent dungeons.
/// </param>
/// <param name="PreferredStart">Start here if it is a staircase (persistent levels arrive where they left).</param>
public sealed record LevelRequest(int Depth, ulong Seed, StairArrival Arrival = StairArrival.None, string? ProfileId = null,
    bool? ConnectStairs = null, IReadOnlyList<StairJoin>? Joins = null, Loc? PreferredStart = null, bool Persistent = false);

/// <summary>A staircase a persistent level must have at <see cref="Loc"/> (a down staircase if <see cref="Down"/>).</summary>
public sealed record StairJoin(Loc Loc, bool Down);

public sealed record GeneratedLevel(Level Level, Loc PlayerStart, int Attempts);

/// <summary>Entry point for level generation. Deterministic for a given <see cref="LevelRequest"/>.</summary>
public sealed class DungeonGenerator
{
    public const int MaxAttempts = 100;

    private static readonly Dictionary<string, ILevelGenerator> Generators = new ILevelGenerator[]
    {
        new TownGenerator(),
        new ClassicGenerator(),
        new CavernGenerator(),
        new LabyrinthGenerator(),
    }.ToDictionary(g => g.Id, StringComparer.Ordinal);

    private readonly GameData _data;

    public DungeonGenerator(GameData data)
    {
        _data = data;
        foreach (var profile in data.Profiles)
        {
            if (!Generators.ContainsKey(profile.Generator))
                throw new GameDataException(
                    $"Profile '{profile.Id}' uses unknown generator '{profile.Generator}'. Known: {string.Join(", ", Generators.Keys)}.");
            foreach (var room in profile.Rooms)
                if (!RoomBuilders.TryGet(room.Type, out _))
                    throw new GameDataException(
                        $"Profile '{profile.Id}' uses unknown room type '{room.Type}'. Known: {string.Join(", ", RoomBuilders.Types)}.");
        }
        if (!data.Profiles.Any(p => p.Generator == "town"))
            throw new GameDataException("No profile uses the 'town' generator.");
    }

    public static IReadOnlyCollection<string> GeneratorIds => Generators.Keys;

    public GeneratedLevel Generate(LevelRequest request)
    {
        if (request.Depth < 0 || request.Depth > _data.Constants.MaxDepth)
            throw new ArgumentOutOfRangeException(nameof(request), $"Depth {request.Depth} is out of range.");

        var rng = new GameRandom(request.Seed);
        var errors = (IReadOnlyList<string>)[];

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var profile = ChooseProfile(rng, request.Depth, request.ProfileId, request.Persistent);
            var ctx = new GenContext(_data, profile, request.Depth, rng);
            if (!Generators[profile.Generator].Generate(ctx)) continue;
            if (request.Joins is { Count: > 0 } joins) StairJoins.Apply(ctx, joins);

            var start = request.PreferredStart is { } want && ctx.Level.InBoundsFully(want)
                        && ctx.Level.FeatureAt(want).HasAny(TerrainFlags.UpStair | TerrainFlags.DownStair)
                ? want
                : Allocator.PlacePlayer(ctx, request.Arrival, request.ConnectStairs ?? _data.Constants.ConnectedStairs);
            if (start is not { } s) continue;

            errors = LevelValidator.Validate(ctx.Level, s, _data.Constants);
            if (errors.Count > 0) continue;

            ctx.Level.ProfileId = profile.Id;
            ctx.Level.Seed = request.Seed;
            return new GeneratedLevel(ctx.Level, s, attempt);
        }

        throw new InvalidOperationException(
            $"Failed to generate depth {request.Depth} (seed {request.Seed}) after {MaxAttempts} attempts. " +
            $"Last errors: {string.Join("; ", errors)}");
    }

    public DungeonProfileDef ChooseProfile(GameRandom rng, int depth, string? forcedId = null, bool persistent = false)
    {
        if (forcedId is not null)
            return _data.Profiles.FirstOrDefault(p => p.Id == forcedId)
                   ?? throw new ArgumentException($"Unknown profile '{forcedId}'.", nameof(forcedId));

        if (depth == 0) return _data.Profiles.First(p => p.Generator == "town");

        var eligible = _data.Profiles
            .Where(p => p.Generator != "town" && p.MinDepth <= depth && p.MaxDepth >= depth)
            .Where(p => !persistent || p.Generator != "labyrinth") // 4.2: "no labyrinth levels in persistent dungeons"
            .ToList();
        return rng.PickWeighted(eligible, p => p.Weight)
               ?? throw new GameDataException($"No dungeon profile is eligible for depth {depth}.");
    }
}
