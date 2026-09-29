using Angband.Core.Game;
using Angband.Core.Items;

namespace Angband.Tests;

/// <summary>
/// Lanterns: the general store's AVABand staple (no shop sells them in 4.2.5), and refilling as
/// 4.2.5's do_cmd_refill does — from a flask, or another lantern, carried or underfoot.
/// </summary>
public class LanternTests
{
    private static GameSession InTown(ulong seed = 1) =>
        GameSession.NewGame(TestData.Game, seed, CharacterSpec.Default("human", "warrior"));

    private static Store General(GameSession game)
    {
        game.Player.Position = game.Level.AllLocs().First(p => game.Level.FeatureAt(p).Shop == "general");
        return game.StoreHere!;
    }

    [Fact]
    public void The_general_store_always_has_lanterns_and_never_runs_out()
    {
        var game = InTown();
        var store = General(game);
        Assert.Contains("lantern", store.Def.Staples);
        Assert.DoesNotContain("lantern", store.Def.Always); // 4.2.5's own list is untouched (the drift check compares it)
        var lantern = store.Stock.Single(i => i.Kind.Id == "lantern");
        Assert.True(store.IsAlways(lantern));
        for (var day = 0; day < 10; day++) game.Maintain(store);
        lantern = store.Stock.Single(i => i.Kind.Id == "lantern");

        game.Player.Gold = 10_000;
        game.Execute(new BuyCommand(lantern)); // (buying takes no game time)
        var bought = game.Player.Inventory.Pack.Single(i => i.Kind.Id == "lantern");
        Assert.Equal(7500, bought.Fuel); // a new lantern is half full
        Assert.Contains(lantern, store.Stock);
    }

    [Fact]
    public void A_store_from_an_older_save_has_its_lanterns_when_you_walk_in()
    {
        var game = InTown();
        var store = General(game);
        store.Stock.RemoveAll(i => i.Kind.Id == "lantern");
        game.Execute(new EnterStoreCommand());
        Assert.Contains(store.Stock, i => i.Kind.Id == "lantern" && store.IsAlways(i));
    }

    private static (GameSession Game, Item Lantern, List<string> Said) Wearing(string light = "lantern", int fuel = 100)
    {
        var game = Arena.Create();
        var lamp = game.Objects.Create(light);
        lamp.Fuel = fuel;
        game.Player.Inventory.Add(lamp);
        game.Execute(new WieldCommand(lamp));
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        return (game, lamp, said);
    }

    [Fact]
    public void Topping_up_to_full_says_so()
    {
        var (game, lantern, said) = Wearing(fuel: 14_000);
        var flask = game.Player.Inventory.Pack.First(i => i.IsFuel);
        Assert.True(game.CanRefillFrom(flask));
        game.Execute(new RefuelCommand(flask));
        Assert.Equal(15_000, lantern.Fuel);
        Assert.Equal(["You fuel your lamp.", "Your lamp is full."], said.Where(s => s.Contains("lamp")));
    }

    [Fact]
    public void Another_lantern_gives_up_its_oil_and_a_stack_gives_one_up()
    {
        var (game, lantern, _) = Wearing(fuel: 100);
        var spares = game.Objects.Create("lantern", 2);
        spares.Fuel = 3000;
        game.Player.Inventory.Add(spares);
        var before = lantern.Fuel;
        Assert.True(game.CanRefillFrom(spares));
        game.Execute(new RefuelCommand(spares));
        Assert.Equal(before + 3000, lantern.Fuel);
        var lanterns = game.Player.Inventory.Pack.Where(i => i.Kind.Id == "lantern").ToList();
        Assert.Contains(lanterns, l => l.Number == 1 && l.Fuel == 3000);
        Assert.Contains(lanterns, l => l.Number == 1 && l.Fuel == 0);
        Assert.False(game.CanRefillFrom(lanterns.Single(l => l.Fuel == 0))); // an empty one gives nothing
    }

    [Fact]
    public void A_flask_on_the_floor_will_do()
    {
        var (game, lantern, _) = Wearing(fuel: 100);
        var flask = game.Objects.Create("flask_of_oil");
        game.DropNear(flask, game.Player.Position);
        var before = lantern.Fuel;
        game.Execute(new RefuelCommand(flask));
        Assert.Equal(before + 7500, lantern.Fuel);
        Assert.DoesNotContain(flask, game.Level.Objects.At(game.Player.Position));
    }

    [Fact]
    public void A_torch_cannot_be_refilled_and_no_light_says_so()
    {
        var (game, _, _) = Wearing("wooden_torch", 2000);
        Assert.Equal("Your light cannot be refilled.", game.RefillProblem());
        Assert.False(game.CanRefillFrom(game.Player.Inventory.Pack.First(i => i.IsFuel)));
        game.Execute(new TakeOffCommand(game.Player.Inventory.Light!));
        Assert.Equal("You are not wielding a light.", game.RefillProblem());
    }
}
