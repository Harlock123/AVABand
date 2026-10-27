using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Persistence;
using Angband.Core.Records;
using Angband.Core.World;

namespace Angband.Tests;

/// <summary>AVABand's messages from the fallen: last words on the headstone, and found later scratched into the floor where they fell.</summary>
public class FallenNoteTests
{
    private static GameData Data => TestData.Game;

    private static FallenNote Note(string name = "Arwen", int depth = 3, string words = "Beware the Grip.") => new()
    {
        Key = $"{name.ToLowerInvariant()}-1", Name = name, Race = "Elf", Class = "Mage", Depth = depth,
        KilledBy = "Grip, Farmer Maggot's Dog", Words = words, DateUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    private static GameSession Game(ulong seed = 5, bool notes = true, params FallenNote[] fallen)
    {
        var spec = CharacterSpec.Default("human", "warrior") with { Options = new Dictionary<string, bool> { [OptionIds.AvaFallenNotes] = notes } };
        var game = GameSession.NewGame(Data, seed, spec);
        game.UseGraveyardNotes(fallen);
        return game;
    }

    private static List<Loc> NotesHere(GameSession game) =>
        [.. game.Level.AllLocs().Where(p => game.Level.FeatureAt(p).Id == FallenNotes.Terrain)];

    /// <summary>New levels at a depth until a note is scratched into one (null if none is in so many tries).</summary>
    private static Loc? JumpUntilNote(GameSession game, int depth, int tries = 60)
    {
        for (var i = 0; i < tries; i++)
        {
            game.Execute(new DebugJumpCommand(depth));
            if (NotesHere(game) is [var at]) return at;
        }
        return null;
    }

    /// <summary>Walks onto a square from beside it (anything standing there, or beside it, moved off first).</summary>
    private static void StepOnto(GameSession game, Loc at)
    {
        var level = game.Level;
        var from = DirectionExtensions.Compass.Select(d => at.Step(d))
            .First(p => level.IsPassable(p) && level[p].Trap == 0 && !level.IsDoor(p) && level.FeatureAt(p).Id != FallenNotes.Terrain);
        foreach (var p in new[] { at, from })
            if (level.Monsters.At(p) is { } m)
                level.Monsters.Move(m, level.AllLocs().First(l => level.IsEmptyFloor(l) && l.DistanceTo(at) > 5 && l != game.Player.Position));
        game.Player.Position = from;
        game.Execute(new WalkCommand(DirectionExtensions.FromOffset(at.X - from.X, at.Y - from.Y)));
        Assert.Equal(at, game.Player.Position);
    }

    // --- The headstone --------------------------------------------------------------------------------

    [Fact]
    public void LastWords_AreKeptOnTheHeadstone_AndAnOldGraveyardLoads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "avaband-notes-" + Guid.NewGuid());
        try
        {
            var path = Path.Combine(dir, "graveyard.json");
            var yard = new Graveyard();
            var stone = new FallenRecord { Name = "Arwen", Race = "Elf", Class = "Mage", Depth = 3, MaxDepth = 3, KilledBy = "a cave orc", Seed = 9,
                DateUtc = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc) };
            yard.Add(stone);
            Assert.True(yard.SetLastWords(stone.Key, "Beware the cave orc."));
            Assert.False(yard.SetLastWords("nobody-1", "Hello."));
            yard.Save(path);
            Assert.Equal("Beware the cave orc.", Assert.Single(Graveyard.Load(path).Fallen).LastWords);

            // A graveyard from before there were last words.
            File.WriteAllText(path, """{"Fallen":[{"Name":"Old","Race":"Human","Class":"Warrior","Depth":4,"KilledBy":"a jackal","Epitaph":"Gone."}],"Backfilled":true}""");
            var old = Assert.Single(Graveyard.Load(path).Fallen);
            Assert.Null(old.LastWords);
            Assert.Equal("Gone.", old.Epitaph);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TheGraveyard_SendsTheNewestLastWords_ButNotWinners_TownDeaths_OrStonesWithout()
    {
        var yard = new Graveyard();
        FallenRecord Stone(string name, int depth, string? words, bool won = false, int day = 1) => new()
        {
            Name = name, Race = "Human", Class = "Mage", Level = 10, Depth = depth, MaxDepth = depth, Won = won, Seed = (ulong)day, LastWords = words,
            KilledBy = won ? "retired victorious" : "a cave spider", DateUtc = new DateTime(2026, 9, day, 0, 0, 0, DateTimeKind.Utc),
        };
        yard.Add(Stone("Winner", 100, "I won!", won: true, day: 1));
        yard.Add(Stone("Townie", 0, "The town!", day: 2));
        yard.Add(Stone("Older", 4, "Turn back.", day: 3));
        yard.Add(Stone("Silent", 7, null, day: 4));
        yard.Add(Stone("Blank", 7, "   ", day: 5));
        yard.Add(Stone("Newest", 9, "Treasure ahead.", day: 6));
        Assert.Equal(["Newest", "Older"], FallenNotes.FromGraveyard(yard).Select(n => n.Name));
        Assert.Equal(["Newest"], FallenNotes.FromGraveyard(yard, max: 1).Select(n => n.Name));
        Assert.Equal("Treasure ahead.", FallenNotes.FromGraveyard(yard)[0].Words);
    }

    [Fact]
    public void LastWords_AreOneShortLine_AndTheReadyMadePhrases_NameWhatKilledYou()
    {
        Assert.Null(FallenNotes.Clean(null));
        Assert.Null(FallenNotes.Clean("  \n\t "));
        Assert.Equal("Turn back. Now.", FallenNotes.Clean("  Turn back.\n\n  Now.  "));
        Assert.Equal(FallenNotes.MaxLength, FallenNotes.Clean(new string('a', 200))!.Length);

        var phrases = FallenNotes.Phrases("a cave orc");
        Assert.Equal("Beware the cave orc.", phrases[0]);
        Assert.Equal("Trust not the cave orc.", phrases[1]);
        Assert.Contains("Turn back. The way is shut.", phrases);
        Assert.Contains("Treasure ahead.", phrases);
        Assert.All(phrases, p => Assert.InRange(p.Length, 1, FallenNotes.MaxLength));
        Assert.Equal("Beware Grip, Farmer Maggot's Dog.", FallenNotes.Phrases("Grip, Farmer Maggot's Dog")[0]);
        Assert.Equal("Scratched into the floor: 'Beware the Grip.' — Arwen the Elf Mage, killed here by Grip, Farmer Maggot's Dog.",
            FallenNotes.Text(Note()));
    }

    [Fact]
    public void TheCharacterDump_HasTheLastWords()
    {
        var game = Game(seed: 3);
        game.TakeHit(100_000, "a cave orc");
        Assert.DoesNotContain("Last words:", CharacterDump.Build(game));
        game.LastWords = "Beware the cave orc.";
        var dump = CharacterDump.Build(game);
        Assert.Contains(" Last words: \"Beware the cave orc.\"", dump);
        Assert.True(dump.IndexOf(" Last words: ", StringComparison.Ordinal) > dump.IndexOf(" Killed by a cave orc", StringComparison.Ordinal));

        // A dump written before they were left: put in under how they died (once, however many times).
        game.LastWords = null;
        var written = CharacterDump.Build(game);
        var amended = CharacterDump.WithLastWords(CharacterDump.WithLastWords(written, "Turn back."), "Remember me.");
        var lines = amended.Split('\n');
        var killed = Array.FindIndex(lines, l => l.StartsWith(" Killed by ", StringComparison.Ordinal));
        Assert.Equal(" Last words: \"Remember me.\"", lines[killed + 1]);
        Assert.Single(lines, l => l.StartsWith(" Last words: ", StringComparison.Ordinal));
    }

    // --- Finding them ---------------------------------------------------------------------------------

    [Fact]
    public void TheBirthOption_IsOnByDefault()
    {
        var option = OptionCatalog.Find(OptionIds.AvaFallenNotes)!;
        Assert.Equal(OptionKind.Birth, option.Kind);
        Assert.True(option.Default);
        Assert.True(GameSession.NewGame(Data, 3).FallenNotesOn);
        var terrain = Data.Terrain[FallenNotes.Terrain];
        Assert.True(terrain.Has(TerrainFlags.Passable));
        Assert.True(terrain.Has(TerrainFlags.Interesting));
    }

    [Fact]
    public void ANote_IsFoundOnlyAtTheDepthItsWriterFellAt_InARoom_OneALevel()
    {
        var game = Game(seed: 7, fallen: [Note("Ann", 3), Note("Bob", 3), Note("Cid", 5)]);
        for (var i = 0; i < 30; i++)
        {
            game.Execute(new DebugJumpCommand(i % 2 == 0 ? 2 : 4));
            Assert.Empty(NotesHere(game));                                  // never at a depth they didn't fall at
        }
        for (var i = 0; i < 30; i++)
        {
            game.Execute(new DebugJumpCommand(3));
            var here = NotesHere(game);
            Assert.True(here.Count <= 1);                                   // one a level, though two fell here
            if (here is not [var at]) continue;
            Assert.True(game.Level[at].Has(SquareFlags.Room) || game.Level.AllLocs().All(p => !game.Level[p].Has(SquareFlags.Room)));
            Assert.Contains(game.FallenNoteAt(at)!.Name, new[] { "Ann", "Bob" });
            Assert.Equal(game.Level.FallenNotes[at], game.FallenNoteAt(at)!.Key);
        }
    }

    [Fact]
    public void ANote_IsFound_AboutOneTimeInThree()
    {
        var game = Game(seed: 11, fallen: [Note()]);
        var found = 0;
        const int levels = 240;
        for (var i = 0; i < levels; i++)
        {
            game.Execute(new DebugJumpCommand(3));
            if (NotesHere(game).Count > 0) found++;
        }
        Assert.InRange(found, levels / 3 - 30, levels / 3 + 30);
    }

    [Fact]
    public void SteppingOnANote_ReadsIt_TheLogKeepsIt_AndItIsNeverFoundAgain()
    {
        var game = Game(seed: 13, fallen: [Note()]);
        var messages = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));
        var at = JumpUntilNote(game, 3)!.Value;
        StepOnto(game, at);
        Assert.Contains(FallenNotes.Text(Note()), messages);
        Assert.Equal(["arwen-1"], game.FloorNotes.Read);
        Assert.Contains(game.History, h => h.Text == "Read the last words of Arwen");

        // Stepping on it again shows it again, but it is read once.
        StepOnto(game, at);
        Assert.Equal(2, messages.Count(m => m == FallenNotes.Text(Note())));
        Assert.Single(game.FloorNotes.Read);

        // Never found again by this character (nor handed in again from the graveyard).
        Assert.Null(JumpUntilNote(game, 3, tries: 40));
        game.UseGraveyardNotes([Note()]);
        Assert.Empty(game.FloorNotes.Fallen);
    }

    [Fact]
    public void LookingAtANote_ReadsIt_IfYouCanSeeIt()
    {
        var game = Game(seed: 13, fallen: [Note()]);
        var at = JumpUntilNote(game, 3)!.Value;
        var messages = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));
        game.Player.Position = game.Level.AllLocs().First(p => p.DistanceTo(at) > 30 && game.Level.IsEmptyFloor(p));
        game.UpdateView();
        game.LookAtFallenNote(at);                                          // (out of sight: nothing to read)
        Assert.Empty(game.FloorNotes.Read);

        game.Player.Position = DirectionExtensions.Compass.Select(d => at.Step(d)).First(p => game.Level.IsPassable(p));
        game.UpdateView();
        Assert.True(game.Level[at].Has(SquareFlags.Seen));
        game.LookAtFallenNote(at);
        game.LookAtFallenNote(at);
        Assert.Single(messages, FallenNotes.Text(Note()));
        Assert.Single(game.FloorNotes.Read);
    }

    [Fact]
    public void NoNotesAreFound_WithTheOptionOff_InTheDailyDungeon_TheWeeklyChallenge_OrTheTutorial()
    {
        var off = Game(seed: 7, notes: false, fallen: [Note()]);
        Assert.False(off.FallenNotesOn);
        Assert.Empty(off.FloorNotes.Fallen);                                // (nothing kept in the save either)
        Assert.Null(JumpUntilNote(off, 3, tries: 30));

        var daily = Game(seed: 7);
        daily.Player.DailyDate = "2026-10-26";
        daily.UseGraveyardNotes([Note()]);
        Assert.False(daily.FallenNotesOn);
        Assert.Empty(daily.FloorNotes.Fallen);
        Assert.Null(JumpUntilNote(daily, 3, tries: 30));

        var weekly = Game(seed: 7);
        weekly.Player.WeeklyWeek = "2026-W44";
        weekly.UseGraveyardNotes([Note()]);
        Assert.False(weekly.FallenNotesOn);
        Assert.Null(JumpUntilNote(weekly, 3, tries: 30));

        Assert.False(Tutorial.Create(Data).FallenNotesOn);
    }

    [Fact]
    public void ThisCharacter_NeverFindsItsOwnWords()
    {
        var game = Game(seed: 9);
        var self = Note(game.Player.Name);
        self.Key = $"{game.Seed:x}-{DateTime.UtcNow.Ticks:x}";
        game.UseGraveyardNotes([self, Note("Other")]);
        Assert.Equal(["Other"], game.FloorNotes.Fallen.Select(n => n.Name));
    }

    [Fact]
    public void ANote_IsKeptInTheSave_WithEverythingItNeeds()
    {
        var game = Game(seed: 15, fallen: [Note(), Note("Later", depth: 9)]);
        var at = JumpUntilNote(game, 3)!.Value;
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(Data, stream);
        Assert.Equal([at], NotesHere(loaded));
        Assert.Equal("Arwen", loaded.FallenNoteAt(at)?.Name);
        Assert.Equal(["arwen-1", "later-1"], loaded.FloorNotes.Fallen.Select(n => n.Key));

        // Handed a graveyard without it, the note scratched into this level stays readable.
        loaded.UseGraveyardNotes([Note("Newcomer", depth: 4)]);
        Assert.Equal(["arwen-1", "newcomer-1"], loaded.FloorNotes.Fallen.Select(n => n.Key));
        StepOnto(loaded, at);
        Assert.Equal(["arwen-1"], loaded.FloorNotes.Read);

        using var again = new MemoryStream();
        SaveGame.Save(loaded, again);
        again.Position = 0;
        Assert.Equal(["arwen-1"], SaveGame.Load(Data, again).FloorNotes.Read);
    }

    [Fact]
    public void AReplay_FindsTheSameNote_WithoutTheGraveyard()
    {
        var game = Game(seed: 17, fallen: [Note(), Note("Bob", depth: 4)]);
        game.Recorder = new ReplayRecorder(game);                         // (as the app does: after the graveyard is handed in)
        var at = JumpUntilNote(game, 3)!.Value;
        for (var i = 0; i < 5; i++) game.Execute(new DebugJumpCommand(4));
        var file = game.Recorder.ToFile(game);
        Assert.Equal(4, file.Version);

        var player = new ReplayPlayer(Data, file);
        var found = new List<(int Depth, Loc At)>();
        while (player.Step())
            if (NotesHere(player.Game) is [var n]) found.Add((player.Game.Level.Depth, n));
        Assert.True(player.Matches);
        Assert.Contains((3, at), found);
        Assert.Equal(game.Level.FallenNotes.Keys, player.Game.Level.FallenNotes.Keys);

        // A replay recorded before there were notes finds none.
        file.Version = 3;
        var older = new ReplayPlayer(Data, file);
        Assert.False(older.Game.FallenNotesOn);
        while (older.Step()) Assert.Empty(NotesHere(older.Game));
    }

    [Fact]
    public void ANoteReadByLooking_IsReadInTheReplayToo()
    {
        var game = Game(seed: 17, fallen: [Note()]);
        var at = JumpUntilNote(game, 3)!.Value;
        game.Player.Position = DirectionExtensions.Compass.Select(d => at.Step(d)).First(p => game.Level.IsPassable(p) && game.Level.Monsters.At(p) is null);
        game.UpdateView();
        game.Recorder = new ReplayRecorder(game);
        game.LookAtFallenNote(at);
        game.Execute(new HoldCommand());
        var file = game.Recorder.ToFile(game);

        var player = new ReplayPlayer(Data, file);
        Assert.Empty(player.Game.FloorNotes.Read);
        while (player.Step()) { }
        Assert.True(player.Matches);
        Assert.Equal(["arwen-1"], player.Game.FloorNotes.Read);
    }

    [Fact]
    public void TheFeat_HeededTheFallen_IsTenNotesRead()
    {
        var feat = Feats.All(Data).Single(f => f.Id == "fallen_notes_10");
        Assert.Equal("Heeded the Fallen", feat.Name);
        var game = Game(seed: 3);
        for (var i = 0; i < 9; i++) game.FloorNotes.Read.Add($"n{i}");
        Assert.False(feat.Earned(game));
        game.FloorNotes.Read.Add("n9");
        Assert.True(feat.Earned(game));
    }
}
