using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Angband.Avalonia.Views;

/// <summary>The message history (Ctrl+P), opened at the newest message.</summary>
public partial class MessageHistoryWindow : Window
{
    public MessageHistoryWindow()
    {
        InitializeComponent();
        DialogKeys.CloseOnEscape(this);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // Start at the bottom, where the latest messages are.
        Dispatcher.UIThread.Post(() => Scroller.ScrollToEnd(), DispatcherPriority.Background);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
