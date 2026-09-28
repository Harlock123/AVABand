using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Angband.Avalonia.Views;

/// <summary>The journey: depth over time, what happened at each depth, and the history.</summary>
public partial class JourneyWindow : Window
{
    public JourneyWindow()
    {
        InitializeComponent();
        DialogKeys.CloseOnEscape(this);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
