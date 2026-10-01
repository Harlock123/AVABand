using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Input;
using Avalonia.Threading;

namespace Angband.Avalonia.ViewModels;

// Runs drawn a step at a time (AVABand's own; the option "Draw runs a step at a time", on by default).
// The game can take a whole run in one go, which leaves you at its end with no sight of how you got
// there; instead the run is taken step by step (RunStartCommand, then RunOnCommand — each recorded, so
// replays play it back the same), the map drawn after each and held for a moment, as Angband's own
// game loop draws a run. A key (or a controller button) stops the run where it is, as in Angband —
// except the key that started it, repeating while it's held.
public sealed partial class MainWindowViewModel
{
    /// <summary>Milliseconds each step of a run is shown for.</summary>
    public const int RunStepMs = 20;

    /// <summary>How long each step shows (tests hold a run between steps with a long one).</summary>
    public TimeSpan RunStepDelay { get; set; } = TimeSpan.FromMilliseconds(RunStepMs);

    private DispatcherTimer? _runTimer;

    /// <summary>A run is being drawn a step at a time (more steps to come).</summary>
    public bool IsRunningSteps { get; private set; }

    /// <summary>The action that started the run under way (its repeats, while held, don't stop it).</summary>
    public InputAction RunAction { get; private set; }

    /// <summary>Runs in the direction: step by step with the option on, else all at once.</summary>
    private void StartRun(Direction direction, InputAction startedBy = InputAction.None)
    {
        StopRun();
        if (!OptionValue(DisplayOptions.RunStepByStep))
        {
            Execute(new RunCommand(direction));
            return;
        }
        RunAction = startedBy;
        Execute(new RunStartCommand(direction));
        ContinueRunLater();
    }

    /// <summary>The next step after a moment, while the run goes on and nothing else needs the player.</summary>
    private void ContinueRunLater()
    {
        if (!_game.IsRunning || _game.Player.IsDead || IsPrompting || HasScene || IsInStore)
        {
            StopRun();
            return;
        }
        IsRunningSteps = true;
        _runTimer ??= new DispatcherTimer(RunStepDelay, DispatcherPriority.Input, (_, _) => RunStepNow());
        _runTimer.Interval = RunStepDelay;
        _runTimer.Start();
    }

    /// <summary>One more step of the run (the timer's, or a test's).</summary>
    public void RunStepNow()
    {
        _runTimer?.Stop();
        if (!IsRunningSteps) return;
        if (!_game.IsRunning)
        {
            StopRun();
            return;
        }
        Execute(new RunOnCommand());
        ContinueRunLater();
    }

    /// <summary>Stops a run being drawn (a key, a new game, a prompt); the game's run ends with the next command.</summary>
    public void StopRun()
    {
        _runTimer?.Stop();
        IsRunningSteps = false;
        RunAction = InputAction.None;
    }

    /// <summary>Tests: the rest of the run under way, at once.</summary>
    public void FinishRun()
    {
        for (var i = 0; i < GameSession.MaxRunSteps && IsRunningSteps; i++) RunStepNow();
    }
}
