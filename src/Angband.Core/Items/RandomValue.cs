using Angband.Core.Randomness;

namespace Angband.Core.Items;

/// <summary>
/// Angband's random values, as written in object.txt: <c>B+NdXMY</c> — a base, plus <c>NdX</c> (N
/// dice of X sides; <c>dX</c> is one), plus <c>MY</c> (a magic bonus up to Y that grows with
/// depth). Any part may be missing, e.g. <c>5+d5M10</c>, <c>1+M5</c>, <c>d4</c>, <c>2d3</c>,
/// <c>6+d8</c> or <c>3</c>; a leading <c>-</c> negates it.
/// </summary>
public readonly record struct RandomValue(int Base, int Dice, int Bonus, bool Negative, int DiceCount = 1)
{
    public static RandomValue Parse(string text)
    {
        var s = text.Replace(" ", "");
        var negative = s.StartsWith('-');
        if (negative) s = s[1..];
        int b = 0, n = 1, d = 0, m = 0;
        var i = 0;
        int Number()
        {
            var start = i;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            return i > start ? int.Parse(s[start..i], System.Globalization.CultureInfo.InvariantCulture) : 0;
        }
        while (i < s.Length)
        {
            switch (s[i])
            {
                case '+': i++; break;
                case 'd': i++; n = 1; d = Number(); break;
                case 'M': i++; m = Number(); break;
                default:
                    if (!char.IsDigit(s[i])) throw new FormatException($"Invalid random value '{text}'.");
                    var number = Number();
                    // A number straight before 'd' is how many dice ("2d3"); otherwise it is the base.
                    if (i < s.Length && s[i] == 'd')
                    {
                        i++;
                        n = number;
                        d = Number();
                    }
                    else b = number;
                    break;
            }
        }
        return new RandomValue(b, d, m, negative, d > 0 ? n : 1);
    }

    /// <summary>Angband randcalc(AVERAGE): the base plus the dice's average (a magic bonus counts as nothing).</summary>
    public int Average => (Base + (Dice > 0 ? DiceCount * (Dice + 1) / 2 : 0)) * (Negative ? -1 : 1);

    public int Roll(GameRandom rng, int level)
    {
        var value = Base + (Dice > 0 ? rng.Damroll(DiceCount, Dice) : 0) + (Bonus > 0 ? ObjectFactory.MagicBonus(rng, Bonus, level) : 0);
        return Negative ? -value : value;
    }
}
