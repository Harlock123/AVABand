using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Time;
using Angband.Core.World;

namespace Angband.Core.Game;

/// <summary>Search the squares around you carefully (AVABand's own; 'S'). Takes a turn; takes a count.</summary>
public sealed record SearchCommand : GameCommand;

// AVABand's own: a careful search. Angband 4.2 has no search command (secret doors beside you are always
// found, traps seen when your search skill reaches them), so looking again the ordinary way finds nothing
// more. A careful search does better: a turn spent searching the eight squares around you, your search
// skill raised by 10 — and by 10 more for each turn you go on searching the same spot, up to 50 — and
// done by touch as well as by eye, so it finds a secret door even in the dark. It says when there's
// nothing to find. A count (0, then a number) searches that many turns, stopping at a find.
public sealed partial class GameSession
{
    /// <summary>The search skill a careful search adds each turn in the same spot, and the most it adds.</summary>
    public const int CarefulSearchBonus = 10, CarefulSearchBonusMax = 50;

    private Loc _searchSpot;
    private Level? _searchLevel;
    private int _searchTurns;

    /// <summary>The bonus the next careful search here would have (for the status of a long search).</summary>
    public int CarefulSearchBonusNow => Math.Min(CarefulSearchBonusMax, CarefulSearchBonus * (SearchingHereAgain ? _searchTurns + 1 : 1));

    private bool SearchingHereAgain => _searchTurns > 0 && ReferenceEquals(_searchLevel, Level) && _searchSpot == Player.Position;

    /// <summary>Anything but a search starts the next search afresh.</summary>
    private void ForgetSearching(GameCommand command)
    {
        if (command is not (SearchCommand or CountedCommand { Command: SearchCommand })) _searchTurns = 0;
    }

    private int CarefulSearch()
    {
        if (Player.Timed.Has(Effects.TimedIds.Confused) || Player.Timed.Has(Effects.TimedIds.Image))
        {
            Publish(new MessageEvent("You are too confused to search carefully."));
            return 0;
        }
        _searchTurns = SearchingHereAgain ? _searchTurns + 1 : 1;
        _searchSpot = Player.Position;
        _searchLevel = Level;
        var skill = SearchSkill + Math.Min(CarefulSearchBonusMax, CarefulSearchBonus * _searchTurns);

        var doors = 0;
        var traps = 0;
        foreach (var p in Level.Neighbors(Player.Position))
        {
            if (Level.FeatureAt(p).Has(TerrainFlags.Secret))
            {
                RevealSecretDoor(p); // (by touch, if not by eye)
                doors++;
            }
            ref var sq = ref Level[p];
            if (sq.Trap != 0 && !sq.Has(SquareFlags.TrapVisible) && sq.TrapPower <= skill)
            {
                sq.Flags |= SquareFlags.TrapVisible;
                Known.Remember(Level, p);
                traps++;
            }
        }
        if (doors > 0) Publish(new MessageEvent(doors == 1 ? "You have found a secret door." : $"You have found {doors} secret doors."));
        if (traps > 0)
        {
            Publish(new MessageEvent(traps == 1 ? "You have found a trap." : $"You have found {traps} traps."));
            Publish(new TrapFoundEvent(traps));
            TrapsFound += traps;
        }
        if (doors + traps > 0) Disturb(); // (a long search stops at a find)
        else Publish(new MessageEvent("You find nothing."));
        return EnergyTable.MoveEnergy;
    }
}
