using System.Globalization;

namespace Angband.Core.Randomness;

/// <summary>
/// Angband 4.2's dice strings with expressions (dice.c, expression.c), as monster spells give their
/// damage: <c>$Dd6</c> with <c>D = SPELL_POWER / 8 + 1</c>, <c>15+1d$S</c> with <c>S = SPELL_POWER * 3</c>,
/// <c>$B+5d5</c> with <c>B = SPELL_POWER * 3 / 2 + 30</c>. A string is an optional base, then optional
/// dice (<c>NdM</c>, or <c>dM</c> for one die); any number may be a <c>$</c> variable. An expression
/// starts from a value (SPELL_POWER, or MAX_SIGHT = 20) and applies its operators left to right in
/// whole numbers, as Angband does.
/// </summary>
public static class DiceFormula
{
    public const int MaxSight = 20;

    /// <summary>A variable's value: its expression applied to the spell power.</summary>
    public static int Evaluate(string expression, int power)
    {
        var parts = expression.Split(':', 2);
        var value = parts[0] switch { "MAX_SIGHT" => MaxSight, _ => power };
        var tokens = parts.Length > 1 ? parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];
        for (var i = 0; i + 1 < tokens.Length; i += 2)
        {
            var n = int.Parse(tokens[i + 1], CultureInfo.InvariantCulture);
            value = tokens[i] switch
            {
                "+" => value + n,
                "-" => value - n,
                "*" => value * n,
                "/" => n == 0 ? value : value / n,
                _ => value,
            };
        }
        return value;
    }

    /// <summary>The formula with its variables worked out: base, dice and sides.</summary>
    public static (int Base, int Dice, int Sides) Resolve(string formula, IReadOnlyDictionary<string, string> terms, int power)
    {
        int Number(string text)
        {
            text = text.Trim();
            if (text.Length == 0) return 0;
            if (text[0] == '$')
                return terms.TryGetValue(text[1..], out var expr) ? Math.Max(0, Evaluate(expr, power)) : 0;
            return int.Parse(text, CultureInfo.InvariantCulture);
        }

        var baseText = "";
        var diceText = formula;
        var d = formula.IndexOf('d');
        if (d < 0)
        {
            baseText = formula;
            diceText = "";
        }
        else
        {
            // The base is what comes before a '+' ahead of the dice (or nothing: "$Dd6", "d$S").
            var plus = formula.LastIndexOf('+', d);
            if (plus >= 0)
            {
                baseText = formula[..plus];
                diceText = formula[(plus + 1)..];
            }
        }
        var (count, sides) = (0, 0);
        if (diceText.Length > 0)
        {
            var k = diceText.IndexOf('d');
            count = k == 0 ? 1 : Number(diceText[..k]);
            sides = Number(diceText[(k + 1)..]);
        }
        return (Number(baseText), count, sides);
    }

    public static int Roll(string formula, IReadOnlyDictionary<string, string> terms, int power, GameRandom rng)
    {
        var (b, n, s) = Resolve(formula, terms, power);
        return b + (n > 0 && s > 0 ? rng.Damroll(n, s) : 0);
    }

    /// <summary>The most it can come to (for monster recall).</summary>
    public static int Max(string formula, IReadOnlyDictionary<string, string> terms, int power)
    {
        var (b, n, s) = Resolve(formula, terms, power);
        return b + n * s;
    }
}
