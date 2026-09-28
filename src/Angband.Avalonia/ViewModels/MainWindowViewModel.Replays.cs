using Angband.Core.Game;
using Angband.Core.Persistence;
using Angband.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

// Replays (AVABand's own). Every game is deterministic, so each is recorded as it is played — where
// it began, then every command and choice — into <AppData>/AVABand/replays, rewritten whenever the
// game saves and at death (the option "Record every game as a replay"). Game → Watch a replay…
// plays one back in the main window: Enter pauses, → steps, ↑ ↓ change the speed, Esc stops (and
// the character in play comes back). At the end it says whether the replay came out exactly as
// recorded. A replay file can be sent to someone else and opened with "Open a replay file…".
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Where replays are written: replays/ beside the saves (so none are written while games aren't
    /// saved, as in tests), unless set.
    /// </summary>
    public string? ReplayDirectory
    {
        get => _replayDirectory ?? (_saves is null ? null : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_saves.Directory))!, "replays"));
        set => _replayDirectory = value;
    }
    private string? _replayDirectory;

    private string? _replayName;
    private ReplayPlayer? _replay;
    private string? _resumeAfterReplay;
    private global::Avalonia.Threading.DispatcherTimer? _replayClock;

    [ObservableProperty] private bool _isReplaying;
    [ObservableProperty] private string _replayStatus = "";
    [ObservableProperty] private bool _replayPaused;
    [ObservableProperty] private int _replaySpeed = 4;

    /// <summary>Raised to ask for a replay file to open (the view shows a file picker).</summary>
    public event Action? ReplayFileRequested;

    /// <summary>Starts recording the game just attached (not the tutorial, nor a replay being watched).</summary>
    private void StartRecording()
    {
        _replayName = null;
        if (_game.IsTutorial || _game.IsReplay || !OptionValue(DisplayOptions.RecordReplays)) return;
        _game.Recorder = new ReplayRecorder(_game);
        var p = _game.Player;
        var name = string.Concat(p.Name.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        _replayName = $"{name}-{_game.Seed:x}-{_game.GameTurn}{ReplayFile.Extension}";
    }

    /// <summary>Writes the replay so far (with each save, and at death).</summary>
    private void WriteReplay()
    {
        if (_game.Recorder is not { } recorder || _replayName is null || ReplayDirectory is not { } folder) return;
        try
        {
            recorder.ToFile(_game).Write(Path.Combine(folder, _replayName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A replay is a keepsake: failing to write one never stops play.
        }
    }

    /// <summary>The replays there are, newest first: path and a line to show.</summary>
    public IReadOnlyList<(string Path, string Title)> ListReplays()
    {
        if (ReplayDirectory is not { } folder || !Directory.Exists(folder)) return [];
        var list = new List<(string, string, DateTime)>();
        foreach (var path in Directory.GetFiles(folder, "*" + ReplayFile.Extension))
        {
            try
            {
                var r = ReplayFile.Read(path);
                var how = r.KilledBy is { } killer ? $"killed by {killer}" : "alive";
                list.Add((path, $"{r.Name} the {r.Race} {r.Class}, level {r.Level}, {r.MaxDepth * 50} ft, {how} ({r.Steps.Count} steps)", r.WrittenUtc));
            }
            catch (SaveGameException)
            {
                // Not a replay this AVABand can read: left out.
            }
        }
        return [.. list.OrderByDescending(e => e.Item3).Select(e => (e.Item1, e.Item2))];
    }

    /// <summary>Game → Watch a replay…: the replays, and a way to open one from elsewhere.</summary>
    [RelayCommand]
    public void WatchReplay()
    {
        if (IsReplaying) StopReplay();
        var menu = ListReplays().Take(20).Select(r => (r.Title, (Action)(() => PlayReplay(r.Path)))).ToList();
        menu.Add(("Open a replay file…", () => ReplayFileRequested?.Invoke()));
        ShowMenu(menu.Count > 1 ? "Watch which replay?" : "No replays yet: every game you play is recorded.", menu);
    }

    /// <summary>Plays a replay file back in the main window.</summary>
    public bool PlayReplay(string path)
    {
        ReplayPlayer player;
        try
        {
            player = new ReplayPlayer(_data, ReplayFile.Read(path));
        }
        catch (Exception ex) when (ex is SaveGameException or IOException or UnauthorizedAccessException or FormatException)
        {
            AddMessage($"Could not open the replay: {ex.Message}");
            return false;
        }
        if (!IsReplaying)
        {
            if (!_game.Player.IsDead && !_game.IsTutorial) TrySave();
            _resumeAfterReplay = _saves is not null && !_game.Player.IsDead && !_game.IsTutorial ? _saves.PathFor(_game) : null;
        }
        _replay = player;
        AttachGame(player.Game);
        IsReplaying = true;
        ReplayPaused = false;
        LastMessage = $"Watching {player.File.Name}'s game.";
        _replayClock ??= new global::Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(250),
            global::Avalonia.Threading.DispatcherPriority.Background, (_, _) => ReplayTick());
        _replayClock.Interval = TimeSpan.FromMilliseconds(1000.0 / ReplaySpeed);
        _replayClock.Start();
        UpdateReplayStatus();
        return true;
    }

    private void ReplayTick()
    {
        if (!ReplayPaused) StepReplay();
    }

    /// <summary>One step of the replay; at the end, it pauses and says whether all came out as recorded.</summary>
    public void StepReplay()
    {
        if (_replay is not { } replay) return;
        Effects.Clear();
        if (replay.Step()) Refresh();
        if (replay.Done)
        {
            ReplayPaused = true;
            LastMessage = replay.Matches == true
                ? "The replay has ended: it played out exactly as recorded. (Esc to stop.)"
                : "The replay has ended, but not as recorded (made with a different AVABand?). (Esc to stop.)";
        }
        UpdateReplayStatus();
    }

    /// <summary>Esc: back to the character in play (or a quick start, if there was none).</summary>
    public void StopReplay()
    {
        _replayClock?.Stop();
        _replay = null;
        IsReplaying = false;
        ReplayStatus = "";
        var resume = _resumeAfterReplay;
        _resumeAfterReplay = null;
        if (resume is not null && File.Exists(resume) && TryLoad(resume, out _)) return;
        StartGame((ulong)Environment.TickCount64);
    }

    /// <summary>Keys while watching: Enter pauses, → steps, ↑ ↓ the speed, Esc stops. Everything else waits.</summary>
    private bool ReplayAction(InputAction action)
    {
        if (!IsReplaying || IsPrompting || IsConfirming) return false;
        switch (action)
        {
            case InputAction.Cancel: StopReplay(); break;
            case InputAction.Confirm or InputAction.Hold: ReplayPaused = !ReplayPaused; break;
            case InputAction.MoveEast or InputAction.RunEast:
                ReplayPaused = true;
                StepReplay();
                break;
            case InputAction.MoveNorth: ReplaySpeed = Math.Min(64, ReplaySpeed * 2); break;
            case InputAction.MoveSouth: ReplaySpeed = Math.Max(1, ReplaySpeed / 2); break;
            default:
                return action is not (InputAction.MonsterKnowledge or InputAction.CharacterSheet or InputAction.MessageHistory
                    or InputAction.OverviewMap or InputAction.MonsterList or InputAction.ObjectList or InputAction.ShowCommands
                    or InputAction.Help or InputAction.ZoomIn or InputAction.ZoomOut or InputAction.ToggleTiles or InputAction.ToggleMute);
        }
        UpdateReplayStatus();
        return true;
    }

    partial void OnReplaySpeedChanged(int value)
    {
        if (_replayClock is not null) _replayClock.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, value));
    }

    private void UpdateReplayStatus()
    {
        if (_replay is not { } r)
        {
            ReplayStatus = "";
            return;
        }
        ReplayStatus = $"Replay: {r.File.Name} — step {r.Position} of {r.Count} — {ReplaySpeed} a second{(ReplayPaused ? ", paused" : "")}"
                       + "   ·   Enter pause   → step   ↑↓ speed   Esc stop";
    }
}
