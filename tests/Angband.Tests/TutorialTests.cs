using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>The tutorial level: each lesson, in order, then the stairs.</summary>
public class TutorialTests
{
    [Fact]
    public void TheTutorial_CanBePlayedThrough_StepByStep()
    {
        var game = Tutorial.Create(TestData.Game);
        Assert.True(game.IsTutorial);
        Assert.Equal(1, game.Player.Depth);
        Assert.Equal(TutorialStep.PickUp, Tutorial.StepOf(game));

        // Pick up the potion.
        var potion = game.Level.Objects.All.Single().Loc;
        while (game.Player.Position != potion) game.Execute(new WalkCommand(Direction.East));
        if (game.Level.Objects.Any(potion)) game.Execute(new PickupCommand());
        Assert.Equal(TutorialStep.OpenDoor, Tutorial.StepOf(game));

        // Open the door (walking into it, as the tutorial says).
        for (var i = 0; i < 20 && Tutorial.StepOf(game) == TutorialStep.OpenDoor; i++) game.Execute(new WalkCommand(Direction.East));
        Assert.Equal(TutorialStep.Trap, Tutorial.StepOf(game));

        // Past the trap: walk at it until it is disarmed (or jumped).
        for (var i = 0; i < 400 && Tutorial.StepOf(game) == TutorialStep.Trap; i++)
        {
            game.Player.Hp = game.Player.MaxHp;
            game.Execute(new WalkCommand(Direction.East));
        }
        Assert.Equal(TutorialStep.Fight, Tutorial.StepOf(game));

        // The kobold, then the stairs.
        var kobold = game.Level.Monsters.All.Single(m => m.Race.Id == Tutorial.MonsterId);
        game.DamageMonster(kobold, 10_000);
        Assert.Equal(TutorialStep.Stairs, Tutorial.StepOf(game));
        Assert.Contains(game.Level.AllLocs(), p => game.Level.Has(p, Angband.Core.Definitions.TerrainFlags.DownStair));
    }
}
