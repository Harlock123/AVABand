using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Angband.Avalonia.Views;

public partial class HighScoresWindow : Window
{
    public HighScoresWindow()
    {
        InitializeComponent();
        DialogKeys.CloseOnEscape(this, alsoEnter: true);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
