namespace Angband.Core.Game;

/// <summary>
/// One hurt the player took (AVABand's own, for the death recap): what did it, how much, and the hit
/// points left after. Ticks of the same hurt in a row (poison, bleeding, hunger) are counted into one.
/// </summary>
/// <param name="Killer">What did it, as a death would name it ("a Cave orc", "poison").</param>
/// <param name="Damage">The damage, after damage reduction (all of the ticks, when counted together).</param>
/// <param name="HpAfter">Hit points left after it (below zero for the last).</param>
/// <param name="MaxHp">Maximum hit points at the time.</param>
/// <param name="RaceId">The monster race that did it, if a monster did.</param>
/// <param name="Element">The element it came as (fire, poison...), if any.</param>
/// <param name="Breath">Whether it was a breath.</param>
/// <param name="Count">How many hurts this is (more than one for ticks counted together).</param>
/// <param name="Turn">When (in normal-speed player turns, <see cref="GameSession.NormalTurns"/>); the last tick's, when counted together.</param>
public sealed record HurtRecord(string Killer, int Damage, int HpAfter, int MaxHp, string? RaceId = null, string? Element = null,
    bool Breath = false, int Count = 1, long Turn = 0);

// AVABand's own: the last few hurts taken, kept in memory for the death recap. Nothing here is saved
// (a loaded game starts with none) and nothing rolls dice, so how the game plays is untouched.
public sealed partial class GameSession
{
    /// <summary>How many hurts <see cref="RecentHurts"/> keeps.</summary>
    public const int RecentHurtCapacity = 8;

    private readonly List<HurtRecord> _recentHurts = [];

    /// <summary>The element of the elemental hit now being taken (set around its <see cref="TakeHit"/>).</summary>
    private string? _hurtElement;

    /// <summary>Whether the hit now being taken is a breath.</summary>
    private bool _hurtByBreath;

    /// <summary>The last few hurts taken, oldest first (at most <see cref="RecentHurtCapacity"/>).</summary>
    public IReadOnlyList<HurtRecord> RecentHurts => [.. _recentHurts];

    private void RecordHurt(int damage, string killer, bool tick)
    {
        // A tick of the same hurt again (poison, bleeding or hunger, which aren't noted) adds to the last one.
        if (tick && _attacker is null && _recentHurts.Count > 0 && _recentHurts[^1] is { RaceId: null } last
            && last.Killer == killer && last.Element == _hurtElement && !_hurtByBreath && !last.Breath)
        {
            _recentHurts[^1] = last with { Damage = last.Damage + damage, HpAfter = Player.Hp, MaxHp = Player.MaxHp, Count = last.Count + 1,
                Turn = NormalTurns };
            return;
        }
        _recentHurts.Add(new HurtRecord(killer, damage, Player.Hp, Player.MaxHp, _attacker, _hurtElement, _hurtByBreath,
            Turn: NormalTurns));
        if (_recentHurts.Count > RecentHurtCapacity) _recentHurts.RemoveAt(0);
    }
}
