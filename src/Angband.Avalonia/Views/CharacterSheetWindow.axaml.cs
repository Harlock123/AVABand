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

    /// <summary>Monster recall and other plain pages keep the old size; the character sheet grows for its paper doll.</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.CharacterSheetViewModel { HasPaperDoll: false }) Height = 760;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
