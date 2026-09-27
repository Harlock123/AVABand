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
        var level = TestLevels.FromAscii(out var eye, rows);
        // The start square belongs to the room only if it is next to room floor (not in a corridor).
        var inRoom = level.Neighbors(eye).Any(n => level[n].Has(Angband.Core.World.SquareFlags.Room) && level.IsFloor(n));
        level[eye].Flags |= inRoom
            ? Angband.Core.World.SquareFlags.Glow | Angband.Core.World.SquareFlags.Room
            : Angband.Core.World.SquareFlags.None;
        game.UseLevel(level, eye);
        return game;
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
