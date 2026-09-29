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

/// <summary>Angband EASY_KNOW: a known kind's ego flags are known too (object_flags_known).</summary>
public class EasyKnowTests
{
    [Fact]
    public void A_Lantern_of_True_Sight_shows_its_protections_at_once()
    {
        var game = Arena.Create(4);
        var data = game.Data;
        Assert.Equal(22, data.Objects.Count(k => k.Has("EASY_KNOW")));
        var lantern = game.Objects.Create("lantern");
        Angband.Core.Items.ObjectFactory.ApplyEgo(new Angband.Core.Randomness.GameRandom(1), lantern, data.Egos.Single(e => e.Id == "of_true_sight"), 10);
        Assert.True(game.Knowledge.KnowsKind(lantern));
        Assert.False(game.Knowledge.KnowsRune(Angband.Core.Definitions.RuneIds.Resist("see_invis")));
        var text = ObjectInfo.DescribeItem(game, lantern);
        Assert.Contains("see invisible", text, StringComparison.OrdinalIgnoreCase);

        // A sword isn't EASY_KNOW: its ego's free action stays a mystery until the rune is learned.
        var sword = game.Objects.Create("dagger");
        var westernesse = data.Egos.First(e => e.Resists.Contains("free_act") && e.Bases.Contains("sword"));
        Angband.Core.Items.ObjectFactory.ApplyEgo(new Angband.Core.Randomness.GameRandom(1), sword, westernesse, 30);
        Assert.False(game.Knowledge.KnowsProperty(sword, Angband.Core.Definitions.RuneIds.Resist("free_act")));
    }
}

/// <summary>The Curses knowledge page's text.</summary>
public class CurseKnowledgeTests
{
    [Fact]
    public void A_curse_says_what_it_does_how_often_where_and_what_you_carry_it_on()
    {
        var game = Arena.Create(4);
        var dagger = TestGames.GiveCursedItem(game, "teleportation", 30);
        var text = ObjectInfo.DescribeCurse(game, game.Data.Curse("teleportation")!);
        Assert.StartsWith("Teleportation curse", text);
        Assert.Contains("It randomly makes you teleport.", text);
        Assert.Contains("It acts every 1 to", text);
        Assert.Contains("It can be found on", text);
        Assert.Contains("anti-teleportation", text); // its conflict
        Assert.Contains($"You carry it on {game.Describe(dagger)} (strength 30).", text);
    }
}
