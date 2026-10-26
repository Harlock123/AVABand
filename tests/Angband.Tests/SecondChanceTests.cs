using Angband.Core.Game;
using Angband.Core.Persistence;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>AVABand's second chance: once, a shallow death sends you back to town instead.</summary>
public class SecondChanceTests
{
    private static GameSession NewGame(bool secondChance = true) =>
        GameSession.NewGame(TestData.Game, 6, CharacterSpec.Default("human", "warrior") with
        {
            Options = new Dictionary<string, bool> { [OptionIds.AvaSecondChance] = secondChance },
        });

    [Fact]
    public void Its_off_by_default_and_on_only_in_the_relaxed_preset()
    {
        Assert.False(OptionCatalog.Find(OptionIds.AvaSecondChance)!.Default);
        Assert.Equal([true, false, false], Challenges.Presets.Select(p => p.Options[OptionIds.AvaSecondChance]));
        Assert.Equal("classic", Challenges.PresetFor(id => new GameOptions()[id])?.Id);
    }

    [Fact]
    public void A_shallow_death_wakes_you_in_town_once_and_the_next_one_is_for_real()
    {
        var game = NewGame();
        game.MarkDebugUsed();
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        game.Execute(new DebugJumpCommand(GameSession.SecondChanceDepth));
        game.Player.Timed.Set(TestData.Game.Timed("poisoned")!, 20);

        game.TakeHit(game.Player.Hp + 50, "a cave spider");

        Assert.False(game.Player.IsDead);
        Assert.True(game.Player.SecondChanceUsed);
        Assert.Equal(0, game.Player.Depth);
        Assert.Equal(Math.Max(1, game.Player.MaxHp / 3), game.Player.Hp);
        Assert.False(game.Player.Timed.Has("poisoned"));
        Assert.Contains(said, s => s.Contains("second chance"));
        Assert.Contains(game.History, h => h.Text.Contains("second chance"));
        Assert.Contains("Second chance: spent", CharacterDump.Build(game));

        game.TakeHit(game.Player.Hp + 50, "Grip, Farmer Maggot's Dog");
        Assert.True(game.Player.IsDead);
    }

    [Fact]
    public void Deeper_than_250_ft_or_without_the_option_death_is_death()
    {
        var deep = NewGame();
        deep.MarkDebugUsed();
        deep.Execute(new DebugJumpCommand(GameSession.SecondChanceDepth + 1));
        deep.TakeHit(deep.Player.Hp + 50, "an orc");
        Assert.True(deep.Player.IsDead);

        var plain = NewGame(secondChance: false);
        plain.TakeHit(plain.Player.Hp + 50, "a jackal");
        Assert.True(plain.Player.IsDead);
    }

    [Fact]
    public void Its_not_a_cheat_and_whether_its_spent_is_saved()
    {
        var game = NewGame();
        game.TakeHit(game.Player.Hp + 50, "a jackal");
        Assert.False(game.IsCheater);
        Assert.True(Feats.Counts(game));

        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.True(loaded.Player.SecondChanceUsed);
        Assert.False(loaded.SecondChanceReady);
    }
}
