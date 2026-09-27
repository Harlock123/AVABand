namespace Angband.Core.Items;

/// <summary>
/// Angband's inscriptions (the <c>{...}</c> notes players write on objects, obj-util.c and
/// cmd-pickup.c): <c>@&lt;cmd&gt;&lt;n&gt;</c> or <c>@&lt;n&gt;</c> lets a digit pick the object in a prompt,
/// <c>!&lt;cmd&gt;</c> or <c>!*</c> asks for confirmation before a command uses it, <c>=g</c> always
/// picks it up (<c>=g5</c>: until five are carried), <c>!g</c> never does, and <c>@f#</c> / <c>@v#</c>
/// order the quiver.
/// </summary>
public static class Inscription
{
    /// <summary>How often <paramref name="text"/> appears in the note (Angband check_for_inscrip).</summary>
    public static int Count(Item item, string text)
    {
        if (string.IsNullOrEmpty(item.Note)) return 0;
        var n = 0;
        for (var i = item.Note.IndexOf(text, StringComparison.Ordinal); i >= 0; i = item.Note.IndexOf(text, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }

    public static bool Has(Item item, string text) => Count(item, text) > 0;

    /// <summary>
    /// <paramref name="text"/> immediately followed by a number (Angband check_for_inscrip_with_int):
    /// <c>=g5</c> gives 5. Null if there is no such inscription.
    /// </summary>
    public static int? NumberAfter(Item item, string text)
    {
        if (string.IsNullOrEmpty(item.Note)) return null;
        for (var i = item.Note.IndexOf(text, StringComparison.Ordinal); i >= 0; i = item.Note.IndexOf(text, i + 1, StringComparison.Ordinal))
        {
            var start = i + text.Length;
            var end = start;
            while (end < item.Note.Length && char.IsAsciiDigit(item.Note[end])) end++;
            if (end > start && int.TryParse(item.Note.AsSpan(start, end - start), out var n)) return n;
        }
        return null;
    }

    /// <summary>Whether a command (by its key) must ask before using the object: <c>!d</c>, <c>!*</c>.</summary>
    public static bool AsksFirst(Item item, char command) => Has(item, "!" + command) || Has(item, "!*");

    /// <summary>Whether a digit picks the object for a command: <c>@q1</c> for q then 1, or <c>@1</c> for any command.</summary>
    public static bool HasTag(Item item, char command, char digit) => Has(item, $"@{command}{digit}") || Has(item, $"@{digit}");

    /// <summary>
    /// Angband preferred_quiver_slot: the number after <c>@f</c> or <c>@v</c> on ammunition or a
    /// throwing weapon, or null.
    /// </summary>
    public static int? QuiverSlot(Item item)
    {
        if (!(item.IsAmmo || item.IsThrowing) || string.IsNullOrEmpty(item.Note)) return null;
        for (var i = item.Note.IndexOf('@'); i >= 0 && i + 2 < item.Note.Length + 1; i = item.Note.IndexOf('@', i + 1))
            if (i + 2 < item.Note.Length && item.Note[i + 1] is 'f' or 'v' && char.IsAsciiDigit(item.Note[i + 2]))
                return item.Note[i + 2] - '0';
        return null;
    }

    /// <summary>
    /// Angband auto_pickup_okay: how many of a floor object to pick up automatically. With
    /// pickup_always, all that fit; <c>!g</c> never; <c>=g</c> all; with <c>=gN</c> (on the object or the
    /// matching stack carried, the carried one winning) up to N in the pack; with pickup_inven, all of
    /// an object matching a carried stack unless that stack says <c>!g</c>.
    /// </summary>
    public static int AutoPickupCount(Item item, int canCarry, IReadOnlyList<Item> matching, bool pickupAlways, bool pickupInven)
    {
        if (canCarry <= 0) return 0;
        if (pickupAlways) return canCarry;
        if (Has(item, "!g")) return 0;
        var objMax = NumberAfter(item, "=g");
        if (Count(item, "=g") > (objMax is null ? 0 : 1)) return canCarry;
        if (!pickupInven && objMax is null) return 0;

        var gear = matching.FirstOrDefault();
        if (gear is null) return objMax is { } n ? Math.Min(canCarry, n) : 0;
        if (Has(gear, "!g")) return 0;
        var gearMax = NumberAfter(gear, "=g");
        if (Count(gear, "=g") > (gearMax is null ? 0 : 1)) return canCarry;
        if ((gearMax ?? objMax) is { } max)
        {
            var carried = matching.Sum(i => i.Number);
            return carried >= max ? 0 : Math.Min(canCarry, max - carried);
        }
        return canCarry;
    }
}
