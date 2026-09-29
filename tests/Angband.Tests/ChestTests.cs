using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Persistence;
using Angband.Core.World;

namespace Angband.Tests;

public class ChestTests
{
    private static GameSession Game(ulong seed = 3)
    {
        var game = Arena.Create(seed,
            "#################",
            "#,,,,,,,,,,,,,,,#",
            "#,,@,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,#",
            "#################");
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 5000;
        return game;
    }

    private static int Bit(string trapId) => TestData.Game.ChestTraps.Single(t => t.Id == trapId).Bit;

    private static Item PlaceChest(GameSession game, string kind, int state, Loc at)
    {
        var chest = game.Objects.Create(kind);
        chest.ChestState = state;
        chest.OriginDepth = 10;
        game.Level.Objects.Add(at, chest);
        game.UpdateView();
        return chest;
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    private static int FloorItems(GameSession game) => game.Level.Objects.All.Count(o => !o.Item.IsChest);

    [Fact]
    public void Chests_and_their_traps_are_loaded()
    {
        var data = TestData.Game;
        Assert.Equal(6, data.Objects.Count(k => k.Base == "chest"));
        Assert.Equal([1, 2, 4, 8, 16, 32, 64], data.ChestTraps.Select(t => t.Bit));
        Assert.Equal("locked", data.ChestTraps[0].Id);
    }

    [Fact]
    public void Shallow_chests_only_get_shallow_traps()
    {
        var game = Game();
        var wooden = game.Data.Object("small_wooden_chest")!;
        var deep = Bit("summon") | Bit("paralysis_gas") | Bit("explosion");
        var lockedOnly = 0;
        for (var i = 0; i < 500; i++)
        {
            var traps = game.Objects.PickChestTraps(game.Rng, wooden);
            Assert.True(traps >= 1);
            Assert.Equal(0, traps & deep);
            if (traps == 1) lockedOnly++;
        }
        Assert.InRange(lockedOnly, 20, 90); // one in ten
    }

    [Theory]
    [InlineData(0, "(empty)")]
    [InlineData(1, "(locked)")]
    [InlineData(-1, "(unlocked)")]
    [InlineData(-2, "(disarmed)")]
    [InlineData(2, "(gas trap)")]
    [InlineData(4, "(poison needle)")]
    [InlineData(4 | 8, "(poison needle)")]
    [InlineData(2 | 64, "(multiple traps)")]
    public void Chests_say_what_state_they_are_in(int state, string suffix)
    {
        var game = Game();
        var chest = game.Objects.Create("small_iron_chest");
        chest.ChestState = state;
        Assert.EndsWith(suffix, game.Describe(chest));
    }

    [Fact]
    public void Opening_a_locked_chest_picks_the_lock_and_spills_its_treasure()
    {
        var game = Game();
        game.Player.DisarmSkill = 200;
        var messages = Messages(game);
        var chest = PlaceChest(game, "small_iron_chest", 1, game.Player.Position + new Loc(1, 0));
        Assert.True(game.Execute(new OpenCommand(Direction.East)));
        Assert.Contains("You have picked the lock.", messages);
        Assert.Equal(0, chest.ChestState);
        Assert.Equal(2, FloorItems(game)); // iron chests hold two things
        Assert.EndsWith("(empty)", game.Describe(chest));
        Assert.False(game.Execute(new OpenCommand(Direction.East)));
        Assert.Contains("The chest is empty.", messages);
    }

    [Fact]
    public void A_chest_underfoot_can_be_opened()
    {
        var game = Game();
        game.Player.DisarmSkill = 200;
        var chest = PlaceChest(game, "small_wooden_chest", -1, game.Player.Position);
        Assert.True(game.Execute(new OpenCommand(Direction.Here)));
        Assert.Equal(0, chest.ChestState);
        Assert.Equal(1, FloorItems(game));
    }

    [Fact]
    public void Opening_a_trapped_chest_sets_the_trap_off()
    {
        var game = Game();
        game.Player.DisarmSkill = 200;
        PlaceChest(game, "small_wooden_chest", Bit("poison_gas"), game.Player.Position + new Loc(1, 0));
        game.Execute(new OpenCommand(Direction.East));
        Assert.True(game.Player.Timed.Has("poisoned"));
    }

    [Fact]
    public void An_exploding_chest_destroys_what_was_inside()
    {
        var game = Game();
        game.Player.DisarmSkill = 200;
        var messages = Messages(game);
        var hp = game.Player.Hp;
        PlaceChest(game, "large_steel_chest", Bit("explosion"), game.Player.Position + new Loc(1, 0));
        game.Execute(new OpenCommand(Direction.East));
        Assert.Contains("There is a sudden explosion! Everything inside the chest is destroyed!", messages);
        Assert.True(game.Player.Hp < hp);
        Assert.Equal(0, FloorItems(game));
    }

    [Fact]
    public void Disarmed_chests_open_safely()
    {
        var game = Game();
        game.Player.DisarmSkill = 500;
        var messages = Messages(game);
        var chest = PlaceChest(game, "small_wooden_chest", Bit("needle_str"), game.Player.Position + new Loc(1, 0));
        var str = game.Player.Stats["str"];
        Assert.True(game.Execute(new DisarmCommand(Direction.East)));
        Assert.Contains("You have disarmed the chest.", messages);
        Assert.EndsWith("(disarmed)", game.Describe(chest));
        game.Execute(new OpenCommand(Direction.East));
        Assert.Equal(str, game.Player.Stats["str"]);
        Assert.Equal(1, FloorItems(game));
    }

    [Fact]
    public void Fumbling_a_disarm_can_set_the_trap_off()
    {
        var game = Game();
        game.Player.DisarmSkill = 0;
        var messages = Messages(game);
        var chest = PlaceChest(game, "small_wooden_chest", Bit("poison_gas"), game.Player.Position + new Loc(1, 0));
        for (var i = 0; i < 20 && !messages.Contains("You set off a trap!"); i++)
            game.Execute(new DisarmCommand(Direction.East));
        Assert.Contains("You set off a trap!", messages);
        Assert.True(chest.ChestState > 1); // still trapped
    }

    [Fact]
    public void Untrapped_chests_have_nothing_to_disarm()
    {
        var game = Game();
        var messages = Messages(game);
        PlaceChest(game, "small_wooden_chest", -1, game.Player.Position + new Loc(1, 0));
        Assert.False(game.Execute(new DisarmCommand(Direction.East)));
        Assert.Contains("The chest is not trapped.", messages);
    }

    // --- Floor traps --------------------------------------------------------------------------

    private static Loc Trap(GameSession game, string trapId, Loc at)
    {
        game.Level[at].Trap = game.Data.Traps.Single(t => t.Id == trapId).Index;
        game.Level[at].Flags |= SquareFlags.TrapVisible;
        return at;
    }

    [Fact]
    public void Walking_into_a_pit_hurts()
    {
        var game = Game();
        var messages = Messages(game);
        Trap(game, "pit", game.Player.Position + new Loc(1, 0));
        var damage = 0;
        game.Events.Subscribe<PlayerHurtEvent>(e => damage += e.Damage); // (5000 HP regenerate a pit's worth at once)
        game.Execute(new JumpCommand(Direction.East));
        Assert.Contains("You fall into a pit!", messages); // 4.2.5's message
        Assert.InRange(damage, 2, 12);
    }

    [Fact]
    public void Gas_traps_respect_protection()
    {
        var game = Game();
        game.Player.IntrinsicResists["pois"] = 1;
        game.RecalculateBonuses();
        Trap(game, "poison_gas", game.Player.Position + new Loc(1, 0));
        game.Execute(new JumpCommand(Direction.East));
        Assert.False(game.Player.Timed.Has("poisoned"));
    }

    [Fact]
    public void Trap_doors_drop_you_a_level()
    {
        var game = GameSession.NewGame(TestData.Game, 4);
        game.Execute(new DebugJumpCommand(3));
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 5000;
        var next = game.Level.Neighbors(game.Player.Position).First(p => game.Level.IsEmptyFloor(p));
        Trap(game, "trap_door", next);
        game.Execute(new JumpCommand(DirectionExtensions.FromOffset(next.X - game.Player.Position.X, next.Y - game.Player.Position.Y)));
        Assert.Equal(4, game.Player.Depth);
    }

    [Fact]
    public void Floor_traps_can_be_disarmed()
    {
        var game = Game();
        game.Player.DisarmMagicSkill = 500; // 4.2.5's fire trap is a rune: magical disarming
        var messages = Messages(game);
        var at = Trap(game, "fire_rune", game.Player.Position + new Loc(1, 0));
        Assert.True(game.Execute(new DisarmCommand(Direction.East)));
        Assert.Contains("You have disarmed the discolored spot.", messages);
        Assert.Equal(0, game.Level[at].Trap);
        Assert.False(game.Execute(new DisarmCommand(Direction.East)));
        Assert.Contains("You see nothing there to disarm.", messages);
    }

    // --- Mimics and saving -------------------------------------------------------------------------

    [Fact]
    public void Chest_mimics_look_like_trapped_chests()
    {
        var game = Game();
        var mimic = Arena.AddMonster(game, "chest_mimic", new Loc(10, 2));
        game.DisguiseMonsters();
        Assert.True(mimic.MimicItem!.IsChest);
        Assert.NotEqual(0, mimic.MimicItem.ChestState);
    }

    [Fact]
    public void Chest_state_survives_saving()
    {
        var game = Game();
        var chest = PlaceChest(game, "large_iron_chest", Bit("summon") | Bit("needle_con"), new Loc(8, 2));
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal(chest.ChestState, loaded.Level.Objects.At(new Loc(8, 2)).Single().ChestState);
    }
}
