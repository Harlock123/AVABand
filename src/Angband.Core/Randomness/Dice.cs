using System.Globalization;
using System.Text.RegularExpressions;

namespace Angband.Core.Randomness;

/// <summary>A dice expression such as <c>2d6+3</c>, <c>d8</c> or a plain constant <c>5</c>.</summary>
public readonly partial record struct Dice(int Count, int Sides, int Bonus = 0)
{
    public static readonly Dice Zero = new(0, 0);

    public int Min => Count * (Sides > 0 ? 1 : 0) + Bonus;
    public int Max => Count * Sides + Bonus;

    /// <summary>Average times two, kept integral (Angband style).</summary>
    public int AverageTimesTwo => Count * (Sides + 1) + 2 * Bonus;

    public int Roll(GameRandom rng) => rng.Damroll(Count, Sides) + Bonus;

    public static Dice Constant(int value) => new(0, 0, value);

    public static Dice Parse(string text)
    {
        if (!TryParse(text, out var dice))
            throw new FormatException($"Invalid dice expression '{text}'.");
        return dice;
    }

    public static bool TryParse(string? text, out Dice dice)
    {
        dice = Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = DicePattern().Match(text.Replace(" ", ""));
        if (!m.Success) return false;

        if (!m.Groups["d"].Success)
        {
            dice = Constant(int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture));
            return true;
        }

        var count = m.Groups["n"].Success ? int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture) : 1;
        var sides = int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture);
        var bonus = m.Groups["b"].Success ? int.Parse(m.Groups["b"].Value, CultureInfo.InvariantCulture) : 0;
        dice = new Dice(count, sides, bonus);
        return true;
    }

    public override string ToString() =>
        Count == 0 || Sides == 0 ? Bonus.ToString(CultureInfo.InvariantCulture)
        : Bonus == 0 ? $"{Count}d{Sides}"
        : $"{Count}d{Sides}{Bonus:+0;-0}";

    [GeneratedRegex(@"^(?<n>\d+)?(?:(?<d>[dD])(?<s>\d+)(?<b>[+-]\d+)?)?$")]
    private static partial Regex DicePattern();
}
