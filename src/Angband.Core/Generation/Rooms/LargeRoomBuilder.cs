using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Generation.Rooms;

/// <summary>
/// A big room containing an inner room (Angband build_large). The inner room is one of five
/// variants: empty, treasure chamber, pillars, checkerboard maze or four compartments.
/// </summary>
internal sealed class LargeRoomBuilder : RoomBuilder
{
    public override string Type => "large";

    // Interior is 23x9; inner room walls are 19x5, leaving a 17x3 inner interior.
    public override RoomPlan Plan(GenContext ctx)
    {
        var lit = ctx.RollRoomLight();
        var variant = ctx.Rng.RandInt1(5);

        return new RoomPlan(25, 11, (c, tl) =>
        {
            var x0 = tl.X + 12;
            var y0 = tl.Y + 5;
            c.BuildRoom(new Rect(x0 - 11, y0 - 4, x0 + 11, y0 + 4), lit);

            var innerWalls = new Rect(x0 - 9, y0 - 2, x0 + 9, y0 + 2);
            foreach (var p in innerWalls.Edge()) c.SetInnerWall(p, lit);
            var inner = innerWalls.Inflate(-1);

            switch (variant)
            {
                case 1: BuildEmpty(c, innerWalls, x0, y0); break;
                case 2: BuildTreasureChamber(c, innerWalls, x0, y0, lit); break;
                case 3: BuildPillars(c, innerWalls, x0, y0, lit); break;
                case 4: BuildCheckerboard(c, inner, lit); break;
                default: BuildCompartments(c, inner, x0, y0, lit); break;
            }
            return new Loc(x0, y0 - 3); // the corridor ring between the walls
        });
    }

    /// <summary>A door somewhere on the non-corner edge of a wall rectangle.</summary>
    internal static Loc RandomEdgeDoorSpot(GenContext c, Rect walls)
    {
        return c.Rng.RandInt0(4) switch
        {
            0 => new Loc(c.Rng.RandRange(walls.X1 + 1, walls.X2 - 1), walls.Y1),
            1 => new Loc(c.Rng.RandRange(walls.X1 + 1, walls.X2 - 1), walls.Y2),
            2 => new Loc(walls.X1, c.Rng.RandRange(walls.Y1 + 1, walls.Y2 - 1)),
            _ => new Loc(walls.X2, c.Rng.RandRange(walls.Y1 + 1, walls.Y2 - 1)),
        };
    }

    private static void BuildEmpty(GenContext c, Rect walls, int x0, int y0)
    {
        c.PlaceSecretDoor(RandomEdgeDoorSpot(c, walls));
        c.AddHint(new Loc(x0, y0), SpawnKind.Monster, 2);
    }

    private static void BuildTreasureChamber(GenContext c, Rect walls, int x0, int y0, bool lit)
    {
        c.PlaceSecretDoor(RandomEdgeDoorSpot(c, walls));
        // A 3x3 ring splits the inner room in two; doors on both sides keep it connected.
        foreach (var p in new Rect(x0 - 1, y0 - 1, x0 + 1, y0 + 1).Edge()) c.SetInnerWall(p, lit);
        c.PlaceSecretDoor(new Loc(x0 - 1, y0));
        c.PlaceSecretDoor(new Loc(x0 + 1, y0));
        c.AddHint(new Loc(x0, y0), SpawnKind.GoodObject, 3);
        c.AddHint(new Loc(x0 - 5, y0), SpawnKind.Monster, 3);
        c.AddHint(new Loc(x0 + 5, y0), SpawnKind.Monster, 3);
        c.PlaceTrap(new Loc(x0 - 2, y0));
        c.PlaceTrap(new Loc(x0 + 2, y0));
    }

    private static void BuildPillars(GenContext c, Rect walls, int x0, int y0, bool lit)
    {
        c.PlaceRandomDoor(RandomEdgeDoorSpot(c, walls));
        c.SetInnerWall(new Loc(x0, y0), lit);
        if (c.Rng.OneIn(2))
        {
            c.SetInnerWall(new Loc(x0 - 5, y0), lit);
            c.SetInnerWall(new Loc(x0 + 5, y0), lit);
        }
        c.AddHint(new Loc(x0 + (c.Rng.OneIn(2) ? -2 : 2), y0), SpawnKind.Object, 1);
    }

    private static void BuildCheckerboard(GenContext c, Rect inner, bool lit)
    {
        foreach (var p in inner.Cells())
            if ((p.X + p.Y) % 2 == 0) c.SetInnerWall(p, lit);

        // Door on the top wall above an open (odd parity) square.
        var candidates = Enumerable.Range(inner.X1, inner.Width)
            .Where(x => (x + inner.Y1) % 2 == 1).ToList();
        c.PlaceSecretDoor(new Loc(c.Rng.Pick(candidates), inner.Y1 - 1));

        var open = inner.Cells().Where(p => (p.X + p.Y) % 2 == 1).ToList();
        for (var i = 0; i < 3; i++) c.AddHint(c.Rng.Pick(open), SpawnKind.Monster, 3);
        for (var i = 0; i < 2; i++) c.AddHint(c.Rng.Pick(open), SpawnKind.Object, 3);
    }

    private static void BuildCompartments(GenContext c, Rect inner, int x0, int y0, bool lit)
    {
        for (var x = inner.X1; x <= inner.X2; x++) c.SetInnerWall(new Loc(x, y0), lit);
        for (var y = inner.Y1; y <= inner.Y2; y++) c.SetInnerWall(new Loc(x0, y), lit);

        c.PlaceSecretDoor(new Loc(c.Rng.RandRange(inner.X1, x0 - 1), inner.Y1 - 1));
        c.PlaceSecretDoor(new Loc(c.Rng.RandRange(x0 + 1, inner.X2), inner.Y1 - 1));
        c.PlaceSecretDoor(new Loc(c.Rng.RandRange(inner.X1, x0 - 1), inner.Y2 + 1));
        c.PlaceSecretDoor(new Loc(c.Rng.RandRange(x0 + 1, inner.X2), inner.Y2 + 1));

        c.AddHint(new Loc(c.Rng.RandRange(inner.X1, x0 - 1), inner.Y1), SpawnKind.Object, 2);
        c.AddHint(new Loc(c.Rng.RandRange(x0 + 1, inner.X2), inner.Y1), SpawnKind.Object, 2);
        c.AddHint(new Loc(c.Rng.RandRange(inner.X1, x0 - 1), inner.Y2), SpawnKind.Object, 2);
        c.AddHint(new Loc(c.Rng.RandRange(x0 + 1, inner.X2), inner.Y2), SpawnKind.Object, 2);
    }
}

/// <summary>
/// Monster pits (ranked rows, strongest in the middle) and nests (a random jumble). The room is
/// built here; the monster population step later fills the hints with a themed monster group.
/// </summary>
internal sealed class MonsterPitBuilder(bool nest) : RoomBuilder
{
    public override string Type => nest ? "nest" : "pit";

    public override RoomPlan Plan(GenContext ctx) => new(25, 11, (c, tl) =>
    {
        var x0 = tl.X + 12;
        var y0 = tl.Y + 5;
        c.BuildRoom(new Rect(x0 - 11, y0 - 4, x0 + 11, y0 + 4), lit: false);

        var walls = new Rect(x0 - 9, y0 - 2, x0 + 9, y0 + 2);
        foreach (var p in walls.Edge()) c.SetInnerWall(p, lit: false);
        var door = LargeRoomBuilder.RandomEdgeDoorSpot(c, walls);
        if (c.Rng.OneIn(2)) c.PlaceSecretDoor(door);
        else c.PlaceClosedDoor(door);

        foreach (var p in walls.Inflate(-1).Cells())
        {
            c.Level[p].Flags |= SquareFlags.NoTrap | SquareFlags.NoStairs;
            if (nest) c.AddHint(p, SpawnKind.NestMonster, 2, "nest");
            else c.AddHint(p, SpawnKind.PitMonster, Math.Max(0, 10 - Math.Abs(p.X - x0)), "pit");
        }
        return new Loc(x0, y0 - 3);
    });
}
