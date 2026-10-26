using Angband.Core.Game;

namespace Angband.Core.Records;

/// <summary>What an unlock gives.</summary>
public enum UnlockKind
{
    /// <summary>Another picture the title screen can show (one of the bundled scene pictures).</summary>
    TitleScene,

    /// <summary>A frame for character cards (Settings → Display → Card frame).</summary>
    CardFrame,

    /// <summary>An alternative starting kit on the creation screen (<see cref="StartingKits"/>).</summary>
    StartingKit,
}

/// <summary>
/// An unlock: a reward for feats that changes nothing about the game's balance — a picture, a card
/// frame, another choice of kit.
/// </summary>
/// <param name="Key">What it unlocks: the picture's name for a title scene, the frame's id, or the kit's id.</param>
/// <param name="Feats">The feats it needs, every one of them (none: only a count).</param>
/// <param name="FeatCount">How many feats, of any kind, it needs (0: only <paramref name="Feats"/>).</param>
public sealed record UnlockDef(string Id, string Name, UnlockKind Kind, string Key, string Description,
    IReadOnlyList<string> Feats, int FeatCount = 0)
{
    /// <summary>Whether the feats done so far earn it.</summary>
    public bool IsEarned(IReadOnlyCollection<string> done) =>
        Feats.All(done.Contains) && done.Count >= FeatCount;

    /// <summary>What earns it, in words: the feats by name (as the Feats page has them), or how many.</summary>
    public string Requirement(IReadOnlyList<FeatDef> feats)
    {
        var named = Feats.Select(id => feats.FirstOrDefault(f => f.Id == id) is { } f ? $"“{f.Name}” ({f.Description.TrimEnd('.')})" : id).ToList();
        var parts = new List<string>();
        if (named.Count > 0) parts.Add("the feat " + string.Join(" and ", named));
        if (FeatCount > 0) parts.Add($"{FeatCount} feats of any kind");
        return "Earned by " + string.Join(", with ", parts) + ".";
    }
}

/// <summary>
/// AVABand's unlocks (the Knowledge screen's Unlocks page): cosmetic rewards and extra choices earned
/// through feats, across every character — a reason to start another. Each is worked out from the
/// feats done (<see cref="FeatBook"/>), so there is nothing more to save.
/// </summary>
public static class Unlocks
{
    /// <summary>Every unlock, in the order the page lists them.</summary>
    public static IReadOnlyList<UnlockDef> All { get; } =
    [
        // Title scenes: the bundled pictures of moments in the game, behind the title in turn.
        new("title_shaft", "The Long Fall", UnlockKind.TitleScene, "deep-descent",
            "a title-screen scene: a shaft falling away to a red glow", ["depth_20"]),
        new("title_pony", "The Prancing Pony", UnlockKind.TitleScene, "quest-complete",
            "a title-screen scene: a letter of thanks on the Pony's table", ["ava_quest"]),
        new("title_halls", "The Dwarves' Halls", UnlockKind.TitleScene, "level-moria",
            "a title-screen scene: the pillared halls of the dwarves", ["heart_returned"]),
        new("title_crown", "A Fallen Crown", UnlockKind.TitleScene, "boss-slain",
            "a title-screen scene: a fallen crown in a shaft of light", ["uniques_10"]),

        // Card frames: drawn around character cards.
        new("frame_trophy", "Trophy frame", UnlockKind.CardFrame, "trophy",
            "a studded bronze character-card frame", ["uniques_1"]),
        new("frame_spectral", "Spectral frame", UnlockKind.CardFrame, "spectral",
            "a pale, glowing character-card frame", ["ghost"]),
        new("frame_iron", "Iron frame", UnlockKind.CardFrame, "iron",
            "a heavy, riveted iron character-card frame", ["challenge_ironman"]),
        new("frame_dawn", "Dawn frame", UnlockKind.CardFrame, "dawn",
            "a character-card frame in the colours of daybreak", ["daily"]),
        new("frame_gold", "Gold frame", UnlockKind.CardFrame, "gold",
            "a gold character-card frame, for a winner", ["win"]),
        new("frame_silver", "Silver frame", UnlockKind.CardFrame, "silver",
            "a silver character-card frame", [], 5),
        new("frame_elven", "Elven frame", UnlockKind.CardFrame, "elven",
            "a green and silver character-card frame, leaf-cornered", [], 15),
        new("frame_mithril", "Mithril frame", UnlockKind.CardFrame, "mithril",
            "a shining mithril character-card frame", [], 30),

        // Starting kits: other choices on the creation screen, worth no more than the standard kit.
        new("kit_delver", StartingKits.Delver.Name, UnlockKind.StartingKit, StartingKits.Delver.Id,
            "a starting-kit choice on the creation screen: oil, food and Phase Door instead of the Word of Recall", ["depth_40"]),
        new("kit_scholar", StartingKits.Scholar.Name, UnlockKind.StartingKit, StartingKits.Scholar.Id,
            "a starting-kit choice on the creation screen: scrolls of lore instead of the Word of Recall", ["level_20"]),
    ];

    /// <summary>The unlocks the feats done so far earn, in the page's order.</summary>
    public static IReadOnlyList<UnlockDef> Earned(FeatBook book) => Earned(book.Earned.Keys.ToHashSet(StringComparer.Ordinal));

    /// <summary>The unlocks these feats earn.</summary>
    public static IReadOnlyList<UnlockDef> Earned(IReadOnlyCollection<string> done) => [.. All.Where(u => u.IsEarned(done))];

    /// <summary>The unlocks earned only now, by the feats just done (for the "Unlocked:" messages).</summary>
    public static IReadOnlyList<UnlockDef> NewlyEarned(FeatBook book, IEnumerable<FeatDef> fresh)
    {
        var now = book.Earned.Keys.ToHashSet(StringComparer.Ordinal);
        var before = now.Except(fresh.Select(f => f.Id)).ToHashSet(StringComparer.Ordinal);
        return [.. All.Where(u => u.IsEarned(now) && !u.IsEarned(before))];
    }

    /// <summary>Whether a kind of unlock with this key is earned (a frame, a scene, a kit).</summary>
    public static bool Has(FeatBook book, UnlockKind kind, string key) => Earned(book).Any(u => u.Kind == kind && u.Key == key);
}
