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
        var crashed = saves.LastSessionCrashed;
        saves.MarkSessionOpen();
        if (!resume) return;
        foreach (var entry in saves.List().Where(e => !e.Summary.IsDead))
        {
            if (!TryLoad(entry.Path, out _)) continue;
            _loadedAtStartup = true;
            if (crashed) _crashResume = File.GetLastWriteTimeUtc(entry.Path);
            return;
        }
        // No living character, though there was one: ask what next rather than just starting over.
        ShouldOfferStartMenu = _settings.LastCharacter is not null;
    }

    /// <summary>When the save resumed after a crash was written (null: the last session closed cleanly).</summary>
    private DateTime? _crashResume;

    /// <summary>The last session crashed, and its latest save was loaded: the window asks about it.</summary>
    public bool ShouldOfferResume => _crashResume is not null;

    /// <summary>
    /// After a crash: "Resume ... from the save made 3 minutes ago?" Yes plays on; no opens the list
    /// of saved characters (closing it keeps this one).
    /// </summary>
    public void OfferResume()
    {
        if (_crashResume is not { } saved) return;
        _crashResume = null;
        var p = _game.Player;
        AskYesNo($"AVABand didn't close properly last time. Resume {p.Name} the {p.Race?.Name} {p.Class?.Name} from the save made {Ago(DateTime.UtcNow - saved)}? (y/n)",
            yes =>
            {
                if (yes) AddMessage("Resumed from the last save; anything after it was lost.");
                else LoadRequested?.Invoke();
            });
    }

    /// <summary>"a moment ago", "3 minutes ago", "2 hours ago", "4 days ago".</summary>
    public static string Ago(TimeSpan span) =>
        span.TotalMinutes < 1 ? "a moment ago"
        : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} minute{((int)span.TotalMinutes == 1 ? "" : "s")} ago"
        : span.TotalDays < 1 ? $"{(int)span.TotalHours} hour{((int)span.TotalHours == 1 ? "" : "s")} ago"
        : $"{(int)span.TotalDays} day{((int)span.TotalDays == 1 ? "" : "s")} ago";

    /// <summary>The window is closing normally: this session did not crash.</summary>
    public void ClosedCleanly() => _saves?.MarkSessionClosed();

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

    /// <summary>Ctrl+X (Angband's save and quit): saves the living character, then closes the game.</summary>
    [RelayCommand]
    public void SaveAndQuit()
    {
        if (!_game.Player.IsDead && _saves is not null && !TrySave()) return; // (said why; stay rather than lose it)
        ExitRequested?.Invoke();
    }

    [RelayCommand]
    private void LoadGame()
    {
        if (_saves is not null) LoadRequested?.Invoke();
    }

    public LoadGameViewModel CreateLoadGame()
    {
        var store = _saves ?? SaveStore.Default();
        var dialog = new LoadGameViewModel(store);
        dialog.LoadRequested += entry =>
        {
            // Keep the current character before switching to another (or before rolling it back).
            if (!_game.Player.IsDead) TrySave();
            var toLoad = entry;
            if (entry.IsBackup)
            {
                try { toLoad = store.Restore(entry); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    dialog.Error = $"Could not restore that save: {ex.Message}";
                    return false;
                }
            }
            if (TryLoad(toLoad.Path, out var error))
            {
                if (toLoad.RestoredFrom is { } when)
                {
                    var text = $"Restored from an earlier save ({when.ToLocalTime():g}).";
                    _game.AddHistory(text);
                    AddMessage(text);
                }
                return true;
            }
            dialog.Error = error;
            return false;
        };
        return dialog;
    }

    /// <summary>How often the game saves by itself while you play (besides each level change and exit).</summary>
    public static readonly TimeSpan AutosaveInterval = TimeSpan.FromMinutes(5);

    private DateTime? _lastSave;

    /// <summary>After a command: save if it has been <see cref="AutosaveInterval"/> since the last save (the option allowing).</summary>
    private void AutosaveIfDue()
    {
        if (_saves is null || _game.Player.IsDead || !OptionValue(DisplayOptions.Autosave)) return;
        var now = Clock();
        _lastSave ??= now; // the clock starts with the game
        if (now - _lastSave.Value >= AutosaveInterval) TrySave();
    }

    /// <summary>Saves quietly (autosave); returns false and reports if the disk refused.</summary>
    public bool TrySave()
    {
        SaveLore();
        if (_saves is null || _game.Player.IsDead || _game.IsTutorial || _game.IsReplay) return false; // (nor is the tutorial, or a replay)
        try
        {
            _saves.Save(_game);
            _lastSave = Clock();
            WriteReplay();
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
            // Carried on from a save, it's the character "the same character" means now (not whoever was made last).
            if (_game.Player.DailyDate is null && SpecOf(_game) is { } spec) RememberCharacter(spec);
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

    private void OnLevelChanged(LevelChangedEvent e)
    {
        TrySave();
        SceneForArrival(e);
    }

    private void OnPlayerDied(PlayerDiedEvent e)
    {
        if (TutorialDeath()) return;
        if (_game.IsReplay) return; // watching a replay: nothing to record or delete
        WriteReplay();
        SceneForDeath(); // shown behind the game-over menu
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
