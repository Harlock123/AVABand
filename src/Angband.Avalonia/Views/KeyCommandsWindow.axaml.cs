using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Angband.Avalonia.Views;

/// <summary>The keyboard commands window ('?' or F1).</summary>
public partial class KeyCommandsWindow : Window
{
    public KeyCommandsWindow()
    {
        InitializeComponent();
        DialogKeys.CloseOnEscape(this);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
