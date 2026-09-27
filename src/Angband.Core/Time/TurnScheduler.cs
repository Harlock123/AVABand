namespace Angband.Core.Time;

/// <summary>Anything that takes turns: the player and monsters.</summary>
public interface IActor
{
    /// <summary>Stable id used to break ties deterministically (lower acts first).</summary>
    int ActorId { get; }
    /// <summary>Speed relative to normal (+10 = twice as fast).</summary>
    int Speed { get; }
    int Energy { get; set; }
    /// <summary>False once dead or removed; inactive actors are skipped and pruned.</summary>
    bool IsActive { get; }
}

/// <summary>Game-specific behaviour plugged into the scheduler.</summary>
public interface ITurnHandler
{
    /// <summary>True for actors controlled by input (the player): the scheduler stops and waits.</summary>
    bool NeedsInput(IActor actor);

    /// <summary>Lets an AI-controlled actor act. Returns the energy the action cost.</summary>
    int TakeTurn(IActor actor);

    /// <summary>World upkeep (regeneration, timers, hunger) every 10 game turns.</summary>
    void OnWorldTick(long gameTurn);
}

/// <summary>
/// Angband's variable-speed energy system. Each game turn every actor gains energy from
/// <see cref="EnergyTable"/>; actors holding at least <see cref="EnergyTable.MoveEnergy"/> act, the most
/// energetic first (ties by <see cref="IActor.ActorId"/>), at most once per game turn. When an
/// input-driven actor is due, <see cref="Advance"/> returns it; the caller performs the chosen
/// command, deducts its energy, and calls <see cref="Advance"/> again to resume mid-turn.
/// </summary>
public sealed class TurnScheduler
{
    private readonly List<IActor> _actors = [];
    private readonly List<IActor> _ready = [];
    private int _readyIndex;

    public long GameTurn { get; private set; }

    /// <summary>Actors still due to act in the current game turn after the one awaiting input.</summary>
    public int PendingInTurn => _ready.Count - _readyIndex;

    public IReadOnlyList<IActor> Actors => _actors;

    /// <summary>Restores the turn counter (loading a save).</summary>
    public void SetGameTurn(long turn) => GameTurn = turn;

    /// <summary>Actors still due to act this game turn, in order (for saving mid-turn).</summary>
    public IEnumerable<IActor> PendingActors => _ready.Skip(_readyIndex);

    /// <summary>Restores the actors still due this game turn (loading a save).</summary>
    public void SetPending(IEnumerable<IActor> pending)
    {
        _ready.Clear();
        _ready.AddRange(pending);
        _readyIndex = 0;
    }

    public void Add(IActor actor)
    {
        if (!_actors.Contains(actor)) _actors.Add(actor);
    }

    public void Remove(IActor actor) => _actors.Remove(actor);

    /// <summary>Removes every actor, e.g. when changing level.</summary>
    public void Clear()
    {
        _actors.Clear();
        _ready.Clear();
        _readyIndex = 0;
    }

    /// <summary>
    /// Runs game turns until an input-driven actor is ready, returning it. Returns null when
    /// <paramref name="maxGameTurns"/> elapse first (useful for simulations and tests).
    /// </summary>
    public IActor? Advance(ITurnHandler handler, long maxGameTurns = long.MaxValue)
    {
        long elapsed = 0;
        while (true)
        {
            // Finish the current game turn.
            while (_readyIndex < _ready.Count)
            {
                var actor = _ready[_readyIndex++];
                if (!actor.IsActive || actor.Energy < EnergyTable.MoveEnergy) continue;

                if (handler.NeedsInput(actor)) return actor;
                actor.Energy -= Math.Max(0, handler.TakeTurn(actor));
            }

            if (elapsed++ >= maxGameTurns) return null;
            BeginGameTurn(handler);
        }
    }

    private void BeginGameTurn(ITurnHandler handler)
    {
        GameTurn++;
        if (GameTurn % EnergyTable.GameTurnsPerNormalTurn == 0) handler.OnWorldTick(GameTurn);

        _actors.RemoveAll(a => !a.IsActive);
        _ready.Clear();
        _readyIndex = 0;
        foreach (var actor in _actors)
        {
            actor.Energy += EnergyTable.EnergyPerTurn(actor.Speed);
            if (actor.Energy >= EnergyTable.MoveEnergy) _ready.Add(actor);
        }
        _ready.Sort(static (a, b) =>
        {
            var byEnergy = b.Energy.CompareTo(a.Energy);
            return byEnergy != 0 ? byEnergy : a.ActorId.CompareTo(b.ActorId);
        });
    }
}
