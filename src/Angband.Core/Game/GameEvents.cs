using Angband.Core.Geometry;

namespace Angband.Core.Game;

/// <summary>Marker for everything published on the <see cref="GameEventBus"/>.</summary>
public interface IGameEvent;

public sealed record MessageEvent(string Text) : IGameEvent;
public sealed record LevelChangedEvent(int Depth, string ProfileId) : IGameEvent;
public sealed record PlayerMovedEvent(Loc From, Loc To) : IGameEvent;
public sealed record DoorOpenedEvent(Loc Loc) : IGameEvent;
public sealed record DoorClosedEvent(Loc Loc) : IGameEvent;
public sealed record LockPickFailedEvent(Loc Loc) : IGameEvent;
public sealed record StairsTakenEvent(bool Down) : IGameEvent;
public sealed record DayNightChangedEvent(bool IsDaytime) : IGameEvent;
public sealed record PlayerAttackEvent(int MonsterId, bool Hit, int Damage, Combat.CriticalGrade Critical, bool Ranged = false) : IGameEvent;
/// <summary>A monster blow; <see cref="Method"/> is the blow method id (hit, bite, claw...).</summary>
public sealed record MonsterAttackEvent(int MonsterId, bool Hit, int Damage, string Method = "hit") : IGameEvent;
public sealed record MissileFiredEvent(string Missile, Loc From, IReadOnlyList<Loc> Path) : IGameEvent;

/// <summary>What kind of thing a <see cref="ProjectionEvent"/> shows.</summary>
public enum ProjectionKind { Missile, Bolt, Beam, Ball, Breath }

/// <summary>
/// Something flew and struck (Angband's projection animation): along <see cref="Path"/> from
/// <see cref="From"/>, then bursting over <see cref="Burst"/> (balls, breaths). The UI plays these
/// after the turn; <see cref="Element"/> colours them (null for plain missiles).
/// </summary>
public sealed record ProjectionEvent(Loc From, IReadOnlyList<Loc> Path, IReadOnlyList<Loc> Burst, string? Element,
    ProjectionKind Kind) : IGameEvent;

/// <summary>A monster took damage (for Angband's show_damage numbers).</summary>
public sealed record MonsterDamagedEvent(int MonsterId, Loc Loc, int Damage) : IGameEvent;
public sealed record MonsterKilledEvent(string RaceId, Loc Loc, int Experience, bool IsUnique = false) : IGameEvent;
public sealed record PlayerHurtEvent(int Damage, int Hp, int MaxHp) : IGameEvent;
public sealed record PlayerDiedEvent(string KilledBy, int Depth) : IGameEvent;

/// <summary>Word of Recall took the player up to the town, or down into the dungeon.</summary>
public sealed record RecalledEvent(bool Up) : IGameEvent;

/// <summary>A unique monster seen for the very first time (its lore had no sighting).</summary>
public sealed record UniqueFirstSeenEvent(Monsters.Monster Monster) : IGameEvent;
public sealed record StatusChangedEvent(string EffectId, int Value) : IGameEvent;
/// <summary>Traps noticed as they came into sight (your search skill was up to them).</summary>
public sealed record TrapFoundEvent(int Count) : IGameEvent;

/// <summary>A trap disarmed.</summary>
public sealed record TrapDisarmedEvent(Geometry.Loc At, string TrapId) : IGameEvent;

public sealed record ItemPickedUpEvent(string KindId, int Amount, bool Gold = false) : IGameEvent;
public sealed record ItemWieldedEvent(string KindId) : IGameEvent;
public sealed record ItemUsedEvent(string KindId, string Verb) : IGameEvent;
public sealed record RuneLearnedEvent(string Rune) : IGameEvent;
public sealed record LightOutEvent : IGameEvent;
public sealed record ItemDroppedEvent(string KindId) : IGameEvent;
/// <summary>The player walked into (or asked to enter) a store; the UI shows it.</summary>
public sealed record ShopEnteredEvent(string StoreId, bool IsHome) : IGameEvent;
public sealed record ShopLeftEvent(string StoreId) : IGameEvent;
public sealed record ItemBoughtEvent(string StoreId, string KindId, long Price) : IGameEvent;
public sealed record ItemSoldEvent(string StoreId, string KindId, long Price) : IGameEvent;
public sealed record LockPickedEvent(Loc Loc) : IGameEvent;
public sealed record PlayerTeleportedEvent(Loc From, Loc To) : IGameEvent;
/// <summary>Every 10 game turns (one player turn at normal speed): drives ambience.</summary>
public sealed record WorldTickEvent(long GameTurn, int Depth, bool IsDaytime) : IGameEvent;
/// <summary>Digging at a square (<paramref name="Done"/> when the tunnel is through).</summary>
public sealed record DigEvent(Loc Loc, bool Done) : IGameEvent;
/// <summary>A mimic or lurker has been found out.</summary>
public sealed record MonsterRevealedEvent(int MonsterId, string RaceId) : IGameEvent;
public sealed record TrapSprungEvent(Loc Loc, string TrapId) : IGameEvent;
public sealed record QuestCompletedEvent(string QuestId) : IGameEvent;
public sealed record GameWonEvent : IGameEvent;
/// <summary>The player took a shape (or returned to their own, when null).</summary>
public sealed record PlayerShapeChangedEvent(string? ShapeId) : IGameEvent;
public sealed record HungerChangedEvent(HungerLevel Level, bool Worse) : IGameEvent;
public sealed record LevelUpEvent(int Level) : IGameEvent;
public sealed record SpellLearnedEvent(string SpellId) : IGameEvent;
public sealed record SpellCastEvent(string SpellId, string Realm = "arcane") : IGameEvent;
public sealed record MonsterBredEvent(string RaceId, Loc Loc, bool Seen = false) : IGameEvent;
public sealed record MonsterSpellEvent(int MonsterId, string SpellId, bool Seen, string? Sound = null) : IGameEvent;

/// <summary>
/// Synchronous publish/subscribe hub. Presentation concerns (sound, animations, message log)
/// subscribe here so the engine never references them.
/// </summary>
public sealed class GameEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];
    private readonly List<Action<IGameEvent>> _all = [];

    public IDisposable Subscribe<T>(Action<T> handler) where T : IGameEvent
    {
        if (!_handlers.TryGetValue(typeof(T), out var list)) _handlers[typeof(T)] = list = [];
        list.Add(handler);
        return new Subscription(() => list.Remove(handler));
    }

    /// <summary>Receives every event (loggers, replays).</summary>
    public IDisposable SubscribeAll(Action<IGameEvent> handler)
    {
        _all.Add(handler);
        return new Subscription(() => _all.Remove(handler));
    }

    public void Publish<T>(T evt) where T : IGameEvent
    {
        if (_handlers.TryGetValue(typeof(T), out var list))
            foreach (var handler in list.ToArray()) ((Action<T>)handler)(evt);
        foreach (var handler in _all.ToArray()) handler(evt);
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
