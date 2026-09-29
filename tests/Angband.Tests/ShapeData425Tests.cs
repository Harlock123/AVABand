using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>
/// The shapes brought into line with Angband 4.2.5's shape.txt: extra moves (fox, eagle,
/// werewolf), the Púkel-man's poison immunity, damage reduction and body of stone, and the
/// werewolf's howl at the player's level.
/// </summary>
public class ShapeData425Tests
{
    [Fact]
    public void A_fox_moves_twice_as_often()
    {
        var game = Arena.Create(3);
        var normal = game.MoveEnergyPerStep;
        game.Shapechange("fox");
        Assert.Equal(normal / 2, game.MoveEnergyPerStep);
        game.Shapechange("eagle");
        Assert.Equal(normal / 4, game.MoveEnergyPerStep); // three extra moves
    }

    [Fact]
    public void The_Pukel_man_is_stone()
    {
        var game = Arena.Create(4);
        game.Player.Hp = game.Player.MaxHp = 1000;
        game.IncreaseTimed(TimedIds.Cut, 50); // cut before the change
        game.Shapechange("pukel_man");
        Assert.Equal(3, game.Player.Resists["pois"]);
        Assert.Equal(10, game.DamageReduction);

        for (var i = 0; i < 30; i++) game.Execute(new HoldCommand());
        Assert.Equal(50, game.Player.Timed[TimedIds.Cut]); // neither bleeding nor healing
        Assert.Equal(1000, game.Player.Hp);
        Assert.False(game.IncreaseTimed(TimedIds.Cut, 50)); // and stone can't be cut (player_timed.txt fail:4:ROCK)
        Assert.Equal(50, game.Player.Timed[TimedIds.Cut]);
    }

    [Fact]
    public void A_werewolf_howls_the_monsters_into_flight()
    {
        Assert.Contains("project_los:scare:{L}", TestData.Game.Shape("werewolf")!.Effect);
        // (A monster gets a saving throw against being scared: one of a few orcs is enough.)
        var scared = false;
        for (ulong seed = 5; seed < 15 && !scared; seed++)
        {
            var game = Arena.Create(seed);
            var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(3, 0));
            game.UpdateView();
            game.Shapechange("werewolf");
            scared = orc.Fear > 0;
        }
        Assert.True(scared);
    }
}
