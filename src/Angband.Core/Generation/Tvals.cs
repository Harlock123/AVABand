namespace Angband.Core.Generation;

/// <summary>Angband's tvals (as vault.txt and room_template.txt name them) in AVABand's object bases.</summary>
public static class Tvals
{
    /// <summary>The object bases a tval covers: a bow is any launcher, the rest one base each.</summary>
    public static IReadOnlyList<string> Bases(string tval) => tval switch
    {
        "bow" => ["sling", "bow", "crossbow"],
        "magic_book" or "prayer_book" or "nature_book" or "shadow_book" => [tval],
        _ => [tval.Replace(' ', '_')],
    };
}
