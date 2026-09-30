using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>
/// A save with everything AVABand has added in use (Saves/Rich/, named by the commit that wrote it):
/// socketed bracers with a gem and a cursed gem, a bag of holding, a second weapon in the off hand,
/// (Porter) boots, a pack heavy enough to be burdened, and the birth options of the time. It must keep
/// loading, with all of it, whatever later versions change. To write one: RICH_FIXTURE_OUT=path dotnet test
/// --filter FullyQualifiedName~RichSaveTests.Make (it does nothing otherwise).
/// </summary>
public class RichSaveTests
{
    [Fact]
    public void Make()
    {
        if (Environment.GetEnvironmentVariable("RICH_FIXTURE_OUT") is not { Length: > 0 } path) return;
        var game = GameSession.NewGame(TestData.Game, 11, "warrior"); // a Human
        game.GainExperience(game.ExperienceForLevel(19));
        game.Execute(new DebugJumpCommand(5));
        Item Carry(string kind)
        {
            var item = game.Objects.Create(kind);
            game.Knowledge.LearnKind(item.Kind);
            return game.Player.Inventory.Add(item)!;
        }
        var bracers = Carry("iron_bracers");
        game.Execute(new WieldCommand(bracers));
        bracers = game.Player.Inventory.InSlot(EquipSlot.Arms)!;
        game.Execute(new SetGemCommand(bracers, Carry("ruby")));
        game.Execute(new SetGemCommand(bracers, Carry("bloodstone")));
        var boots = game.Objects.Create("leather_boots");
        boots.Ego = game.Data.Egos.Single(e => e.Id == "porter");
        game.Player.Inventory.Add(boots);
        game.Execute(new WieldCommand(boots));
        game.Execute(new WieldOffHandCommand(Carry("main_gauche")));
        Carry("bag_of_holding");
        Carry("sapphire");
        game.Execute(new HoldCommand()); // (the bag counts from here)
        while (game.BurdenPenalty < 1)
        {
            Carry("flask_of_oil");
            game.RecalculateBonuses();
        }
        game.Execute(new HoldCommand());
        Assert.True(game.BurdenPenalty >= 1);
        SaveGame.SaveToFile(game, path);
    }

    public static TheoryData<string> Saves()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Saves", "Rich"), "*" + SaveGame.Extension).Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(file));
        return data;
    }

    [Fact]
    public void ThereIsARichSaveToTest() => Assert.NotEmpty(Saves());

    [Theory]
    [MemberData(nameof(Saves))]
    public void A_rich_save_loads_with_everything_in_it(string name)
    {
        var game = SaveGame.LoadFromFile(TestData.Game, Path.Combine(AppContext.BaseDirectory, "Saves", "Rich", name));
        var inv = game.Player.Inventory;
        var bracers = inv.InSlot(EquipSlot.Arms);
        Assert.NotNull(bracers);
        Assert.Equal(["ruby", "bloodstone"], bracers.Gems.Select(g => g.Kind.Id));
        Assert.Contains("fire", bracers.Resists);                      // the ruby's, merged in
        Assert.Contains("impair_hitpoint_recovery", bracers.Curses);   // the bloodstone's
        Assert.Contains("fire", bracers.Gems[0].AddedResists);          // and what each added, to take out again
        Assert.Equal("main_gauche", game.OffHand?.Kind.Id);
        Assert.Contains(inv.Equipped, i => i.Ego?.Id == "porter");
        Assert.Equal("bag_of_holding", inv.BestBag?.Kind.Id);
        Assert.Equal(25, inv.PackSize);
        Assert.True(game.BurdenPenalty >= 1);
        Assert.Contains(inv.Pack, i => i.Kind.Id == "sapphire");

        // It plays on, and saves again the same.
        for (var i = 0; i < 40 && !game.IsGameOver; i++)
        {
            game.Player.Hp = game.Player.MaxHp;
            game.Execute(new HoldCommand());
        }
        using var again = new MemoryStream();
        SaveGame.Save(game, again);
        again.Position = 0;
        var reloaded = SaveGame.Load(TestData.Game, again);
        Assert.Equal(game.Describe(bracers), reloaded.Describe(reloaded.Player.Inventory.InSlot(EquipSlot.Arms)!));
        Assert.Equal(game.Player.Speed, reloaded.Player.Speed);
        Assert.Equal(game.Player.Blows, reloaded.Player.Blows);
        Assert.Equal(game.Player.WeightLimit, reloaded.Player.WeightLimit);
    }
}
