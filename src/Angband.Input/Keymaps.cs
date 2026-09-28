namespace Angband.Input;

/// <summary>One keystroke of a keymap: a named key (<c>Escape</c>, <c>F1</c>, <c>Up</c>), or a typed character, maybe with Ctrl.</summary>
public sealed record KeyStroke(string? KeyName, char? Character, bool Ctrl = false);

/// <summary>
/// Angband keymaps: a key that types a string of other keys, e.g. F2 → <c>ma'</c> (cast the first
/// spell at the nearest monster; AVABand's spell menu has no book step, so Angband's <c>maa'</c>
/// loses its book letter). The action is written as Angband writes it: characters stand for
/// themselves; <c>^x</c> is Ctrl+x; <c>\e</c> Escape, <c>\n</c> Enter, <c>\t</c> Tab, <c>\\</c>,
/// <c>\^</c> and <c>\[</c> the characters; <c>[F1]</c>, <c>[Up]</c> and the like any named key.
/// </summary>
public static class KeymapText
{
    public static IReadOnlyList<KeyStroke> Parse(string action)
    {
        var strokes = new List<KeyStroke>();
        for (var i = 0; i < action.Length; i++)
        {
            var c = action[i];
            if (c == '\\' && i + 1 < action.Length)
            {
                var next = action[++i];
                strokes.Add(next switch
                {
                    'e' => new KeyStroke("Escape", null),
                    'n' or 'r' => new KeyStroke("Enter", null),
                    't' => new KeyStroke("Tab", null),
                    _ => new KeyStroke(null, next),
                });
            }
            else if (c == '^' && i + 1 < action.Length)
                strokes.Add(new KeyStroke(null, char.ToLowerInvariant(action[++i]), Ctrl: true));
            else if (c == '[' && action.IndexOf(']', i + 1) is var end and > 0 && end - i > 1)
            {
                strokes.Add(new KeyStroke(action[(i + 1)..end], null));
                i = end;
            }
            else strokes.Add(new KeyStroke(null, c));
        }
        return strokes;
    }
}
