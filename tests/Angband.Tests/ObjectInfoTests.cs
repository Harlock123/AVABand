using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Persistence;
using Angband.Core.Records;

namespace Angband.Tests;

public class ObjectInfoTests
{
    [Fact]
    public void EffectText_DescribesPotionsInPlainEnglish()
    {
        var text = ObjectInfo.EffectText(TestData.Game, "heal:20:15; cure:blind; timed:oppose_fire:10+1d10; timed:oppose_cold:10+1d10");
        Assert.Contains("heals 20 hit points", text);
        Assert.Contains("cures blindness", text);
        Assert.Contains("grants temporary resistance to fire and cold", text);
    }

    [Fact]
    public void EffectText_ExplainsRandomChoices()
    {
        var text = ObjectInfo.EffectText(TestData.Game, "random:2; teleport:10; teleport:100");
        Assert.Equal("does one of these at random: teleports you a short distance or teleports you", text);
    }

    [Fact]
    public void EveryObjectEgoAndArtifact_CanBeDescribed()
    {
        var game = GameSession.NewGame(TestData.Game, 3);
        foreach (var kind in TestData.Game.Objects)
        {
            game.Knowledge.LearnKind(kind);
            var text = ObjectInfo.DescribeKind(game, kind);
            Assert.False(string.IsNullOrWhiteSpace(text), kind.Id);
            if (kind.Effect is { Length: > 0 }) Assert.Contains("When ", text);
        }
        foreach (var ego in TestData.Game.Egos) Assert.StartsWith(ego.Name[..1].ToUpperInvariant(), ObjectInfo.DescribeEgo(game, ego));
        foreach (var art in TestData.Game.Artifacts) Assert.Contains(art.Name, ObjectInfo.DescribeArtifact(game, art));
        foreach (var rune in ObjectInfo.AllRunes(TestData.Game)) Assert.Contains("\n\n", ObjectInfo.DescribeRune(game, rune));
    }

    [Fact]
    public void UnawareFlavours_StayUnknownUntilLearned()
    {
        var game = GameSession.NewGame(TestData.Game, 5);
        var speed = TestData.Game.Object("speed")!;
        var before = ObjectInfo.DescribeKind(game, speed);
        Assert.Contains("You don't know what it does.", before);
        Assert.DoesNotContain("Speed", before);

        game.Knowledge.LearnKind(speed);
        var after = ObjectInfo.DescribeKind(game, speed);
        Assert.StartsWith("Potion of Speed", after);
        Assert.Contains("When quaffed, it hastes you", after);
    }

    [Fact]
    public void DescribingLeavesTheGameUntouched()
    {
        var game = GameSession.NewGame(TestData.Game, 6);
        var serial = game.Objects.NextSerial;
        var phial = TestData.Game.Artifacts.Single(a => a.Id == "galadriel");
        ObjectInfo.DescribeKind(game, TestData.Game.Object("dagger")!);
        ObjectInfo.DescribeArtifact(game, phial);
        Assert.Equal(serial, game.Objects.NextSerial);
        Assert.DoesNotContain("galadriel", game.Objects.CreatedArtifacts);

        game.Objects.CreateArtifact(phial);
        ObjectInfo.DescribeArtifact(game, phial);
        Assert.Contains("galadriel", game.Objects.CreatedArtifacts);
    }

    [Fact]
    public void ItemText_ShowsOnlyKnownRunes()
    {
        var game = GameSession.NewGame(TestData.Game, 7);
        var sword = game.Objects.Create("long_sword");
        sword.Ego = TestData.Game.Egos.Single(e => e.Id == "slay_animal");
        sword.Slays.AddRange(sword.Ego.Slays);
        Assert.DoesNotContain("It slays", ObjectInfo.DescribeItem(game, sword));
        Assert.Contains("You do not know all of its runes.", ObjectInfo.DescribeItem(game, sword));

        game.Knowledge.LearnRune(RuneIds.Slay("ANIMAL"));
        Assert.Contains("It slays", ObjectInfo.DescribeItem(game, sword));
    }

    [Fact]
    public void SeeingObjects_RecordsKinds_ButEgosOnlyOnceIdentified()
    {
        var game = Arena.Create(8);
        var sword = game.Objects.Create("long_sword");
        sword.Ego = TestData.Game.Egos.Single(e => e.Id == "slay_animal");
        sword.Slays.AddRange(sword.Ego.Slays);
        game.Level.Objects.Add(game.Player.Position.Step(Angband.Core.Geometry.Direction.East), sword);
        game.UpdateView();

        Assert.Contains("long_sword", game.Knowledge.SeenKinds);
        Assert.DoesNotContain("slay_animal", game.Knowledge.SeenEgos);

        game.Knowledge.LearnRune(RuneIds.ToHit);
        game.Knowledge.LearnRune(RuneIds.ToDam);
        game.Knowledge.LearnRune(RuneIds.Slay("ANIMAL"));
        game.UpdateView();
        Assert.Contains("slay_animal", game.Knowledge.SeenEgos);
    }

    [Fact]
    public void SeenKnowledge_IsSaved()
    {
        var game = Arena.Create(9);
        var brand = game.Objects.CreateArtifact(TestData.Game.Artifacts.Single(a => a.Id == "narthanc"));
        game.Player.Inventory.Add(brand);
        game.RecalculateBonuses();
        Assert.DoesNotContain("narthanc", game.Knowledge.SeenArtifacts); // not until its runes are known
        foreach (var rune in brand.Runes()) game.Knowledge.LearnRune(rune);
        game.RecalculateBonuses();
        Assert.Contains("narthanc", game.Knowledge.SeenArtifacts);

        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(TestData.Game, stream);
        Assert.Equal(game.Knowledge.SeenKinds.Order(), loaded.Knowledge.SeenKinds.Order());
        Assert.Contains("narthanc", loaded.Knowledge.SeenArtifacts);
    }
}
