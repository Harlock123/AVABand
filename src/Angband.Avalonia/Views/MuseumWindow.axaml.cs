using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Angband.Avalonia.Views;

public partial class MuseumWindow : Window
{
    public MuseumWindow()
    {
        InitializeComponent();
        DialogKeys.CloseOnEscape(this);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
