using Angband.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>Help → Check for updates: asks GitHub whether a newer AVABand has been released.</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>The check itself (GitHub by default; tests give their own answer).</summary>
    internal Func<CancellationToken, Task<UpdateCheck>> CheckUpdates { get; set; } =
        cancel => UpdateChecker.CreateDefault().CheckAsync(UpdateChecker.BuildCommit(), cancel);

    /// <summary>Asks the window to open a web page in the browser.</summary>
    public event Action<string>? OpenUrlRequested;

    private bool _checkingUpdates;

    /// <summary>
    /// A newer release found at start-up (the option "Check GitHub for a newer AVABand when the game
    /// starts"): shown on the title screen, and said once in the messages. Null when there is none,
    /// the option is off, or GitHub couldn't be asked (which is not worth a word).
    /// </summary>
    [ObservableProperty] private string? _updateNotice;

    private string? _updateUrl;

    /// <summary>At start-up, if the option is on: a quiet look for a newer release.</summary>
    public async Task CheckUpdatesAtStartAsync()
    {
        if (!OptionValue(DisplayOptions.CheckUpdatesAtStart)) return;
        UpdateCheck result;
        try { result = await CheckUpdates(CancellationToken.None); }
        catch (Exception) { return; } // never let a start-up nicety get in the way
        if (result is not { Status: UpdateStatus.NewerAvailable, ReleaseUrl: { } url }) return;
        _updateUrl = url;
        UpdateNotice = result.Message;
        AddMessage(result.Message + " (Help → Check for updates)");
    }

    /// <summary>The title screen's notice, clicked: the download page.</summary>
    [RelayCommand]
    private void OpenUpdatePage()
    {
        if (_updateUrl is { } url) OpenUrlRequested?.Invoke(url);
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        if (_checkingUpdates) return;
        _checkingUpdates = true;
        try
        {
            LastMessage = "Checking GitHub for a newer AVABand...";
            var result = await CheckUpdates(CancellationToken.None);
            if (result is { Status: UpdateStatus.NewerAvailable or UpdateStatus.Diverged, ReleaseUrl: { } url })
                AskFirst(result.Message + " Open the download page?", () =>
                {
                    OpenUrlRequested?.Invoke(url);
                    LastMessage = "Opening the download page in your browser.";
                });
            else LastMessage = result.Message;
        }
        finally { _checkingUpdates = false; }
    }
}
