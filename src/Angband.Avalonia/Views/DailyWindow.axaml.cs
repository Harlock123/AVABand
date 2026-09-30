using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Angband.Avalonia.Views;

public partial class DailyWindow : Window
{
    public DailyWindow()
    {
        InitializeComponent();
        DialogKeys.CloseOnEscape(this);
        // Playing or watching closes the window: the game goes on in the main one.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not ViewModels.DailyViewModel daily) return;
            daily.PlayRequested += Close;
            daily.WatchRequested += _ => Close();
        };
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
