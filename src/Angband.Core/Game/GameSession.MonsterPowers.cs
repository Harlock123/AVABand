using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Items;
using Angband.Core.Magic;
using Angband.Core.Monsters;
using Angband.Core.World;

namespace Angband.Core.Game;

// Monster abilities beyond plain damage: thieves, drains, disenchantment, invisibility and walls
// (Angband mon-blow-effects.c, monster movement flags), and the player's stat and life drains.
public sealed partial class GameSession
{
    /// <summary>Angband adj_dex_safe: protection from thieves by Dexterity.</summary>
    private static readonly int[] AdjDexSafe =
        [0, 1, 2, 3, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 15, 15, 20, 25, 30, 35, 40, 45, 50, 60, 70, 80, 90, 100, 100, 100, 100, 100, 100, 100, 100];

    /// <summary>Whether the player sees invisible things (gear, race or a timed effect).</summary>
    public bool SeesInvisible => Player.Resists.GetValueOrDefault("see_invis") > 0 || Player.Timed.Has("see_invisible");

    /// <summary>
    /// Whether the player can see a monster: on a seen square (or by infravision for warm-blooded ones),
    /// and not invisible unless the player sees invisible.
    /// </summary>
    public bool MonsterVisible(Monster monster)
    {
        if (monster.Camouflaged) return false;
        if (Senses(monster)) return true;
        if (monster.Race.Has(MonsterFlags.Invisible) && !SeesInvisible) return false;
        var sq = Level[monster.Position];
        return sq.Has(SquareFlags.Seen)
               || (Player.TotalInfravision > 0 && !Player.IsBlind && sq.Has(SquareFlags.View)
                   && !monster.Race.Has("COLD_BLOOD")
                   && monster.Position.DistanceTo(Player.Position) <= Player.TotalInfravision);
    }

    /// <summary>
    /// Telepathy (Angband ESP): monsters with minds nearby are sensed even through walls; mindless
    /// ones never are, and weird minds only flicker into view.
    /// </summary>
    private bool Senses(Monster monster)
    {
        if (!Player.HasGearFlag(ItemFlags.Telepathy) && !Player.Timed.Has("telepathy")) return false;
        if (monster.Race.Has("EMPTY_MIND")) return false;
        if (monster.Race.Has("WEIRD_MIND") && (monster.Id + (int)(GameTurn / 10)) % 10 != 0) return false;
        return monster.Position.DistanceTo(Player.Position) <= Data.Constants.MaxSight * 2;
    }

    // --- Stats -------------------------------------------------------------------------------

    /// <summary>Drains a stat by one point unless sustained (Angband player_stat_dec).</summary>
    public bool DrainStat(string stat)
    {
        if (Player.Resists.GetValueOrDefault("sust_" + stat) > 0)
        {
            Publish(new MessageEvent($"You feel very {StatAdjective(stat, good: false)} for a moment, but the feeling passes."));
            return false;
        }
        var current = Player.Stats.GetValueOrDefault(stat, 15);
        if (current <= 3) return false;
        Player.StatDrain[stat] = Player.StatDrain.GetValueOrDefault(stat) + 1;
        Publish(new MessageEvent($"You feel very {StatAdjective(stat, good: false)}."));
        RecalculateAfterStatChange();
        return true;
    }

    /// <summary>Restores a drained stat (Angband player_stat_res).</summary>
    public bool RestoreStat(string stat)
    {
        if (Player.StatDrain.GetValueOrDefault(stat) <= 0) return false;
        Player.StatDrain[stat] = 0;
        Publish(new MessageEvent($"You feel less {StatAdjective(stat, good: false)}."));
        RecalculateAfterStatChange();
        return true;
    }

    /// <summary>
    /// A permanent stat gain (Angband player_stat_inc, as from potions of Strength): one point, up to
    /// 18/100, also restoring any drain.
    /// </summary>
    public bool GainStat(string stat)
    {
        Player.StatDrain[stat] = 0;
        var natural = Player.NaturalStats.GetValueOrDefault(stat, 15);
        if (natural < 28) Player.NaturalStats[stat] = natural + 1;
        Publish(new MessageEvent($"You feel very {StatAdjective(stat, good: true)}!"));
        RecalculateAfterStatChange();
        return true;
    }

    private void RecalculateAfterStatChange()
    {
        RecalculateBonuses();
        ApplySkills();
        RecalculateMana();
    }

    private static string StatAdjective(string stat, bool good) => (stat, good) switch
    {
        ("str", true) => "strong", ("str", false) => "weak",
        ("int", true) => "smart", ("int", false) => "stupid",
        ("wis", true) => "wise", ("wis", false) => "naive",
        ("dex", true) => "dextrous", ("dex", false) => "clumsy",
        ("con", true) => "healthy", ("con", false) => "sickly",
        _ => good ? "good" : "bad",
    };

    /// <summary>Carried loot falls to the floor when a thief dies.</summary>
    private void DropCarried(Monster monster)
    {
        foreach (var item in monster.Carried) DropNear(item, monster.Position);
        monster.Carried.Clear();
    }

}
