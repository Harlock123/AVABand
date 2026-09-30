using System.Globalization;
using Avalonia.Platform;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// What's new (AVABand's own): Assets/Help/11-whats-new.md, a dated section ("## 2026-10-01") for
/// each round of changes, newest first. After an update the sections newer than the last one the
/// player saw are shown once; the whole page is in Help (Help → What's new...).
/// </summary>
public static class WhatsNew
{
    public const string Page = "avares://AVABand/Assets/Help/11-whats-new.md";

    /// <summary>The page's text (bundled with the game).</summary>
    public static string Load()
    {
        using var reader = new StreamReader(AssetLoader.Open(new Uri(Page)));
        return reader.ReadToEnd();
    }

    /// <summary>The page's dated sections, newest first: each date and its lines.</summary>
    public static IReadOnlyList<(string Date, string Text)> Sections(string markdown)
    {
        var sections = new List<(string, string)>();
        string? date = null;
        var lines = new List<string>();
        foreach (var line in markdown.Replace("\r", "").Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal)
                && DateOnly.TryParseExact(line[3..].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                if (date is not null) sections.Add((date, string.Join('\n', lines).Trim()));
                date = line[3..].Trim();
                lines.Clear();
            }
            else if (date is not null) lines.Add(line);
        }
        if (date is not null) sections.Add((date, string.Join('\n', lines).Trim()));
        return sections.OrderByDescending(s => s.Item1, StringComparer.Ordinal).ToList();
    }

    /// <summary>The newest section's date: what the player has seen, once shown.</summary>
    public static string? Newest(string markdown) => Sections(markdown).FirstOrDefault().Date;

    /// <summary>
    /// The page to show now, or null if there is nothing new: the sections newer than
    /// <paramref name="lastSeen"/> (if the player has never been shown one, just the newest).
    /// </summary>
    public static HelpTopic? Since(string markdown, string? lastSeen)
    {
        var sections = Sections(markdown);
        var fresh = lastSeen is null ? sections.Take(1).ToList()
            : sections.Where(s => string.CompareOrdinal(s.Date, lastSeen) > 0).ToList();
        if (fresh.Count == 0) return null;
        var text = "# What's new since you last played\n\nThe whole list is in Help → What's new...\n\n"
                   + string.Join("\n\n", fresh.Select(s => $"## {s.Date}\n\n{s.Text}"));
        return HelpViewModel.Parse(text);
    }
}
