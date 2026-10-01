using Angband.Core.Game;

namespace Angband.Tests;

/// <summary>All 138 of Angband 4.2's artifacts are there, including the body armours once lost to a spelling.</summary>
public class ArtifactSetTests
{
    [Fact]
    public void All138_AreThere()
    {
        // (Angband's 138, and AVABand's own in ava_artifacts.json beside them.)
        var ava = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Angband.Data.DataLoader.DefaultDataDirectory,
            Angband.Data.DataLoader.AvaArtifactsFile))).RootElement.GetArrayLength();
        Assert.Equal(138 + ava, TestData.Game.Artifacts.Count);
        foreach (var name in new[] { "'Bladeturner'", "'Soulkeeper'", "'Razorback'", "'Mediator'", "of Isildur", "of Celeborn",
                     "of Arvedui", "of Caspanion", "of the Rohirrim", "of Himring", "'Thalkettoth'", "'Belegennon'" })
            Assert.Contains(TestData.Game.Artifacts, a => a.Name == name);
    }

    [Fact]
    public void Bladeturner_IsPowerDragonScaleMail_WithItsResistances()
    {
        var game = GameSession.NewGame(TestData.Game, 1);
        var art = game.Data.Artifacts.Single(a => a.Name == "'Bladeturner'");
        var item = game.Objects.CreateArtifact(art);
        Assert.Equal("power_dragon_scale_mail", item.Kind.Id);
        Assert.Contains("nether", item.Resists);
        Assert.Contains("chaos", item.Resists);
        Assert.NotNull(item.Activation);
    }
}
