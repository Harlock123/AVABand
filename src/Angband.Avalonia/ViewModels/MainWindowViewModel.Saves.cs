using Angband.Core.Game;
using Angband.Core.Persistence;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

// Saving and loading: Ctrl+S saves, Ctrl+O opens the load dialog, the game autosaves on every level
// change and on exit, and death deletes the save (permadeath).
public sealed partial class MainWindowViewModel
{
    private SaveStore? _saves;

    public bool CanSave => _saves is not null;

    /// <summary>The game in progress.</summary>
    public GameSession Game => _game;

    /// <summary>Raised when the load dialog should open (the view shows it).</summary>
    public event Action? LoadRequested;

    /// <summary>Uses a save folder; if <paramref name="resume"/>, continues the most recent living character.</summary>
    public void UseSaves(SaveStore saves, bool resume)
    {
        _saves = saves;
        OnPropertyChanged(nameof(CanSave));
        if (!resume) return;
        foreach (var entry in saves.List().Where(e => !e.Summary.IsDead))
        {
            if (!TryLoad(entry.Path, out _)) continue;
            _loadedAtStartup = true;
            return;
        }
        // No living character, though there was one: ask what next rather than just starting over.
        ShouldOfferStartMenu = _settings.LastCharacter is not null;
    }

    [RelayCommand]
    public void SaveGame()
    {
        if (_saves is null) return;
        if (_game.Player.IsDead)
        {
            AddMessage("The dead cannot be saved.");
            return;
        }
        if (TrySave()) AddMessage("Game saved.");
    }

    [RelayCommand]
    private void LoadGame()
    {
        if (_saves is not null) LoadRequested?.Invoke();
    }

    public LoadGameViewModel CreateLoadGame()
    {
        var dialog = new LoadGameViewModel(_saves ?? SaveStore.Default());
        dialog.LoadRequested += entry =>
        {
            // Keep the current character before switching to another.
            if (!_game.Player.IsDead) TrySave();
            if (TryLoad(entry.Path, out var error)) return true;
            dialog.Error = error;
            return false;
        };
        return dialog;
    }

    /// <summary>Saves quietly (autosave); returns false and reports if the disk refused.</summary>
    public bool TrySave()
    {
        SaveLore();
        if (_saves is null || _game.Player.IsDead) return false;
        try
        {
            _saves.Save(_game);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddMessage($"Could not save the game: {ex.Message}");
            return false;
        }
    }

    public bool TryLoad(string path, out string error)
    {
        error = "";
        if (_saves is null) return false;
        try
        {
            AttachGame(_saves.Load(_data, path));
            AddMessage($"Welcome back, {_game.Player.Name} the {_game.Player.Race?.Name} {_game.Player.Class?.Name}.");
            Refresh();
            return true;
        }
        catch (Exception ex) when (ex is SaveGameException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            AddMessage($"Could not load {Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    private void OnLevelChanged(LevelChangedEvent e) => TrySave();

    private void OnPlayerDied(PlayerDiedEvent e)
    {
        var score = RecordDeath();
        try
        {
            _saves?.DeleteFor(_game);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The save may linger; it is marked alive, but death is final in the UI either way.
        }
        ShowDeathMenu(score);
    }
}
