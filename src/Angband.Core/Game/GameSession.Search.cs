using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Time;
using Angband.Core.World;

namespace Angband.Core.Game;

/// <summary>
/// Angband 4.2 has no search command: secret doors next to the player are always found — after
/// every step, when holding still and on arriving on a level (player-util.c search()).
/// </summary>
public sealed partial class GameSession
{
    /// <summary>
    /// Angband search(): reveals any secret door beside the player, unless they are blind, confused,
    /// hallucinating or standing in the dark. Finding one disturbs (stops a run).
    /// </summary>
    private void Search()
    {
        if (Player.IsBlind || Player.Timed.Has(Effects.TimedIds.Confused) || Player.Timed.Has("image") || !StandingInLight)
            return;
        foreach (var p in Level.Neighbors(Player.Position))
        {
            if (!Level.FeatureAt(p).Has(TerrainFlags.Secret)) continue;
            RevealSecretDoor(p);
            Publish(new MessageEvent("You have found a secret door."));
            Disturb();
        }
    }

    /// <summary>
    /// Angband place_closed_door: the secret door becomes an ordinary closed door, locked one time in
    /// four (strength 1-7), and the player remembers it.
    /// </summary>
    private void RevealSecretDoor(Loc p)
    {
        ref var sq = ref Level[p];
        sq.Feature = Data.Terrain.Ids.ClosedDoor;
        sq.LockPower = Rng.OneIn(4) ? (byte)Rng.RandInt1(7) : (byte)0;
        Known.Remember(Level, p);
    }

    /// <summary>
    /// Angband no_light(), reversed: the player's square is lit by a light they carry or a glowing
    /// room. (A necromancer's unlight lets them see in the dark, but it isn't light to search by.)
    /// </summary>
    private bool StandingInLight => Player.LightRadius > 0 || Level[Player.Position].Has(SquareFlags.Glow);

    /// <summary>Angband do_cmd_hold: stay put for a turn, searching.</summary>
    private int Hold()
    {
        Search();
        return EnergyTable.MoveEnergy;
    }
}
