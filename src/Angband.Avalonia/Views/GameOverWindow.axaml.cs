using Angband.Avalonia.ViewModels;
using Avalonia;
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
            // (Letters typed into the last message, or used in its list, are words, not choices.)
            if (e.Source is Visual v && (v is TextBox or ComboBox || v.FindAncestorOfType<TextBox>() is not null || v.FindAncestorOfType<ComboBox>() is not null)) return;
            if (e.KeySymbol is { Length: 1 } s && char.IsLetter(s[0]) && menu.ChooseLetter(s[0])) e.Handled = true;
        };
        // The first choice has the focus, so Enter (or the controller's A) takes it.
        Opened += (_, _) => ChoiceList.GetVisualDescendants().OfType<Button>().FirstOrDefault()?.Focus();
    }

    private void OnCarve(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GameOverMenuViewModel { LastWords: { } words } && words.Carve())
            ChoiceList.GetVisualDescendants().OfType<Button>().FirstOrDefault()?.Focus();
    }

    private void OnChoice(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: GameOverChoice choice } && DataContext is GameOverMenuViewModel menu) menu.Choose(choice);
    }
}
