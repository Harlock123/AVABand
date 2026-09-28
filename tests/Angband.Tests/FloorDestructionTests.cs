using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Monsters;

namespace Angband.Tests;

/// <summary>Balls and breaths destroy what lies on the floor (Angband 4.2 project-obj.c project_o).</summary>
public class FloorDestructionTests
{
    private static GameSession Game()
    {
        var game = Arena.Create(4,
            "#####################",
            "#,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,@,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,#",
            "#,,,,,,,,,,,,,,,,,,,#",
            "#####################");
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 1_000_000;
        return game;
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    private static Item Drop(GameSession game, string kind, Loc at, int number = 1)
    {
        var item = game.Objects.Create(kind, number);
        game.Knowledge.LearnKind(item.Kind);
        game.Level.Objects.Add(at, item);
        return item;
    }

    private static bool Lies(GameSession game, Item item, Loc at) => game.Level.Objects.At(at).Contains(item);

    private static Monster Breather(GameSession game, string race, Loc at)
    {
        var m = Arena.AddMonster(game, race, at);
        m.Held = 1000;
        game.UpdateView();
        return m;
    }

    [Fact]
    public void AFireBreath_BurnsTheScrollsInItsPath_NotPotions_NorWhatIsProof_NorBehindIt()
    {
        var game = Game();
        var you = game.Player.Position;
        var dragon = Breather(game, "baby_red_dragon", you + new Loc(-6, 0));
        var inPath = you + new Loc(-3, 0);
        var scrolls = Drop(game, "phase_door", inPath, 5);
        var potions = Drop(game, "cure_light_wounds", inPath, 5);
        var arrows = Drop(game, "mithril_arrow", inPath, 10);  // IGNORE_FIRE
        var behind = Drop(game, "phase_door", dragon.Position + new Loc(-2, 0)); // the other way
        var aside = Drop(game, "phase_door", you + new Loc(-3, 4));             // well off the cone
        game.UpdateView();
        var said = Messages(game);

        game.CastSpellForTest(dragon, "BR_FIRE");

        Assert.False(Lies(game, scrolls, inPath));
        Assert.True(Lies(game, potions, inPath));
        Assert.True(Lies(game, arrows, inPath));
        Assert.True(Lies(game, behind, dragon.Position + new Loc(-2, 0)));
        Assert.True(Lies(game, aside, you + new Loc(-3, 4)));
        Assert.Contains(said, m => m.StartsWith("The Scrolls of Phase Door burn up"));
        Assert.Contains(said, m => m.Contains("Mithril Arrows are unaffected!"));
    }

    [Fact]
    public void AColdBall_ShattersPotionsAroundYou_WithinItsRadius()
    {
        var game = Game();
        var you = game.Player.Position;
        var near = Drop(game, "cure_light_wounds", you + new Loc(2, 0));
        var far = Drop(game, "cure_light_wounds", you + new Loc(5, 0));
        game.UpdateView();
        game.CastSpellForTest(Breather(game, "jackal", you + new Loc(-6, 0)), "BA_COLD");
        Assert.False(Lies(game, near, you + new Loc(2, 0)));
        Assert.True(Lies(game, far, you + new Loc(5, 0)));
    }

    [Fact]
    public void Mana_DestroysEverything_ButArtifacts()
    {
        var game = Game();
        var at = game.Player.Position + new Loc(1, 1);
        var sword = Drop(game, "long_sword", at);
        var potion = Drop(game, "cure_light_wounds", at);
        var art = game.Objects.CreateArtifact(game.Data.Artifacts.First());
        game.Level.Objects.Add(at, art);
        game.UpdateView();
        game.DestroyFloorObjects(game.BallArea(game.Player.Position, 2), "mana");
        Assert.False(Lies(game, sword, at));
        Assert.False(Lies(game, potion, at));
        Assert.True(Lies(game, art, at));
    }

    [Fact]
    public void Plasma_BurnsAndBlastsAsFireAndLightning()
    {
        var game = Game();
        var at = game.Player.Position + new Loc(1, 0);
        var scroll = Drop(game, "phase_door", at);
        var wand = Drop(game, "wand_of_magic_missile", at);
        var potion = Drop(game, "cure_light_wounds", at);
        game.DestroyFloorObjects(game.BallArea(game.Player.Position, 2), "plasma");
        Assert.False(Lies(game, scroll, at));
        Assert.False(Lies(game, wand, at));
        Assert.True(Lies(game, potion, at));
    }

    [Fact]
    public void YourOwnFireBall_BurnsScrollsWhereItLands()
    {
        var game = Game();
        var target = game.Player.Position + new Loc(5, 0);
        var scrolls = Drop(game, "phase_door", target + new Loc(0, 1), 3);
        var wand = game.Objects.Create("wand_of_fire_balls");
        wand.Charges = 5;
        game.Knowledge.LearnKind(wand.Kind);
        game.Player.Inventory.Add(wand);
        game.Player.SkillDevice = 200;
        game.UpdateView();
        for (var i = 0; i < 10 && Lies(game, scrolls, target + new Loc(0, 1)); i++)
            game.Execute(new UseCommand(wand, target));
        Assert.False(Lies(game, scrolls, target + new Loc(0, 1)));
    }
}
