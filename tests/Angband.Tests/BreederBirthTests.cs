using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>AVABand's own: a breeder race has at most 250 young on a level, counted from your arrival.</summary>
public class BreederBirthTests
{
    private static readonly string[] Room =
    [
        "##############################",
        "#,,,,,,,,,,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,,,,,,,,,,,,,,,#",
        "#,,,,,,,,,,,,,,@,,,,,,,,,,,,,#",
        "##############################",
    ];

    private const string Worm = "white_worm_mass";

    private static GameSession Swarm()
    {
        var game = Arena.Create(5, Room);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        game.Player.Stealth = 60;
        Arena.AddMonster(game, Worm, new Loc(10, 3));
        return game;
    }

    private static int Worms(GameSession game) => game.Level.Monsters.All.Count(m => m.Race.Id == Worm);

    private static void Hold(GameSession game, int turns)
    {
        for (var i = 0; i < turns && !game.IsGameOver; i++) game.Execute(new HoldCommand());
    }

    [Fact]
    public void Every_young_is_counted_against_its_race_on_the_level()
    {
        var game = Swarm();
        Hold(game, 150);
        Assert.True(Worms(game) > 3);
        Assert.Equal(Worms(game) - 1, game.Level.Births[Worm]);
    }

    /// <summary>The trap it ends: kill the swarm down as fast as it breeds, and it runs dry.</summary>
    [Fact]
    public void A_swarm_cut_down_as_it_breeds_runs_out_of_young()
    {
        var game = Swarm();
        game.Level.Births[Worm] = GameSession.BirthsPerBreederPerLevel - 5;
        var original = game.Level.Monsters.All.Single(m => m.Race.Id == Worm);
        for (var round = 0; round < 60; round++)
        {
            Hold(game, 10);
            foreach (var young in game.Level.Monsters.All.Where(m => m != original).ToList()) game.Level.Monsters.Remove(young);
        }
        Assert.Equal(GameSession.BirthsPerBreederPerLevel, game.Level.Births[Worm]);
        Assert.True(game.BreederSpent(original.Race));
        Hold(game, 200);
        Assert.Equal(1, Worms(game)); // no more young, however long you wait
    }

    [Fact]
    public void A_spent_race_leaves_other_breeders_alone()
    {
        var game = Swarm();
        game.Level.Births[Worm] = GameSession.BirthsPerBreederPerLevel;
        Arena.AddMonster(game, "giant_white_louse", new Loc(20, 2));
        Hold(game, 150);
        Assert.Equal(1, Worms(game));
        Assert.True(game.Level.Monsters.All.Count(m => m.Race.Id == "giant_white_louse") > 1);
    }

    [Fact]
    public void The_count_is_kept_in_a_save()
    {
        var game = Swarm();
        game.Level.Births[Worm] = 123;
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        Assert.Equal(123, SaveGame.Load(TestData.Game, stream).Level.Births[Worm]);
    }

    [Fact]
    public void The_count_starts_again_on_a_new_level_and_on_coming_back_to_a_kept_one()
    {
        var game = GameSession.NewGame(TestData.Game, 11, "warrior");
        game.Options[OptionIds.LevelsPersist] = true;
        game.Player.Hp = game.Player.MaxHp = 1_000_000;
        void Take(bool down)
        {
            game.Player.Position = game.Level.FindFeature(down ? TerrainFlags.DownStair : TerrainFlags.UpStair).First();
            TestGames.ClearMonsters(game);
            Assert.True(game.Execute(new TakeStairsCommand(down)));
        }
        Take(down: true);                                 // town -> 1
        var level1 = game.Level;
        level1.Births[Worm] = GameSession.BirthsPerBreederPerLevel;
        Take(down: true);                                 // 1 -> 2: a new level, a new count
        Assert.Empty(game.Level.Births);
        Take(down: false);                                // 2 -> 1: the same level, the count begun again
        Assert.Same(level1, game.Level);
        Assert.Empty(game.Level.Births);
    }
}
