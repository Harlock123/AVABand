using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Generation.Rooms;

/// <summary>A plain rectangle, occasionally with pillars or ragged edges.</summary>
internal sealed class SimpleRoomBuilder : RoomBuilder
{
    public override string Type => "simple";

    public override RoomPlan Plan(GenContext ctx)
    {
        var rng = ctx.Rng;
        var h = rng.RandInt1(4) + rng.RandInt1(3) + 1;
        var w = rng.RandInt1(11) + rng.RandInt1(11) + 1;
        var lit = ctx.RollRoomLight();
        var big = h >= 3 && w >= 3;
        var pillars = big && rng.OneIn(20);
        var ragged = big && !pillars && rng.OneIn(50);

        return new RoomPlan(w + 2, h + 2, (c, tl) =>
        {
            var interior = Rect.FromSize(tl.X + 1, tl.Y + 1, w, h);
            c.BuildRoom(interior, lit);

            foreach (var p in interior.Cells())
            {
                int ox = p.X - interior.X1, oy = p.Y - interior.Y1;
                if (pillars && ox % 2 == 1 && oy % 2 == 1)
                    c.SetInnerWall(p, lit);
                else if (ragged && interior.IsOnEdge(p) && (ox + oy) % 2 == 0)
                    c.SetInnerWall(p, lit);
            }
            return FirstFloorNear(c, interior.Center, interior);
        });
    }

    internal static Loc FirstFloorNear(GenContext c, Loc center, Rect area) =>
        c.Level.IsFloor(center)
            ? center
            : area.Cells().Where(c.Level.IsFloor).OrderBy(p => p.DistanceTo(center)).ThenBy(p => p.Y).ThenBy(p => p.X)
                  .DefaultIfEmpty(center).First();
}

/// <summary>Two overlapping rectangles.</summary>
internal sealed class OverlapRoomBuilder : RoomBuilder
{
    public override string Type => "overlap";

    public override RoomPlan Plan(GenContext ctx)
    {
        var rng = ctx.Rng;
        var a = new Rect(-rng.RandInt1(11), -rng.RandInt1(4), rng.RandInt1(10), rng.RandInt1(3));
        var b = new Rect(-rng.RandInt1(10), -rng.RandInt1(3), rng.RandInt1(11), rng.RandInt1(4));
        return CompositePlan(ctx, [a, b]);
    }

    /// <summary>Builds a plan from several interiors given relative to a (0,0) centre.</summary>
    internal static RoomPlan CompositePlan(GenContext ctx, Rect[] interiors, Action<GenContext, Loc, bool>? decorate = null)
    {
        var minX = interiors.Min(r => r.X1);
        var minY = interiors.Min(r => r.Y1);
        var maxX = interiors.Max(r => r.X2);
        var maxY = interiors.Max(r => r.Y2);
        var lit = ctx.RollRoomLight();

        return new RoomPlan(maxX - minX + 3, maxY - minY + 3, (c, tl) =>
        {
            var center = new Loc(tl.X + 1 - minX, tl.Y + 1 - minY);
            var placed = interiors
                .Select(r => new Rect(r.X1 + center.X, r.Y1 + center.Y, r.X2 + center.X, r.Y2 + center.Y))
                .ToArray();
            foreach (var r in placed) c.DrawRoomWalls(r, lit);
            foreach (var r in placed) c.DrawRoomFloor(r, lit);
            decorate?.Invoke(c, center, lit);
            return center;
        });
    }
}

/// <summary>A cross (plus sign) shape, sometimes with a pillar or treasure at the crossing.</summary>
internal sealed class CrossedRoomBuilder : RoomBuilder
{
    public override string Type => "crossed";

    public override RoomPlan Plan(GenContext ctx)
    {
        var rng = ctx.Rng;
        var dy = rng.RandRange(3, 4);
        var dx = rng.RandRange(8, 11);
        var wx = rng.RandRange(1, 2);
        var vertical = new Rect(-wx, -dy, wx, dy);
        var horizontal = new Rect(-dx, -1, dx, 1);
        var variant = rng.RandInt0(4);

        return OverlapRoomBuilder.CompositePlan(ctx, [vertical, horizontal], (c, center, lit) =>
        {
            switch (variant)
            {
                case 1: // central pillar; the crossing ring keeps all arms connected
                    c.SetInnerWall(center, lit);
                    break;
                case 2: // a little treasure where the arms meet, guarded at the far ends
                    c.AddHint(center, SpawnKind.Object, 2);
                    c.AddHint(center + new Loc(-dx, 0), SpawnKind.Monster, 2);
                    c.AddHint(center + new Loc(dx, 0), SpawnKind.Monster, 2);
                    break;
            }
        });
    }
}

/// <summary>A disc-shaped room, larger ones sometimes hiding a tiny treasure chamber.</summary>
internal sealed class CircularRoomBuilder : RoomBuilder
{
    public override string Type => "circular";

    public override RoomPlan Plan(GenContext ctx)
    {
        var rng = ctx.Rng;
        var radius = 2 + rng.RandInt1(2) + rng.RandInt1(3);
        var lit = ctx.RollRoomLight();
        var inner = radius >= 5 && rng.OneIn(3);
        var size = 2 * radius + 3;

        return new RoomPlan(size, size, (c, tl) =>
        {
            var center = new Loc(tl.X + radius + 1, tl.Y + radius + 1);
            var floorArea = new Rect(center.X - radius, center.Y - radius, center.X + radius, center.Y + radius);
            foreach (var p in floorArea.Cells())
                if (center.DistanceTo(p) <= radius) c.SetFloor(p, SquareFlags.Room | GenContext.Light(lit));

            foreach (var p in floorArea.Inflate(1).Cells())
            {
                if (c.Level[p].Has(SquareFlags.Room) && c.Level.IsFloor(p)) continue;
                if (c.Level.Neighbors(p).Any(n => c.Level[n].Has(SquareFlags.Room) && c.Level.IsFloor(n)))
                    c.SetWall(p, c.F.Granite, SquareFlags.WallOuter, SquareFlags.Room | GenContext.Light(lit));
            }

            if (inner)
            {
                foreach (var p in new Rect(center.X - 1, center.Y - 1, center.X + 1, center.Y + 1).Edge())
                    c.SetInnerWall(p, lit);
                c.PlaceSecretDoor(center.Step(c.Rng.Pick(DirectionExtensions.Orthogonal)));
                c.AddHint(center, SpawnKind.GoodObject, 3);
                return center + new Loc(0, -radius);
            }
            return center;
        });
    }
}
