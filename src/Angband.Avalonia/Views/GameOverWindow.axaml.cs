using Angband.Avalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Views;

/// <summary>The game-over menu: after a death, or on starting with no living character.</summary>
public partial class GameOverWindow : Window
{
    public GameOverWindow()
    {
        InitializeComponent();
        DialogKeys.CloseOnEscape(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is GameOverMenuViewModel menu) menu.Chosen += Close;
        };
        KeyDown += (_, e) =>
        {
            if (e.Handled || DataContext is not GameOverMenuViewModel menu) return;
            if (e.KeySymbol is { Length: 1 } s && char.IsLetter(s[0]) && menu.ChooseLetter(s[0])) e.Handled = true;
        };
        // The first choice has the focus, so Enter (or the controller's A) takes it.
        Opened += (_, _) => ChoiceList.GetVisualDescendants().OfType<Button>().FirstOrDefault()?.Focus();
    }

    private void OnChoice(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GameOverChoice choice } && DataContext is GameOverMenuViewModel menu) menu.Choose(choice);
    }
}
