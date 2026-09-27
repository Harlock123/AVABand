using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Core.Game;

/// <summary>
/// Running (Angband's Shift+direction, '.' or roguelike Shift+letter): keep stepping one way, following
/// corridors round their bends, until something interesting happens — a monster comes into view or
/// moves, an object, a visible trap, a door, stairs or a shop comes alongside, the corridor opens or
/// branches, a wall is ahead, or the player is hurt or otherwise disturbed. A port of 4.2's
/// player-path.c run_init / run_test / run_step.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Angband's run counter when no count is given.</summary>
    public const int MaxRunSteps = 9999;

    // Angband's cycle[] and chome[]: the directions in turn round the compass (keypad numbers), and
    // where each direction sits in the middle of that cycle.
    private static readonly int[] Cycle = [1, 2, 3, 6, 9, 8, 7, 4, 1, 2, 3, 6, 9, 8, 7, 4, 1];
    private static readonly int[] Chome = [0, 8, 9, 10, 7, 0, 11, 6, 5, 4];

    private int _runCurDir;     // the direction we are running
    private int _runOldDir;     // the direction we came from
    private bool _runOpenArea;  // looking for an open area
    private bool _runBreakRight; // looking for a break on the right
    private bool _runBreakLeft;  // ... and on the left

    /// <summary>Set by <see cref="Disturb"/>; stops a run (and rest) in progress.</summary>
    private bool _disturbed;

    /// <summary>
    /// Angband disturb: something happened that should stop whatever the player is repeating — resting,
    /// running. (Monsters coming into view and being hurt are noticed by the loops themselves.)
    /// </summary>
    public void Disturb()
    {
        Player.IsResting = false;
        _disturbed = true;
    }

    /// <summary>Angband do_cmd_run: the first step is checked like a walk, then the run goes on by itself.</summary>
    private bool Run(Direction direction)
    {
        if (direction is Direction.Here or Direction.None) return false;
        var dir = (int)direction;
        var first = Player.Position.Step(direction);
        if (!Level.InBounds(first)) return false;

        // do_cmd_walk_test: a known wall or rubble stops the run before it starts; a monster in the
        // way is attacked, a closed door opened (by the first step), and unknown squares tried.
        if (Level.Monsters.At(first) is not { Camouflaged: false } && Known.IsKnown(first)
            && !Level.FeatureAt(first).Has(TerrainFlags.Passable) && !Level.FeatureAt(first).Has(TerrainFlags.DoorClosed))
        {
            Publish(new MessageEvent(Level.FeatureAt(first).Has(TerrainFlags.Rubble) ? "There is a pile of rubble in the way!" : "There is a wall in the way!"));
            return false;
        }

        RunInit(dir);
        var level = Level;
        var hp = Player.Hp;
        var seen = VisibleMonsters();
        var moved = false;
        _disturbed = false;

        for (var step = 0; step < MaxRunSteps && !IsGameOver; step++)
        {
            if (step > 0 && RunTest()) break;
            var from = Player.Position;
            // Only the first step can be sent astray by confusion (Angband player_confuse_dir).
            var energy = Walk(FromKeypad(_runCurDir), confuse: step == 0);
            if (energy <= 0) break;
            SpendAndAdvance(energy);
            moved = true;

            // Angband disturb: hurt, a monster coming into view (or one in view moving, with
            // disturb_near), anything else that disturbs, a new level, or not having moved at all
            // (an attack, a door opened).
            if (Level != level || Player.Hp < hp || _disturbed || MonstersDisturb(seen) || Player.Position == from) break;
            seen = VisibleMonsters();
        }
        _disturbed = false;
        return moved;
    }

    /// <summary>Angband run_init: sets up a run in a new direction, spotting corridor entries.</summary>
    private void RunInit(int dir)
    {
        _runCurDir = dir;
        _runOldDir = dir;
        _runOpenArea = true;
        _runBreakRight = _runBreakLeft = false;
        bool deepLeft = false, deepRight = false, shortLeft = false, shortRight = false;

        var grid = Player.Position.Step(FromKeypad(dir));
        var i = Chome[dir];

        if (SeeWall(Cycle[i + 1], Player.Position)) { _runBreakLeft = true; shortLeft = true; }
        else if (SeeWall(Cycle[i + 1], grid)) { _runBreakLeft = true; deepLeft = true; }

        if (SeeWall(Cycle[i - 1], Player.Position)) { _runBreakRight = true; shortRight = true; }
        else if (SeeWall(Cycle[i - 1], grid)) { _runBreakRight = true; deepRight = true; }

        if (!_runBreakLeft || !_runBreakRight) return;
        _runOpenArea = false;

        // Angled or blunt corridor entry.
        if ((dir & 1) != 0)
        {
            if (deepLeft && !deepRight) _runOldDir = Cycle[i - 1];
            else if (deepRight && !deepLeft) _runOldDir = Cycle[i + 1];
        }
        else if (SeeWall(Cycle[i], Player.Position))
        {
            if (shortLeft && !shortRight) _runOldDir = Cycle[i - 2];
            else if (shortRight && !shortLeft) _runOldDir = Cycle[i + 2];
        }
    }

    /// <summary>Angband run_test: picks the next direction, or returns true when the run should stop.</summary>
    private bool RunTest()
    {
        var option = 0;
        var option2 = 0;
        var prevDir = _runOldDir;
        var max = (prevDir & 1) + 1; // newly adjacent squares: 5 for diagonals, 3 for cardinals

        for (var i = -max; i <= max; i++)
        {
            var newDir = Cycle[Chome[prevDir] + i];
            var grid = Player.Position.Step(FromKeypad(newDir));
            if (!Level.InBounds(grid)) continue;

            // Visible monsters, visible traps and seen objects stop the run.
            if (Level.Monsters.At(grid) is { IsVisible: true }) return true;
            if (Level[grid].Trap != 0 && Level[grid].Has(SquareFlags.TrapVisible)) return true;
            if (Known.RememberedObject(grid) is not null && Level.Objects.At(grid).Any(o => !IsIgnored(o))) return true;

            var unknown = true;
            if (Known.IsKnown(grid))
            {
                // Doors, stairs, shops and other interesting features, as the player remembers them.
                if (Data.Terrain[Known.Feature(grid)].Has(TerrainFlags.Interesting)) return true;
                unknown = false;
            }

            if (unknown || Level.FeatureAt(grid).Has(TerrainFlags.Passable))
            {
                if (_runOpenArea) { }
                else if (option == 0) option = newDir;           // the first new direction
                else if (option2 != 0) return true;              // three new directions
                else if (option != Cycle[Chome[prevDir] + i - 1]) return true; // two, not adjacent
                else if ((newDir & 1) != 0) option2 = newDir;    // two adjacent (case 1)
                else { option2 = option; option = newDir; }      // two adjacent (case 2)
            }
            else if (_runOpenArea)
            {
                if (i < 0) _runBreakRight = true;
                else if (i > 0) _runBreakLeft = true;
            }
        }

        // Squares about to come alongside: an obvious monster there stops the run too.
        for (var i = -max; i <= max; i++)
        {
            var newDir = Cycle[Chome[prevDir] + i];
            var grid = Player.Position.Step(FromKeypad(prevDir)).Step(FromKeypad(newDir));
            if (!Level.InBounds(grid)) continue;
            if (Level.Monsters.At(grid) is { IsVisible: true, Camouflaged: false }) return true;
        }

        if (_runOpenArea)
        {
            for (var i = -max; i < 0; i++)
            {
                var grid = Player.Position.Step(FromKeypad(Cycle[Chome[prevDir] + i]));
                if (!Level.InBounds(grid)) continue;
                if (!Known.IsKnown(grid) || Level.FeatureAt(grid).Has(TerrainFlags.Passable))
                {
                    if (_runBreakRight) return true;
                }
                else if (_runBreakLeft) return true;
            }
            for (var i = max; i > 0; i--)
            {
                var grid = Player.Position.Step(FromKeypad(Cycle[Chome[prevDir] + i]));
                if (!Level.InBounds(grid)) continue;
                if (!Known.IsKnown(grid) || Level.FeatureAt(grid).Has(TerrainFlags.Passable))
                {
                    if (_runBreakLeft) return true;
                }
                else if (_runBreakRight) return true;
            }
        }
        else
        {
            if (option == 0) return true;   // nowhere to go
            _runCurDir = option;
            _runOldDir = option2 == 0 ? option : option2; // with two options, allow curving
        }

        // About to hit a known wall.
        return SeeWall(_runCurDir, Player.Position);
    }

    /// <summary>Angband see_wall: is the square that way a wall the player knows about?</summary>
    private bool SeeWall(int dir, Loc from)
    {
        var grid = from.Step(FromKeypad(dir));
        if (!Level.InBounds(grid)) return false;
        return Level.FeatureAt(grid).Has(TerrainFlags.Rock) && Known.IsKnown(grid);
    }

    /// <summary>The direction for a keypad number (1-9, as Angband counts them; <see cref="Direction"/> uses the same numbers).</summary>
    private static Direction FromKeypad(int dir) => (Direction)dir;
}
