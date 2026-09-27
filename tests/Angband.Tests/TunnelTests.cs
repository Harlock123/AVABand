using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Generation;
using Angband.Core.Geometry;
using Angband.Core.World;

namespace Angband.Tests;

public class TunnelTests
{
    // The player stands at (2,2) in a lit room; tests turn the square to the east into rock.
    private static GameSession Game(ulong seed = 3)
    {
        var game = Arena.Create(seed,
            "#########",
            "#,,,,,,,#",
            "#,@,,,,,#",
            "#,,,,,,,#",
            "#########");
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 5000;
        return game;
    }

    private static Loc Rock(GameSession game, Func<WellKnownTerrain, ushort> feature)
    {
        var p = game.Player.Position + new Loc(1, 0);
        game.Level[p].Feature = feature(game.Data.Terrain.Ids);
        game.UpdateView();
        return p;
    }

    private static void GiveDigger(GameSession game, string kind) =>
        game.Player.Inventory.Add(game.Objects.Create(kind));

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    [Fact]
    public void Diggers_in_the_pack_count_without_being_wielded()
    {
        var game = Game();
        var bare = game.DiggingSkill;
        GiveDigger(game, "mattock");
        Assert.Equal(bare - game.Player.Inventory.Weapon!.Weight / 10 + 3 * 20 + 25, game.DiggingSkill);
        Assert.Equal("mattock", game.BestDigger!.Kind.Id);
    }

    [Fact]
    public void Dwarves_dig_better()
    {
        var human = GameSession.NewGame(TestData.Game, 1, CharacterSpec.Default("human", "warrior"));
        var dwarf = GameSession.NewGame(TestData.Game, 1, CharacterSpec.Default("dwarf", "warrior"));
        Assert.True(dwarf.DiggingSkill >= human.DiggingSkill + 40);
    }

    [Fact]
    public void Harder_rock_is_harder_to_dig()
    {
        var game = Game();
        GiveDigger(game, "mattock");
        var rubble = game.DiggingChance(DiggingKind.Rubble);
        var magma = game.DiggingChance(DiggingKind.Magma);
        var quartz = game.DiggingChance(DiggingKind.Quartz);
        var granite = game.DiggingChance(DiggingKind.Granite);
        Assert.True(rubble > magma && magma > quartz && quartz > granite && granite > 0);
        Assert.Equal(0, game.DiggingChance(DiggingKind.Permanent));
    }

    [Fact]
    public void Rubble_is_cleared()
    {
        var game = Game();
        var messages = Messages(game);
        var p = Rock(game, t => t.Rubble);
        for (var i = 0; i < 10 && !game.Level.IsFloor(p); i++) game.Execute(new TunnelCommand(Direction.East));
        Assert.True(game.Level.IsFloor(p));
        Assert.Contains("You have removed the rubble.", messages);
    }

    [Fact]
    public void A_mattock_digs_through_granite()
    {
        var game = Game();
        GiveDigger(game, "mattock");
        var messages = Messages(game);
        var p = Rock(game, t => t.Granite);
        for (var i = 0; i < 20 && !game.Level.IsFloor(p); i++) game.Execute(new TunnelCommand(Direction.East));
        Assert.True(game.Level.IsFloor(p));
        Assert.Contains("You have finished the tunnel.", messages);
        Assert.True(game.Known.IsKnown(p));
    }

    [Fact]
    public void Treasure_veins_give_gold()
    {
        var game = Game();
        GiveDigger(game, "mattock");
        var p = Rock(game, t => t.MagmaTreasure);
        for (var i = 0; i < 20 && !game.Level.IsFloor(p); i++) game.Execute(new TunnelCommand(Direction.East));
        Assert.Contains(game.Level.Objects.At(p), o => o.IsGold);
    }

    [Fact]
    public void Bare_hands_only_chip_at_granite()
    {
        var game = Game();
        game.Player.Inventory.TakeOff(game.Player.Inventory.Weapon!);
        var messages = Messages(game);
        var p = Rock(game, t => t.Granite);
        var turn = game.GameTurn;
        Assert.True(game.Execute(new TunnelCommand(Direction.East)));
        Assert.Contains("You chip away futilely at the granite wall.", messages);
        Assert.False(game.Level.IsFloor(p));
        Assert.True(game.GameTurn - turn <= 20); // one attempt, not 99
    }

    [Fact]
    public void Permanent_rock_and_empty_floor_take_no_time()
    {
        var game = Game();
        var messages = Messages(game);
        Rock(game, t => t.Permanent);
        Assert.False(game.Execute(new TunnelCommand(Direction.East)));
        Assert.Contains("This seems to be permanent rock.", messages);
        Assert.False(game.Execute(new TunnelCommand(Direction.West)));
        Assert.Contains("You see nothing there to tunnel.", messages);
    }

    [Fact]
    public void Tunnelling_a_secret_door_finds_it()
    {
        var game = Game();
        GiveDigger(game, "mattock");
        var p = Rock(game, t => t.SecretDoor);
        for (var i = 0; i < 20 && game.Level.FeatureAt(p).Has(TerrainFlags.Secret); i++)
            game.Execute(new TunnelCommand(Direction.East));
        Assert.Equal(game.Data.Terrain.Ids.ClosedDoor, game.Level[p].Feature);
    }

    [Fact]
    public void Being_hurt_stops_the_digging()
    {
        var game = Game();
        game.Player.NaturalStats["str"] = game.Player.Stats["str"] = 3;
        GiveDigger(game, "pick");
        game.RecalculateBonuses();
        var p = Rock(game, t => t.Quartz);
        Assert.True(game.DiggingChance(DiggingKind.Quartz) is > 0 and < 200);
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(0, 1));
        orc.Hp = orc.MaxHp = 10_000;
        var turn = game.GameTurn;
        game.Execute(new TunnelCommand(Direction.East));
        Assert.True(game.GameTurn - turn < GameSession.TunnelRepeats * 10 || game.Level.IsFloor(p));
    }

    [Fact]
    public void A_monster_in_the_way_is_attacked()
    {
        var game = Game();
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(1, 0));
        orc.Hp = orc.MaxHp = 10_000;
        var attacked = false;
        game.Events.Subscribe<PlayerAttackEvent>(_ => attacked = true);
        game.Execute(new TunnelCommand(Direction.East));
        Assert.True(attacked);
    }

    [Fact]
    public void Sealed_vault_pockets_are_valid_levels()
    {
        var level = TestLevels.FromAscii(out var start,
            "#########",
            "#@..#.#.#",
            "#...#####",
            "#########");
        foreach (var p in level.AllLocs()) level[p].Feature = level.Bounds.Edge().Contains(p) ? TestData.Game.Terrain.Ids.Permanent : level[p].Feature;
        level[new Loc(3, 2)].Feature = TestData.Game.Terrain.Ids.UpStair;
        level[new Loc(2, 2)].Feature = TestData.Game.Terrain.Ids.DownStair;
        var pocket = new Loc(5, 1);
        Assert.Contains(LevelValidator.Validate(level, start, TestData.Game.Constants), e => e.Contains("unreachable"));
        level[pocket].Flags |= SquareFlags.Vault;
        level[new Loc(7, 1)].Flags |= SquareFlags.Vault;
        Assert.DoesNotContain(LevelValidator.Validate(level, start, TestData.Game.Constants), e => e.Contains("unreachable"));
    }

    [Fact]
    public void Some_deep_vaults_must_be_dug_into()
    {
        var generator = new DungeonGenerator(TestData.Game);
        var sealedFound = Enumerable.Range(1, 40).Any(seed =>
        {
            var result = generator.Generate(new LevelRequest(45, (ulong)seed));
            var level = result.Level;
            var reachable = Connectivity.Reachable(level, result.PlayerStart);
            return level.AllLocs().Any(p => level.IsTraversable(p) && level[p].Has(SquareFlags.Vault)
                                            && !reachable[p.Y * level.Width + p.X]);
        });
        Assert.True(sealedFound);
    }
}
