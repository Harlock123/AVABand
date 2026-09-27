using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

public class TargetingTests
{
    private static GameSession Game()
    {
        var game = Arena.Create(3,
            "###################",
            "#,,,,,,,,,,,,,,,,,#",
            "#,,@,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,#",
            "###################");
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        game.Player.SkillBow = game.Player.SkillThrow = 1000;
        return game;
    }

    [Fact]
    public void Firing_prefers_the_target_over_the_nearest_monster()
    {
        var game = Game();
        var near = Arena.AddMonster(game, "cave_orc", new Loc(6, 1), awake: false); // asleep: out of the line of fire
        var far = Arena.AddMonster(game, "cave_orc", new Loc(11, 3), awake: false);
        near.Hp = near.MaxHp = far.Hp = far.MaxHp = 100_000;
        game.SetTarget(far);
        var hit = new HashSet<int>();
        game.Events.Subscribe<PlayerAttackEvent>(e => hit.Add(e.MonsterId));
        for (var i = 0; i < 10 && hit.Count == 0; i++) game.Execute(new FireCommand());
        Assert.Equal([far.Id], hit);
    }

    [Fact]
    public void An_invalid_target_falls_back_to_the_nearest()
    {
        var game = Game();
        var near = Arena.AddMonster(game, "cave_orc", new Loc(6, 2));
        var far = Arena.AddMonster(game, "cave_orc", new Loc(14, 2));
        game.SetTarget(far);
        Assert.True(game.TargetOkay());
        game.DamageMonster(far, 100_000);
        Assert.False(game.TargetOkay());
        Assert.Equal(near.Position, game.AimPoint());
    }

    [Fact]
    public void A_spot_can_be_targeted()
    {
        var game = Game();
        game.Player.SkillDevice = 500;
        var orc = Arena.AddMonster(game, "cave_orc", new Loc(14, 1));
        orc.Hp = orc.MaxHp = 100_000;
        game.SetTarget(new Loc(14, 2)); // next to the orc: a ball there catches it
        var wand = game.Objects.Create("wand_of_stinking_cloud");
        wand.Charges = 3;
        game.Knowledge.LearnKind(wand.Kind);
        Assert.True(game.Execute(new UseCommand(game.Player.Inventory.Add(wand)!)));
        Assert.True(orc.Hp < 100_000);
    }

    [Fact]
    public void Direction_to_target_points_the_way()
    {
        var game = Game();
        game.SetTarget(new Loc(10, 2));
        Assert.Equal(Direction.East, game.DirectionToTarget());
        game.SetTarget(new Loc(3, 1));
        Assert.Equal(Direction.North, game.DirectionToTarget());
        game.ClearTarget();
        Assert.Null(game.DirectionToTarget());
    }

    [Fact]
    public void Targetable_monsters_are_visible_ones_nearest_first()
    {
        var game = Game();
        var far = Arena.AddMonster(game, "cave_orc", new Loc(15, 2));
        var near = Arena.AddMonster(game, "jackal", new Loc(6, 2));
        var ghost = Arena.AddMonster(game, "poltergeist", new Loc(8, 2)); // invisible
        game.UpdateView();
        Assert.Equal([near, far], game.TargetableMonsters());
        Assert.DoesNotContain(ghost, game.TargetableMonsters());
    }

    [Fact]
    public void Changing_level_clears_the_target()
    {
        var game = GameSession.NewGame(TestData.Game, 2);
        game.SetTarget(game.Player.Position + new Loc(1, 0));
        game.Execute(new DebugJumpCommand(1));
        Assert.Null(game.TargetMonster);
        Assert.Null(game.TargetLocation);
    }
}
