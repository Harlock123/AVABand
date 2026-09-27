using Angband.Core.Definitions;
using Angband.Core.Generation.Rooms;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Generation.Generators;

/// <summary>
/// Angband's classic generator: rooms placed on a coarse block grid, joined by wandering tunnels
/// that pierce room walls, mineral streamers, doors at junctions, then stairs, rubble and traps.
/// </summary>
internal sealed class ClassicGenerator : ILevelGenerator
{
    public string Id => "classic";

    public bool Generate(GenContext ctx)
    {
        var profile = ctx.Profile;
        var level = ctx.CreateLevel(profile.Width, profile.Height);
        ctx.Fill(level.Bounds, ctx.F.Granite);
        ctx.DrawEdge(level.Bounds, ctx.F.Permanent, SquareFlags.WallSolid);

        BuildRooms(ctx);
        if (ctx.RoomCenters.Count < 2) return false;

        var centers = ctx.RoomCenters.ToList();
        ctx.Rng.Shuffle(centers);
        var previous = centers[^1];
        foreach (var center in centers)
        {
            Tunneler.Dig(ctx, previous, center);
            previous = center;
        }

        Streamers.Build(ctx);
        Tunneler.PlaceJunctionDoors(ctx);

        if (!Connectivity.EnsureConnected(ctx)) return false;
        return Allocator.AllocStandard(ctx);
    }

    private static void BuildRooms(GenContext ctx)
    {
        var profile = ctx.Profile;
        var block = profile.BlockSize;
        var blockRows = profile.Height / block;
        var blockCols = profile.Width / block;
        var used = new bool[blockRows, blockCols];
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        var eligible = profile.Rooms.Where(r => r.MinDepth <= ctx.Depth && r.MaxDepth >= ctx.Depth).ToList();
        for (var attempt = 0; attempt < profile.RoomAttempts; attempt++)
        {
            var choice = ctx.Rng.PickWeighted(eligible, r =>
                r.MaxCount > 0 && counts.GetValueOrDefault(r.Type) >= r.MaxCount ? 0 : r.Weight);
            if (choice is null) break;
            if (!RoomBuilders.TryGet(choice.Type, out var builder)) continue;

            var plan = builder.Plan(ctx);
            if (plan is null || !TryReserve(ctx, used, plan, out var topLeft)) continue;

            ctx.RoomCenters.Add(plan.Draw(ctx, topLeft));
            counts[choice.Type] = counts.GetValueOrDefault(choice.Type) + 1;
        }
    }

    /// <summary>Finds free blocks for the plan and a top-left corner inside them (and inside the level edge).</summary>
    private static bool TryReserve(GenContext ctx, bool[,] used, RoomPlan plan, out Loc topLeft)
    {
        topLeft = default;
        var block = ctx.Profile.BlockSize;
        var rows = used.GetLength(0);
        var cols = used.GetLength(1);
        var bh = (plan.Height + block - 1) / block;
        var bw = (plan.Width + block - 1) / block;
        if (bh > rows || bw > cols) return false;

        for (var attempt = 0; attempt < 25; attempt++)
        {
            var by = ctx.Rng.RandInt0(rows - bh + 1);
            var bx = ctx.Rng.RandInt0(cols - bw + 1);
            if (AnyUsed(used, bx, by, bw, bh)) continue;

            var minX = Math.Max(bx * block, 1);
            var maxX = Math.Min((bx + bw) * block - plan.Width, ctx.Level.Width - 1 - plan.Width);
            var minY = Math.Max(by * block, 1);
            var maxY = Math.Min((by + bh) * block - plan.Height, ctx.Level.Height - 1 - plan.Height);
            if (maxX < minX || maxY < minY) continue;

            for (var y = by; y < by + bh; y++)
            for (var x = bx; x < bx + bw; x++)
                used[y, x] = true;

            topLeft = new Loc(ctx.Rng.RandRange(minX, maxX), ctx.Rng.RandRange(minY, maxY));
            return true;
        }
        return false;
    }

    private static bool AnyUsed(bool[,] used, int bx, int by, int bw, int bh)
    {
        for (var y = by; y < by + bh; y++)
        for (var x = bx; x < bx + bw; x++)
            if (used[y, x]) return true;
        return false;
    }
}

/// <summary>Angband-style corridor digging between room centres.</summary>
internal static class Tunneler
{
    private const int MaxSteps = 2000;

    public static void Dig(GenContext ctx, Loc start, Loc end)
    {
        var level = ctx.Level;
        var t = ctx.Profile.Tunnel;
        var dug = new List<Loc>();
        var pierced = new List<Loc>();
        var cur = start;
        var dir = ctx.CorrectDir(start, end);
        var inCorridor = false;

        for (var steps = 0; cur != end && steps < MaxSteps; steps++)
        {
            if (ctx.Rng.Percent(t.Change))
            {
                dir = ctx.CorrectDir(cur, end);
                if (ctx.Rng.Percent(t.Random)) dir = ctx.RandomOrthogonal();
            }

            var next = cur + dir;
            if (!level.InBoundsFully(next))
            {
                dir = ctx.CorrectDir(cur, end);
                continue;
            }

            ref var sq = ref level[next];
            if (level.IsPermanent(next) || sq.Has(SquareFlags.WallSolid))
            {
                // Blocked: re-aim, sometimes at random, and try again next step.
                dir = ctx.Rng.OneIn(2) ? ctx.RandomOrthogonal() : ctx.CorrectDir(cur, end);
                continue;
            }

            if (sq.Has(SquareFlags.WallOuter))
            {
                var beyond = next + dir;
                if (!level.InBoundsFully(beyond)) continue;
                ref var b = ref level[beyond];
                if (level.IsPermanent(beyond) || b.HasAny(SquareFlags.WallOuter | SquareFlags.WallSolid)) continue;

                pierced.Add(next);
                cur = next;
                // Forbid another piercing right beside this one.
                foreach (var n in level.Neighbors(next))
                {
                    ref var ns = ref level[n];
                    if (ns.Has(SquareFlags.WallOuter))
                        ns.Flags = (ns.Flags & ~SquareFlags.WallOuter) | SquareFlags.WallSolid;
                }
                inCorridor = false;
            }
            else if (sq.Has(SquareFlags.Room))
            {
                cur = next; // rooms are crossed freely
                inCorridor = false;
            }
            else if (level.IsRock(next))
            {
                dug.Add(next);
                cur = next;
                inCorridor = false;
            }
            else
            {
                // An existing corridor: remember the junction and maybe stop here.
                cur = next;
                if (!inCorridor)
                {
                    ctx.Junctions.Add(next);
                    inCorridor = true;
                }
                if (!ctx.Rng.Percent(t.Continue)
                    && (Math.Abs(cur.X - start.X) > 10 || Math.Abs(cur.Y - start.Y) > 10))
                    break;
            }
        }

        foreach (var p in dug)
            if (level.IsRock(p) && !level[p].Has(SquareFlags.Room))
                ctx.SetFloor(p);

        foreach (var p in pierced)
        {
            ctx.SetFloor(p, level[p].Flags & (SquareFlags.Room | SquareFlags.Glow));
            if (ctx.Rng.Percent(t.PierceDoor)) ctx.PlaceRandomDoor(p);
        }
    }

    /// <summary>Doors beside junctions where a corridor is flanked by walls (Angband try_door).</summary>
    public static void PlaceJunctionDoors(GenContext ctx)
    {
        var level = ctx.Level;
        foreach (var j in ctx.Junctions)
        foreach (var d in DirectionExtensions.Orthogonal)
        {
            var p = j.Step(d);
            if (!level.InBoundsFully(p) || !level.IsEmptyFloor(p) || level[p].Has(SquareFlags.Room)) continue;
            if (ctx.Rng.Percent(ctx.Profile.Tunnel.JunctionDoor) && IsPossibleDoorway(level, p))
                ctx.PlaceRandomDoor(p);
        }
    }

    private static bool IsPossibleDoorway(Level level, Loc p)
    {
        bool Wall(Direction d) => level.Has(p.Step(d), TerrainFlags.Wall);
        return (Wall(Direction.North) && Wall(Direction.South)) || (Wall(Direction.East) && Wall(Direction.West));
    }
}

/// <summary>Magma and quartz veins, sometimes carrying treasure (Angband build_streamer).</summary>
internal static class Streamers
{
    public static void Build(GenContext ctx)
    {
        var s = ctx.Profile.Streamers;
        for (var i = 0; i < s.MagmaCount; i++) Build(ctx, ctx.F.Magma, ctx.F.MagmaTreasure, s.MagmaTreasure);
        for (var i = 0; i < s.QuartzCount; i++) Build(ctx, ctx.F.Quartz, ctx.F.QuartzTreasure, s.QuartzTreasure);
    }

    private static void Build(GenContext ctx, ushort vein, ushort treasure, int treasureOneIn)
    {
        var level = ctx.Level;
        var s = ctx.Profile.Streamers;
        var p = new Loc(ctx.Rng.RandRange(1, level.Width - 2), ctx.Rng.RandRange(1, level.Height - 2));
        var dir = ctx.Rng.Pick(DirectionExtensions.Compass).Offset();

        while (level.InBoundsFully(p))
        {
            for (var i = 0; i < s.Density; i++)
            {
                var q = new Loc(ctx.Rng.Spread(p.X, s.Range), ctx.Rng.Spread(p.Y, s.Range));
                if (!level.InBoundsFully(q) || !level.IsGranite(q) || level.IsDoor(q)) continue;
                if (level[q].HasAny(SquareFlags.Vault | SquareFlags.WallSolid)) continue;
                level[q].Feature = ctx.Rng.OneIn(treasureOneIn) ? treasure : vein;
            }
            p += dir;
        }
    }
}
