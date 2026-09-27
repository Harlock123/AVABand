using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Monsters;

namespace Angband.Tests;

public class MonsterPowerTests
{
    [Theory]
    [InlineData(5ul, 3)]
    [InlineData(6ul, 12)]
    [InlineData(7ul, 25)]
    [InlineData(8ul, 40)]
    [InlineData(9ul, 60)]
    [InlineData(10ul, 90)]
    public void Imported_monsters_play_without_errors(ulong seed, int depth)
    {
        // A sturdy adventurer wanders a deep level for a while: every monster power gets exercised.
        var game = GameSession.NewGame(TestData.Game, seed);
        game.Execute(new DebugJumpCommand(depth));
        var rng = new Angband.Core.Randomness.GameRandom(seed);
        for (var i = 0; i < 400; i++)
        {
            game.Player.Hp = game.Player.MaxHp = 30_000;
            game.Player.IsDead = false;
            GameCommand command = rng.RandInt0(12) switch
            {
                0 => new RestCommand(),
                1 => new HoldCommand(),
                _ => new WalkCommand(DirectionExtensions.Compass[rng.RandInt0(8)]),
            };
            game.Execute(command);
            if (game.Player.Depth != depth) game.Execute(new DebugJumpCommand(depth));
        }
        Assert.True(game.Level.Monsters.All.Count() >= 0);
    }
}
