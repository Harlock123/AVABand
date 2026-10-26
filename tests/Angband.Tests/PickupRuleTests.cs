using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>AVABand's smarter pickup: rules by kind, ammunition for your launcher, and things not yet learned.</summary>
public class PickupRuleTests
{
    private static GameSession Game()
    {
        var game = Arena.Create(31);
        // Only the new rules: nothing picked up by Angband's own options.
        game.SetOption(OptionIds.PickupAlways, false);
        game.SetOption(OptionIds.PickupInven, false);
        return game;
    }

    /// <summary>Puts an item east of the player and steps onto it; true if it was picked up.</summary>
    private static bool StepOnto(GameSession game, Item item)
    {
        game.Level.Objects.Add(game.Player.Position + new Loc(1, 0), item);
        game.Execute(new WalkCommand(Direction.East));
        return !game.Level.Objects.At(game.Player.Position).Contains(item);
    }

    [Fact]
    public void Always_and_never_rules_go_by_kind_and_an_items_own_inscription_comes_first()
    {
        var game = Game();
        var oil = TestData.Game.Object("flask_of_oil")!;
        Assert.False(StepOnto(game, game.Objects.Create("flask_of_oil")));

        game.SetPickupRule(oil, PickupRule.Always);
        Assert.True(StepOnto(game, game.Objects.Create("flask_of_oil")));

        game.SetPickupRule(oil, PickupRule.Never);
        game.SetOption(OptionIds.PickupInven, true); // (never beats matching what you carry)
        Assert.False(StepOnto(game, game.Objects.Create("flask_of_oil")));

        var inscribed = game.Objects.Create("flask_of_oil");
        inscribed.Note = "=g";
        Assert.True(StepOnto(game, inscribed));

        game.SetPickupRule(oil, null);
        Assert.Null(game.PickupRuleFor(oil));
        Assert.Empty(game.PickupRules);
    }

    [Fact]
    public void Ammunition_for_your_launcher_is_picked_up_whatever_its_enchantment()
    {
        var game = Game();
        Assert.True(OptionCatalog.Find(OptionIds.PickupAmmo)!.Default);
        foreach (var bow in game.Player.Inventory.Equipped.Where(i => i.Base.AmmoClass is not null).ToList())
            game.Player.Inventory.Remove(bow, bow.Number, () => game.Objects.NextSerial++);
        var shot = game.Objects.Create("iron_shot");
        shot.ToHit = 7;
        Assert.False(StepOnto(game, shot)); // (no sling yet)

        game.Player.Inventory.Wield(game.Objects.Create("sling"), () => game.Objects.NextSerial++);
        game.RecalculateBonuses();
        var magic = game.Objects.Create("iron_shot");
        magic.ToHit = 7;
        Assert.True(StepOnto(game, magic));
        Assert.Contains(magic, game.Player.Inventory.Quiver);
        Assert.False(StepOnto(game, game.Objects.Create("arrow"))); // (not what a sling fires)

        game.SetOption(OptionIds.PickupAmmo, false);
        Assert.False(StepOnto(game, game.Objects.Create("iron_shot")));
    }

    [Fact]
    public void Things_you_havent_learned_are_picked_up_until_you_know_them()
    {
        var game = Game();
        var kind = TestData.Game.Objects.First(k => k.Id != "cure_light_wounds" && game.Knowledge.Flavor(k) is not null && !game.Knowledge.IsAware(k)
                                                    && game.Objects.Create(k.Id).IsFlavored);
        Assert.True(StepOnto(game, game.Objects.Create(kind.Id)));

        game.Knowledge.LearnKind(kind);
        Assert.False(StepOnto(game, game.Objects.Create(kind.Id)));

        var other = TestData.Game.Objects.First(k => k != kind && game.Knowledge.Flavor(k) is not null && !game.Knowledge.IsAware(k));
        game.SetOption(OptionIds.PickupUnknown, false);
        Assert.False(StepOnto(game, game.Objects.Create(other.Id)));
    }

    [Fact]
    public void Rules_are_saved_and_replayed_and_old_replays_play_without_the_new_options()
    {
        var game = Game();
        var oil = TestData.Game.Object("flask_of_oil")!;
        var recorder = new ReplayRecorder(game);
        game.Recorder = recorder;
        game.SetPickupRule(oil, PickupRule.Always);
        game.Recorder = null;

        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        Assert.Equal(PickupRule.Always, SaveGame.Load(TestData.Game, stream).PickupRuleFor(oil));

        var file = recorder.ToFile(game);
        var player = new ReplayPlayer(TestData.Game, file);
        while (player.Step()) { }
        Assert.Equal(PickupRule.Always, player.Game.PickupRuleFor(oil));
        Assert.True(player.Game.Options[OptionIds.PickupAmmo]);

        file.Version = 1;
        var old = new ReplayPlayer(TestData.Game, file);
        Assert.False(old.Game.Options[OptionIds.PickupAmmo] || old.Game.Options[OptionIds.PickupUnknown]);
    }
}
