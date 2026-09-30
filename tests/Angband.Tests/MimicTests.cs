using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Persistence;

namespace Angband.Tests;

public class MimicTests
{
    private static GameSession Game()
    {
        var game = Arena.Create(3,
            "#################",
            "#,,,,,,,,,,,,,,,#",
            "#,,@,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,#",
            "#################");
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 5000;
        return game;
    }

    private static Monster Hide(GameSession game, string race, Loc at)
    {
        var m = Arena.AddMonster(game, race, at);
        game.DisguiseMonsters();
        game.UpdateView();
        return m;
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    [Fact]
    public void Mimics_and_lurkers_are_imported()
    {
        var data = TestData.Game;
        foreach (var id in new[] { "creeping_copper_coins", "potion_mimic", "scroll_mimic", "ring_mimic", "lurker", "trapper" })
            Assert.True(data.Monster(id)!.Has(MonsterFlags.Unaware), id);
        Assert.Equal(["copper"], data.Monster("creeping_copper_coins")!.Mimics);
        Assert.Contains("potion_of_healing", data.Monster("potion_mimic")!.Mimics);
        Assert.Empty(data.Monster("lurker")!.Mimics);
        Assert.Contains("small_wooden_chest", data.Monster("chest_mimic")!.Mimics);
    }

    [Fact]
    public void A_mimic_looks_like_its_object()
    {
        var game = Game();
        var mimic = Hide(game, "potion_mimic", new Loc(8, 2));
        Assert.True(mimic.Camouflaged);
        Assert.False(mimic.IsVisible);
        Assert.Equal("potion", mimic.MimicItem!.Base.Id);
        Assert.Same(mimic.MimicItem, game.ObjectShownAt(mimic.Position));
        Assert.Same(mimic.MimicItem, game.Known.RememberedObject(mimic.Position));
        Assert.Empty(game.Level.Objects.At(mimic.Position)); // not a real object
    }

    [Fact]
    public void Creeping_coins_look_like_gold()
    {
        var game = Game();
        var coins = Hide(game, "creeping_silver_coins", new Loc(8, 2));
        Assert.True(coins.MimicItem!.IsGold);
        Assert.True(coins.MimicItem.GoldValue > 0);
    }

    [Fact]
    public void Bumping_into_a_mimic_reveals_it()
    {
        var game = Game();
        var messages = Messages(game);
        var mimic = Hide(game, "potion_mimic", game.Player.Position + new Loc(1, 0));
        var name = game.Describe(mimic.MimicItem!, withArticle: false);
        var hp = mimic.Hp;
        game.Player.IntrinsicResists["blind"] = 1; // (its spell can blind you as it's found out: then you couldn't see it)
        game.RecalculateBonuses();
        Assert.True(game.Execute(new WalkCommand(Direction.East)));
        Assert.False(mimic.Camouflaged);
        Assert.Null(mimic.MimicItem);
        Assert.True(mimic.IsVisible);
        Assert.Contains($"The {name} was really a monster!", messages);
        Assert.Equal(hp, mimic.Hp); // found out, not attacked
    }

    [Fact]
    public void Mimics_lie_in_wait_until_you_come_close()
    {
        var game = Game();
        var spells = 0;
        game.Events.Subscribe<MonsterSpellEvent>(_ => spells++);
        var mimic = Hide(game, "ring_mimic", new Loc(12, 2));
        for (var i = 0; i < 30; i++) game.Execute(new HoldCommand());
        Assert.True(mimic.Camouflaged);
        Assert.Equal(new Loc(12, 2), mimic.Position);
        Assert.Equal(0, spells);
    }

    [Fact]
    public void An_object_mimic_lies_in_wait_even_beside_the_player()
    {
        var game = Game();
        var mimic = Hide(game, "potion_mimic", game.Player.Position + new Loc(0, 1));
        var attacked = false;
        game.Events.Subscribe<MonsterAttackEvent>(e => attacked |= e.MonsterId == mimic.Id);
        // 4.2.5's process_monsters skips a mimicking monster until something gives it away.
        for (var i = 0; i < 10 && !attacked; i++) game.Execute(new HoldCommand());
        Assert.False(attacked);
        Assert.True(mimic.Camouflaged);
    }

    [Fact]
    public void Hurting_a_mimic_reveals_it()
    {
        var game = Game();
        var mimic = Hide(game, "scroll_mimic", new Loc(8, 2));
        mimic.Hp = mimic.MaxHp = 1000;
        game.DamageMonster(mimic, 5);
        Assert.False(mimic.Camouflaged);
        Assert.Null(game.Known.RememberedObject(mimic.Position));
    }

    [Fact]
    public void Detection_sees_the_disguise_not_the_monster()
    {
        var game = Game();
        var mimic = Hide(game, "potion_mimic", new Loc(12, 2));
        game.Known.RememberObject(mimic.Position, null);
        Assert.True(game.ApplyEffects("detect_monsters:30"));
        Assert.False(mimic.IsDetected);
        game.ApplyEffects("detect_objects:30");
        Assert.Same(mimic.MimicItem, game.Known.RememberedObject(mimic.Position));
    }

    [Fact]
    public void Lurkers_look_like_floor_and_stay_invisible_once_found()
    {
        var game = Game();
        var lurker = Hide(game, "lurker", game.Player.Position + new Loc(1, 0));
        Assert.True(lurker.Camouflaged);
        Assert.Null(lurker.MimicItem);
        Assert.Null(game.ObjectShownAt(lurker.Position));

        var messages = Messages(game);
        game.Execute(new WalkCommand(Direction.East));
        Assert.Contains("You have found a lurker!", messages);
        Assert.False(lurker.IsVisible); // an invisible creature
        game.IncreaseTimed("see_invisible", 50);
        game.UpdateView();
        Assert.True(lurker.IsVisible);
    }

    [Fact]
    public void Camouflage_survives_saving()
    {
        var game = Game();
        var mimic = Hide(game, "ring_mimic", new Loc(10, 2));
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        var copy = loaded.Level.Monsters.All.Single();
        Assert.True(copy.Camouflaged);
        Assert.Equal(mimic.MimicItem!.Kind.Id, copy.MimicItem!.Kind.Id);
        Assert.Same(copy.MimicItem, loaded.ObjectShownAt(copy.Position));
    }

    [Fact]
    public void Mimics_turn_up_in_the_dungeon()
    {
        var found = false;
        for (ulong seed = 1; seed <= 40 && !found; seed++)
        {
            var game = GameSession.NewGame(TestData.Game, seed);
            game.Execute(new DebugJumpCommand(25));
            found = game.Level.Monsters.All.Any(m => m.Camouflaged && (m.MimicItem is not null || m.Race.Mimics.Count == 0));
        }
        Assert.True(found);
    }
}
