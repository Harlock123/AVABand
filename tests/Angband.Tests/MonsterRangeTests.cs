using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>
/// Angband 4.2's combat ranges (mon-move.c get_move_find_range): morale — a monster that judges the
/// player too strong keeps right away — and the preferred range at which it casts twice as often.
/// </summary>
public class MonsterRangeTests
{
    private static GameSession Room(int playerLevel)
    {
        var game = Arena.Create(9,
            "#####################",
            "#,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,@,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,#",
            "#####################");
        TestGames.ClearMonsters(game);
        game.Player.Level = playerLevel;
        return game;
    }

    /// <summary>Runs the monster's turns with the player standing still and unhurt; returns melee attacks made.</summary>
    private static int Turns(GameSession game, Angband.Core.Monsters.Monster monster, int turns)
    {
        var attacks = 0;
        using var sub = game.Events.Subscribe<MonsterAttackEvent>(a => attacks += a.MonsterId == monster.Id ? 1 : 0);
        for (var i = 0; i < turns; i++)
        {
            game.Player.Hp = game.Player.MaxHp;
            game.UpdateView();
            game.RunMonsterTurn(monster);
        }
        return attacks;
    }

    [Fact]
    public void AnOutclassedMonster_KeepsAway()
    {
        // A level 40 player against a jackal (level 1): its morale breaks, it never closes in.
        var game = Room(40);
        var jackal = Arena.AddMonster(game, "jackal", game.Player.Position + new Loc(4, 0));
        Assert.Equal(game.FleeRange, game.CombatRange(jackal).Min);
        var start = jackal.Position.DistanceTo(game.Player.Position);

        Assert.Equal(0, Turns(game, jackal, 10));
        Assert.True(jackal.Position.DistanceTo(game.Player.Position) > start);
        Assert.False(jackal.IsAfraid); // not frightened — it just knows better
    }

    [Fact]
    public void AnEvenMatch_ComesToFight()
    {
        var game = Room(1);
        var jackal = Arena.AddMonster(game, "jackal", game.Player.Position + new Loc(4, 0));
        Assert.Equal(1, game.CombatRange(jackal).Min);
        Assert.True(Turns(game, jackal, 10) > 0);
    }

    [Fact]
    public void AMonsterClosePlayerInLevel_LosesHeartWhenBadlyHurt()
    {
        // A cave orc (level 7) against a level 30 player: at full health it fights; badly hurt, it keeps away.
        var game = Room(30);
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(6, 0));
        Assert.Equal(1, game.CombatRange(orc).Min);
        orc.Hp = orc.MaxHp / 10;
        Assert.Equal(game.FleeRange, game.CombatRange(orc).Min);
    }

    [Fact]
    public void Taunting_OverridesMorale()
    {
        var game = Room(40);
        var jackal = Arena.AddMonster(game, "jackal", game.Player.Position + new Loc(4, 0));
        game.Player.Timed.Set(game.Data.Timed("taunt")!, 100);
        Assert.Equal((1, 1), game.CombatRange(jackal));
    }

    [Fact]
    public void Cornered_BesideThePlayer_ItFightsBack()
    {
        // A dead end, and a player too weak to scare it (get_move_find_range): the jackal bites.
        var game = Arena.Create(9, "#####", "#@,,#", "#####");
        TestGames.ClearMonsters(game);
        game.Player.Level = 1;
        game.Player.Position = new Loc(2, 1);
        var jackal = Arena.AddMonster(game, "jackal", new Loc(3, 1));
        Assert.True(Turns(game, jackal, 5) > 0);
    }

    [Fact]
    public void Cornered_BesideAMuchStrongerPlayer_ItCowers()
    {
        // 4.2.5: a player far above its level puts it at flee range; it can't get away and doesn't bite.
        var game = Arena.Create(9, "#####", "#@,,#", "#####");
        TestGames.ClearMonsters(game);
        game.Player.Level = 40;
        game.Player.Position = new Loc(2, 1);
        var jackal = Arena.AddMonster(game, "jackal", new Loc(3, 1));
        Assert.Equal(0, Turns(game, jackal, 5));
    }

    [Fact]
    public void PreferredRanges_FollowAngbandsRules()
    {
        var game = Room(1);
        // A quylthulg never moves nor strikes (+3, +3) and casts often (another +3).
        var far = Arena.AddMonster(game, "quylthulg", game.Player.Position + new Loc(7, 0));
        Assert.Equal((7, 10), game.CombatRange(far));
        // Within turn-range the extra distance is dropped.
        var near = Arena.AddMonster(game, "quylthulg", game.Player.Position + new Loc(3, 0));
        Assert.Equal((1, 4), game.CombatRange(near));
        // A kobold archer shoots too often to count as a patient archer (4.2's freq_innate < 4): range 1.
        var archer = Arena.AddMonster(game, "kobold_archer", game.Player.Position + new Loc(0, 2));
        Assert.Equal((1, 1), game.CombatRange(archer));
    }
}
