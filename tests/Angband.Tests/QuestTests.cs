using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Persistence;
using Angband.Core.Records;

namespace Angband.Tests;

public class QuestTests
{
    private static GameSession At(int depth, ulong seed = 7)
    {
        var game = GameSession.NewGame(TestData.Game, seed);
        // Tough enough to survive the monsters' first turn on arrival, however the level rolls.
        game.Player.Hp = game.Player.MaxHp = 100_000;
        game.Execute(new DebugJumpCommand(depth));
        game.Player.Hp = game.Player.MaxHp;
        return game;
    }

    private static Monster Guardian(GameSession game, string raceId) =>
        game.Level.Monsters.All.Single(m => m.Race.Id == raceId);

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    [Fact]
    public void Sauron_and_Morgoth_are_the_quests()
    {
        var quests = TestData.Game.Quests;
        Assert.Equal(["sauron", "morgoth"], quests.Select(q => q.Id));
        Assert.Equal([99, 100], quests.Select(q => q.Level));
        Assert.True(TestData.Game.Monster("sauron_the_sorcerer")!.Has(MonsterFlags.Questor));
    }

    [Fact]
    public void Quest_monsters_never_turn_up_at_random()
    {
        var spawner = new MonsterSpawner(TestData.Game);
        var rng = new Angband.Core.Randomness.GameRandom(1);
        for (var i = 0; i < 3000; i++)
            Assert.False(spawner.PickRace(rng, 100, new HashSet<string>())!.Has(MonsterFlags.Questor));
    }

    [Fact]
    public void The_quest_level_has_its_guardian_and_no_way_down()
    {
        var game = At(99);
        Assert.Single(game.Level.Monsters.All, m => m.Race.Id == "sauron_the_sorcerer");
        Assert.Empty(game.Level.FindFeature(TerrainFlags.DownStair));
        Assert.DoesNotContain(game.Level.AllLocs(), p => game.Level[p].Trap != 0 && game.Data.TrapByIndex(game.Level[p].Trap)!.IsTrapDoor);
        Assert.Same(game.Data.Quests[0], game.QuestAt(99));
    }

    [Fact]
    public void Nothing_carries_you_past_an_unfinished_quest()
    {
        var game = At(97);
        Assert.Equal(99, game.CapDepth(97, 102));
        Assert.Equal(98, game.CapDepth(97, 98));
        game.QuestKills["sauron"] = 1;
        Assert.Equal(100, game.CapDepth(97, 102));
        game.QuestKills["morgoth"] = 1;
        Assert.Equal(102, game.CapDepth(97, 102));
    }

    [Fact]
    public void Deep_descent_stops_at_the_quest_level()
    {
        var game = At(96);
        game.ApplyEffects("deep_descent");
        for (var i = 0; i < 40 && game.Player.Depth == 96; i++)
        {
            game.Player.Hp = game.Player.MaxHp;
            TestGames.ClearMonsters(game);
            game.Execute(new HoldCommand());
        }
        Assert.Equal(99, game.Player.Depth);
    }

    [Fact]
    public void Killing_Sauron_opens_a_stair_down()
    {
        var game = At(99);
        var messages = Messages(game);
        var sauron = Guardian(game, "sauron_the_sorcerer");
        game.DamageMonster(sauron, 1_000_000);
        Assert.True(game.IsQuestComplete(game.Data.Quests[0]));
        Assert.Contains("A magical staircase appears...", messages);
        Assert.Single(game.Level.FindFeature(TerrainFlags.DownStair));
        Assert.False(game.Player.IsWinner);
        Assert.Null(game.QuestAt(99));
    }

    [Fact]
    public void Killing_Morgoth_wins_and_retiring_ends_the_game()
    {
        var game = At(100);
        game.QuestKills["sauron"] = 1;
        var messages = Messages(game);
        var won = false;
        game.Events.Subscribe<GameWonEvent>(_ => won = true);
        game.DamageMonster(Guardian(game, "morgoth_lord_of_darkness"), 1_000_000);
        Assert.True(won);
        Assert.True(game.Player.IsWinner);
        Assert.Contains("You have won the game!", messages);
        Assert.Contains("*** Winner: slayer of Morgoth ***", CharacterDump.Build(game));

        Assert.True(game.Execute(new RetireCommand()));
        Assert.True(game.Player.IsDead);
        Assert.Equal("Ripe Old Age", game.Player.KilledBy);
        var entry = ScoreEntry.For(game);
        Assert.True(entry.Won);
        Assert.Contains("Retired victorious.", CharacterDump.Build(game));
    }

    [Fact]
    public void Only_winners_can_retire()
    {
        var game = At(5);
        var messages = Messages(game);
        Assert.False(game.Execute(new RetireCommand()));
        Assert.False(game.Player.IsDead);
        Assert.Contains("You can retire once you have defeated Morgoth.", messages);
    }

    [Fact]
    public void A_dead_guardian_stays_dead()
    {
        var game = At(99);
        game.DamageMonster(Guardian(game, "sauron_the_sorcerer"), 1_000_000);
        game.Execute(new DebugJumpCommand(98));
        game.Execute(new DebugJumpCommand(99));
        Assert.DoesNotContain(game.Level.Monsters.All, m => m.Race.Id == "sauron_the_sorcerer");
        Assert.NotEmpty(game.Level.FindFeature(TerrainFlags.DownStair));
    }

    [Fact]
    public void Quest_progress_survives_saving()
    {
        var game = At(99);
        game.QuestKills["sauron"] = 1;
        game.Player.IsWinner = true;
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal(1, loaded.QuestKills["sauron"]);
        Assert.True(loaded.Player.IsWinner);
    }
}
