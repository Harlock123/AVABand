using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Angband.Avalonia.Views;

/// <summary>The Grimoire: every spell in every book, realm by realm.</summary>
public partial class GrimoireWindow : Window
{
    public GrimoireWindow()
    {
        InitializeComponent();
        DialogKeys.CloseOnEscape(this);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
