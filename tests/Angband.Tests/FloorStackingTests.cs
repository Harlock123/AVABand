using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>Angband 4.2's birth_stacking ("Stack objects on the floor"): off, a square holds one object.</summary>
public class FloorStackingTests
{
    private static GameSession Game(bool stacking)
    {
        var game = Arena.Create(4, "#######", "#,,,,,#", "#,,@,,#", "#,,,,,#", "#######");
        TestGames.ClearMonsters(game);
        game.Options[OptionIds.Stacking] = stacking;
        return game;
    }

    private static Angband.Core.Items.Item Carry(GameSession game, string kind, int number = 1) =>
        game.Player.Inventory.Add(game.Objects.Create(kind, number))!;

    [Fact]
    public void TheOption_IsAngbands_OnByDefault()
    {
        var option = OptionCatalog.Find(OptionIds.Stacking)!;
        Assert.Equal(OptionKind.Birth, option.Kind);
        Assert.True(option.Default);
        Assert.Equal("Stack objects on the floor", option.Description);
    }

    [Fact]
    public void WithStacking_EverythingDroppedLiesUnderfoot()
    {
        var game = Game(stacking: true);
        game.Execute(new DropCommand(Carry(game, "dagger")));
        game.Execute(new DropCommand(Carry(game, "spear")));
        Assert.Equal(2, game.Level.Objects.At(game.Player.Position).Count());
    }

    [Fact]
    public void WithoutStacking_ASecondThingRollsToTheNextSquare_ButLikeThingsStillStack()
    {
        var game = Game(stacking: false);
        var here = game.Player.Position;
        var scrolls = Carry(game, "phase_door", 5);
        game.Execute(new DropCommand(scrolls, 2));
        game.Execute(new DropCommand(scrolls, 3));   // joins the scrolls underfoot
        Assert.Single(game.Level.Objects.At(here));
        Assert.Equal(5, game.Level.Objects.At(here).Single().Number);

        game.Execute(new DropCommand(Carry(game, "dagger")));
        Assert.Single(game.Level.Objects.At(here));
        var dagger = Assert.Single(game.Level.Objects.All, o => o.Item.Kind.Id == "dagger");
        Assert.Equal(1, dagger.Loc.ChebyshevTo(here));

        // Monster drops and other falls spread out the same way.
        for (var i = 0; i < 4; i++) game.DropNear(game.Objects.Create("spear"), here);
        Assert.All(game.Level.Objects.All.GroupBy(o => o.Loc), g => Assert.Single(g));
    }
}
