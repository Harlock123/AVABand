using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Persistence;

namespace Angband.Tests;

public class SpecialEffectTests
{
    private static GameSession Game()
    {
        var game = Arena.Create(3,
            "###################",
            "#,,,,,,,,,,,,,,,,,#",
            "#,,@,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,#",
            "###################");
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 5000;
        game.Player.SkillDevice = 500;
        return game;
    }

    private static Item Carry(GameSession game, string kind, int charges = 0)
    {
        var item = game.Objects.Create(kind);
        item.Charges = charges;
        game.Knowledge.LearnKind(item.Kind);
        return game.Player.Inventory.Add(item)!;
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    [Fact]
    public void Banishment_removes_every_monster_of_a_kind_except_uniques()
    {
        var game = Game();
        foreach (var x in new[] { 8, 10, 12 }) Arena.AddMonster(game, "jackal", new Loc(x, 2), awake: false);
        var grip = Arena.AddMonster(game, "grip", new Loc(14, 2), awake: false); // a unique 'C'
        var orc = Arena.AddMonster(game, "cave_orc", new Loc(16, 2), awake: false);
        var scroll = Carry(game, "scroll_of_banishment");

        Assert.False(game.Execute(new UseCommand(scroll))); // no letter chosen: nothing used
        Assert.True(game.Player.Inventory.Contains(scroll));

        Assert.True(game.Execute(new UseCommand(scroll, Glyph: 'C')));
        Assert.DoesNotContain(game.Level.Monsters.All, m => m.Race.Id == "jackal");
        Assert.Contains(grip, game.Level.Monsters.All);
        Assert.Contains(orc, game.Level.Monsters.All);
        Assert.InRange(game.Player.MaxHp - game.Player.Hp, 0, 12);
    }

    [Fact]
    public void Probing_tells_all()
    {
        var game = Game();
        var messages = Messages(game);
        var orc = Arena.AddMonster(game, "cave_orc", new Loc(8, 2), awake: false);
        game.Execute(new UseCommand(Carry(game, "rod_of_probing")));
        Assert.Contains($"The cave orc has {orc.Hp} hit points.", messages);
        var recall = game.Recall(orc.Race);
        Assert.Contains($"armour rating of {orc.Race.Armour}", recall);
        Assert.Contains($"({orc.Race.Blows[0].Damage})", recall);
    }

    [Fact]
    public void Door_destruction_clears_the_doors_around_you()
    {
        var game = Game();
        var door = game.Player.Position + new Loc(1, 0);
        game.Level[door].Feature = game.Data.Terrain.Ids.ClosedDoor;
        game.Execute(new UseCommand(Carry(game, "scroll_of_door_destruction")));
        Assert.True(game.Level.IsFloor(door));
    }

    [Fact]
    public void A_glyph_of_warding_holds_weak_monsters_back()
    {
        var game = Game();
        game.Execute(new UseCommand(Carry(game, "scroll_of_rune_of_protection")));
        Assert.True(game.Data.TrapByIndex(game.Level[game.Player.Position].Trap)!.Warding);
        Arena.AddMonster(game, "scruffy_dog", game.Player.Position + new Loc(1, 0)); // level 0: can never break it
        var attacks = 0;
        game.Events.Subscribe<MonsterAttackEvent>(_ => attacks++);
        for (var i = 0; i < 30; i++) game.Execute(new HoldCommand());
        Assert.Equal(0, attacks);
        Assert.NotEqual(0, game.Level[game.Player.Position].Trap);
    }

    [Fact]
    public void Mighty_monsters_break_glyphs()
    {
        var game = Game();
        game.Execute(new UseCommand(Carry(game, "scroll_of_rune_of_protection")));
        var messages = Messages(game);
        var troll = Arena.AddMonster(game, "cave_troll", game.Player.Position + new Loc(1, 0));
        troll.Hp = troll.MaxHp = 100_000;
        for (var i = 0; i < 500 && game.Level[game.Player.Position].Trap != 0; i++)
        {
            game.Player.Hp = game.Player.MaxHp;
            game.Execute(new HoldCommand());
        }
        Assert.Equal(0, game.Level[game.Player.Position].Trap);
        Assert.Contains("The rune of protection is broken!", messages);
    }

    [Fact]
    public void Glowing_hands_confuse_the_next_monster_hit()
    {
        var game = Game();
        game.Player.SkillMelee = 1000;
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(1, 0));
        orc.Hp = orc.MaxHp = 100_000;
        game.Execute(new UseCommand(Carry(game, "scroll_of_monster_confusion")));
        Assert.True(game.Player.Timed.Has("att_conf"));
        for (var i = 0; i < 5 && game.Player.Timed.Has("att_conf"); i++) game.Execute(new WalkCommand(Direction.East));
        Assert.False(game.Player.Timed.Has("att_conf"));
    }

    [Fact]
    public void Curse_weapon_curses_it()
    {
        var game = Game();
        var weapon = game.Player.Inventory.Weapon!;
        game.Execute(new UseCommand(Carry(game, "scroll_of_curse_weapon")));
        Assert.NotEmpty(weapon.Curses);
    }

    [Fact]
    public void Random_effects_pick_exactly_one()
    {
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var game = Arena.Create(seed);
            game.ApplyEffects("random:2; timed:blessed:10; timed:hero:10");
            Assert.True(game.Player.Timed.Has("blessed") ^ game.Player.Timed.Has("hero"));
        }
    }

    [Fact]
    public void Scrambled_stats_come_back()
    {
        var game = Game();
        var before = game.Player.Stats.ToDictionary();
        game.IncreaseTimed("scrambled", 3);
        Assert.Equal(before.Values.Order(), game.Player.Stats.Values.Order()); // the same numbers, shuffled
        TestGames.HoldUntil(game, game.GameTurn + 60);
        Assert.Equal(before, game.Player.Stats.ToDictionary());
    }

    [Fact]
    public void Mushrooms_of_terror_stoneskin_and_sprinting()
    {
        var game = Game();
        var speed = game.Player.Speed;
        var armour = game.Player.Armour;
        game.IncreaseTimed("stoneskin", 20);
        Assert.Equal(armour + 40, game.Player.Armour);
        Assert.Equal(speed - 5, game.Player.Speed);

        var messages = Messages(game);
        game.IncreaseTimed("terror", 20);
        Arena.AddMonster(game, "jackal", game.Player.Position + new Loc(1, 0));
        game.Execute(new WalkCommand(Direction.East));
        Assert.Contains(messages, m => m.StartsWith("You are too afraid to attack"));

        var sprinter = Game();
        sprinter.IncreaseTimed("sprint", 2);
        Assert.Equal(sprinter.Player.Speed, speed + 10);
        TestGames.HoldUntil(sprinter, sprinter.GameTurn + 40);
        Assert.True(sprinter.Player.Timed.Has("slow")); // worn out afterwards
    }

    [Fact]
    public void Bat_form_changes_you_until_you_change_back()
    {
        var game = Game();
        var messages = Messages(game);
        var speed = game.Player.Speed;
        Assert.True(game.Shapechange("bat"));
        Assert.Equal(speed + 3, game.Player.Speed);
        Assert.Contains("You assume the shape of a bat!", messages);

        var scroll = Carry(game, "phase_door");
        Assert.False(game.Execute(new UseCommand(scroll)));
        Assert.Contains("You cannot do this while in bat form.", messages);

        game.Player.SkillMelee = 1000;
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(1, 0));
        orc.Hp = orc.MaxHp = 100_000;
        game.Execute(new WalkCommand(Direction.East));
        Assert.Contains(messages, m => m.StartsWith("You bite") || m.StartsWith("You scratch"));

        Assert.True(game.Execute(new ResumeShapeCommand()));
        Assert.Null(game.Player.Shape);
        Assert.Equal(speed, game.Player.Speed);
    }

    [Fact]
    public void Druids_learn_fox_form()
    {
        var game = GameSession.NewGame(TestData.Game, 2, "druid");
        game.Player.Level = 20;
        game.Player.LearnedSpells.Add("fox_form");
        game.Player.NaturalStats["wis"] = game.Player.Stats["wis"] = 40;
        game.Player.Mana = game.Player.MaxMana = 50;
        game.Player.Inventory.Add(game.Objects.Create("gifts_of_nature"));
        for (var i = 0; i < 5 && game.Player.Shape is null; i++) game.Execute(new CastCommand("fox_form"));
        Assert.Equal("fox", game.Player.Shape);
    }

    [Fact]
    public void Sauron_changes_shape_but_dies_as_himself()
    {
        var game = GameSession.NewGame(TestData.Game, 7);
        game.Execute(new DebugJumpCommand(99));
        var sauron = game.Level.Monsters.All.Single(m => m.Race.Id == "sauron_the_sorcerer");
        game.MonsterShapechange(sauron);
        Assert.Contains(sauron.Race.Id, new[] { "wolf_sauron", "serpent_sauron", "vampire_sauron" });
        Assert.Equal("sauron_the_sorcerer", sauron.OriginalRace!.Id);

        using (var stream = new MemoryStream())
        {
            SaveGame.Save(game, stream);
            stream.Position = 0;
            var loaded = SaveGame.Load(TestData.Game, stream);
            var copy = loaded.Level.Monsters.All.Single(m => m.Id == sauron.Id);
            Assert.Equal(sauron.Race.Id, copy.Race.Id);
            Assert.Equal("sauron_the_sorcerer", copy.OriginalRace!.Id);
        }

        game.DamageMonster(sauron, 1_000_000);
        Assert.True(game.IsQuestComplete(game.Data.Quests[0]));
        Assert.Contains("sauron_the_sorcerer", game.KilledUniques);
    }

    [Fact]
    public void Shape_and_scramble_survive_saving()
    {
        var game = Game();
        game.Shapechange("fox");
        game.IncreaseTimed("scrambled", 50);
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal("fox", loaded.Player.Shape);
        Assert.Equal(game.Player.StatScramble, loaded.Player.StatScramble);
        Assert.Equal(game.Player.Stats, loaded.Player.Stats);
    }
}
