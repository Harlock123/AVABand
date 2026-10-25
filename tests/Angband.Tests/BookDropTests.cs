using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Randomness;

namespace Angband.Tests;

/// <summary>AVABand: dungeon books are found a little more often, and a caster's own a little more often still.</summary>
public class BookDropTests
{
    private const int Draws = 20_000;

    [Fact]
    public void DungeonBooks_AreTheOnesNoShopInTownSells()
    {
        var factory = new ObjectFactory(TestData.Game);
        Assert.True(factory.IsDungeonBook(TestData.Game.Object("healing_and_sanctuary")!));
        Assert.True(factory.IsDungeonBook(TestData.Game.Object("wizards_tome_of_power")!));
        Assert.False(factory.IsDungeonBook(TestData.Game.Object("magical_defences")!));
        Assert.False(factory.IsDungeonBook(TestData.Game.Object("novices_handbook")!));
        Assert.False(factory.IsDungeonBook(TestData.Game.Object("cure_light_wounds")!));
    }

    [Fact]
    public void DungeonBooks_AreFoundHalfAsOftenAgain()
    {
        var factory = new ObjectFactory(TestData.Game);
        int Count(Func<GameRandom, ObjectKindDef?> pick)
        {
            var rng = new GameRandom(11);
            var n = 0;
            for (var i = 0; i < Draws; i++)
                if (pick(rng) is { } kind && factory.IsDungeonBook(kind)) n++;
            return n;
        }
        var plain = Count(rng => factory.PickKind(rng, 45));        // Angband's weights
        var found = Count(rng => factory.Make(rng, 45)?.Kind);       // what the dungeon has
        Assert.True(plain > 100, $"{plain}");
        Assert.InRange(found / (double)plain, 1.3, 1.7);
    }

    [Fact]
    public void ACaster_FindsTheirOwnDungeonBooks_ALittleMoreOften()
    {
        var game = GameSession.NewGame(TestData.Game, 3, CharacterSpec.Default("human", "priest"));
        int Theirs(int depth = 45)
        {
            var rng = new GameRandom(12);
            var n = 0;
            for (var i = 0; i < Draws; i++)
                if (game.Objects.Make(rng, depth) is { } item && game.Objects.IsDungeonBook(item.Kind) && game.PlayerCanBrowse(item.Kind)) n++;
            return n;
        }
        var wanting = Theirs();
        var shallow = Theirs(32);
        var wants = game.Objects.WantsBook;
        game.Objects.WantsBook = null;
        var asIn425 = Theirs();
        var shallow425 = Theirs(32);
        game.Objects.WantsBook = wants;
        Assert.InRange(wanting / (double)asIn425, 1.2, 1.9);  // about half as many again
        Assert.InRange(shallow / (double)shallow425, 1.2, 1.9);

        // Only the books they could read and haven't got (in the pack or at home).
        Assert.True(wants!(TestData.Game.Object("battle_blessings")!));
        Assert.False(wants(TestData.Game.Object("arcane_control")!));
        game.Player.Inventory.Add(game.Objects.Create("battle_blessings"));
        Assert.False(wants(TestData.Game.Object("battle_blessings")!));
        game.Stores.Values.Single(s => s.IsHome).Stock.Add(game.Objects.Create("healing_and_sanctuary"));
        Assert.False(wants(TestData.Game.Object("healing_and_sanctuary")!));
        Assert.True(wants(TestData.Game.Object("wrath_of_the_valar")!));

        // A warrior wants none.
        var warrior = GameSession.NewGame(TestData.Game, 3, CharacterSpec.Default("human", "warrior"));
        Assert.DoesNotContain(TestData.Game.Objects, k => warrior.Objects.WantsBook!(k));
    }

    [Fact]
    public void TheWantedBook_IsStillNativeToTheLevel()
    {
        var game = GameSession.NewGame(TestData.Game, 3, CharacterSpec.Default("human", "priest"));
        var rng = new GameRandom(13);
        var wrath = 0;
        var healing = 0;
        for (var i = 0; i < Draws; i++)
            switch (game.Objects.Make(rng, 32)?.Kind.Id)
            {
                case "wrath_of_the_valar": wrath++; break;   // native to level 60
                case "healing_and_sanctuary": healing++; break; // native to level 30
            }
        Assert.True(healing > 10 * Math.Max(1, wrath), $"healing {healing}, wrath {wrath}");
    }
}
