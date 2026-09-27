using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Persistence;

namespace Angband.Tests;

public class LoreTests
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
        game.Player.Hp = game.Player.MaxHp = 100_000;
        return game;
    }

    private static RaceLore Lore(GameSession game, string race) => game.Lore.For(race);

    [Fact]
    public void Nothing_is_known_at_first()
    {
        var game = Game();
        var text = game.Recall(game.Data.Monster("cave_orc")!);
        Assert.Contains("No battles to the death are recalled.", text);
        Assert.Contains("Nothing is known about his attack.", text); // cave orcs are male
        Assert.DoesNotContain("normally found", text);
        Assert.Contains(game.Data.Monster("cave_orc")!.Description, text);
    }

    [Fact]
    public void Each_monster_is_counted_once_when_seen()
    {
        var game = Game();
        Arena.AddMonster(game, "cave_orc", new Loc(8, 2), awake: false);
        game.UpdateView();
        game.UpdateView();
        Arena.AddMonster(game, "cave_orc", new Loc(9, 2), awake: false);
        Assert.Equal(2, Lore(game, "cave_orc").Sights);
    }

    [Fact]
    public void Uniques_and_their_sex_are_obvious()
    {
        var game = Game();
        Arena.AddMonster(game, "grip", new Loc(8, 2), awake: false);
        var lore = Lore(game, "grip");
        Assert.Contains("UNIQUE", lore.FlagsKnown);
        Assert.StartsWith("Grip, Farmer Maggot's Dog", game.Recall(game.Data.Monster("grip")!));
    }

    [Fact]
    public void Watching_attacks_teaches_them_and_eventually_their_damage()
    {
        var game = Game();
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(1, 0));
        orc.Hp = orc.MaxHp = 100_000;
        game.Player.BaseArmour = 0;
        game.RecalculateBonuses();
        var lore = Lore(game, "cave_orc");
        for (var i = 0; i < 200 && lore.BlowSeen(0) < 10; i++)
        {
            game.Player.Hp = game.Player.MaxHp;
            game.Execute(new HoldCommand());
        }
        var text = game.Recall(orc.Race);
        Assert.Contains("can hit to hurt", text);
        Assert.Contains($"({orc.Race.Blows[0].Damage})", text);
    }

    [Fact]
    public void Kills_reveal_depth_value_and_toughness()
    {
        var game = Game();
        for (var i = 0; i < 3; i++)
        {
            var jackal = Arena.AddMonster(game, "jackal", new Loc(8, 2));
            game.DamageMonster(jackal, 10_000);
        }
        var race = game.Data.Monster("jackal")!;
        Assert.Equal(3, game.CharacterKills["jackal"]);
        Assert.Equal(3, Lore(game, "jackal").TotalKills);
        Assert.Contains("ANIMAL", Lore(game, "jackal").FlagsKnown);
        var text = game.Recall(race);
        Assert.Contains("You have killed 3 of these creatures.", text);
        Assert.Contains("is normally found at depths of", text);
        Assert.Contains("A kill of this creature is worth", text);
        Assert.Contains($"armour rating of {race.Armour}", text);
        Assert.Contains("natural creature", text);
    }

    [Fact]
    public void Spells_are_learned_by_watching_them_cast()
    {
        var game = Game();
        var shaman = Arena.AddMonster(game, "kobold_shaman", new Loc(8, 2));
        shaman.Hp = shaman.MaxHp = 100_000;
        var lore = Lore(game, "kobold_shaman");
        for (var i = 0; i < 300 && lore.SpellsSeen.Count == 0; i++)
        {
            game.Player.Hp = game.Player.MaxHp;
            game.Execute(new HoldCommand());
            if (shaman.Position.ChebyshevTo(game.Player.Position) <= 1) game.Level.Monsters.Move(shaman, new Loc(12, 2));
        }
        Assert.NotEmpty(lore.SpellsSeen);
        Assert.Contains("may ", game.Recall(shaman.Race));
    }

    [Fact]
    public void Resistances_show_when_they_matter()
    {
        var game = Game();
        game.Player.SkillDevice = 500;
        var mold = Arena.AddMonster(game, "grey_mold", new Loc(8, 2)); // IM_POIS
        mold.Hp = mold.MaxHp = 100_000;
        game.ApplyEffects("detect_monsters:30");
        var wand = game.Objects.Create("wand_of_stinking_cloud");
        wand.Charges = 5;
        game.Knowledge.LearnKind(wand.Kind);
        game.Execute(new UseCommand(game.Player.Inventory.Add(wand)!));
        Assert.Contains("IM_POIS", Lore(game, "grey_mold").FlagsKnown);
        Assert.Contains("resists poison", game.Recall(mold.Race));
    }

    [Fact]
    public void Detect_evil_teaches_that_a_monster_is_evil()
    {
        var game = Game();
        Arena.AddMonster(game, "cave_orc", new Loc(12, 2));
        game.ApplyEffects("detect_evil:30");
        Assert.Contains("EVIL", Lore(game, "cave_orc").FlagsKnown);
        Assert.Contains("evil", game.Recall(game.Data.Monster("cave_orc")!));
    }

    [Fact]
    public void Deaths_are_remembered_against_the_killer()
    {
        var game = Game();
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Loc(1, 0));
        orc.Hp = orc.MaxHp = 100_000;
        game.Player.Hp = game.Player.MaxHp = 1;
        for (var i = 0; i < 100 && !game.Player.IsDead; i++) game.Execute(new HoldCommand());
        Assert.True(game.Player.IsDead);
        Assert.Equal(1, Lore(game, "cave_orc").Deaths);
        Assert.Contains("1 of your ancestors has been killed by this creature.", game.Recall(orc.Race));
    }

    [Fact]
    public void Look_describes_health_and_state()
    {
        var game = Game();
        var orc = Arena.AddMonster(game, "cave_orc", new Loc(8, 2), awake: false);
        orc.Hp = orc.MaxHp / 3;
        Assert.Equal("the cave orc (wounded, asleep)", game.LookDescription(orc));
    }

    [Fact]
    public void Memory_survives_on_disk_and_kills_in_the_save()
    {
        var game = Game();
        var jackal = Arena.AddMonster(game, "jackal", new Loc(8, 2));
        game.DamageMonster(jackal, 10_000);

        var path = Path.Combine(Path.GetTempPath(), "avaband-lore-" + Guid.NewGuid() + ".json");
        try
        {
            game.Lore.Save(path);
            var book = MonsterLoreBook.Load(path);
            Assert.Equal(1, book.Find("jackal")!.TotalKills);
        }
        finally
        {
            File.Delete(path);
        }

        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        Assert.Equal(1, SaveGame.Load(TestData.Game, stream).CharacterKills["jackal"]);
    }
}
