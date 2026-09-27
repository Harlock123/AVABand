using Angband.Avalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;

namespace Angband.Avalonia.Views;

/// <summary>
/// <c>RichText.Runs</c>: fills a text block with coloured runs (monster recall's spell colours).
/// When unset the block shows its plain <c>Text</c>.
/// </summary>
public static class RichText
{
    public static readonly AttachedProperty<IReadOnlyList<ColoredRun>?> RunsProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IReadOnlyList<ColoredRun>?>("Runs", typeof(RichText));

    static RichText() => RunsProperty.Changed.AddClassHandler<TextBlock>((block, e) => Apply(block, e.NewValue as IReadOnlyList<ColoredRun>));

    public static IReadOnlyList<ColoredRun>? GetRuns(TextBlock block) => block.GetValue(RunsProperty);
    public static void SetRuns(TextBlock block, IReadOnlyList<ColoredRun>? runs) => block.SetValue(RunsProperty, runs);

    private static void Apply(TextBlock block, IReadOnlyList<ColoredRun>? runs)
    {
        if (runs is null) return;
        var inlines = new InlineCollection();
        foreach (var run in runs)
        {
            var r = new Run(run.Text);
            if (run.Brush is not null) r.Foreground = run.Brush;
            inlines.Add(r);
        }
        block.Inlines = inlines;
    }
}
