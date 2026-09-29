using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Generation;
using Angband.Core.Geometry;
using Angband.Core.Randomness;
using Angband.Core.World;

namespace Angband.Tests;

/// <summary>
/// Angband 4.2.5's level generation (generate.c, gen-cave.c, gen-room.c): its nine profiles and
/// how they are chosen, gauntlets, vaults, pits, quest levels and persistent joins.
/// </summary>
public class Generation425Tests
{
    private static readonly DungeonGenerator Generator = new(TestData.Game);

    [Fact]
    public void Profiles_are_chosen_as_generate_c_does()
    {
        var rng = new GameRandom(9);
        var seen = new Dictionary<int, HashSet<string>>();
        foreach (var depth in new[] { 5, 12, 25, 60 })
        {
            seen[depth] = [];
            for (var i = 0; i < 3000; i++) seen[depth].Add(Generator.ChooseProfile(rng, depth).Name);
        }
        Assert.Equal(new HashSet<string> { "classic", "modified" }, seen[5]); // nothing else is deep enough
        Assert.Contains("moria", seen[12]);                                  // 10-39, one in forty
        Assert.DoesNotContain("labyrinth", seen[12]);                        // not before 13
        Assert.Contains("labyrinth", seen[25]);
        Assert.Contains("lair", seen[25]);
        Assert.Contains("gauntlet", seen[25]);
        Assert.DoesNotContain("hard centre", seen[25]);                      // from 50
        Assert.Contains("hard centre", seen[60]);
        Assert.DoesNotContain("moria", seen[60]);
        Assert.Equal("classic", Generator.ChooseProfile(rng, 99, quest: true).Name);
    }

    [Fact]
    public void A_gauntlet_forbids_teleporting_and_mapping_and_splits_the_stairs()
    {
        var level = Generator.Generate(new LevelRequest(40, 11, StairArrival.Descended, ProfileId: "gauntlet")).Level;
        var noTele = level.AllLocs().Where(p => level[p].Has(SquareFlags.NoTeleport)).ToList();
        var noMap = level.AllLocs().Where(p => level[p].Has(SquareFlags.NoMap)).ToList();
        Assert.NotEmpty(noTele);
        Assert.NotEmpty(noMap);
        Assert.All(noMap, p => Assert.True(level[p].Has(SquareFlags.NoTeleport)));
        // The maze is in the middle; up stairs west of it, down stairs east.
        var mazeRight = noMap.Max(p => p.X);
        Assert.All(level.FindFeature(TerrainFlags.DownStair), p => Assert.True(p.X > mazeRight));
    }

    [Fact]
    public void Teleporting_is_forbidden_from_a_no_teleport_grid_but_a_short_blink_works()
    {
        var game = Arena.Create(3);
        game.Level[game.Player.Position].Flags |= SquareFlags.NoTeleport;
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        var from = game.Player.Position;
        game.TeleportPlayer(60);
        Assert.Equal(from, game.Player.Position);
        Assert.Contains("Teleportation forbidden!", said);
        said.Clear();
        game.TeleportPlayer(10);
        Assert.DoesNotContain("Teleportation forbidden!", said);
    }

    [Fact]
    public void A_hard_centre_holds_a_greater_vault_whose_rating_counts()
    {
        var level = Generator.Generate(new LevelRequest(70, 7, ProfileId: "hard_centre")).Level;
        Assert.Single(level.Vaults);
        Assert.Contains(level.AllLocs(), p => level[p].Has(SquareFlags.Vault));
        Assert.True(level.MonsterRatingBonus > 0);
        var vault = TestData.Game.Vaults.First(v => v.Name == level.Vaults[0]);
        Assert.StartsWith("Greater vault", vault.Type);
    }

    [Fact]
    public void Pit_monsters_stand_awake_in_4_2_5()
    {
        for (ulong seed = 0; seed < 200; seed++)
        {
            var level = Generator.Generate(new LevelRequest(40, seed, ProfileId: "classic")).Level;
            var races = level.SpawnHints.Where(h => h.Kind == SpawnKind.Race).ToList();
            if (races.Count == 0) continue;
            Assert.All(races, h => Assert.EndsWith("|awake", h.Tag));
            return;
        }
        Assert.Fail("no pit or nest in 200 levels");
    }

    [Fact]
    public void A_quest_level_is_classic_and_its_stairs_only_go_up()
    {
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var result = Generator.Generate(new LevelRequest(99, seed, Quest: true));
            Assert.Equal("classic", result.Level.ProfileId);
            Assert.Empty(result.Level.FindFeature(TerrainFlags.DownStair));
            Assert.NotEmpty(result.Level.FindFeature(TerrainFlags.UpStair));
        }
    }

    [Fact]
    public void A_persistent_cavern_builds_its_stairs_at_the_joins()
    {
        var joins = new[] { new StairJoin(new Loc(40, 20), Down: false), new StairJoin(new Loc(70, 30), Down: true) };
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var level = Generator.Generate(new LevelRequest(30, seed, ProfileId: "cavern", Joins: joins, Persistent: true,
                AboveStored: true, BelowStored: true)).Level;
            Assert.Equal([new Loc(40, 20)], level.FindFeature(TerrainFlags.UpStair));
            Assert.Equal([new Loc(70, 30)], level.FindFeature(TerrainFlags.DownStair));
        }
    }

    [Fact]
    public void A_known_labyrinth_is_mapped_from_the_start()
    {
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var level = Generator.Generate(new LevelRequest(15, seed, ProfileId: "labyrinth")).Level;
            if (!level.IsKnown) continue;
            Assert.All(level.AllLocs(), p => Assert.True(level[p].Has(SquareFlags.Mark)));
            return;
        }
        Assert.Fail("no known labyrinth in 40 tries");
    }

    [Fact]
    public void Levels_are_4_2_5s_size()
    {
        var classic = Generator.Generate(new LevelRequest(10, 3, ProfileId: "classic")).Level;
        Assert.Equal((198, 66), (classic.Width, classic.Height));
        var cavern = Generator.Generate(new LevelRequest(30, 3, ProfileId: "cavern")).Level;
        Assert.InRange(cavern.Width, 99, 148);
        Assert.InRange(cavern.Height, 33, 49);
    }

    [Fact]
    public void The_whole_game_goes_on_a_generated_level()
    {
        var game = GameSession.NewGame(TestData.Game, 21, "warrior");
        game.MarkDebugUsed();
        game.Execute(new DebugJumpCommand(30));
        Assert.True(game.Level.Monsters.All.Count() >= 14);
        Assert.NotEmpty(game.Level.Objects.All);
        for (var i = 0; i < 50 && !game.IsGameOver; i++) game.Execute(new HoldCommand());
    }
}
