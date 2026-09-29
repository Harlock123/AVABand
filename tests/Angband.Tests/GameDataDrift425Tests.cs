using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Magic;
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

    [Fact]
    public void Characters_are_born_with_a_background_age_height_and_weight()
    {
        var hobbit = GameSession.NewGame(Data, 12, CharacterSpec.Default("hobbit", "rogue"));
        var p = hobbit.Player;
        Assert.StartsWith("You are", p.Background);
        var race = Data.Race("hobbit")!;
        Assert.InRange(p.Age, race.Age.Base + 1, race.Age.Base + race.Age.Mod);
        Assert.InRange(p.Height, race.Height.Base - 5 * race.Height.Mod, race.Height.Base + 5 * race.Height.Mod);
        Assert.True(p.Weight > 0);
        // The same seed, the same background; the background has its own dice.
        Assert.Equal(p.Background, GameSession.NewGame(Data, 12, CharacterSpec.Default("hobbit", "rogue")).Player.Background);
        var dump = CharacterDump.Build(hobbit);
        Assert.Contains($"Age    {p.Age}", dump);
        Assert.Contains(p.Background.Split(' ')[3], dump);

        using var stream = new MemoryStream();
        Angband.Core.Persistence.SaveGame.Save(hobbit, stream);
        stream.Position = 0;
        var loaded = Angband.Core.Persistence.SaveGame.Load(Data, stream).Player;
        Assert.Equal((p.Age, p.Height, p.Weight, p.Background), (loaded.Age, loaded.Height, loaded.Weight, loaded.Background));
    }

    [Fact]
    public void A_background_follows_the_charts_to_their_end()
    {
        foreach (var race in Data.Races)
        {
            var text = GameSession.BackgroundFrom(Data, race.History, new GameRandom(1));
            Assert.False(string.IsNullOrWhiteSpace(text), race.Id);
        }
    }

    [Fact]
    public void A_hurt_monster_shows_its_pain_as_4_2_5_says_it()
    {
        var game = Arena.Create(3);
        var orc = Arena.AddMonster(game, "cave_orc", game.Player.Position + new Angband.Core.Geometry.Loc(2, 0));
        orc.Hp = orc.MaxHp = 100;
        orc.Hp = 90;
        Assert.Equal("The cave orc grunts with pain.", game.PainMessage(orc, 10));    // 90% left
        orc.Hp = 5;
        Assert.Equal("The cave orc cries out feebly.", game.PainMessage(orc, 95));   // 5% left
        Assert.Equal("The cave orc is unharmed.", game.PainMessage(orc, 0));
    }

    [Fact]
    public void Abilities_are_named_as_the_birth_screen_names_them()
    {
        var elf = Birth.Abilities(Data, Data.Race("elf"), Data.Class("mage")).Select(a => a.Name).ToList();
        Assert.Equal(["Full Spellcaster", "Extra Spell Beaming", "Spell Choice", "Sustain Dexterity", "Light Resistance"], elf);
        var warrior = Birth.Abilities(Data, Data.Race("human"), Data.Class("warrior"));
        Assert.Contains(("No Magic", "You cannot cast spells."), warrior);
    }

    [Fact]
    public void The_character_sheet_gives_the_title_for_the_level()
    {
        var game = GameSession.NewGame(Data, 3, "warrior");
        game.GainExperience(game.ExperienceForLevel(11) - game.Player.Experience);
        var dump = CharacterDump.Build(game);
        Assert.Contains($"Title  {Data.Class("warrior")!.Titles[(game.Player.Level - 1) / 5]}", dump);
        Assert.Contains("[Abilities]", dump);
    }

    [Fact]
    public void Shopkeepers_sometimes_greet_you_or_pass_on_a_hint()
    {
        var game = GameSession.NewGame(Data, 8, "warrior");
        game.GainExperience(game.ExperienceForLevel(29) - game.Player.Experience);
        var said = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => said.Add(m.Text));
        var shop = game.Level.AllLocs().First(p => Data.Terrain[game.Level[p].Feature].Shop is { } id && id != "home");
        for (var i = 0; i < 60; i++)
        {
            game.Player.Position = shop;
            game.Execute(new EnterStoreCommand());
        }
        Assert.Contains(said, m => Data.Hints.Any(h => m == $"\"{h}\""));
        Assert.Contains(said, m => m.Contains(": \"") && !Data.Hints.Any(h => m == $"\"{h}\""));
    }

    [Fact]
    public void Cure_all_heals_cures_and_feeds_as_the_wizard_command_does()
    {
        var game = GameSession.NewGame(Data, 4, "warrior");
        game.Player.Hp = 1;
        game.IncreaseTimed(TimedIds.Blind, 20);
        game.IncreaseTimed(TimedIds.Poisoned, 20);
        game.Player.Food = 100;
        game.Execute(new DebugCureAllCommand());
        Assert.Equal(game.Player.MaxHp, game.Player.Hp);
        Assert.False(game.Player.Timed.Has(TimedIds.Blind));
        Assert.False(game.Player.Timed.Has(TimedIds.Poisoned));
        Assert.Equal(Data.Constants.FoodFull - 1, game.Player.Food);
        Assert.True(game.IsCheater); // a debug command, so not scored
    }

    [Fact]
    public void The_One_Ring_is_a_plain_gold_ring_until_you_stand_on_it()
    {
        var game = GameSession.NewGame(Data, 6, "warrior");
        var ring = game.Objects.CreateArtifact(Data.Artifacts.First(a => a.Id == "the_one_ring"));
        Assert.Equal("a Plain Gold Ring", ItemNaming.Describe(ring, game.Knowledge, full: false));
        ring.Assessed = true; // (object_touch)
        Assert.StartsWith("the ", ItemNaming.Describe(ring, game.Knowledge, full: false));
        Assert.EndsWith("'The One Ring'", ItemNaming.Describe(ring, game.Knowledge, full: false));
    }
}
