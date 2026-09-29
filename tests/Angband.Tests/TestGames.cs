using Angband.Core.Game;

namespace Angband.Tests;

internal static class TestGames
{
    public static void ClearMonsters(GameSession game)
    {
        foreach (var monster in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(monster);
    }

    /// <summary>Holds until the game turn is reached; fails instead of hanging if time stops (e.g. death).</summary>
    public static void HoldUntil(GameSession game, long gameTurn)
    {
        for (var i = 0; game.GameTurn < gameTurn; i++)
        {
            Assert.True(i < 100_000 && game.Execute(new HoldCommand()), $"time stopped at turn {game.GameTurn}");
        }
    }
}

internal static class Arena
{
    /// <summary>A lit, walled room with the player at '@'.</summary>
    public static GameSession Create(ulong seed = 1, params string[] rows) => CreateWith(TestData.Game, seed, rows);

    public static GameSession CreateWith(Angband.Core.Definitions.GameData data, ulong seed, params string[] rows)
    {
        if (rows.Length == 0)
            rows =
            [
                "###########",
                "#,,,,,,,,,#",
                "#,,,,@,,,,#",
                "#,,,,,,,,,#",
                "###########",
            ];
        var game = GameSession.NewGame(data, seed);
        GiveTestKit(game);
        var level = TestLevels.FromAscii(out var eye, rows);
        // The start square belongs to the room only if it is next to room floor (not in a corridor).
        var inRoom = level.Neighbors(eye).Any(n => level[n].Has(Angband.Core.World.SquareFlags.Room) && level.IsFloor(n));
        level[eye].Flags |= inRoom
            ? Angband.Core.World.SquareFlags.Glow | Angband.Core.World.SquareFlags.Room
            : Angband.Core.World.SquareFlags.None;
        game.UseLevel(level, eye);
        return game;
    }

    /// <summary>
    /// A test fixture's kit, on top of the warrior's own (4.2.5's: food, torches, a potion of
    /// Berserk Strength, a dagger, soft leather armour and Word of Recall): the things tests reach for —
    /// Cure Light Wounds, Phase Door and flasks of oil, and a sling with iron shots.
    /// </summary>
    public static void GiveTestKit(GameSession game)
    {
        if (game.Data.Object("cure_light_wounds") is null) return; // a test's own small data set
        foreach (var (kind, count) in new[] { ("cure_light_wounds", 2), ("phase_door", 2), ("flask_of_oil", 2), ("iron_shot", 40) })
        {
            var item = game.Objects.Create(kind, count);
            game.Knowledge.LearnKind(item.Kind);
            game.Player.Inventory.Add(item);
        }
        if (game.Player.Inventory.Bow is null)
        {
            var sling = game.Objects.Create("sling");
            game.Knowledge.LearnKind(sling.Kind);
            game.Player.Inventory.Wield(sling, () => game.Objects.NextSerial++);
        }
        game.RecalculateBonuses();
    }

    /// <summary>Takes off all equipment (into the pack) and zeroes intrinsic armour.</summary>
    public static void StripGear(GameSession game)
    {
        foreach (var item in game.Player.Inventory.Equipped.ToList()) game.Player.Inventory.TakeOff(item);
        game.Player.BaseArmour = 0;
        game.RecalculateBonuses();
    }

    public static Angband.Core.Monsters.Monster AddMonster(GameSession game, string raceId, Angband.Core.Geometry.Loc at, bool awake = true)
    {
        var race = game.Data.Monster(raceId) ?? throw new ArgumentException(raceId);
        var monster = new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, race, at, asleep: !awake);
        game.Scheduler.Add(monster);
        game.UpdateView();
        return monster;
    }
}
