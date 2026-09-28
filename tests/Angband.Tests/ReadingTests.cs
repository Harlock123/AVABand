using Angband.Core.Effects;
using Angband.Core.Game;

namespace Angband.Tests;

/// <summary>Angband 4.2 player_can_read: blind, in the dark, confused or amnesiac, you can't read.</summary>
public class ReadingTests
{
    [Fact]
    public void AConfusedPlayer_CannotRead()
    {
        var game = Arena.Create(3, "#####", "#,@,#", "#####");
        TestGames.ClearMonsters(game);
        game.UpdateView();
        var scroll = game.Objects.Create("phase_door", 2);
        game.Knowledge.LearnKind(scroll.Kind);
        scroll = game.Player.Inventory.Add(scroll)!;
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));

        game.IncreaseTimed(TimedIds.Confused, 10);
        Assert.Equal("You are too confused to read!", game.CannotRead());
        Assert.False(game.Execute(new UseCommand(scroll)));
        Assert.Contains("You are too confused to read!", said);

        game.Player.Timed.Set(game.Data.Timed(TimedIds.Confused)!, 0);
        Assert.Null(game.CannotRead());
        Assert.True(game.Execute(new UseCommand(scroll)));
    }
}
