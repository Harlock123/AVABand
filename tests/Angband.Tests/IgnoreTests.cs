using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>Ignoring (Angband obj-ignore.c and the ignore menu in ui-object.c).</summary>
public class IgnoreTests
{
    private static Loc East(GameSession game) => game.Player.Position + new Loc(1, 0);

    private static Item Known(GameSession game, string kind, int toHit = 0, int toDam = 0, int toAc = 0)
    {
        var item = game.Objects.Create(kind);
        (item.ToHit, item.ToDam, item.ToAc) = (toHit, toDam, toAc);
        game.Knowledge.LearnKind(item.Kind);
        foreach (var rune in item.Runes()) game.Knowledge.LearnRune(rune);
        game.Knowledge.LearnRune(RuneIds.ToHit);
        game.Knowledge.LearnRune(RuneIds.ToDam);
        game.Knowledge.LearnRune(RuneIds.ToAc);
        return item;
    }

    [Theory]
    [InlineData("dagger", "sharp")]
    [InlineData("blade_of_chaos", "great")]
    [InlineData("mace", "blunt")]
    [InlineData("sling", "sling")]
    [InlineData("arrow", "arrow")]
    [InlineData("robe", "robe")]
    [InlineData("soft_leather_armour", "body_armour")]
    [InlineData("elven_cloak", "elven_cloak")]
    [InlineData("ring_of_protection", "ring")]
    public void Items_FallIntoAngbandsIgnoreTypes(string kind, string type) =>
        Assert.Equal(type, Ignoring.TypeOf(TestData.Game.Object(kind)!)!.Id);

    [Fact]
    public void Potions_HaveNoQualityType_ButCanBeIgnoredByKind()
    {
        var potion = TestData.Game.Object("cure_light_wounds")!;
        Assert.Null(Ignoring.TypeOf(potion));
        Assert.Contains("potion", Ignoring.KindBases);
    }

    [Fact]
    public void QualityLevels_FollowAngband()
    {
        var game = Arena.Create(1);
        Assert.Equal(IgnoreLevel.Bad, Ignoring.LevelOf(Known(game, "dagger", toHit: -2), game.Knowledge));
        Assert.Equal(IgnoreLevel.Average, Ignoring.LevelOf(Known(game, "dagger"), game.Knowledge));
        Assert.Equal(IgnoreLevel.Good, Ignoring.LevelOf(Known(game, "dagger", toDam: 1, toHit: -1), game.Knowledge)); // damage counts double

        // Unknown runes: unknown, until the object has been looked over (then it's no artifact: "all").
        var unknown = game.Objects.Create("dagger");
        unknown.ToHit = 3;
        var fresh = new PlayerKnowledge(TestData.Game, 1);
        Assert.Equal(IgnoreLevel.Unknown, Ignoring.LevelOf(unknown, fresh));
        unknown.Assessed = true;
        Assert.Equal(IgnoreLevel.All, Ignoring.LevelOf(unknown, fresh));
    }

    [Fact]
    public void IgnoredObjects_AreNotPickedUp_OrMentioned()
    {
        var game = Arena.Create(2);
        game.SetOption(OptionIds.PickupAlways, true);
        var dagger = Known(game, "dagger", toHit: -3);
        game.Level.Objects.Add(East(game), dagger);
        game.SetIgnoreQuality("sharp", IgnoreLevel.Bad);
        Assert.True(game.IsIgnored(dagger));

        var messages = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => messages.Add(m.Text));
        game.Execute(new WalkCommand(Direction.East));
        Assert.DoesNotContain(game.Player.Inventory.Pack, i => i == dagger);
        Assert.DoesNotContain(messages, m => m.Contains("Dagger"));
        Assert.False(game.Execute(new PickupCommand()));
    }

    [Fact]
    public void IgnoringWhatYouCarry_DropsIt_UnlessWornOrInscribed()
    {
        var game = Arena.Create(3);
        var potions = game.Player.Inventory.Pack.First(i => i.Kind.Id == "cure_light_wounds");
        var options = game.IgnoreOptions(potions);
        Assert.Contains(options, o => o.Choice == IgnoreChoice.Kind && o.Label == "All Potions of Cure Light Wounds");

        game.Execute(new IgnoreCommand(potions, IgnoreChoice.Kind));
        Assert.DoesNotContain(potions, game.Player.Inventory.Pack);
        Assert.Contains(potions, game.Level.Objects.At(game.Player.Position));

        // Inscribed !d: kept (and shown as ignored).
        var flasks = game.Player.Inventory.Pack.First(i => i.Kind.Id == "flask_of_oil");
        flasks.Note = "!d";
        game.Execute(new IgnoreCommand(flasks, IgnoreChoice.ThisItem));
        Assert.Contains(flasks, game.Player.Inventory.Pack);
        Assert.EndsWith("{!d, ignore}", game.Describe(flasks));

        // Worn things are never dropped by ignoring.
        var armour = game.Player.Inventory.Equipped.First(i => i.Base.Id == "soft_armour");
        game.Execute(new IgnoreCommand(armour, IgnoreChoice.ThisItem));
        Assert.Contains(armour, game.Player.Inventory.Equipped);
    }

    [Fact]
    public void Artifacts_AndBangK_AreNeverIgnoredByRule()
    {
        var game = Arena.Create(4);
        var phial = game.Objects.CreateArtifact(TestData.Game.Artifacts.Single(a => a.Id == "galadriel"));
        game.Ignore.KindsAware.Add(phial.Kind.Id);
        game.Ignore.Quality["light"] = IgnoreLevel.All;
        Assert.False(game.IsIgnored(phial));

        var clw = Known(game, "cure_light_wounds");
        game.Ignore.KindsAware.Add(clw.Kind.Id);
        Assert.True(game.IsIgnored(clw));
        clw.Note = "!k";
        Assert.False(game.IsIgnored(clw));

        // By hand, though, anything can be.
        game.Execute(new IgnoreCommand(phial, IgnoreChoice.ThisItem));
        Assert.True(game.IsIgnored(phial));
    }

    [Fact]
    public void UnknownFlavours_AreIgnoredSeparately()
    {
        var game = Arena.Create(5);
        var speed = game.Objects.Create("speed");
        var label = game.IgnoreOptions(speed).Single(o => o.Choice == IgnoreChoice.Kind).Label;
        Assert.DoesNotContain("Speed", label); // named by its flavour
        game.Execute(new IgnoreCommand(speed, IgnoreChoice.Kind));
        Assert.True(game.IsIgnored(speed));

        // Once identified, it isn't ignored any more (Angband's IGNORE_IF_UNAWARE).
        game.Knowledge.LearnKind(speed.Kind);
        Assert.False(game.IsIgnored(speed));
    }

    [Fact]
    public void Egos_AreIgnoredPerItemType()
    {
        var game = Arena.Create(6);
        var sword = Known(game, "long_sword");
        sword.Ego = TestData.Game.Egos.Single(e => e.Id == "slay_animal");
        sword.Slays.AddRange(sword.Ego.Slays);
        foreach (var rune in sword.Runes()) game.Knowledge.LearnRune(rune);
        var ego = game.IgnoreOptions(sword).Single(o => o.Choice == IgnoreChoice.Ego);
        Assert.Equal("All Sharp Melee Weapons of Slay Animal", ego.Label);
        game.Execute(new IgnoreCommand(sword, IgnoreChoice.Ego));
        Assert.True(game.IsIgnored(sword));

        var mace = Known(game, "mace");
        mace.Ego = sword.Ego;
        mace.Slays.AddRange(sword.Ego.Slays);
        Assert.False(game.IsIgnored(mace)); // another type
    }

    [Fact]
    public void TheQualityChoice_SetsTheTypesThreshold()
    {
        var game = Arena.Create(7);
        var average = Known(game, "dagger");
        var option = game.IgnoreOptions(average).Single(o => o.Choice == IgnoreChoice.Quality);
        Assert.Equal("All average Sharp Melee Weapons", option.Label);
        game.Execute(new IgnoreCommand(average, IgnoreChoice.Quality));
        Assert.Equal(IgnoreLevel.Average, game.Ignore.QualityFor("sharp"));
        Assert.True(game.IsIgnored(Known(game, "main_gauche", toHit: -1)));  // bad: at or below average
        Assert.False(game.IsIgnored(Known(game, "main_gauche", toDam: 2))); // good
    }

    [Fact]
    public void K_ShowsIgnoredObjectsAgain()
    {
        var game = Arena.Create(8);
        var dagger = Known(game, "dagger");
        dagger.Ignored = true;
        Assert.True(game.IsIgnored(dagger));
        Assert.False(game.Execute(new ToggleUnignoreCommand()));
        Assert.False(game.IsIgnored(dagger));
        Assert.True(game.IsMarkedIgnored(dagger));
    }

    [Fact]
    public void IgnoreSettings_AreSaved()
    {
        var game = Arena.Create(9);
        var dagger = Known(game, "dagger");
        dagger.Ignored = true;
        game.Level.Objects.Add(East(game), dagger);
        game.Ignore.KindsAware.Add("cure_light_wounds");
        game.Ignore.KindsUnaware.Add("speed");
        game.Ignore.Egos.Add(IgnoreSettings.EgoKey("slay_animal", "sharp"));
        game.SetIgnoreQuality("ring", IgnoreLevel.Bad);

        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Contains("cure_light_wounds", loaded.Ignore.KindsAware);
        Assert.Contains("speed", loaded.Ignore.KindsUnaware);
        Assert.Contains(IgnoreSettings.EgoKey("slay_animal", "sharp"), loaded.Ignore.Egos);
        Assert.Equal(IgnoreLevel.Bad, loaded.Ignore.QualityFor("ring"));
        Assert.True(loaded.Level.Objects.At(East(game)).Single().Ignored);
    }
}
