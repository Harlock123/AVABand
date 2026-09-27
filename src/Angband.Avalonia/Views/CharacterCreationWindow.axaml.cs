using Angband.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Angband.Avalonia.Views;

public partial class CharacterCreationWindow : Window
{
    public CharacterCreationWindow()
    {
        InitializeComponent();
        DialogKeys.CloseOnEscape(this); // like Cancel
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is CharacterCreationViewModel vm) vm.Started += _ => Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
