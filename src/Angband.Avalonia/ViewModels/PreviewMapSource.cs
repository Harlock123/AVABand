using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Data.Tiles;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// A tiny hand-drawn scene showing a tileset's walls, floors, doors, stairs, a shop, traps, the
/// player and some monsters, with lit, torch-lit and remembered areas.
/// </summary>
public sealed class PreviewMapSource : IMapSource
{
    private static readonly string[] Scene =
    [
        "##############",
        "#,,,,,#......#",
        "#,a,,,+..^...#",
        "#,,@,,#..j...#",
        "#,,,t,'......#",
        "#,,,,,#%%**::#",
        "###1###<>~~###",
    ];

    // Symbol -> terrain id.
    private static readonly Dictionary<char, string> Terrain = new()
    {
        ['#'] = "granite_wall", [','] = "floor", ['.'] = "floor", ['+'] = "closed_door", ['\''] = "open_door",
        ['%'] = "magma_vein", ['*'] = "quartz_with_treasure", [':'] = "rubble", ['<'] = "up_staircase",
        ['>'] = "down_staircase", ['~'] = "lava", ['1'] = "shop_general",
    };

    private readonly MapCell[,] _cells;

    public PreviewMapSource(GameData data, MapCellBuilder builder)
    {
        _cells = new MapCell[Scene[0].Length, Scene.Length];
        for (var y = 0; y < Scene.Length; y++)
        for (var x = 0; x < Scene[y].Length; x++)
        {
            var ch = Scene[y][x];
            // The left room is lit, near the player is torch-lit, the right room is remembered.
            var lighting = x >= 7 ? TileLighting.Dark : Math.Max(Math.Abs(x - 3), Math.Abs(y - 3)) <= 1 ? TileLighting.Torch : TileLighting.Lit;
            var terrainId = Terrain.GetValueOrDefault(ch, "floor");
            var terrain = data.Terrain.TryGet(terrainId, out var t) ? t : data.Terrain["floor"];
            var cell = builder.Terrain(terrain, ch is ',' or '@' or 'a' or 't' ? TileLighting.Lit : lighting);

            _cells[x, y] = ch switch
            {
                '@' => builder.Player(cell),
                'a' when data.Monster("cave_orc") is { } orc => builder.Monster(orc, cell),
                't' when data.Monster("kobold") is { } kobold => builder.Monster(kobold, cell),
                'j' when data.Monster("jackal") is { } jackal => builder.Monster(jackal, builder.Terrain(terrain, TileLighting.Lit)),
                '^' when data.Traps.FirstOrDefault(tr => tr.Id == "pit") is { } pit => builder.Trap(pit, cell),
                _ => cell,
            };
        }
    }

    public int Width => _cells.GetLength(0);
    public int Height => _cells.GetLength(1);
    public Loc Focus => new(Width / 2, Height / 2);
    public MapCell GetCell(int x, int y) => _cells[x, y];
}
