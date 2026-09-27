namespace Angband.Core.Time;

/// <summary>
/// Angband's speed-to-energy table. Speed is expressed relative to normal (0 = normal, +10 = "fast
/// +10"). Every game turn each actor gains <see cref="EnergyPerTurn"/> energy and may act once its
/// energy reaches <see cref="MoveEnergy"/>. Normal speed gains 10 per game turn, so one action
/// takes 10 game turns; +10 gains 20 and acts twice as often.
/// </summary>
public static class EnergyTable
{
    /// <summary>Energy an ordinary action costs.</summary>
    public const int MoveEnergy = 100;

    /// <summary>Game turns in one player turn at normal speed (world upkeep cadence).</summary>
    public const int GameTurnsPerNormalTurn = 10;

    /// <summary>Index of normal speed in the internal table (Angband stores speed as 110 = normal).</summary>
    public const int NormalIndex = 110;

    public const int MinSpeed = -NormalIndex;
    public const int MaxSpeed = 199 - NormalIndex;

    private static readonly byte[] Table =
    [
        /* S-110 .. S-51: crawling */
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        /* S-50 */ 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        /* S-40 */ 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
        /* S-30 */ 2, 2, 2, 2, 2, 2, 2, 3, 3, 3,
        /* S-20 */ 3, 3, 3, 3, 3, 4, 4, 4, 4, 4,
        /* S-10 */ 5, 5, 5, 5, 6, 6, 7, 7, 8, 9,
        /* Norm */ 10, 11, 12, 13, 14, 15, 16, 17, 18, 19,
        /* F+10 */ 20, 21, 22, 23, 24, 25, 26, 27, 28, 29,
        /* F+20 */ 30, 31, 32, 33, 34, 35, 36, 36, 37, 37,
        /* F+30 */ 38, 38, 39, 39, 40, 40, 40, 41, 41, 41,
        /* F+40 */ 42, 42, 42, 43, 43, 43, 44, 44, 44, 44,
        /* F+50 */ 45, 45, 45, 45, 45, 46, 46, 46, 46, 46,
        /* F+60 */ 47, 47, 47, 47, 47, 48, 48, 48, 48, 48,
        /* F+70 */ 49, 49, 49, 49, 49, 49, 49, 49, 49, 49,
        /* Fast */ 49, 49, 49, 49, 49, 49, 49, 49, 49, 49,
    ];

    public static int Count => Table.Length;

    /// <summary>Energy gained per game turn at the given relative speed (clamped to the table).</summary>
    public static int EnergyPerTurn(int speed) => Table[Math.Clamp(speed + NormalIndex, 0, Table.Length - 1)];
}
