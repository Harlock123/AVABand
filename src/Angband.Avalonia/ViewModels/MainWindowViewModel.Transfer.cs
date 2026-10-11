using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

// Moving a character to another computer (AVABand's own): Game → Export this character... copies the
// save of the one you're playing (saved first) anywhere you choose; the title screen's "Import a save
// from another computer" (and the Load dialog's Import) brings one in. See LoadGameViewModel.
public sealed partial class MainWindowViewModel
{
    /// <summary>The window's file picker for where to export, given a suggested file name (null: cancelled).</summary>
    public Func<string, Task<string?>>? PickExportFile { get; set; }

    /// <summary>Raised to import a save: the view opens the Load dialog and its file picker.</summary>
    public event Action? ImportRequested;

    /// <summary>Why the character in play can't be exported, or null.</summary>
    private string? CannotExport() =>
        _saves is null ? "Saving is off."
        : _game.Player.IsDead ? "The dead cannot be saved, nor exported."
        : _game.IsTutorial ? "The tutorial isn't saved."
        : _game.IsReplay ? "A replay isn't a character to export."
        : null;

    [RelayCommand]
    private async Task ExportCharacter()
    {
        if (CannotExport() is { } why)
        {
            AddMessage(why);
            return;
        }
        if (PickExportFile is null || await PickExportFile(Path.GetFileName(_saves!.PathFor(_game))) is not { } destination) return;
        ExportCharacterTo(destination);
    }

    /// <summary>Saves the character in play, then copies its save to <paramref name="destination"/>.</summary>
    public bool ExportCharacterTo(string destination)
    {
        if (CannotExport() is { } why)
        {
            AddMessage(why);
            return false;
        }
        if (!TrySave()) return false; // (said why)
        try
        {
            _saves!.Export(_saves.PathFor(_game), destination);
            AddMessage($"Copied {_game.Player.Name}'s save to {destination}. On the other computer: Load character, then Import a save.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddMessage($"Could not export the save: {ex.Message}");
            return false;
        }
    }

    private void RequestImport() => ImportRequested?.Invoke();
}
