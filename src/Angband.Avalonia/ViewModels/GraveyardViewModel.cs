using System.Collections.ObjectModel;
using System.Globalization;
using Angband.Core.Records;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Angband.Avalonia.ViewModels;

/// <summary>One headstone: a fallen (or retired) character, as its stone reads.</summary>
public sealed record HeadstoneRow(FallenRecord Fallen)
{
    public string Name => Fallen.Name;
    public string Title => $"the {Fallen.Race} {Fallen.Class}" + (Fallen.Heroic ? " [Heroic]" : "");
    public string Deeds => $"Level {Fallen.Level}  ·  deepest {Fallen.MaxDepth * 50} ft";
    public string End => Fallen.Won ? "Retired victorious"
        : $"Slain by {Fallen.KilledBy}{(Fallen.Depth == 0 ? " in the town" : $" at {Fallen.Depth * 50} ft")}";
    public string Date => Fallen.DateUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
    public string Epitaph => Fallen.Epitaph;
    public bool Won => Fallen.Won;
    public bool HasReplay => Fallen.ReplayPath is { } path && File.Exists(path);
}

/// <summary>
/// The graveyard (AVABand's own): a headstone for every character that died or retired, newest
/// first, over the death scene's picture; choose one to read its last character dump.
/// </summary>
public sealed partial class GraveyardViewModel : ObservableObject
{
    public GraveyardViewModel(Graveyard yard, Bitmap? background)
    {
        foreach (var fallen in yard.Fallen) Stones.Add(new HeadstoneRow(fallen));
        Background = background;
        Selected = Stones.FirstOrDefault();
    }

    public ObservableCollection<HeadstoneRow> Stones { get; } = [];
    public Bitmap? Background { get; }
    public bool IsEmpty => Stones.Count == 0;

    public string Summary => Stones.Count switch
    {
        0 => "No one rests here yet.",
        1 => "One adventurer rests here.",
        var n => $"{n} adventurers rest here.",
    };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(WatchCommand))]
    private HeadstoneRow? _selected;

    /// <summary>Raised to save the chosen stone as a character card.</summary>
    public event Action<FallenRecord>? CardRequested;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SaveCard()
    {
        if (Selected is { } stone) CardRequested?.Invoke(stone.Fallen);
    }

    /// <summary>Raised with a replay to watch their last moments in (the window closes, the main window plays it).</summary>
    public event Action<string>? WatchRequested;

    private bool CanWatch => Selected?.HasReplay == true;

    [CommunityToolkit.Mvvm.Input.RelayCommand(CanExecute = nameof(CanWatch))]
    private void Watch()
    {
        if (Selected?.Fallen.ReplayPath is { } path) WatchRequested?.Invoke(path);
    }
    [ObservableProperty] private string _story = "";

    partial void OnSelectedChanged(HeadstoneRow? value)
    {
        if (value is null)
        {
            Story = "";
            return;
        }
        var header = $"{value.Name}, {value.Title}\n{value.End}.\n\n“{value.Epitaph}”\n\n";
        string? dump = null;
        try
        {
            if (value.Fallen.DumpPath is { } path && File.Exists(path)) dump = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable dump is left out.
        }
        Story = header + (dump ?? "No account of their last days was kept (they fell before the graveyard kept them).");
    }
}

public sealed partial class MainWindowViewModel
{
    /// <summary>Raised to show the graveyard (the view opens its window).</summary>
    public event Action<GraveyardViewModel>? GraveyardRequested;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void ShowGraveyard() => GraveyardRequested?.Invoke(CreateGraveyard());

    public GraveyardViewModel CreateGraveyard()
    {
        Bitmap? background = null;
        try
        {
            if (PictureFor("death") is { } path) background = new Bitmap(path);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            // No picture: a plain dark ground.
        }
        var yard = new GraveyardViewModel(_records?.LoadGraveyard() ?? new Graveyard(), background);
        yard.WatchRequested += path => PlayLastMoments(path);
        yard.CardRequested += SaveFallenCard;
        return yard;
    }
}
