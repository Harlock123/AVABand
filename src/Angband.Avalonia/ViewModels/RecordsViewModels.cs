using System.Collections.ObjectModel;
using System.Globalization;
using Angband.Core.Records;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>The character sheet: the dump text, with a button to write it to a file.</summary>
public sealed partial class CharacterSheetViewModel(string title, string text, Func<string?>? save) : ObservableObject
{
    public string Title { get; } = title;
    public string Text { get; } = text;
    /// <summary>The text in colour, when it has any (monster recall); otherwise plain <see cref="Text"/>.</summary>
    public IReadOnlyList<ColoredRun>? Runs { get; init; }
    /// <summary>Prose (monster recall) wraps; the character sheet keeps its columns.</summary>
    public global::Avalonia.Media.TextWrapping Wrapping => Runs is null ? global::Avalonia.Media.TextWrapping.NoWrap : global::Avalonia.Media.TextWrapping.Wrap;
    public global::Avalonia.Controls.Primitives.ScrollBarVisibility HorizontalScroll =>
        Runs is null ? global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
    public bool CanSave => save is not null;

    [ObservableProperty] private string _status = "";

    [RelayCommand]
    private void Save()
    {
        if (save is null) return;
        try
        {
            Status = save() is { } path ? $"Saved to {path}" : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not save: {ex.Message}";
        }
    }
}

/// <summary>One row of the high-score table.</summary>
public sealed record ScoreRow(int Rank, ScoreEntry Entry, bool IsCurrent)
{
    public string RankText => Rank.ToString(CultureInfo.InvariantCulture);
    public string Points => Entry.Points.ToString("N0", CultureInfo.CurrentCulture);
    public string Who => $"{Entry.Name} the {Entry.Race} {Entry.Class}";
    public string Level => Entry.MaxLevel > Entry.Level ? $"{Entry.Level} ({Entry.MaxLevel})" : Entry.Level.ToString(CultureInfo.InvariantCulture);
    public string Depth => Entry.MaxDepth == 0 ? "Town" : $"{Entry.MaxDepth * 50} ft";
    public string Fate => Entry.Won
        ? (Entry.IsAlive ? "Winner (current game)" : "Retired victorious")
        : Entry.IsAlive
        ? (IsCurrent ? "Alive (current game)" : "Retired")
        : $"Killed by {Entry.KilledBy}{(Entry.Depth == 0 ? " in the town" : $" at {Entry.Depth * 50} ft")}";
    public string Date => Entry.DateUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
}

/// <summary>The high-score table, with the living character shown where it would place.</summary>
public sealed class HighScoresViewModel
{
    public HighScoresViewModel(ScoreBoard board, ScoreEntry? living, ScoreEntry? highlight)
    {
        var rows = board.Entries.Select((e, i) => new ScoreRow(i + 1, e, ReferenceEquals(e, highlight))).ToList();
        if (living is not null)
        {
            var rank = board.RankOf(living);
            rows.Insert(rank - 1, new ScoreRow(rank, living, true));
            for (var i = rank; i < rows.Count; i++) rows[i] = rows[i] with { Rank = i + 1 };
        }
        foreach (var row in rows) Rows.Add(row);
        Current = rows.FirstOrDefault(r => r.IsCurrent);
    }

    public ObservableCollection<ScoreRow> Rows { get; } = [];
    public ScoreRow? Current { get; }
    public bool IsEmpty => Rows.Count == 0;
}
