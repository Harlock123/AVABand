using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.World;

namespace Angband.Core.Game;

/// <summary>The steps of the tutorial, in the order they are taught.</summary>
public enum TutorialStep { PickUp, OpenDoor, Trap, Fight, Stairs }

/// <summary>
/// AVABand's tutorial (not Angband's): a short level made for teaching, one lesson after another —
/// pick up a potion, open a door, get past a trap, fight a sleeping kobold, take the stairs. A
/// tutorial game is never saved and never scored; the interface guides each step.
/// </summary>
public static class Tutorial
{
    /// <summary>
    /// <c>#</c> granite, <c>,</c> lit room floor, <c>.</c> dark corridor, <c>+</c> closed door,
    /// <c>@</c> the start, <c>!</c> a potion, <c>^</c> a pit, <c>k</c> a kobold, <c>&gt;</c> the stairs down.
    /// </summary>
    public static readonly string[] Map =
    [
        "#############################################",
        "#,,,,,,,####################,,,,,,,,,,#######",
        "#,@,,!,,+..........^.......+,,,,,,k,,,+....>#",
        "#,,,,,,,####################,,,,,,,,,,#######",
        "#############################################",
    ];

    public const string MonsterId = "small_kobold";
    public const string PotionId = "cure_light_wounds";
    public const string TrapId = "pit";

    /// <summary>A new tutorial: a Human Warrior named Pupil on the tutorial level.</summary>
    public static GameSession Create(GameData data, ulong seed = 1)
    {
        var spec = CharacterSpec.Default("human", "warrior") with { Name = "Pupil" };
        var game = GameSession.NewGame(data, seed, spec);
        game.IsTutorial = true;

        var t = data.Terrain;
        var level = new Level(t, Map[0].Length, Map.Length, depth: 1) { ProfileId = "tutorial" };
        var start = default(Loc);
        for (var y = 0; y < Map.Length; y++)
        for (var x = 0; x < Map[y].Length; x++)
        {
            var at = new Loc(x, y);
            ref var sq = ref level[x, y];
            var ch = Map[y][x];
            sq.Feature = ch switch
            {
                '#' => t.Ids.Granite,
                '+' => t.Ids.ClosedDoor,
                '>' => t.Ids.DownStair,
                _ => t.Ids.Floor,
            };
            // The rooms are lit, walls and doors included (as Angband lights a room's edges).
            var room = ch is ',' or '@' or '!' or 'k' || ch is '#' or '+' && TouchesRoom(x, y);
            if (room) sq.Flags |= SquareFlags.Glow | SquareFlags.Room;
            switch (ch)
            {
                case '@': start = at; break;
                case '!': level.Objects.Add(at, game.Objects.Create(PotionId)); break;
                case '^':
                    sq.Trap = data.Traps.First(tr => tr.Id == TrapId).Index;
                    sq.Flags |= SquareFlags.TrapVisible;
                    break;
                case 'k':
                    new MonsterSpawner(data).Place(level, game.Rng, data.Monster(MonsterId)!, at, asleep: true);
                    break;
            }
        }
        game.UseLevel(level, start);
        return game;
    }

    private static bool TouchesRoom(int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var ny = y + dy;
            var nx = x + dx;
            if (ny >= 0 && ny < Map.Length && nx >= 0 && nx < Map[ny].Length && Map[ny][nx] is ',' or '@' or '!' or 'k')
                return true;
        }
        return false;
    }

    /// <summary>Where the player has got to: the first lesson not yet done.</summary>
    public static TutorialStep StepOf(GameSession game)
    {
        var level = game.Level;
        if (level.Objects.All.Any(o => o.Item.Kind.Id == PotionId)) return TutorialStep.PickUp;
        if (Doors(level).First() is var first && level.Has(first, TerrainFlags.DoorClosed)) return TutorialStep.OpenDoor;
        if (level.AllLocs().Any(p => level[p].Trap != 0) && game.Player.Position.X < TrapColumn()) return TutorialStep.Trap;
        if (level.Monsters.All.Any(m => m.Race.Id == MonsterId)) return TutorialStep.Fight;
        return TutorialStep.Stairs;
    }

    private static IEnumerable<Loc> Doors(Level level) =>
        Enumerable.Range(0, Map[2].Length).Where(x => Map[2][x] == '+').Select(x => new Loc(x, 2));

    private static int TrapColumn() => Map[2].IndexOf('^');
}
