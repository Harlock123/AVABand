using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Angband.Avalonia.Views;

public partial class KnowledgeWindow : Window
{
    public KnowledgeWindow()
    {
        InitializeComponent();
        DialogKeys.CloseOnEscape(this);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
