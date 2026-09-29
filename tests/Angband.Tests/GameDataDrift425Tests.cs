using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Randomness;
using Angband.Core.Records;

namespace Angband.Tests;

/// <summary>
/// blow_methods.txt, realm.txt, object_base.txt, flavor.txt and names.txt as 4.2.5 has them, and what
/// the game does with them.
/// </summary>
public class GameDataDrift425Tests
{
    private static GameData Data => TestData.Game;

    [Fact]
    public void Blow_methods_are_4_2_5s()
    {
        var gaze = Data.BlowMethod("gaze")!;
        Assert.False(gaze.Miss);   // a gaze that misses isn't announced
        Assert.False(gaze.Phys);
        Assert.True(Data.BlowMethod("hit")! is { Miss: true, Phys: true, Cut: true, Stun: true });
        Assert.Equal(8, Data.BlowMethod("insult")!.Messages.Count);
        Assert.Equal("release spores", Data.BlowMethod("spore")!.Description);
    }

    [Fact]
    public void A_blow_message_fills_in_its_tags()
    {
        var insult = Data.BlowMethod("insult")!;
        var seen = new HashSet<string>();
        var rng = new GameRandom(3);
        for (var i = 0; i < 400; i++) seen.Add(insult.Act(rng));
        Assert.Equal(8, seen.Count);
        Assert.Contains("insults your mother!", seen);
        Assert.Contains("moons you!!!", seen);
        Assert.All(seen, s => Assert.DoesNotContain("{", s));
        Assert.Contains("asks if you have seen his dogs", Enumerable.Range(0, 400).Select(_ => Data.BlowMethod("moan")!.Act(rng)));
    }

    [Fact]
    public void Recall_describes_blows_as_4_2_5_does()
    {
        var race = Data.Monster("grip")!;
        var lore = new RaceLore();
        for (var i = 0; i < race.Blows.Count; i++) lore.SeeBlow(i);
        Assert.Contains("can bite", MonsterRecall.Describe(Data, race, lore, 1));
    }

    [Fact]
    public void Nature_magic_is_chanted_in_verses()
    {
        var nature = Data.Realms.First(r => r.Id == "nature");
        Assert.Equal(("chant", "verse"), (nature.Verb, nature.SpellNoun));
    }

    [Fact]
    public void Object_bases_break_and_burn_as_4_2_5s()
    {
        Assert.Equal(0, Data.ObjectBase("shot")!.BreakChance);
        Assert.Equal(20, Data.ObjectBase("bolt")!.BreakChance);
        Assert.Equal(10, Data.ObjectBase("wand")!.BreakChance);
        Assert.Contains("fire", Data.ObjectBase("sling")!.Hates);   // 4.2.5's one bow base hates acid and fire
        Assert.Contains("EASY_KNOW", Data.ObjectBase("potion")!.Flags);
    }

    [Fact]
    public void Unflavoured_kinds_have_their_own_colours()
    {
        Assert.NotNull(Data.Object("dagger")!.Color);
        Assert.Null(Data.Object("potion_of_cure_light_wounds")?.Color ?? Data.Objects.First(o => o.Base == "potion").Color);
        Assert.NotEqual(Data.Object("copper")?.Color, Data.Object("silver")?.Color);
    }

    [Fact]
    public void The_rings_of_power_keep_their_stones()
    {
        var knowledge = new PlayerKnowledge(Data, 7);
        Assert.Equal("Plain Gold", knowledge.Flavor(Data.Object("ring_of_power")!)?.Name);
        Assert.Equal("Ruby", knowledge.Flavor(Data.Object("ring_of_fire")!)?.Name);
        Assert.DoesNotContain(Data.Objects.Where(o => o.Base == "ring" && !o.IsSpecialArtifactKind),
            k => knowledge.Flavor(k)?.Name is "Plain Gold" or "Ruby");
    }

    [Fact]
    public void Scroll_titles_are_made_up_words_under_fifteen_letters()
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var knowledge = new PlayerKnowledge(Data, seed);
            var titles = Data.Objects.Where(o => o.Base == "scroll").Select(o => knowledge.Flavor(o)!.Name).ToList();
            Assert.Equal(titles.Count, titles.Distinct().Count());
            Assert.All(titles, t =>
            {
                Assert.InRange(t.Length, 2, 14);
                Assert.All(t.Split(' '), w => Assert.InRange(w.Length, 2, 9));
                Assert.Matches("^[a-z ]+$", t);
            });
        }
    }
}
