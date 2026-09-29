namespace Angband.Core.Definitions;

/// <summary>A shopkeeper (Angband store.txt "owner").</summary>
public sealed class StoreOwnerDef
{
    public required string Name { get; init; }
    /// <summary>Most gold the owner will pay for one item (when selling for gold is on).</summary>
    public int Purse { get; init; } = 5000;
}

/// <summary>A store's trading rules (Angband store.txt). The id matches the town's shop id.</summary>
public sealed class StoreDef
{
    public required string Id { get; init; }
    public IReadOnlyList<StoreOwnerDef> Owners { get; init; } = [];
    /// <summary>Object kinds always in stock, in any quantity.</summary>
    public IReadOnlyList<string> Always { get; init; } = [];
    /// <summary>
    /// AVABand's own staples, kept apart from 4.2.5's list (which the drift check compares): the
    /// general store's lanterns, which no shop in 4.2.5 sells.
    /// </summary>
    public IReadOnlyList<string> AvabandAlways { get; init; } = [];
    /// <summary>Every staple: 4.2.5's and AVABand's.</summary>
    public IReadOnlyList<string> Staples => [.. Always, .. AvabandAlways];
    /// <summary>Object kinds the store randomly stocks.</summary>
    public IReadOnlyList<string> Normal { get; init; } = [];
    /// <summary>Object bases the store will take from the player.</summary>
    public IReadOnlyList<string> Buys { get; init; } = [];
    /// <summary>Angband slots: the range of piles, besides the staples, the store keeps.</summary>
    public int MinItems { get; init; } = 6;
    public int MaxItems { get; init; } = 18;
    /// <summary>Angband turnover: up to this many piles are sold off, and bought in, each store day.</summary>
    public int Turnover { get; init; } = 2;
    /// <summary>The black market stocks anything worth its prices from deeper than the player has been, at three times the value.</summary>
    public bool BlackMarket { get; init; }
    /// <summary>The player's home: free storage, no trading.</summary>
    public bool Home { get; init; }
    /// <summary>Home capacity in stacks.</summary>
    public int Capacity { get; init; } = 24;
}
