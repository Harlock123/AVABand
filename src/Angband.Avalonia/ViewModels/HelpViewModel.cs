using System.Text.RegularExpressions;
using Avalonia.Platform;

namespace Angband.Avalonia.ViewModels;

/// <summary>A piece of a help line: plain, <c>**bold**</c>, <c>*italic*</c> or <c>`a key`</c>.</summary>
public sealed record HelpSpan(string Text, bool Bold = false, bool Code = false, bool Italic = false);

/// <summary>A line of a help page: a heading, a paragraph, or a list item.</summary>
public sealed record HelpBlock(HelpBlockKind Kind, IReadOnlyList<HelpSpan> Spans);

public enum HelpBlockKind { Heading, Paragraph, Bullet }

/// <summary>A help page (Assets/Help/*.md): its title and its blocks.</summary>
public sealed record HelpTopic(string Title, IReadOnlyList<HelpBlock> Blocks)
{
    public override string ToString() => Title;
}

/// <summary>
/// The help ('?', as in Angband): pages on playing, written in a little Markdown — <c># title</c>,
/// <c>## heading</c>, <c>- list item</c>, paragraphs, <c>**bold**</c> and <c>`keys`</c>.
/// </summary>
public sealed partial class HelpViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public IReadOnlyList<HelpTopic> Topics { get; }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private HelpTopic? _selected;

    public HelpViewModel(IReadOnlyList<HelpTopic>? topics = null)
    {
        Topics = topics ?? LoadBundled();
        _selected = Topics.FirstOrDefault();
    }

    /// <summary>The pages bundled with the game, in file-name order.</summary>
    public static IReadOnlyList<HelpTopic> LoadBundled()
    {
        var folder = new Uri("avares://AVABand/Assets/Help/");
        return [.. AssetLoader.GetAssets(folder, null)
            .Where(u => u.AbsolutePath.EndsWith(".md", StringComparison.Ordinal))
            .OrderBy(u => u.AbsolutePath, StringComparer.Ordinal)
            .Select(u =>
            {
                using var reader = new StreamReader(AssetLoader.Open(u));
                return Parse(reader.ReadToEnd());
            })];
    }

    public static HelpTopic Parse(string markdown)
    {
        var title = "Help";
        var blocks = new List<HelpBlock>();
        (HelpBlockKind Kind, List<string> Lines)? open = null;

        void Flush()
        {
            if (open is { } o && o.Lines.Count > 0) blocks.Add(new HelpBlock(o.Kind, Spans(string.Join(" ", o.Lines))));
            open = null;
        }

        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("# ", StringComparison.Ordinal)) { Flush(); title = line[2..].Trim(); }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                // (What's new's later rounds of a day, "2026-09-30.2", read as just the date.)
                var heading = line[3..].Trim();
                if (SameDayRound().IsMatch(heading)) heading = heading[..10];
                blocks.Add(new HelpBlock(HelpBlockKind.Heading, Spans(heading)));
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal)) { Flush(); open = (HelpBlockKind.Bullet, [line[2..].Trim()]); }
            else if (line.Length == 0) Flush();
            else if (open is { } o) o.Lines.Add(line.Trim());
            else open = (HelpBlockKind.Paragraph, [line.Trim()]);
        }
        Flush();
        return new HelpTopic(title, blocks);
    }

    /// <summary>Splits a line into plain, bold and code spans.</summary>
    public static IReadOnlyList<HelpSpan> Spans(string text)
    {
        var spans = new List<HelpSpan>();
        foreach (var part in SpanPattern().Split(text))
        {
            if (part.Length == 0) continue;
            if (part.Length > 4 && part.StartsWith("**", StringComparison.Ordinal) && part.EndsWith("**", StringComparison.Ordinal))
                spans.Add(new HelpSpan(part[2..^2], Bold: true));
            else if (part.Length > 2 && part[0] == '`' && part[^1] == '`') spans.Add(new HelpSpan(part[1..^1], Code: true));
            else if (part.Length > 2 && part[0] == '*' && part[^1] == '*') spans.Add(new HelpSpan(part[1..^1], Italic: true));
            else spans.Add(new HelpSpan(part));
        }
        return spans;
    }

    [GeneratedRegex(@"(\*\*[^*]+\*\*|`[^`]+`|\*[^*\s][^*]*\*)")]
    private static partial Regex SpanPattern();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}\.\d+$")]
    private static partial Regex SameDayRound();
}
