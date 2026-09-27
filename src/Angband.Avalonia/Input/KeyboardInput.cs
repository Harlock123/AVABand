using Angband.Input;
using Avalonia.Input;

namespace Angband.Avalonia.Input;

/// <summary>
/// Keyboard provider: turns Avalonia key events into binding chords and looks them up. Chords are
/// tried from most to least specific: modifier chords (<c>Ctrl+T</c>), plain key names (<c>Up</c>,
/// <c>NumPad8</c>, <c>F7</c>) and finally the typed character (<c>Char:&gt;</c>), so symbols follow
/// the keyboard layout while arrows and the keypad work everywhere.
/// </summary>
public static class KeyboardInput
{
    private static readonly HashSet<Key> ModifierKeys =
        [Key.LeftShift, Key.RightShift, Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt, Key.LWin, Key.RWin];

    public static bool IsModifierKey(Key key) => ModifierKeys.Contains(key);

    /// <summary>The character a key produced, with Shift applied to letters (some platforms report the unshifted symbol).</summary>
    public static string? Symbol(KeyEventArgs e)
    {
        var s = e.KeySymbol;
        if (s is { Length: 1 } && char.IsLetter(s[0]) && (e.KeyModifiers & KeyModifiers.Shift) != 0)
            return s.ToUpperInvariant();
        return s;
    }

    /// <summary>
    /// A key's binding name. Some Avalonia keys share a value with an alias whose name ToString()
    /// prefers (Enter/Return, PageUp/Prior, PageDown/Next); bindings always use the friendly name.
    /// </summary>
    public static string KeyName(Key key) => key switch
    {
        Key.Enter => "Enter",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.CapsLock => "CapsLock",
        _ => key.ToString(),
    };

    /// <summary>Chords describing this key press, most specific first.</summary>
    public static IEnumerable<string> Chords(KeyEventArgs e)
    {
        var name = KeyName(e.Key);
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var alt = (e.KeyModifiers & KeyModifiers.Alt) != 0;
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        if (ctrl || alt)
        {
            yield return (ctrl ? "Ctrl+" : "") + (alt ? "Alt+" : "") + (shift ? "Shift+" : "") + name;
            yield break;
        }
        if (shift) yield return "Shift+" + name;
        yield return name;
        if (Symbol(e) is { Length: 1 } symbol && !char.IsControl(symbol[0])) yield return "Char:" + symbol;
    }

    /// <summary>The bound action for a key press, or <see cref="InputAction.None"/>.</summary>
    public static InputAction Resolve(KeyEventArgs e, InputBindings bindings) =>
        Chords(e).Select(bindings.ForKey).FirstOrDefault(a => a != InputAction.None);

    /// <summary>The chord to store when the player rebinds an action to this key press.</summary>
    public static string CaptureChord(KeyEventArgs e)
    {
        var ctrlOrAlt = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0;
        if (!ctrlOrAlt && Symbol(e) is { Length: 1 } symbol && !char.IsControl(symbol[0]) && !char.IsWhiteSpace(symbol[0])
            && !KeyName(e.Key).StartsWith("NumPad", StringComparison.Ordinal))
            return "Char:" + symbol;
        return Chords(e).First();
    }

    /// <summary>A chord as players read it: "Char:G" → "G", "Ctrl+OemPlus" → "Ctrl+OemPlus".</summary>
    public static string Display(string chord)
    {
        if (!chord.StartsWith("Char:", StringComparison.Ordinal)) return chord;
        var c = chord[5..];
        return c.Length == 1 && !char.IsLetterOrDigit(c[0]) ? $"'{c}'" : c;
    }
}
