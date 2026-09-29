using Angband.Core.Definitions;
using Angband.Core.Generation.Generators;
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
/// <param name="ProfileId">Force a specific profile (testing, debug commands); null chooses as 4.2.5 does.</param>
/// <param name="ConnectStairs">Arrive on a staircase leading back the way you came (Angband birth_connect_stairs); null uses the data default.</param>
/// <param name="Joins">
/// Persistent levels (Angband birth_levels_persist): staircases this level must have, where the
/// stored levels above and below have theirs — an up staircase under each down staircase of the
/// level above, a down staircase over each up staircase below.
/// </param>
/// <param name="PreferredStart">Start here if it is a staircase (persistent levels arrive where they left).</param>
/// <param name="Quest">A quest level (Sauron's, Morgoth's): always classic, and its stairs only go up.</param>
/// <param name="AboveStored">A persistent level: the level above is kept (so its joins are this level's up stairs).</param>
/// <param name="BelowStored">A persistent level: the level below is kept.</param>
/// <param name="OneOffAbove">
/// Persistent levels with no level kept above but one kept two above (Angband one_off_above): that
/// level's down staircases, which this level's up staircases keep clear of, so the level between
/// can meet both when it is made.
/// </param>
/// <param name="OneOffBelow">Likewise the up staircases of a level kept two below (Angband one_off_below).</param>
public sealed record LevelRequest(int Depth, ulong Seed, StairArrival Arrival = StairArrival.None, string? ProfileId = null,
    bool? ConnectStairs = null, IReadOnlyList<StairJoin>? Joins = null, Loc? PreferredStart = null, bool Persistent = false,
    bool Quest = false, bool AboveStored = false, bool BelowStored = false,
    IReadOnlyList<StairJoin>? OneOffAbove = null, IReadOnlyList<StairJoin>? OneOffBelow = null,
    bool NoDiagonalSqueezes = false, string? QuestRoom = null, bool QuestRoomOptional = false);

/// <summary>A staircase a persistent level must have at <see cref="Loc"/> (a down staircase if <see cref="Down"/>).</summary>
public sealed record StairJoin(Loc Loc, bool Down);

public sealed record GeneratedLevel(Level Level, Loc PlayerStart, int Attempts);

/// <summary>
/// Entry point for level generation (Angband generate.c cave_generate): up to a hundred attempts,
/// each choosing a profile and running its builder. The town is AVABand's own. Deterministic for a
/// given <see cref="LevelRequest"/>.
/// </summary>
public sealed class DungeonGenerator
{
    public const int MaxAttempts = 100;

    private readonly GameData _data;

    public DungeonGenerator(GameData data)
    {
        _data = data;
        foreach (var profile in data.Profiles)
            foreach (var room in profile.Rooms)
                if (!Cave.RoomNames.Contains(room.Name))
                    throw new GameDataException($"Profile '{profile.Id}' uses unknown room '{room.Name}'. Known: {string.Join(", ", Cave.RoomNames)}.");
    }

    /// <summary>The room builders 4.2.5 has (by room profile name).</summary>
    public static IReadOnlySet<string> RoomNames => Cave.RoomNames;

    /// <summary>The level builders 4.2.5 has (by profile name).</summary>
    public static IReadOnlyCollection<string> GeneratorIds { get; } =
        ["town", "classic", "labyrinth", "cavern", "modified", "moria", "lair", "gauntlet", "hard centre"];

    public GeneratedLevel Generate(LevelRequest request)
    {
        if (request.Depth < 0 || request.Depth > _data.Constants.MaxDepth)
            throw new ArgumentOutOfRangeException(nameof(request), $"Depth {request.Depth} is out of range.");

        var rng = new GameRandom(request.Seed);
        var errors = (IReadOnlyList<string>)[];
        string? error = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (request.Depth == 0 && request.ProfileId is null or "town")
            {
                if (GenerateTown(request, rng) is { } town) return town with { Attempts = attempt };
                continue;
            }
            var profile = ChooseProfile(rng, request.Depth, request.ProfileId, request.Quest || request.QuestRoom is not null && !request.QuestRoomOptional);
            var (level, start, why) = new Cave(_data, rng).Build(request, profile);
            if (level is null)
            {
                error = why;
                continue;
            }
            errors = LevelValidator.Validate(level, start, _data.Constants, request.Quest);
            if (errors.Count > 0) continue;
            if (request.NoDiagonalSqueezes) Connectivity.OpenDiagonalSqueezes(level);
            level.ProfileId = profile.Id;
            level.Seed = request.Seed;
            return new GeneratedLevel(level, start, attempt);
        }

        throw new InvalidOperationException(
            $"Failed to generate depth {request.Depth} (seed {request.Seed}) after {MaxAttempts} attempts. " +
            $"Last errors: {string.Join("; ", errors.DefaultIfEmpty(error ?? ""))}");
    }

    private GeneratedLevel? GenerateTown(LevelRequest request, GameRandom rng)
    {
        var profile = _data.Profiles.FirstOrDefault(p => p.Id == "town") ?? new DungeonProfileDef { Id = "town", Name = "town" };
        var ctx = new GenContext(_data, profile, 0, rng);
        if (!new TownGenerator().Generate(ctx)) return null;
        var start = Allocator.PlacePlayer(ctx, request.Arrival, request.ConnectStairs ?? _data.Constants.ConnectedStairs);
        if (start is not { } s || LevelValidator.Validate(ctx.Level, s, _data.Constants).Count > 0) return null;
        if (request.NoDiagonalSqueezes) Connectivity.OpenDiagonalSqueezes(ctx.Level);
        ctx.Level.ProfileId = "town";
        ctx.Level.Seed = request.Seed;
        return new GeneratedLevel(ctx.Level, s, 1);
    }

    /// <summary>
    /// Angband choose_profile: the town; classic on a quest level; now and then a labyrinth
    /// (labyrinth_check) or, between levels 10 and 39, one time in forty a moria level; otherwise by
    /// the profiles' allocations among those deep enough (classic when none is).
    /// </summary>
    public DungeonProfileDef ChooseProfile(GameRandom rng, int depth, string? forcedId = null, bool quest = false)
    {
        DungeonProfileDef Find(string name) => _data.Profiles.FirstOrDefault(p => p.Name == name)
            ?? throw new GameDataException($"No dungeon profile '{name}'.");
        if (forcedId is not null)
            return _data.Profiles.FirstOrDefault(p => p.Id == forcedId)
                   ?? throw new ArgumentException($"Unknown profile '{forcedId}'.", nameof(forcedId));
        if (depth == 0) return Find("town");
        if (quest) return Find("classic");
        var labyrinth = _data.Profiles.FirstOrDefault(p => p.Name == "labyrinth");
        if (labyrinth is { Alloc: > 0 or -1 } && LabyrinthCheck(rng, depth)) return labyrinth;
        var moria = _data.Profiles.FirstOrDefault(p => p.Name == "moria");
        if (depth is >= 10 and < 40 && rng.OneIn(40) && moria is { Alloc: > 0 or -1 }) return moria;

        DungeonProfileDef? chosen = null;
        var total = 0;
        foreach (var p in _data.Profiles)
        {
            if (p.Alloc <= 0 || depth < p.MinLevel) continue;
            total += p.Alloc;
            if (rng.RandInt0(total) < p.Alloc) chosen = p;
        }
        return chosen ?? Find("classic");
    }

    /// <summary>Angband labyrinth_check: 2 in 100 from level 13, one more for each of 3, 5, 7, 11 and 13 that divides it.</summary>
    private static bool LabyrinthCheck(GameRandom rng, int depth)
    {
        if (depth < 13) return false;
        var chance = 2;
        foreach (var d in new[] { 3, 5, 7, 11, 13 })
            if (depth % d == 0) chance++;
        return rng.RandInt0(100) < chance;
    }
}
