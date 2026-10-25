using Angband.Core.Game;
using Angband.Core.Magic;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>The potions' maximum (18/100): what a potion of Strength says and does there, and the sheet's colours.</summary>
public class StatCapTests
{
    private static GameSession Game()
    {
        var game = GameSession.NewGame(TestData.Game, 6, CharacterSpec.Default("human", "warrior"));
        foreach (var stat in CharacterSpec.StatIds) game.Player.NaturalStats[stat] = 15;
        game.Player.StatDrain.Clear();
        game.RecalculateBonuses();
        return game;
    }

    private static List<string> Messages(GameSession game)
    {
        var list = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => list.Add(m.Text));
        return list;
    }

    [Fact]
    public void ThePotionCap_Is18_100()
    {
        Assert.Equal("18/100", StatTables.Format(StatTables.PotionCap));
    }

    [Fact]
    public void BelowTheCap_APotionRaisesTheStat()
    {
        var game = Game();
        var said = Messages(game);
        game.Player.NaturalStats["str"] = StatTables.PotionCap - 1;
        game.Player.StatDrain["str"] = 2;
        Assert.True(game.GainStat("str"));
        Assert.Equal(StatTables.PotionCap, game.Player.NaturalStats["str"]);
        Assert.Equal(0, game.Player.StatDrain.GetValueOrDefault("str"));
        Assert.Contains("You feel very strong!", said);
    }

    [Fact]
    public void AtTheCap_APotionOnlyRestores_AndSaysSo()
    {
        var game = Game();
        var said = Messages(game);
        game.Player.NaturalStats["con"] = StatTables.PotionCap;
        Assert.True(game.GainStat("con")); // (still used up, and learned)
        Assert.Equal(StatTables.PotionCap, game.Player.NaturalStats["con"]);
        Assert.Equal(["You feel as healthy as you can be made."], said);

        said.Clear();
        game.Player.StatDrain["con"] = 3;
        game.GainStat("con");
        Assert.Equal(0, game.Player.StatDrain.GetValueOrDefault("con"));
        Assert.Equal(["You feel less sickly.", "You feel as healthy as you can be made."], said);
    }

    [Fact]
    public void ThePotionsDescription_SaysWhichStatsAreAtTheCap()
    {
        var game = Game();
        string Describe(string kind)
        {
            var item = game.Objects.Create(kind);
            game.Knowledge.LearnKind(item.Kind);
            return ObjectInfo.DescribeItem(game, item);
        }
        Assert.DoesNotContain("as high as potions", Describe("potion_of_strength"));

        game.Player.NaturalStats["str"] = StatTables.PotionCap + 2; // gear doesn't count, birth can be above it
        Assert.Contains("Your strength is already as high as potions can raise it (18/100); this would only restore it if drained.",
            Describe("potion_of_strength"));
        Assert.DoesNotContain("as high as potions", Describe("potion_of_intelligence"));

        game.Player.NaturalStats["con"] = StatTables.PotionCap;
        Assert.Contains("Your strength and constitution are already as high as potions can raise them (18/100); this would only restore them if drained.",
            Describe("draught_of_the_ents"));
        Assert.Contains("Your strength and constitution are already as high", Describe("potion_of_augmentation"));
    }

    [Fact]
    public void TheSheetColoursStats_AtTheCapGold_DrainedRed()
    {
        var game = Game();
        var p = game.Player;
        p.NaturalStats["str"] = StatTables.PotionCap;
        p.NaturalStats["dex"] = StatTables.PotionCap + 5;
        p.NaturalStats["con"] = StatTables.PotionCap;
        p.StatDrain["con"] = 1;  // drained wins
        p.StatDrain["int"] = 2;
        game.RecalculateBonuses();

        Assert.Equal(CharacterDump.AtMaximumColor, CharacterDump.StatColor(p, "str"));
        Assert.Equal(CharacterDump.AtMaximumColor, CharacterDump.StatColor(p, "dex"));
        Assert.Equal(CharacterDump.DrainedColor, CharacterDump.StatColor(p, "con"));
        Assert.Equal(CharacterDump.DrainedColor, CharacterDump.StatColor(p, "int"));
        Assert.Null(CharacterDump.StatColor(p, "wis"));

        var marked = CharacterDump.BuildMarked(game, [], new DateTime(2026, 10, 25));
        var runs = RecallMarkup.Parse(marked).Where(r => r.Color is not null).ToList();
        Assert.Contains((StatTables.Format(p.Stats["str"]), CharacterDump.AtMaximumColor), runs);
        Assert.Contains((StatTables.Format(p.Stats["int"]), CharacterDump.DrainedColor), runs);
        Assert.Contains("as high as potions raise it", marked);

        // The file and the clipboard get the plain sheet, as before: no colours, no key.
        var plain = CharacterDump.Build(game, [], new DateTime(2026, 10, 25));
        Assert.DoesNotContain("as high as potions raise it", plain);
        Assert.DoesNotContain('\u0001', plain);
        var strRow = plain.Split('\n').First(l => l.StartsWith(" STR"));
        Assert.Equal(strRow, RecallMarkup.Strip(marked).Split('\n').First(l => l.StartsWith(" STR")));
    }
}
