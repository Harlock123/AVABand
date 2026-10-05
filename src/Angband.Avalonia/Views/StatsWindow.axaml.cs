using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Angband.Avalonia.Views;

/// <summary>The statistics page: a character's tallies and the monsters they've killed most.</summary>
public partial class StatsWindow : Window
{
    public StatsWindow()
    {
        InitializeComponent();
        DialogKeys.CloseOnEscape(this);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
