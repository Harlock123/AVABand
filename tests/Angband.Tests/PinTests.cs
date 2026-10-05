using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>Map pins (AVABand's own): a note on a square, kept with the level and its save.</summary>
public class PinTests
{
    [Fact]
    public void A_pin_is_set_changed_and_taken_away_without_taking_time()
    {
        var game = Arena.Create(1, "#####", "#@,,#", "#####");
        var at = new Loc(3, 1);
        game.Execute(new HoldCommand());
        var turn = game.GameTurn;
        game.Execute(new PinCommand(at, "  come back with a pick  "));
        Assert.Equal("come back with a pick", game.PinAt(at));
        Assert.Equal(turn, game.GameTurn);
        game.Execute(new PinCommand(at, new string('x', 100)));
        Assert.Equal(GameSession.MaxPinLength, game.PinAt(at)!.Length);
        game.Execute(new PinCommand(at, ""));
        Assert.Null(game.PinAt(at));
    }

    [Fact]
    public void Pins_are_kept_in_the_save()
    {
        var game = GameSession.NewGame(TestData.Game, 3, "warrior");
        game.MarkDebugUsed();
        game.Execute(new DebugJumpCommand(2));
        var at = game.Player.Position;
        game.Execute(new PinCommand(at, "vault here"));
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        Assert.Equal("vault here", SaveGame.Load(TestData.Game, stream).PinAt(at));
    }
}
