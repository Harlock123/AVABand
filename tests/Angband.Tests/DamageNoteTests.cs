using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>show_damage_taken (AVABand's own): the damage you take, noted on the message that says what hit.</summary>
public class DamageNoteTests
{
    private static (GameSession Game, List<IGameEvent> Seen) Game()
    {
        var game = Arena.Create(4);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        game.Player.Armour = 0;
        var seen = new List<IGameEvent>();
        game.Events.Subscribe<MessageEvent>(seen.Add);
        game.Events.Subscribe<DamageNoteEvent>(seen.Add);
        game.Events.Subscribe<PlayerHurtEvent>(seen.Add);
        return (game, seen);
    }

    [Fact]
    public void A_monsters_blow_carries_its_damage()
    {
        var (game, seen) = Game();
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(1, 0));
        // (A cave orc may shoot as well as hit: the first hurt from a blow is the one wanted.)
        bool ByBlow(PlayerHurtEvent h) => seen.Take(seen.IndexOf(h)).LastOrDefault(e => e is MessageEvent) is MessageEvent m && m.Text.Contains("hits you");
        for (var i = 0; i < 60 && !seen.OfType<PlayerHurtEvent>().Any(ByBlow); i++)
        {
            game.Player.Hp = game.Player.MaxHp;
            game.RunMonsterTurn(orc);
        }
        var hurt = seen.OfType<PlayerHurtEvent>().First(ByBlow);
        var at = seen.IndexOf(hurt);
        var note = Assert.IsType<DamageNoteEvent>(seen[at + 1]);
        Assert.Equal(hurt.Damage, note.Damage);
        Assert.Contains("hits you", ((MessageEvent)seen.Take(at).Last(e => e is MessageEvent)).Text);
    }

    [Fact]
    public void Poison_ticks_go_unnumbered()
    {
        var (game, seen) = Game();
        TestGames.ClearMonsters(game);
        game.Player.Timed.Set(game.Data.Timed(TimedIds.Poisoned)!, 20);
        for (var i = 0; i < 5; i++) game.Execute(new HoldCommand());
        Assert.NotEmpty(seen.OfType<PlayerHurtEvent>());
        Assert.Empty(seen.OfType<DamageNoteEvent>());
        Assert.DoesNotContain(seen.OfType<MessageEvent>(), m => m.Text.StartsWith("You take"));
    }

    [Fact]
    public void Damage_with_nothing_said_this_turn_gets_its_own_line()
    {
        var (game, seen) = Game();
        TestGames.ClearMonsters(game);
        game.Execute(new HoldCommand());
        seen.Clear();
        game.TakeHit(7, "a test");
        Assert.Equal("You take 7 damage.", Assert.Single(seen.OfType<MessageEvent>()).Text);
    }

    [Fact]
    public void Off_it_notes_nothing()
    {
        var (game, seen) = Game();
        game.Options[OptionIds.ShowDamageTaken] = false;
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(1, 0));
        for (var i = 0; i < 20 && !seen.OfType<PlayerHurtEvent>().Any(); i++) game.RunMonsterTurn(orc);
        Assert.NotEmpty(seen.OfType<PlayerHurtEvent>());
        Assert.Empty(seen.OfType<DamageNoteEvent>());
    }
}
