using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Angband.Avalonia.ViewModels;

/// <summary>A message as the log keeps it: repeats of the same message in a row are counted, as Angband does.</summary>
public sealed record LoggedMessage(string Text, int Count, long Turn)
{
    /// <summary>"You hit the orc. &lt;x3&gt;" — Angband's way of showing a repeated message.</summary>
    public string Display => Count > 1 ? $"{Text} <x{Count}>" : Text;
}

/// <summary>An earlier line in the message area: brighter if said this turn, dimmer if before.</summary>
public sealed record MessageLine(string Text, bool ThisTurn)
{
    public global::Avalonia.Media.IBrush Brush { get; } = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(
        ThisTurn ? global::Avalonia.Media.Color.FromRgb(0xC8, 0xC8, 0xC8) : global::Avalonia.Media.Color.FromRgb(0x78, 0x78, 0x78));
}

/// <summary>
/// The message history (Angband Ctrl+P): everything said this game, oldest first so the newest is at
/// the bottom, with a Find box to narrow it.
/// </summary>
public sealed partial class MessageHistoryViewModel : ObservableObject
{
    private readonly IReadOnlyList<LoggedMessage> _all;

    public MessageHistoryViewModel(IReadOnlyList<LoggedMessage> messages)
    {
        _all = messages;
        ApplyFilter();
    }

    public ObservableCollection<LoggedMessage> Rows { get; } = [];

    [ObservableProperty] private string _filter = "";

    public string Summary => Filter.Trim().Length == 0
        ? $"{_all.Count} messages"
        : $"{Rows.Count} of {_all.Count} messages";

    public bool IsEmpty => Rows.Count == 0;

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Rows.Clear();
        var text = Filter.Trim();
        foreach (var m in _all)
            if (text.Length == 0 || m.Text.Contains(text, StringComparison.OrdinalIgnoreCase)) Rows.Add(m);
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
