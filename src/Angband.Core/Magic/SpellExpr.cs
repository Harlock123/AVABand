using System.Globalization;

namespace Angband.Core.Magic;

/// <summary>
/// Tiny integer expression evaluator for spell data: numbers, <c>L</c> (caster level), + - * /
/// and parentheses, e.g. <c>3+(L-1)/5</c>. Division truncates, as in Angband's expression code.
/// </summary>
public static class SpellExpr
{
    public static int Eval(string text, int level)
    {
        var pos = 0;
        var value = ParseSum(text.Replace(" ", ""), ref pos, level);
        if (pos != text.Replace(" ", "").Length) throw new FormatException($"Unexpected input in expression '{text}'.");
        return value;
    }

    public static bool IsValid(string text)
    {
        try { Eval(text, 1); return true; }
        catch (Exception ex) when (ex is FormatException or DivideByZeroException) { return false; }
    }

    private static int ParseSum(string s, ref int pos, int level)
    {
        var value = ParseProduct(s, ref pos, level);
        while (pos < s.Length && s[pos] is '+' or '-')
        {
            var op = s[pos++];
            var rhs = ParseProduct(s, ref pos, level);
            value = op == '+' ? value + rhs : value - rhs;
        }
        return value;
    }

    private static int ParseProduct(string s, ref int pos, int level)
    {
        var value = ParseAtom(s, ref pos, level);
        while (pos < s.Length && s[pos] is '*' or '/')
        {
            var op = s[pos++];
            var rhs = ParseAtom(s, ref pos, level);
            value = op == '*' ? value * rhs : value / rhs;
        }
        return value;
    }

    private static int ParseAtom(string s, ref int pos, int level)
    {
        if (pos >= s.Length) throw new FormatException("Expression ended early.");
        if (s[pos] == '(')
        {
            pos++;
            var v = ParseSum(s, ref pos, level);
            if (pos >= s.Length || s[pos] != ')') throw new FormatException("Missing ')'.");
            pos++;
            return v;
        }
        if (s[pos] == '-') { pos++; return -ParseAtom(s, ref pos, level); }
        if (s[pos] is 'L' or 'l') { pos++; return level; }
        var start = pos;
        while (pos < s.Length && char.IsDigit(s[pos])) pos++;
        if (start == pos) throw new FormatException($"Expected a number at '{s[start..]}'.");
        return int.Parse(s[start..pos], CultureInfo.InvariantCulture);
    }
}
