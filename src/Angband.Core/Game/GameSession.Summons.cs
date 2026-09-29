using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Time;

namespace Angband.Core.Game;

// Summoning (Angband 4.2.5 effect_handler_SUMMON, summon_specific, call_monster; the kinds of
// summons are summons.json, from summon.txt).
public sealed partial class GameSession
{
    /// <summary>
    /// A monster summons (Angband effect_handler_SUMMON, monster branch): up to the spell's most
    /// attempts, until the levels of what came (squared, added up) reach depth × the caster's level;
    /// its kind's fallback is tried when nothing came. Summons appear up to four squares from the caster.
    /// </summary>
    private void SummonMonsters(Monster caster, MonsterSpellDef spell)
    {
        if (InArena || spell.Summon is not { } id || Data.Summon(id) is not { } type) return;
        var max = Math.Max(1, spell.Count.Roll(Rng));
        var kin = caster.Race.Base;
        var rlev = caster.Race.Depth;
        var budget = Player.Depth * rlev;
        int val = 0, count = 0;

        void Attempts(SummonDef kind)
        {
            for (var attempts = 0; val < budget && attempts < max; attempts++)
            {
                var got = SummonSpecific(caster.Position, rlev, kind, kin, delay: false, call: false);
                val += got * got;
                if (val > 0) count++; // Angband counts on once anything came
            }
        }

        Attempts(type);
        if (count == 0 && type.Fallback is { } fb && Data.Summon(fb) is { } fallback) Attempts(fallback);
        DisguiseMonsters();
        UpdateView();
        if (count == 0) Publish(new MessageEvent("But nothing comes."));
    }

    /// <summary>
    /// Monsters appear around the player — a trap, a curse, the scroll or staff of summoning (Angband
    /// effect_handler_SUMMON, other branch): <paramref name="count"/> tries at the depth (+ boost); the
    /// newcomers wait for the player to act, and one time in four a monster already on the level, out
    /// of sight, is called over instead. Whether anything came.
    /// </summary>
    internal bool SummonNearPlayer(int count, string? kind, int levelBoost = 0)
    {
        if (InArena) return false;
        var type = Data.Summon(string.IsNullOrEmpty(kind) || kind == "none" ? "ANY" : kind);
        if (type is null) return false;
        var total = 0;
        for (var i = 0; i < count; i++)
            total += SummonSpecific(Player.Position, Player.Depth + levelBoost, type, null, delay: true, call: Rng.OneIn(4));
        DisguiseMonsters();
        UpdateView();
        return total > 0;
    }

    /// <summary>
    /// One summons near <paramref name="grid"/> (Angband summon_specific): an empty square in view of
    /// it up to four away; a monster of the kind at about the average of the depth and
    /// <paramref name="lev"/>, plus five. The monster's level, or 0 when none came.
    /// </summary>
    private int SummonSpecific(Loc grid, int lev, SummonDef type, string? kinBase, bool delay, bool call)
    {
        Loc? near = null;
        for (var d = 1; d < 5 && near is null; d++)
        {
            var feasible = new List<Loc>();
            for (var y = grid.Y - d; y <= grid.Y + d; y++)
            for (var x = grid.X - d; x <= grid.X + d; x++)
            {
                var g = new Loc(x, y);
                if (!Level.InBoundsFully(g)) continue;
                if (d > 1 && grid.DistanceTo(g) > d) continue;
                if (!ProjectionPath.Projectable(Level, grid, g, 255)) continue;
                if (!AllowsSummon(g)) continue;
                feasible.Add(g);
            }
            if (feasible.Count > 0) near = feasible[Rng.RandInt0(feasible.Count)];
        }
        if (near is not { } spot) return 0;

        if (call && type.Id is not ("UNIQUE" or "WRAITH")) return CallMonster(spot, type, kinBase);

        var unavailable = new HashSet<string>(KilledUniques);
        foreach (var m in Level.Monsters.All.Where(m => m.Race.IsUnique)) unavailable.Add(m.Race.Id);
        var race = _spawner.PickRace(Rng, Math.Max(1, (Player.Depth + lev) / 2 + 5), unavailable, r => SummonOkay(type, r, kinBase));
        if (race is null) return 0;

        var mon = _spawner.Place(Level, Rng, race, spot, asleep: false);
        Scheduler.Add(mon);
        if (delay)
        {
            // Let the player act before the newcomer, holding a faster one for the turns it would gain.
            var p = EnergyTable.EnergyPerTurn(Player.Speed);
            var me = EnergyTable.EnergyPerTurn(mon.Speed);
            var turns = (EnergyTable.MoveEnergy * (me - p) + me * p - 1) / (me * p);
            mon.Energy = 0;
            if (turns > 0) mon.Held = Math.Min(turns, 32767);
        }
        return race.Depth;
    }

    /// <summary>Angband square_allows_summon: empty floor, no trap, web, rune or decoy, nothing lying there.</summary>
    private bool AllowsSummon(Loc g) =>
        Level.IsEmptyFloor(g) && g != Player.Position && !Level.Objects.Any(g) && Level.Decoy != g;

    /// <summary>
    /// Whether a monster can answer a summons of this kind (Angband summon_specific_okay): uniques
    /// only where allowed, one of the kind's bases, its flag; kin share the caster's base and are
    /// never unique.
    /// </summary>
    internal static bool SummonOkay(SummonDef type, MonsterRaceDef race, string? kinBase)
    {
        var unique = race.IsUnique;
        if (!type.Uniques && unique) return false;
        if (type.Bases.Count > 0 && !type.Bases.Contains(race.Base)) return false;
        if (type.RaceFlag is { } flag && !race.Has(flag)) return false;
        if (type.Id == "KIN") return !unique && race.Base == kinBase;
        return true;
    }

    /// <summary>
    /// A monster of the kind already on the level, out of sight of the spot, is called over to it,
    /// awake (Angband call_monster). Its level, or 0 when there was none.
    /// </summary>
    private int CallMonster(Loc spot, SummonDef type, string? kinBase)
    {
        var callable = Level.Monsters.All
            .Where(m => SummonOkay(type, m.Race, kinBase) && !ProjectionPath.Projectable(Level, spot, m.Position, 255))
            .ToList();
        if (callable.Count == 0) return 0;
        var mon = callable[Rng.RandInt0(Math.Max(1, callable.Count - 1))]; // as Angband: randint0(count - 1)
        Level.Monsters.Move(mon, spot);
        mon.Sleep = 0;
        mon.Energy = 0;
        return mon.Race.Depth;
    }
}
