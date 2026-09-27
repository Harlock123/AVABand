using Angband.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Angband.Avalonia.Views;

public partial class LoadGameWindow : Window
{
    public LoadGameWindow()
    {
        InitializeComponent();
        DialogKeys.CloseOnEscape(this);
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Return && DataContext is LoadGameViewModel vm && vm.LoadCommand.CanExecute(null))
                vm.LoadCommand.Execute(null);
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is LoadGameViewModel vm) vm.Loaded += Close;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is LoadGameViewModel vm && vm.LoadCommand.CanExecute(null)) vm.LoadCommand.Execute(null);
    }
}
