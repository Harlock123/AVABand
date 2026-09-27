namespace Angband.Core.Definitions;

/// <summary>What a <see cref="MapTemplateDef"/> is used for.</summary>
public enum MapTemplateKind
{
    Room,
    LesserVault,
    MediumVault,
    GreaterVault,
}

/// <summary>
/// A hand-drawn room or vault layout. Rows use the legend described in
/// <see cref="Generation.TemplateLegend"/> (Angband vault.txt conventions).
/// </summary>
public sealed class MapTemplateDef
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";
    public MapTemplateKind Kind { get; init; }
    public int MinDepth { get; init; }
    public int MaxDepth { get; init; } = 127;
    public int Weight { get; init; } = 1;
    /// <summary>Whether the generator may rotate and mirror this layout.</summary>
    public bool Rotatable { get; init; } = true;
    /// <summary>Layout rows, padded to equal width with spaces by the loader.</summary>
    public required IReadOnlyList<string> Rows { get; init; }

    public int Width => Rows.Count == 0 ? 0 : Rows[0].Length;
    public int Height => Rows.Count;
}
