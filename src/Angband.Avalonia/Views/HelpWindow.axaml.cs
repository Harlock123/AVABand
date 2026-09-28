using Angband.Avalonia.Rendering;
using Angband.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Angband.Avalonia.Views;

/// <summary>The help ('?'): topics on the left, the page on the right.</summary>
public partial class HelpWindow : Window
{
    private static readonly FontFamily Mono = new(AsciiRenderer.MonoFontUri);
    private static readonly IBrush KeyBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xC3, 0x5C));
    private static readonly IBrush HeadingBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0xB4, 0xF8));

    public HelpWindow()
    {
        InitializeComponent();
        DialogKeys.CloseOnEscape(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is HelpViewModel help)
                help.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(HelpViewModel.Selected)) ShowPage(); };
            ShowPage();
        };
    }

    /// <summary>Lays the selected page out: headings, paragraphs and bulleted lines, with keys highlighted.</summary>
    private void ShowPage()
    {
        Page.Children.Clear();
        if (DataContext is not HelpViewModel { Selected: { } topic }) return;
        Page.Children.Add(new TextBlock { Text = topic.Title, FontSize = 22, FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 4) });
        foreach (var block in topic.Blocks)
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, Inlines = [] };
            foreach (var span in block.Spans)
            {
                var run = new Run(span.Text);
                if (span.Bold) run.FontWeight = FontWeight.SemiBold;
                if (span.Italic) run.FontStyle = FontStyle.Italic;
                if (span.Code)
                {
                    run.FontFamily = Mono;
                    run.Foreground = KeyBrush;
                }
                text.Inlines!.Add(run);
            }
            switch (block.Kind)
            {
                case HelpBlockKind.Heading:
                    text.FontSize = 16;
                    text.FontWeight = FontWeight.SemiBold;
                    text.Foreground = HeadingBrush;
                    text.Margin = new(0, 8, 0, 0);
                    Page.Children.Add(text);
                    break;
                case HelpBlockKind.Bullet:
                    var row = new DockPanel { Margin = new(8, 0, 0, 0) };
                    var dot = new TextBlock { Text = "•", Margin = new(0, 0, 8, 0), FontSize = 14 };
                    DockPanel.SetDock(dot, Dock.Left);
                    row.Children.Add(dot);
                    row.Children.Add(text);
                    Page.Children.Add(row);
                    break;
                default:
                    Page.Children.Add(text);
                    break;
            }
        }
        PageScroller.Offset = default;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
