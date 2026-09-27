using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Data.Tiles;

namespace Angband.Avalonia.ViewModels;

/// <summary>A one-square map: how the paper doll draws an item, or the character, like the map does.</summary>
public sealed class SingleCellSource(MapCell cell) : IMapSource
{
    public int Width => 1;
    public int Height => 1;
    public Loc Focus => new(0, 0);
    public MapCell GetCell(int x, int y) => cell;
}

/// <summary>One equipment slot on the paper doll: its name, what is worn there, and its picture.</summary>
public sealed record PaperDollSlot(string Label, Item? Item, string Name, string Description, IMapSource Picture)
{
    /// <summary>How the picture is drawn: the map's own tiles or letters.</summary>
    public bool UseTiles { get; init; }
    public TilesetManifest? Tileset { get; init; }
    public double FontSize { get; init; } = 16;

    public bool IsEmpty => Item is null && !IsCharacter;
    public double NameOpacity => IsEmpty ? 0.45 : 1.0;
    /// <summary>The character in the middle of the doll, rather than a slot.</summary>
    public bool IsCharacter { get; init; }
}

/// <summary>
/// The character sheet's paper doll: the character in the middle and each piece of equipment where
/// it is worn — head, neck, body, cloak and feet down the middle, light, weapon, left ring and hands
/// on one side, bow, shield, right ring on the other — drawn with the map's tiles or letters.
/// </summary>
public sealed class PaperDollViewModel
{
    public required PaperDollSlot Weapon { get; init; }
    public required PaperDollSlot Bow { get; init; }
    public required PaperDollSlot RingLeft { get; init; }
    public required PaperDollSlot RingRight { get; init; }
    public required PaperDollSlot Amulet { get; init; }
    public required PaperDollSlot Light { get; init; }
    public required PaperDollSlot Body { get; init; }
    public required PaperDollSlot Cloak { get; init; }
    public required PaperDollSlot Shield { get; init; }
    public required PaperDollSlot Head { get; init; }
    public required PaperDollSlot Hands { get; init; }
    public required PaperDollSlot Feet { get; init; }
    public required PaperDollSlot Player { get; init; }
    public required string PlayerName { get; init; }
    /// <summary>All the slots, in <see cref="Inventory.Slots"/> order.</summary>
    public IReadOnlyList<PaperDollSlot> Slots =>
        [Weapon, Bow, RingLeft, RingRight, Amulet, Light, Body, Cloak, Shield, Head, Hands, Feet];
}
