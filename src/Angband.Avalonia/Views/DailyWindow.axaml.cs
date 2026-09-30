using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Angband.Avalonia.Views;

public partial class DailyWindow : Window
{
    public DailyWindow()
    {
        InitializeComponent();
        DialogKeys.CloseOnEscape(this);
        // Playing or watching closes the window: the game goes on in the main one.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not ViewModels.DailyViewModel daily) return;
            daily.PlayRequested += Close;
            daily.WatchRequested += _ => Close();
            daily.CopyRequested += async line =>
            {
                if (Clipboard is { } clipboard) await global::Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, line);
            };
            daily.SaveReplayRequested += SaveReplayAs;
        };
    }

    /// <summary>"Save replay as…": a copy of the try's replay, under a name that says which day and try, wherever you like.</summary>
    private async void SaveReplayAs(string source, string name)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Save the replay",
            SuggestedFileName = name,
            DefaultExtension = Angband.Core.Persistence.ReplayFile.Extension,
            FileTypeChoices = [new global::Avalonia.Platform.Storage.FilePickerFileType("AVABand replays") { Patterns = ["*.avareplay"] }],
        });
        if (file is null) return;
        await using var from = File.OpenRead(source);
        await using var to = await file.OpenWriteAsync();
        await from.CopyToAsync(to);
        if (DataContext is ViewModels.DailyViewModel daily) daily.ShareNote = $"Saved the replay as {file.Name}.";
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
