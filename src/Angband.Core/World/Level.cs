using Angband.Core.Definitions;
using Angband.Core.Geometry;

namespace Angband.Core.World;

/// <summary>One dungeon level (Angband: <c>struct chunk</c>): a grid of squares plus metadata.</summary>
public sealed class Level
{
    private readonly Square[] _squares;

    public Level(TerrainRegistry terrain, int width, int height, int depth)
    {
        if (width < 3 || height < 3) throw new ArgumentOutOfRangeException(nameof(width), "Level too small.");
        Terrain = terrain;
        Width = width;
        Height = height;
        Depth = depth;
        _squares = new Square[width * height];
        Monsters = new Monsters.MonsterRoster(this);
        Objects = new Items.ObjectPiles(width);
    }

    /// <summary>Objects lying on this level.</summary>
    public Items.ObjectPiles Objects { get; }

    /// <summary>Monsters currently on this level.</summary>
    public Monsters.MonsterRoster Monsters { get; }

    public TerrainRegistry Terrain { get; }
    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }

    /// <summary>Id of the profile that generated this level.</summary>
    public string ProfileId { get; set; } = "";

    /// <summary>
    /// Angband's level feeling: the object feeling × 10 plus the monster feeling (0 in town and until
    /// computed). See <see cref="Game.LevelFeelings"/>.
    /// </summary>
    public int Feeling { get; set; }

    /// <summary>How many of the hidden feeling squares the player has seen.</summary>
    public int FeelingSquaresSeen { get; set; }

    /// <summary>Hidden squares that, once seen, count towards the object feeling (Angband SQUARE_FEEL).</summary>
    public HashSet<Loc> FeelSquares { get; } = [];

    /// <summary>A ranger's decoy (Angband GLYPH:DECOY), which monsters that see it go for.</summary>
    public Loc? Decoy { get; set; }

    /// <summary>The vaults built into the level at generation (for Angband's cheat_room; not saved).</summary>
    public List<string> Vaults { get; } = [];
    /// <summary>What vaults, pits and chambers add to the monster rating (Angband add_to_monster_rating).</summary>
    public long MonsterRatingBonus { get; set; }
    /// <summary>Seed the level was generated from.</summary>
    public ulong Seed { get; set; }
    /// <summary>Whole level lit (lit labyrinths, daytime town).</summary>
    public bool IsLit { get; set; }
    /// <summary>Whole level mapped on arrival.</summary>
    public bool IsKnown { get; set; }

    public List<SpawnHint> SpawnHints { get; } = [];
    public PopulationBudget Population { get; set; }

    public Rect Bounds => new(0, 0, Width - 1, Height - 1);

    public ref Square this[Loc p] => ref _squares[p.Y * Width + p.X];
    public ref Square this[int x, int y] => ref _squares[y * Width + x];

    /// <summary>Raw square storage (row-major) for serialization and bulk operations.</summary>
    public Span<Square> Squares => _squares;

    public bool InBounds(Loc p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

    /// <summary>Inside the level and not on its outer edge.</summary>
    public bool InBoundsFully(Loc p) => p.X > 0 && p.Y > 0 && p.X < Width - 1 && p.Y < Height - 1;

    public TerrainDef FeatureAt(Loc p) => Terrain[this[p].Feature];

    public void SetFeature(Loc p, ushort feature) => this[p].Feature = feature;

    public bool Has(Loc p, TerrainFlags flag) => FeatureAt(p).Has(flag);

    public bool IsPassable(Loc p) => Has(p, TerrainFlags.Passable);
    public bool IsFloor(Loc p) => Has(p, TerrainFlags.Floor);
    public bool IsPermanent(Loc p) => Has(p, TerrainFlags.Permanent);
    public bool IsRock(Loc p) => Has(p, TerrainFlags.Rock);
    public bool IsGranite(Loc p) => Has(p, TerrainFlags.Granite);
    public bool IsDoor(Loc p) => Has(p, TerrainFlags.DoorAny);
    public bool IsStair(Loc p) => Has(p, TerrainFlags.Stair);

    /// <summary>Plain floor with no trap or monster: suitable for placing things.</summary>
    public bool IsEmptyFloor(Loc p) => IsFloor(p) && this[p].Trap == 0 && this[p].Monster == 0;

    /// <summary>
    /// Squares a character could eventually walk through without digging: passable terrain,
    /// any door (closed, locked or secret) and rubble.
    /// </summary>
    public bool IsTraversable(Loc p) =>
        FeatureAt(p).HasAny(TerrainFlags.Passable | TerrainFlags.DoorAny | TerrainFlags.Rubble);

    public IEnumerable<Loc> AllLocs()
    {
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
            yield return new Loc(x, y);
    }

    /// <summary>In-bounds 8-neighbours of <paramref name="p"/>.</summary>
    public IEnumerable<Loc> Neighbors(Loc p)
    {
        foreach (var d in DirectionExtensions.Compass)
        {
            var n = p.Step(d);
            if (InBounds(n)) yield return n;
        }
    }

    /// <summary>Counts orthogonal neighbours that are walls (rock or closed/secret doors excluded).</summary>
    public int CountAdjacentWalls(Loc p)
    {
        var count = 0;
        foreach (var d in DirectionExtensions.Orthogonal)
        {
            var n = p.Step(d);
            if (InBounds(n) && Has(n, TerrainFlags.Wall)) count++;
        }
        return count;
    }

    public IEnumerable<Loc> FindFeature(TerrainFlags flag) => AllLocs().Where(p => Has(p, flag));

    /// <summary>Renders the level as text using terrain glyphs (for debugging and tests).</summary>
    public string ToAscii()
    {
        var sb = new System.Text.StringBuilder(Height * (Width + 1));
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                ref var sq = ref this[x, y];
                sb.Append(sq.Trap != 0 ? '^' : Terrain[sq.Feature].Glyph);
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
