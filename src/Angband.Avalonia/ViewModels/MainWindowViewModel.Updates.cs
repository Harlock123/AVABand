using Angband.Avalonia.Services;
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
