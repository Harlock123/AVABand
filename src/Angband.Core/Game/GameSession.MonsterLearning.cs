using Angband.Core.Definitions;
using Angband.Core.Monsters;

namespace Angband.Core.Game;

// Monster learning (Angband 4.2 birth_ai_learn): a monster that sees an element or a status attack
// tested against the player remembers how well the player resisted it (mon-util.c
// update_smart_learn), and later leaves out the spells it knows won't work (mon-attack.c
// remove_bad_spells, mon-spell.c unset_spells). Stupid monsters never learn; smart ones always do.
public sealed partial class GameSession
{
    /// <summary>The monster whose spell or blows are hitting the player just now, if any.</summary>
    private Monster? _actingMonster;

    /// <summary>
    /// Angband update_smart_learn: the monster notes the player's current level of
    /// <paramref name="key"/> (an element or a protection like <c>free_act</c>), or its absence.
    /// </summary>
    private void LearnAboutPlayer(Monster? monster, string? key)
    {
        if (monster is null || key is null || !Options[OptionIds.AiLearn]) return;
        if (monster.Race.Has(MonsterFlags.Stupid)) return;
        if (!monster.Race.Has(MonsterFlags.Smart) && Rng.OneIn(2)) return;
        if (Rng.OneIn(100)) return; // fail very rarely
        var level = Player.Resists.GetValueOrDefault(key);
        if (key == "hold_life" && Player.HasGearFlag(ItemFlags.HoldLife)) level = Math.Max(level, 1);
        if (level == 0) monster.KnownPlayer.Remove(key);
        else monster.KnownPlayer[key] = level;
    }

    /// <summary>
    /// Angband remove_bad_spells' learning part: now and then forget; otherwise drop the element
    /// attacks the player is known to resist (a quarter per level of resistance, half for smart
    /// monsters) and the status spells a known protection stops (always for smart monsters, two
    /// times in three for the others).
    /// </summary>
    internal List<MonsterSpellDef> WithoutKnownFailures(Monster monster, List<MonsterSpellDef> spells)
    {
        if (!Options[OptionIds.AiLearn] || monster.Race.Has(MonsterFlags.Stupid)) return spells;
        if (Rng.OneIn(20)) monster.KnownPlayer.Clear(); // occasionally forget
        if (monster.KnownPlayer.Count == 0) return spells;

        var smart = monster.Race.Has(MonsterFlags.Smart);
        var known = monster.KnownPlayer;
        return spells.Where(s =>
        {
            if (s.Kind is MonsterSpellKind.Bolt or MonsterSpellKind.Ball or MonsterSpellKind.Breath && s.Element is { } element)
                return Rng.RandInt0(100) >= known.GetValueOrDefault(element) * (smart ? 50 : 25);
            if (s.Timed is not null && s.PreventedBy is { } protection && (smart || !Rng.OneIn(3)))
                return known.GetValueOrDefault(protection) <= 0;
            return true;
        }).ToList();
    }
}
