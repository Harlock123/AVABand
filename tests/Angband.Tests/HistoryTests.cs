using Angband.Core.Definitions;
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

    // --- Artifacts missed and lost (history_lose_artifact) ---------------------------------------

    private static GameSession Born(bool loseArts)
    {
        var spec = CharacterSpec.Default("human", "warrior") with
        {
            Options = new Dictionary<string, bool> { [OptionIds.LoseArtifacts] = loseArts },
        };
        return GameSession.NewGame(TestData.Game, 21, spec);
    }

    private static void TakeStairsDown(GameSession game)
    {
        game.Player.Position = game.Level.FindFeature(TerrainFlags.DownStair).First();
        game.Execute(new TakeStairsCommand(Down: true));
    }

    /// <summary>Leaves the Phial on this level, somewhere the player has never seen.</summary>
    private static void HideThePhial(GameSession game)
    {
        var phial = game.Objects.CreateArtifact(TestData.Game.Artifacts.Single(a => a.Id == "galadriel"));
        var far = game.Level.AllLocs().First(p => game.Level.IsEmptyFloor(p)
            && !game.Level[p].Has(Angband.Core.World.SquareFlags.Seen) && !game.Known.IsKnown(p));
        game.Level.Objects.Add(far, phial);
    }

    [Fact]
    public void AnArtifactNeverFound_IsMissed_WhenLeftBehindForGood()
    {
        var game = Born(loseArts: true);
        TakeStairsDown(game);
        HideThePhial(game);
        TakeStairsDown(game);

        var missed = Assert.Single(game.History, h => h.Artifact == "galadriel");
        Assert.Equal("Missed the Phial of Galadriel", missed.Text);
        Assert.True(missed.Lost);
        Assert.Contains("Missed the Phial of Galadriel (LOST)", CharacterDump.Build(game));
    }

    [Fact]
    public void AnArtifactNeverFound_IsNotMissed_WhenItMayComeBack()
    {
        var game = Born(loseArts: false);
        TakeStairsDown(game);
        HideThePhial(game);
        TakeStairsDown(game);
        Assert.DoesNotContain(game.History, h => h.Artifact == "galadriel");
    }

    [Fact]
    public void AnArtifactFound_ThenLeftBehind_IsMarkedLost_AndStaysSoInTheSave()
    {
        var game = Born(loseArts: false);
        TakeStairsDown(game);
        var phial = game.Objects.CreateArtifact(TestData.Game.Artifacts.Single(a => a.Id == "galadriel"));
        game.Knowledge.LearnKind(phial.Kind);
        foreach (var rune in phial.Runes()) game.Knowledge.LearnRune(rune);
        game.Level.Objects.Add(game.Player.Position, phial);
        game.UpdateView();
        var found = Assert.Single(game.History, h => h.Artifact == "galadriel");
        Assert.StartsWith("Found ", found.Text, StringComparison.Ordinal);
        Assert.False(found.Lost);

        TakeStairsDown(game);
        var lost = Assert.Single(game.History, h => h.Artifact == "galadriel");
        Assert.True(lost.Lost);
        Assert.Equal(found.Text + " (LOST)", lost.Shown);

        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        Assert.Equal(game.History, SaveGame.Load(TestData.Game, stream).History);
    }

    [Fact]
    public void AnArtifactSoldOffByAShop_IsLost()
    {
        var game = Born(loseArts: false);
        var store = game.Stores["general"];
        var phial = game.Objects.CreateArtifact(TestData.Game.Artifacts.Single(a => a.Id == "galadriel"));
        store.Stock.Add(phial);
        for (var i = 0; i < 1000 && store.Stock.Contains(phial); i++) game.Maintain(store);

        Assert.DoesNotContain(phial, store.Stock);
        var missed = Assert.Single(game.History, h => h.Artifact == "galadriel");
        Assert.True(missed.Lost);
    }
}

/// <summary>The hotbar is part of the character.</summary>
public class HotbarTests
{
    [Fact]
    public void TheHotbar_IsSavedWithTheCharacter()
    {
        var game = GameSession.NewGame(TestData.Game, 1, "mage");
        game.SetHotbar(0, HotbarEntry.ForKind("cure_light_wounds"));
        game.SetHotbar(9, HotbarEntry.ForSpell("magic_missile"));
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal(game.Hotbar, loaded.Hotbar);
        Assert.Null(loaded.Hotbar[1]);
    }
}

/// <summary>Angband 4.2's equippable comparison.</summary>
public class EquipComparisonTests
{
    [Fact]
    public void AModifier_Shows_OnceItsRuneIsKnown_AndUnknownRunesReadAsQuestionMarks()
    {
        var game = GameSession.NewGame(TestData.Game, 1, "warrior");
        var ring = game.Objects.Create("ring_of_protection");
        ring.Modifiers["str"] = 2;
        game.Player.Inventory.Add(ring);
        var column = EquipComparison.Columns.ToList().FindIndex(c => c.Heading == "St");

        var row = EquipComparison.Rows(game).Single(r => r.Item == ring);
        Assert.Equal("pack", row.Source);
        Assert.Equal("?", row.Cells[column]);

        game.Knowledge.LearnRune(RuneIds.Modifier("str"));
        row = EquipComparison.Rows(game).Single(r => r.Item == ring);
        Assert.Equal("+2", row.Cells[column]);
        Assert.DoesNotContain(EquipComparison.Rows(game, slot: EquipSlot.Weapon), r => r.Item == ring);
    }
}

/// <summary>The journey: levels arrived on and kills, by depth, kept with the character.</summary>
public class JourneyTests
{
    [Fact]
    public void TheJourney_KeepsEachLevelAndKill_ByDepth_AndIsSaved()
    {
        var game = GameSession.NewGame(TestData.Game, 5, "warrior");
        Assert.Equal(0, Assert.Single(game.Visits).Depth); // the town, from the start
        game.MarkDebugUsed();
        game.Execute(new DebugJumpCommand(2));
        for (var i = 0; i < 20; i++) game.Execute(new HoldCommand());
        var monster = Arena.AddMonster(game, "grip", game.Level.AllLocs().First(l => game.Level.IsEmptyFloor(l)));
        game.DamageMonster(monster, 10_000);
        game.Execute(new DebugJumpCommand(4));
        game.Execute(new DebugJumpCommand(2));

        Assert.Equal([0, 2, 4, 2], game.Visits.Select(v => v.Depth));
        var kill = Assert.Single(game.Kills);
        Assert.Equal((2, "grip", true), (kill.Depth, kill.RaceId, kill.Unique));
        var byDepth = game.JourneyByDepth();
        var two = byDepth.Single(l => l.Depth == 2);
        Assert.Equal(2, two.Visits);
        Assert.Equal(1, two.Uniques);
        Assert.True(two.Turns > 0);
        Assert.Equal([0, 2, 4], byDepth.Select(l => l.Depth));

        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal(game.Visits, loaded.Visits);
        Assert.Equal(game.Kills, loaded.Kills);
    }
}
