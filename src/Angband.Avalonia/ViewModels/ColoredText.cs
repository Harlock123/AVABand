using Angband.Core.Records;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Angband.Avalonia.ViewModels;

/// <summary>A stretch of text in one colour (null: the control's own colour).</summary>
public sealed record ColoredRun(string Text, IBrush? Brush);

/// <summary>Turns recall text with Angband colour marks (<see cref="RecallMarkup"/>) into coloured runs.</summary>
public static class ColoredText
{
    /// <summary>Angband's colour names ("Light Green") are the colour table's keys without the space.</summary>
    public static IReadOnlyList<ColoredRun> FromMarked(string marked, MapCellBuilder cells) =>
        RecallMarkup.Parse(marked)
            .Select(r => new ColoredRun(r.Text, r.Color is null ? null
                : new ImmutableSolidColorBrush(Color.FromUInt32(cells.Color(r.Color.Replace(" ", ""))))))
            .ToList();
}
