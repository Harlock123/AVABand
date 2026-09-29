using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>Remove Curse as 4.2.5 has it: you choose the item and the curse (get_item, get_curse).</summary>
public class RemoveCurseTests
{
    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    [Fact]
    public void The_chosen_curse_is_the_one_attacked()
    {
        var game = Arena.Create(4);
        var weak = TestGames.GiveCursedItem(game, "vulnerability", 5);
        var strong = TestGames.GiveCursedItem(game, "teleportation", 30);
        Assert.Equal(2, game.UncursableItems().Count);
        Assert.Equal([("teleportation", 30)], game.RemovableCurses(strong));

        var scroll = game.Player.Inventory.Add(game.Objects.Create("scroll_of_remove_curse"))!; // 50+d50
        game.Execute(new UseCommand(scroll, Uncurse: new CurseChoice(strong, "teleportation")));
        Assert.False(strong.IsCursed);
        Assert.True(weak.IsCursed); // the weaker one, not chosen, stays
    }

    [Fact]
    public void Too_weak_a_spell_leaves_the_item_fragile()
    {
        var game = Arena.Create(4);
        var item = TestGames.GiveCursedItem(game, "teleportation", 95);
        var said = Messages(game);
        game.ApplyEffects("remove_curse:20+d20");
        Assert.True(item.IsCursed);
        Assert.Contains("FRAGILE", item.Flags);
        Assert.Contains(said, m => m.StartsWith("The spell fails; your", StringComparison.Ordinal));
    }

    [Fact]
    public void A_known_scroll_waits_when_there_is_nothing_to_uncurse()
    {
        var game = Arena.Create(4);
        var scroll = game.Player.Inventory.Add(game.Objects.Create("scroll_of_remove_curse"))!;
        game.Knowledge.LearnKind(scroll.Kind);
        var said = Messages(game);
        game.Execute(new UseCommand(scroll));
        Assert.Contains("You have no curses to remove.", said);
        Assert.True(game.Player.Inventory.Contains(scroll)); // not read
    }

    [Fact]
    public void Curses_describe_themselves_as_4_2_5_does()
    {
        var game = Arena.Create(4);
        var item = TestGames.GiveCursedItem(game, "vulnerability", 5);
        var text = ObjectInfo.DescribeItem(game, item);
        Assert.Contains($"It {game.Data.Curse("vulnerability")!.Description}.", text);
        Assert.Equal("30+d30", new Func<string>(() => { game.GainExperience(game.ExperienceForLevel(29)); return game.UncurseStrengthText("remove_curse:{L}+1d{L}"); })());
    }
}
