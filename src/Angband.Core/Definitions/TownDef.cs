namespace Angband.Core.Definitions;

public sealed class TownDef
{
    public int Width { get; init; } = 66;
    public int Height { get; init; } = 22;
    /// <summary>Terrain ids of the shop entrances to build, one building each.</summary>
    public IReadOnlyList<string> Shops { get; init; } = [];
    public int LotColumns { get; init; } = 4;
    public int LotRows { get; init; } = 2;
    public string Rubble { get; init; } = "1d4";
}

public sealed class ShopDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Owner { get; init; } = "";
    public bool IsHome { get; init; }
}
