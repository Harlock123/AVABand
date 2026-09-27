using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Angband.Avalonia.Views;

public partial class CharacterSheetWindow : Window
{
    public CharacterSheetWindow()
    {
        InitializeComponent();
        DialogKeys.CloseOnEscape(this, alsoEnter: true);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
