using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Persistence;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>AVABand's ghosts of fallen characters: who walks, where, how strong, what they leave, and what is kept.</summary>
public class GhostTests
{
    private static GameData Data => TestData.Game;

    private static FallenGhost Fallen(string name = "Aragorn", int depth = 3, int level = 12, string cls = "Warrior",
        List<string>? gear = null, bool randarts = false) => new()
    {
        Key = $"{name.ToLowerInvariant()}-1", Name = name, Race = "Dwarf", Class = cls, Level = level, Depth = depth,
        KilledBy = "Grip, Farmer Maggot's Dog", DateUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), Equipment = gear, Randarts = randarts,
    };

    private static GameSession Game(ulong seed = 5, bool ghosts = true, params FallenGhost[] fallen)
    {
        var spec = CharacterSpec.Default("human", "warrior") with { Options = new Dictionary<string, bool> { [OptionIds.AvaGhosts] = ghosts } };
        var game = GameSession.NewGame(Data, seed, spec);
        game.UseGraveyard(fallen);
        return game;
    }

    private static IEnumerable<Monster> GhostsHere(GameSession game) =>
        game.Level.Monsters.All.Where(m => m.Race.Id.StartsWith(FallenGhosts.RacePrefix, StringComparison.Ordinal));

    /// <summary>New levels at a depth until a ghost walks on one (null if none does in so many tries).</summary>
    private static Monster? JumpUntilGhost(GameSession game, int depth, int tries = 80)
    {
        for (var i = 0; i < tries; i++)
        {
            game.Execute(new DebugJumpCommand(depth));
            if (GhostsHere(game).FirstOrDefault() is { } ghost) return ghost;
        }
        return null;
    }

    private static string Gear(Item item) => SaveGame.ItemToJson(item);

    [Fact]
    public void TheBirthOption_IsOnByDefault()
    {
        var option = OptionCatalog.Find(OptionIds.AvaGhosts)!;
        Assert.Equal(OptionKind.Birth, option.Kind);
        Assert.True(option.Default);
        Assert.True(GameSession.NewGame(Data, 3).GhostsOn);
    }

    [Fact]
    public void TheGraveyard_SendsTheNewestFallen_ButNotWinners_TownDeaths_OrThoseLaidToRest()
    {
        var yard = new Graveyard();
        FallenRecord Stone(string name, int depth, bool won = false, int day = 1) => new()
        {
            Name = name, Race = "Human", Class = "Mage", Level = 10, Depth = depth, MaxDepth = depth, Won = won, Seed = (ulong)day,
            KilledBy = won ? "retired victorious" : "a cave spider", DateUtc = new DateTime(2026, 9, day, 0, 0, 0, DateTimeKind.Utc),
        };
        yard.Add(Stone("Winner", 100, won: true, day: 1));
        yard.Add(Stone("Townie", 0, day: 2));
        yard.Add(Stone("Older", 4, day: 3));
        yard.Add(Stone("Rested", 7, day: 4));
        yard.Add(Stone("Newest", 9, day: 5));
        Assert.True(yard.LayToRest(yard.Fallen.Single(f => f.Name == "Rested").Key, "Bob the Elf Mage", new DateTime(2026, 10, 2)));
        Assert.False(yard.LayToRest(yard.Fallen.Single(f => f.Name == "Rested").Key, "Ann", DateTime.UtcNow)); // (once)

        Assert.Equal(["Newest", "Older"], FallenGhosts.FromGraveyard(yard).Select(f => f.Name));
        Assert.Equal(["Newest"], FallenGhosts.FromGraveyard(yard, max: 1).Select(f => f.Name));
        var rested = yard.Fallen.Single(f => f.Name == "Rested");
        Assert.Equal("Bob the Elf Mage", rested.LaidToRestBy);
        Assert.Equal(new DateTime(2026, 10, 2), rested.LaidToRestUtc);
    }

    [Fact]
    public void AHeadstone_KeepsWhatTheCharacterWore_AndWhetherItsArtifactsWereRandom_ThroughTheFile()
    {
        var game = GameSession.NewGame(Data, 4, "warrior");
        var record = new FallenRecord { Name = "Bob", Seed = 4, DateUtc = new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc) }.KeepGear(game);
        Assert.Equal(game.Player.Inventory.Equipped.Count(), record.Equipment!.Count);
        Assert.False(record.Randarts);
        var path = Path.Combine(Path.GetTempPath(), $"avaband-ghost-graves-{Guid.NewGuid():N}.json");
        try
        {
            var yard = new Graveyard();
            yard.Add(record);
            yard.LayToRest(record.Key, "Ann the Hobbit Rogue", new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc));
            yard.Save(path);
            var back = Graveyard.Load(path).Fallen.Single();
            Assert.Equal(record.Equipment, back.Equipment);
            Assert.Equal(record.Key, back.Key);
            Assert.Equal("Ann the Hobbit Rogue", back.LaidToRestBy);
            Assert.Null(FallenRecord.From(new ScoreEntry { Name = "Old" }).Equipment); // (older stones have none)
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AGhost_IsAToughUniqueThatWalksThroughWalls_StrongerForADeeperOrHigherLevelCharacter()
    {
        var race = FallenGhosts.BuildRace(Data, Fallen(level: 23, depth: 12));
        Assert.Equal("The Ghost of Aragorn", race.Name);
        Assert.Equal('G', race.Glyph);
        Assert.Equal("ghost", race.TileAs);
        Assert.True(race.IsUnique);
        Assert.True(race.Has(MonsterFlags.PassWall));
        Assert.True(race.Has(MonsterFlags.Undead));
        Assert.False(race.Has(MonsterFlags.Invisible));                    // spectral, but seen
        Assert.Equal(12 + 3 + (23 - 12) / 4, race.Depth);
        Assert.Contains(race.Blows, b => b.Effect == "cold");
        Assert.Contains("Grip, Farmer Maggot's Dog", race.Description);
        Assert.Empty(race.Spells);                                         // a warrior casts nothing

        // Deeper and higher, tougher; a warrior has more life than a mage of the same level, and faster deep down.
        var shallow = FallenGhosts.BuildRace(Data, Fallen(level: 10, depth: 5));
        var deep = FallenGhosts.BuildRace(Data, Fallen(level: 40, depth: 45));
        Assert.True(deep.HitPoints > shallow.HitPoints * 10);
        Assert.True(deep.Speed > shallow.Speed);
        Assert.True(deep.Armour > shallow.Armour);
        Assert.True(FallenGhosts.LevelOf(Fallen(level: 40, depth: 12)) > FallenGhosts.LevelOf(Fallen(level: 12, depth: 12)));
        var mage = FallenGhosts.BuildRace(Data, Fallen(cls: "Mage", level: 23, depth: 12));
        Assert.True(race.HitPoints > mage.HitPoints);
        Assert.NotEqual(race.Color, mage.Color);
        // At the depth it fell at, a ghost has the life of a unique there (Bullroarer's 200 at 500 ft, say).
        Assert.InRange(FallenGhosts.BuildRace(Data, Fallen(level: 12, depth: 7)).HitPoints, 150, 400);
    }

    [Fact]
    public void ACastersGhost_CastsSomeOfItsRealmsSpells()
    {
        var mage = FallenGhosts.BuildRace(Data, Fallen(cls: "Mage", level: 23));
        Assert.Contains("BLINK", mage.Spells);
        Assert.Contains("BO_FIRE", mage.Spells);
        Assert.True(mage.SpellFrequency > 0);
        Assert.True(mage.Has(MonsterFlags.Smart));
        var priest = FallenGhosts.BuildRace(Data, Fallen(cls: "Priest", level: 10));
        Assert.Contains("HEAL", priest.Spells);
        Assert.Contains("SCARE", priest.Spells);
        var necro = FallenGhosts.BuildRace(Data, Fallen(cls: "Necromancer", level: 20));
        Assert.Contains("BO_NETH", necro.Spells);
        Assert.Contains(necro.Blows, b => b.Effect.StartsWith("exp_", StringComparison.Ordinal));
        Assert.Contains("ARROW", FallenGhosts.BuildRace(Data, Fallen(cls: "Ranger", level: 15)).Spells);
        var multi = FallenGhosts.BuildRace(Data, Fallen(cls: "Warrior/Mage", level: 30));
        Assert.NotEmpty(multi.Spells);
        Assert.True(multi.Blows.Count >= 4);
        // Every spell a ghost may cast is one the game knows.
        foreach (var cls in Data.Classes.Concat(Data.Multiclasses))
        foreach (var spell in FallenGhosts.BuildRace(Data, Fallen(cls: cls.Name, level: 50, depth: 60)).Spells)
            Assert.NotNull(Data.MonsterSpell(spell));
    }

    [Fact]
    public void AGhost_WalksOnlyAtTheDepthItFellAt_OncePerCharacter_OneALevel_OutOfSightOfTheStairs()
    {
        var game = Game(seed: 7, fallen: [Fallen("Ann", depth: 3), Fallen("Bob", depth: 3), Fallen("Cid", depth: 5)]);
        for (var i = 0; i < 40; i++)
        {
            game.Execute(new DebugJumpCommand(i % 2 == 0 ? 2 : 4));
            Assert.Empty(GhostsHere(game));                                // never at a depth they didn't fall at
        }
        var ghost = JumpUntilGhost(game, 3);
        Assert.NotNull(ghost);
        var arrival = game.Player.Position;
        Assert.Single(GhostsHere(game));                                   // one a level, though two fell here
        Assert.True(ghost.Position.DistanceTo(arrival) > 10);
        Assert.False(ProjectionPath.Projectable(game.Level, arrival, ghost.Position, Data.Constants.MaxSight));
        Assert.Single(game.Ghosts.Met);

        // The other one may walk later; each only once for this character.
        Assert.NotNull(JumpUntilGhost(game, 3));
        Assert.Equal(2, game.Ghosts.Met.Count);
        Assert.Null(JumpUntilGhost(game, 3, tries: 60));
    }

    [Fact]
    public void NoGhostsWalk_WithTheOptionOff_InTheDailyDungeon_OrTheTutorial()
    {
        var off = Game(seed: 7, ghosts: false, fallen: [Fallen(depth: 3)]);
        Assert.False(off.GhostsOn);
        Assert.Empty(off.Ghosts.Fallen);                                   // (nothing kept in the save either)
        Assert.Null(JumpUntilGhost(off, 3, tries: 40));

        var daily = Game(seed: 7);
        daily.Player.DailyDate = "2026-10-26";
        daily.UseGraveyard([Fallen(depth: 3)]);
        Assert.False(daily.GhostsOn);
        Assert.Null(JumpUntilGhost(daily, 3, tries: 40));

        Assert.False(Tutorial.Create(Data).GhostsOn);
    }

    [Fact]
    public void ThisCharacter_IsNeverItsOwnGhost_AndAGhostLaidToRest_IsNotHandedIn()
    {
        var game = Game(seed: 9);
        var self = Fallen(game.Player.Name);
        self.Key = $"{game.Seed:x}-{DateTime.UtcNow.Ticks:x}";
        game.Ghosts.Laid.Add("gone-1");
        var gone = Fallen("Gone");
        game.UseGraveyard([self, gone, Fallen("Other")]);
        Assert.Equal(["Other"], game.Ghosts.Fallen.Select(f => f.Name));
    }

    [Fact]
    public void AGhostSeen_Wails_OfWhatKilledIt()
    {
        var game = Arena.Create(3,
            "###########",
            "#,,,,,,,,,#",
            "#,,@,,,,,,#",
            "#,,,,,,,,,#",
            "###########");
        game.UseGraveyard([Fallen()]);
        var messages = new List<string>();
        using var _ = game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));
        new MonsterSpawner(Data).Place(game.Level, game.Rng, game.MonsterRace(FallenGhosts.RacePrefix + "aragorn-1")!, game.Player.Position.Step(Angband.Core.Geometry.Direction.East).Step(Angband.Core.Geometry.Direction.East));
        game.UpdateView();
        Assert.Contains(messages, m => m.StartsWith("The Ghost of Aragorn wails: \"", StringComparison.Ordinal));
        Assert.Contains(FallenGhosts.Wail(Fallen()), messages.Single(m => m.Contains("wails")));
    }

    /// <summary>A ghost found and beaten; returns the things it let fall (and the events).</summary>
    private static (List<Item> Dropped, List<IGameEvent> Events) LayToRest(GameSession game, int depth = 3)
    {
        var ghost = JumpUntilGhost(game, depth) ?? throw new InvalidOperationException("no ghost walked");
        var before = game.Level.Objects.All.Select(o => o.Item.Serial).ToHashSet();
        var events = new List<IGameEvent>();
        using var _ = game.Events.SubscribeAll(events.Add);
        game.ProjectileHitsMonster(ghost, "test", null, 1_000_000);
        Assert.False(ghost.IsActive);
        return ([.. game.Level.Objects.All.Select(o => o.Item).Where(i => !before.Contains(i.Serial))], events);
    }

    [Fact]
    public void AGhostBeaten_IsLaidToRest_AndLetsFallOneOfTheThingsItWore_AndGold()
    {
        var dead = GameSession.NewGame(Data, 1, "warrior");
        var sword = dead.Objects.Create("long_sword");
        (sword.ToHit, sword.ToDam) = (7, 5);
        sword.Note = "@w1";
        var game = Game(seed: 11, fallen: [Fallen(gear: [Gear(sword), Gear(dead.Objects.Create("wooden_torch"))])]);
        var (dropped, events) = LayToRest(game);

        var reward = Assert.Single(dropped, i => !i.IsGold);
        Assert.Equal("long_sword", reward.Kind.Id);                        // (never the torch)
        Assert.Equal((7, 5), (reward.ToHit, reward.ToDam));
        Assert.Null(reward.Note);
        Assert.Equal(2, dropped.Count(i => i.IsGold));
        Assert.Contains("aragorn-1", game.Ghosts.Laid);
        Assert.Contains(events, e => e is GhostLaidToRestEvent { Key: "aragorn-1", Name: "Aragorn" });
        Assert.Contains(events, e => e is MessageEvent { Text: "Aragorn the Dwarf Warrior is laid to rest at last." });
        Assert.Contains(FallenGhosts.RacePrefix + "aragorn-1", game.KilledUniques);
        Assert.Contains(game.History, h => h.Text == "Killed The Ghost of Aragorn");
        Assert.Contains("The Ghost of Aragorn", CharacterDump.Build(game));
        Assert.Contains(game.TopKills(), k => k.Name == "The Ghost of Aragorn");

        // Feats for it.
        var feats = Feats.All(Data);
        Assert.True(feats.Single(f => f.Id == "ghost").Earned(game));
        Assert.False(feats.Single(f => f.Id == "ghosts_3").Earned(game));
    }

    [Fact]
    public void AnArtifactItWore_ComesAsItself_IfThisGameHasntMadeIt_ElseAsAnEgoOfItsKind()
    {
        var art = Data.Artifacts.First(a => Data.Object(a.Kind) is { Base: "sword" } k && !k.IsSpecialArtifactKind);
        var dead = GameSession.NewGame(Data, 1, "warrior");
        var gear = new List<string> { Gear(dead.Objects.CreateArtifact(art)) };

        var fresh = Game(seed: 11, fallen: [Fallen(gear: gear)]);
        var (dropped, _) = LayToRest(fresh);
        var item = Assert.Single(dropped, i => !i.IsGold);
        Assert.Equal(art.Id, item.Artifact?.Id);
        Assert.Contains(art.Id, fresh.Objects.CreatedArtifacts);

        var made = Game(seed: 11, fallen: [Fallen(gear: gear)]);
        made.Objects.CreatedArtifacts.Add(art.Id);
        (dropped, _) = LayToRest(made);
        item = Assert.Single(dropped, i => !i.IsGold);
        Assert.Null(item.Artifact);
        Assert.Equal(art.Kind, item.Kind.Id);
        Assert.NotNull(item.Ego);

        // From a game with random artifacts, its artifact was its own: an ego too.
        var random = Game(seed: 11, fallen: [Fallen(gear: gear, randarts: true)]);
        (dropped, _) = LayToRest(random);
        item = Assert.Single(dropped, i => !i.IsGold);
        Assert.Null(item.Artifact);
        Assert.Equal(art.Kind, item.Kind.Id);
    }

    [Fact]
    public void AnOlderHeadstone_WithoutItsGear_GivesAGreatThingSuitedToItsClass()
    {
        var game = Game(seed: 11, fallen: [Fallen(cls: "Mage", level: 20)]);
        var (dropped, _) = LayToRest(game);
        var item = Assert.Single(dropped, i => !i.IsGold);
        Assert.Contains(item.Kind.Base, FallenGhosts.RewardBases("mage"));
    }

    [Fact]
    public void TheSameGame_LaysTheSameGhostToRest_TheSameWay()
    {
        string Run()
        {
            var game = Game(seed: 13, fallen: [Fallen(cls: "Priest", level: 25)]);
            var (dropped, _) = LayToRest(game);
            return string.Join(";", dropped.Select(SaveGame.ItemToJson)) + game.Player.Position;
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void AGhost_IsKeptInTheSave_WithEverythingItNeeds()
    {
        var sword = GameSession.NewGame(Data, 1, "warrior").Objects.Create("long_sword");
        var game = Game(seed: 15, fallen: [Fallen(gear: [Gear(sword)]), Fallen("Later", depth: 9)]);
        var ghost = JumpUntilGhost(game, 3)!;
        Assert.NotNull(ghost);
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(Data, stream);

        var back = Assert.Single(GhostsHere(loaded));
        Assert.Equal(ghost.Race.Name, back.Race.Name);
        Assert.Equal(ghost.Race.HitPoints, back.Race.HitPoints);
        Assert.Equal((ghost.Position, ghost.Hp), (back.Position, back.Hp));
        Assert.Equal(game.Ghosts.Met, loaded.Ghosts.Met);
        Assert.Equal(["aragorn-1", "later-1"], loaded.Ghosts.Fallen.Select(f => f.Key));

        // Handed a graveyard without it (laid to rest elsewhere meanwhile), the ghost met stays known.
        loaded.UseGraveyard([Fallen("Newcomer", depth: 4)]);
        Assert.Equal(["aragorn-1", "newcomer-1"], loaded.Ghosts.Fallen.Select(f => f.Key));
        loaded.ProjectileHitsMonster(back, "test", null, 1_000_000);
        Assert.Contains(loaded.Level.Objects.All, o => o.Item.Kind.Id == "long_sword");
    }

    [Fact]
    public void AReplay_BringsTheSameGhost_WithoutTheGraveyard()
    {
        var game = Game(seed: 17, fallen: [Fallen(cls: "Mage", level: 20), Fallen("Bob", depth: 4)]);
        game.Recorder = new ReplayRecorder(game);                        // (as the app does: after the graveyard is handed in)
        var ghost = JumpUntilGhost(game, 3)!;
        Assert.NotNull(ghost);
        for (var i = 0; i < 30; i++) game.Execute(new HoldCommand());
        var file = game.Recorder.ToFile(game);

        var player = new ReplayPlayer(Data, file);
        while (player.Step()) { }
        Assert.True(player.Matches);
        var replayed = Assert.Single(GhostsHere(player.Game));
        var original = Assert.Single(GhostsHere(game));
        Assert.Equal((original.Race.Id, original.Position, original.Hp), (replayed.Race.Id, replayed.Position, replayed.Hp));
        Assert.Equal(game.Ghosts.Met, player.Game.Ghosts.Met);
    }

    [Fact]
    public void AnOlderSave_HasNoGhosts_AndPlaysAsBefore()
    {
        var game = Game(seed: 19);
        Assert.Empty(game.Ghosts.Fallen);
        Assert.Null(JumpUntilGhost(game, 3, tries: 20));
        Assert.Null(game.MonsterRace(FallenGhosts.RacePrefix + "nobody"));
        Assert.Equal("jackal", game.MonsterRace("jackal")?.Id);
    }
}
