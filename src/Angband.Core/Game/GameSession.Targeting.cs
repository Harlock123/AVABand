using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.World;
using Angband.Core.Geometry;
using Angband.Core.Monsters;

namespace Angband.Core.Game;

// Targeting (Angband target.c): the player can pick a monster or a spot to aim at. Firing, throwing,
// aimed spells and devices use it while it stays valid; otherwise they aim at the nearest monster.
public sealed partial class GameSession
{
    /// <summary>The targeted monster, if a monster is targeted.</summary>
    public Monster? TargetMonster { get; private set; }

    /// <summary>The targeted square, if a location is targeted.</summary>
    public Loc? TargetLocation { get; private set; }

    public void SetTarget(Monster monster)
    {
        TargetMonster = monster;
        TargetLocation = null;
    }

    public void SetTarget(Loc location)
    {
        TargetMonster = null;
        TargetLocation = location;
    }

    public void ClearTarget()
    {
        TargetMonster = null;
        TargetLocation = null;
    }

    /// <summary>
    /// Angband target_okay: a monster target must still be alive, on this level, visible and in a
    /// clear line of fire; a location target just has to be in range and in the line of fire.
    /// </summary>
    public bool TargetOkay(int range = MaxRange)
    {
        if (TargetMonster is { } m)
            return m.IsActive && Level.Monsters.All.Contains(m) && (m.IsVisible || m.IsDetected)
                   && ProjectionPath.Projectable(Level, Player.Position, m.Position, range);
        if (TargetLocation is { } p)
            return Level.InBounds(p) && ProjectionPath.Projectable(Level, Player.Position, p, range);
        return false;
    }

    /// <summary>Where the current target is, if it is valid.</summary>
    public Loc? TargetPosition(int range = MaxRange) =>
        TargetOkay(range) ? TargetMonster?.Position ?? TargetLocation : null;

    /// <summary>
    /// What an aimed action should aim at: the target if valid (at any range — a short-ranged shot
    /// simply falls short, as in Angband), else the nearest monster within <paramref name="range"/>.
    /// </summary>
    /// <summary>
    /// Where aimed commands go: the current target when "use old target" is on, otherwise (or with no
    /// valid target) the nearest monster in a clear line.
    /// </summary>
    public Loc? AimPoint(int range = MaxRange) =>
        (Options[OptionIds.UseOldTarget] ? TargetPosition() : null) ?? NearestTarget(range)?.Position;

    /// <summary>The direction from the player toward the target (for bolts of stone to mud and the like).</summary>
    public Direction? DirectionToTarget()
    {
        if (TargetPosition() is not { } t || t == Player.Position) return null;
        return DirectionExtensions.FromOffset(t.X - Player.Position.X, t.Y - Player.Position.Y);
    }

    /// <summary>Monsters worth targeting, nearest first (Angband's "interesting" monsters).</summary>
    /// <summary>
    /// Angband target_set_closest: targets the nearest monster that can be shot at ("No Available
    /// Target." if there is none), and names it unless <paramref name="quiet"/>.
    /// </summary>
    public bool TargetClosest(bool quiet = false)
    {
        ClearTarget();
        if (TargetableMonsters().FirstOrDefault() is not { } nearest)
        {
            Publish(new MessageEvent("No Available Target."));
            return false;
        }
        SetTarget(nearest);
        if (!quiet) Publish(new MessageEvent($"{Capitalize(MonsterName(nearest))} is targeted."));
        return true;
    }

    /// <summary>
    /// Angband look mode's stops (target.c target_accept / target_get_monsters): within range, the
    /// monsters in view, the traps you know of, the objects you remember and the features worth a look
    /// (doors, stairs, shops, rubble, treasure veins), nearest first. Your own square counts only when
    /// something is there. While hallucinating there is nothing to look at.
    /// </summary>
    public IReadOnlyList<Loc> LookSpots()
    {
        if (IsHallucinating) return [];
        var spots = new List<Loc>();
        var me = Player.Position;
        for (var y = me.Y - MaxRange; y <= me.Y + MaxRange; y++)
        for (var x = me.X - MaxRange; x <= me.X + MaxRange; x++)
        {
            var p = new Loc(x, y);
            if (!Level.InBoundsFully(p) || p.DistanceTo(me) > MaxRange) continue;
            if (p != me && Level.Monsters.At(p) is { IsVisible: true, Camouflaged: false })
                spots.Add(p);
            else if (Level[p].Trap != 0 && Level[p].Has(SquareFlags.TrapVisible))
                spots.Add(p);
            else if (Known.RememberedObject(p) is { } seen && !IsIgnored(seen))
                spots.Add(p);
            else if (Known.IsKnown(p) && Data.Terrain[Known.Feature(p)].Has(TerrainFlags.Interesting))
                spots.Add(p);
        }
        return [.. spots.OrderBy(p => p.DistanceTo(me)).ThenBy(p => p.Y).ThenBy(p => p.X)];
    }

    /// <summary>The trap the player knows of at a square, if any.</summary>
    public TrapDef? VisibleTrapAt(Loc p) =>
        Level.InBounds(p) && Level[p].Trap != 0 && Level[p].Has(SquareFlags.TrapVisible) ? Data.TrapByIndex(Level[p].Trap) : null;

    public IReadOnlyList<Monster> TargetableMonsters() =>
        Level.Monsters.All
            .Where(m => m.IsVisible && ProjectionPath.Projectable(Level, Player.Position, m.Position, MaxRange))
            .OrderBy(m => Player.Position.DistanceTo(m.Position)).ThenBy(m => m.Id)
            .ToList();
}
