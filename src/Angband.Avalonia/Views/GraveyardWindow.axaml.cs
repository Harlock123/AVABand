using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Angband.Avalonia.Views;

public partial class GraveyardWindow : Window
{
    public GraveyardWindow()
    {
        InitializeComponent();
        DialogKeys.CloseOnEscape(this);
        // Watching closes the graveyard: the replay plays in the main window.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ViewModels.GraveyardViewModel yard) yard.WatchRequested += _ => Close();
        };
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
