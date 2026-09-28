using Angband.Core.Definitions;
using Angband.Core.Monsters;

namespace Angband.Core.Game;

/// <summary>
/// Angband 4.2's combat ranges (mon-move.c get_move_find_range): each turn a monster works out the
/// least distance it will keep and the distance it likes best. Two things follow, as in 4.2: a
/// monster that judges the player too strong keeps right away (morale), and a monster at exactly
/// its preferred distance casts twice as often (mon-attack.c).
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Angband flee-range: monsters run up to this far beyond sight.</summary>
    public const int FleeRangeBeyondSight = 5;

    /// <summary>Angband turn-range: a monster this close won't keep its distance for other reasons.</summary>
    public const int TurnRange = 5;

    /// <summary>The distance a fleeing monster wants (sight plus flee range).</summary>
    public int FleeRange => Data.Constants.MaxSight + FleeRangeBeyondSight;

    /// <summary>Angband get_move_find_range: the monster's minimum and preferred distances from the player.</summary>
    public (int Min, int Best) CombatRange(Monster monster)
    {
        var race = monster.Race;
        var flee = FleeRange;
        var distance = monster.Position.DistanceTo(Player.Position);
        int min;

        if (monster.IsAfraid || race.Has("FRIGHTENED")) min = flee; // the afraid all run
        else if (LeaderOf(monster) is not null) min = 1;      // bodyguards don't flee
        else
        {
            min = 1;
            if (Player.Timed.Has("taunt")) return (1, 1); // taunted monsters just want to get in your face

            // Morale: the player's level against the monster's (plus 25, and 8 more for some of them).
            var pLev = Player.Level;
            var mLev = race.Depth + (monster.Id & 0x08) + 25;
            if (mLev + 3 < pLev) min = flee;
            else if (mLev - 5 < pLev)
            {
                // Close in level: whoever is in better health relative to their size wins.
                long pMhp = Math.Max(1, Player.MaxHp), pChp = Math.Max(0, Player.Hp);
                long mMhp = Math.Max(1, monster.MaxHp), mChp = Math.Max(0, monster.Hp);
                var pVal = pLev * pMhp + (pChp << 2);
                var mVal = mLev * mMhp + (mChp << 2);
                if (pVal * mMhp > mVal * pMhp) min = flee; // strong players scare strong monsters
            }
        }

        if (min < flee)
        {
            if (race.Has(MonsterFlags.NeverMove)) min += 3;  // those that can't move never like it close
            if (race.Has(MonsterFlags.NeverBlow)) min += 3;  // nor do casters that can't strike
        }
        if (min >= flee) min = flee;
        else if (distance < TurnRange) min = 1;              // nearby monsters won't run away

        var best = min;
        if (LovesArchery(race)) best += 3;                   // archers are happy at a good distance
        if (Percent(race.InnateFrequency) > 24)
        {
            if (Breathes(race) && monster.Hp > monster.MaxHp / 2) best = Math.Max(1, best); // breathers: point blank
        }
        else if (Percent(race.SpellFrequency) > 24) best += 3; // other casters sit back and cast
        return (min, best);
    }

    /// <summary>A 1-in-N frequency as Angband stores it: 100/N percent (0 for never).</summary>
    private static int Percent(int oneIn) => oneIn <= 0 ? 0 : 100 / oneIn;

    /// <summary>Angband monster_loves_archery: it has an archery attack and an innate frequency under 4%.</summary>
    private static bool LovesArchery(MonsterRaceDef race) =>
        race.Spells.Any(s => s is "SHOT" or "ARROW" or "BOLT") && Percent(race.InnateFrequency) < 4;

    private bool Breathes(MonsterRaceDef race) =>
        race.Spells.Any(s => Data.MonsterSpell(s)?.Kind == MonsterSpellKind.Breath);

    /// <summary>
    /// The leader a bodyguard guards, while it lives; when it has died the bodyguard becomes an
    /// ordinary member of its group (Angband monster_group_remove_leader).
    /// </summary>
    public Monster? LeaderOf(Monster monster)
    {
        if (monster.BodyguardOf is not { } id) return null;
        var leader = Level.Monsters.All.FirstOrDefault(m => m.Id == id && m.IsActive);
        if (leader is null) monster.BodyguardOf = null;
        return leader;
    }

    /// <summary>Whether the monster's morale has broken: it keeps right away from the player.</summary>
    private bool KeepsAway(Monster monster) => !monster.IsAfraid && CombatRange(monster).Min >= FleeRange;
}
