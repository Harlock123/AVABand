using System.IO.Compression;
using System.Text.Json.Nodes;
using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>AVABand's easier casting (birth_ava_easy_casting): spells fail half as often.</summary>
public class EasyCastingTests
{
    private static GameSession Mage(bool easy, string cls = "mage") =>
        GameSession.NewGame(TestData.Game, 4, CharacterSpec.Default("human", cls)
            with { Options = new Dictionary<string, bool> { [OptionIds.AvaEasyCasting] = easy } });

    [Fact]
    public void EasyCasting_IsABirthOption_OnByDefault()
    {
        var option = OptionCatalog.Find(OptionIds.AvaEasyCasting)!;
        Assert.Equal(OptionKind.Birth, option.Kind);
        Assert.True(option.Default);
        Assert.True(GameSession.NewGame(TestData.Game, 4, "mage").Options[OptionIds.AvaEasyCasting]);
    }

    [Fact]
    public void EasyCasting_HalvesTheChance_RoundingDown()
    {
        var easy = Mage(true);
        var missile = TestData.Game.Spell("magic_missile")!;
        Assert.Equal(10, easy.SpellFailChance(missile)); // 4.2.5's 20
        easy.GainExperience(easy.ExperienceForLevel(4)); // level 5
        Assert.Equal(4, easy.SpellFailChance(missile));  // 4.2.5's 8
        easy.Player.Mana = 0;
        Assert.Equal(6, easy.SpellFailChance(missile));  // 4.2.5's 13
    }

    [Fact]
    public void EasyCasting_HalvesTheMinimum_AndFear()
    {
        int Best(bool easy, string cls, string spellId)
        {
            var game = Mage(easy, cls);
            game.GainExperience(game.ExperienceForLevel(49));
            game.Player.NaturalStats[game.PlayerRealm!.Stat] = game.Player.Stats[game.PlayerRealm.Stat] = 40;
            game.Player.Mana = game.Player.MaxMana = 100;
            return game.SpellFailChance(game.Data.Spell(spellId)!);
        }
        Assert.Equal(5, Best(false, "rogue", "phase_door"));
        Assert.Equal(2, Best(true, "rogue", "phase_door")); // the 5% floor becomes 2%
        Assert.Equal(0, Best(true, "mage", "magic_missile"));

        int Afraid(bool easy)
        {
            var game = Mage(easy);
            game.IncreaseTimed(TimedIds.Afraid, 10, check: false);
            return game.SpellFailChance(game.Data.Spell("magic_missile")!);
        }
        Assert.Equal(40, Afraid(false));
        Assert.Equal(20, Afraid(true));
    }

    [Fact]
    public void Stunning_AndAmnesia_CountInFull()
    {
        var missile = TestData.Game.Spell("magic_missile")!;
        var stunned = Mage(true);
        var halved = stunned.SpellFailChance(missile);
        stunned.IncreaseTimed(TimedIds.Stun, 10, check: false);
        Assert.Equal(halved + 15, stunned.SpellFailChance(missile));

        var forgetful = Mage(true);
        forgetful.IncreaseTimed(TimedIds.Amnesia, 20, check: false);
        Assert.Equal(50 + halved / 2, forgetful.SpellFailChance(missile));
    }

    [Fact]
    public void ACharacterFromBeforeTheOption_CastsTheEasierWay()
    {
        var game = Mage(false);
        using var saved = new MemoryStream();
        SaveGame.Save(game, saved);
        saved.Position = 0;
        // An older save: its birth options have no value for easier casting.
        JsonNode file;
        using (var gzip = new GZipStream(saved, CompressionMode.Decompress, leaveOpen: true)) file = JsonNode.Parse(gzip)!;
        Assert.True(file["Options"]!.AsObject().Remove(OptionIds.AvaEasyCasting));
        using var old = new MemoryStream();
        using (var gzip = new GZipStream(old, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new System.Text.Json.Utf8JsonWriter(gzip)) file.WriteTo(writer);
        old.Position = 0;

        var loaded = SaveGame.Load(TestData.Game, old);
        Assert.True(loaded.Options[OptionIds.AvaEasyCasting]);
        Assert.Equal(10, loaded.SpellFailChance(TestData.Game.Spell("magic_missile")!));
    }
}
