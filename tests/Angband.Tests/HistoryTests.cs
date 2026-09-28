using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Persistence;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>The player's history (Angband player-history.c) and notes (':', cmd-misc.c do_cmd_note).</summary>
public class HistoryTests
{
    [Fact]
    public void ANewCharacter_BeginsTheQuest_AndLevelsAreLogged()
    {
        var game = GameSession.NewGame(TestData.Game, 1, "warrior");
        Assert.Equal("Began the quest to destroy Morgoth.", game.History[0].Text);
        game.GainExperience(game.ExperienceForLevel(2) - game.Player.Experience);
        Assert.Contains(game.History, h => h.Text == "Reached level 2");
        Assert.Contains(game.History, h => h.Text == "Reached level 3");
    }

    [Fact]
    public void KillingAUnique_AndFindingAnArtifact_AreLogged()
    {
        var game = Arena.Create(3, "#######", "#,,@,,#", "#######");
        TestGames.ClearMonsters(game);
        var grip = Arena.AddMonster(game, "grip", game.Player.Position + new Loc(1, 0));
        game.DamageMonster(grip, 10_000);
        Assert.Contains(game.History, h => h.Text == "Killed Grip, Farmer Maggot's Dog");

        var art = game.Objects.CreateArtifact(game.Data.Artifacts.First());
        game.Knowledge.LearnKind(art.Kind);
        foreach (var rune in art.Runes()) game.Knowledge.LearnRune(rune);
        game.Level.Objects.Add(game.Player.Position + new Loc(-1, 0), art);
        game.UpdateView();
        Assert.Single(game.History, h => h.Text.StartsWith("Found ", StringComparison.Ordinal));
        game.UpdateView();
        Assert.Single(game.History, h => h.Text.StartsWith("Found ", StringComparison.Ordinal)); // once
    }

    [Fact]
    public void Notes_SayAndMe_AreWrittenAsAngbandWritesThem()
    {
        var game = GameSession.NewGame(TestData.Game, 1, "warrior");
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        Assert.True(game.AddNote("found a vault on 12"));
        Assert.True(game.AddNote("/say Hello!"));
        Assert.True(game.AddNote("/me waves."));
        Assert.False(game.AddNote(""));
        Assert.False(game.AddNote(" leading space"));
        var name = game.Player.Name;
        Assert.Equal(["-- Note: found a vault on 12", $"-- {name} says: \"Hello!\"", $"-- {name} waves."],
            game.History.Skip(1).Select(h => h.Text));
        Assert.Contains("Note: found a vault on 12", said); // shown without the "-- "
    }

    [Fact]
    public void TheHistory_IsSaved_AndInTheDump()
    {
        var game = GameSession.NewGame(TestData.Game, 1, "warrior");
        game.AddNote("remember the ring");
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal(game.History, loaded.History);

        var dump = CharacterDump.Build(loaded);
        Assert.Contains("[Player history]", dump);
        Assert.Contains("      Turn   Depth  Note", dump);
        Assert.Contains("0'  -- Note: remember the ring", dump);
    }
}
