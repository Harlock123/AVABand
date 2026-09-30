using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Monsters;

namespace Angband.Tests;

/// <summary>The Apprentice, the Cartographer, the Warden's Fires, and the board's bounties and scouting.</summary>
public partial class AvaQuestTests
{
    [Fact]
    public void The_Apprentice_given_your_Word_of_Recall_is_home_at_once()
    {
        var q = Start();
        var game = q.Game;
        q.TakeQuest("apprentice");
        var state = game.AvaQuests.Get("apprentice")!;
        Assert.Equal("find", state.Stage);
        game.Player.Inventory.Add(game.Objects.Create("scroll_of_word_of_recall", 2));
        var scrolls = q.Carried("scroll_of_word_of_recall")!.Number;
        q.Jump(state.N("depth"));
        Assert.Contains(q.Said, s => s.Contains("someone is calling for help"));

        var hollow = q.FeatureLoc("trapped_apprentice");
        q.WalkInto(hollow);
        Assert.Equal("The trapped apprentice", q.Last.Title);
        Assert.Contains(q.Last.Choices, c => c.Id == "apprentice:recall");
        q.Choose("apprentice:recall");
        Assert.Equal("rescued", state.Stage);
        Assert.Equal(scrolls - 1, q.Carried("scroll_of_word_of_recall")!.Number); // it cost you one
        Assert.Equal("floor", game.Level.FeatureAt(hollow).Id);

        var gold = game.Player.Gold;
        q.EnterShop("alchemist");
        Assert.Equal("The Alchemy shop", q.Last.Title);
        q.Choose("apprentice:reward");
        Assert.Equal("done", state.Stage);
        Assert.True(game.Player.Gold > gold);
        Assert.Equal(-15, game.AvaQuests.PriceAdjust["alchemist"]);
    }

    [Fact]
    public void The_Apprentice_without_a_scroll_can_only_be_sent_alone_and_his_fate_is_settled_then()
    {
        foreach (var seed in new ulong[] { 3, 4, 5, 6, 7, 8 })
        {
            var q = Start(seed: seed);
            var game = q.Game;
            q.TakeQuest("apprentice");
            var state = game.AvaQuests.Get("apprentice")!;
            foreach (var s in game.Player.Inventory.Pack.Where(i => i.Kind.Id == "scroll_of_word_of_recall").ToList())
                game.Player.Inventory.Remove(s, s.Number, () => game.Objects.NextSerial++);
            q.Jump(state.N("depth"));
            q.WalkInto(q.FeatureLoc("trapped_apprentice"));
            Assert.DoesNotContain(q.Last.Choices, c => c.Id == "apprentice:recall");
            q.Choose("apprentice:alone");
            Assert.Equal("alone", state.Stage);
            var fate = state.N("madeit");

            // Asking again doesn't roll again: the answer at the shop is always the one settled.
            q.EnterShop("alchemist");
            var first = q.Last.Choices.Single().Id;
            q.Jump(1);
            q.EnterShop("alchemist");
            Assert.Equal(first, q.Last.Choices.Single().Id);
            Assert.Equal(fate == 1 ? "apprentice:limped" : "apprentice:lost", first);
            q.Choose(first);
            Assert.Equal(fate == 1 ? "done" : "lost", state.Stage);
            Assert.True(state.IsDone);
        }
    }

    [Fact]
    public void The_Cartographer_wants_three_strange_levels_mapped()
    {
        var q = Start(level: 10);
        var game = q.Game;
        q.TakeQuest("cartographer");
        var state = game.AvaQuests.Get("cartographer")!;
        Assert.Equal("map", state.Stage);

        // An ordinary level doesn't count, however well mapped.
        q.Jump(5);
        game.Level.ProfileId = "classic";
        game.Known.RememberAll(game.Level);
        for (var i = 0; i < 20; i++) game.Execute(new HoldCommand());
        Assert.Equal(0, state.N("mapped"));

        foreach (var (profile, n) in new[] { ("cavern", 1), ("labyrinth", 2), ("moria", 3) })
        {
            q.Jump(5 + n);
            game.Level.ProfileId = profile;
            Assert.True(game.MappedFraction() < 0.75);
            game.Known.RememberAll(game.Level);                 // as a Rod of Magic Mapping, or a long walk
            Assert.True(game.MappedFraction() >= 0.75);
            for (var i = 0; i < 20 && state.N("mapped") < n; i++) game.Execute(new HoldCommand());
            Assert.Equal(n, state.N("mapped"));
        }
        Assert.Equal("deliver", state.Stage);
        Assert.Contains("a cavern, a labyrinth, the old mines", state.Texts["which"]);

        q.EnterShop("bookseller");
        q.Choose("cartographer:deliver");
        Assert.Equal("done", state.Stage);
        Assert.NotNull(q.Carried("rod_of_treasure_location"));
        Assert.Equal(-15, game.AvaQuests.PriceAdjust["bookseller"]);
    }

    [Fact]
    public void The_Wardens_Fires_three_braziers_make_the_Shade_mortal_and_it_puts_them_out()
    {
        var q = Start();
        var game = q.Game;
        q.TakeQuest("warden");
        Assert.NotNull(q.Carried("wardens_taper"));
        var state = game.AvaQuests.Get("warden")!;
        q.Jump(state.N("depth"));
        var shade = q.Find("the_shade_of_the_stair")!;
        Assert.False(game.DamageMonster(shade, 100_000));     // no blade touches it yet
        var braziers = game.Level.AllLocs().Where(l => game.Level.FeatureAt(l).Id == "cold_brazier").ToList();
        Assert.Equal(3, braziers.Count);

        // One lit, and the Shade, passing it, puts it out.
        q.WalkInto(braziers[0]);
        Assert.Equal(1, state.N("lit"));
        Assert.Equal("lit_brazier", game.Level.FeatureAt(braziers[0]).Id);
        for (var i = 0; i < 300 && game.Level.FeatureAt(braziers[0]).Id == "lit_brazier"; i++)
        {
            var beside = game.Level.AllLocs().FirstOrDefault(l => l.ChebyshevTo(braziers[0]) == 1 && game.Level.IsEmptyFloor(l)
                                                                   && l.ChebyshevTo(game.Player.Position) > 1);
            if (beside != default && shade.Position.ChebyshevTo(braziers[0]) > 1) game.Level.Monsters.Move(shade, beside);
            game.Execute(new HoldCommand());
        }
        Assert.Equal("cold_brazier", game.Level.FeatureAt(braziers[0]).Id);
        Assert.Equal(0, state.N("lit"));

        // All three, quickly: now it can die.
        game.Level.Monsters.Remove(shade);
        foreach (var b in braziers) q.WalkInto(b);
        Assert.Equal("slay", state.Stage);
        Assert.Contains(q.Said, s => s.Contains("the Shade can be harmed now"));
        var spot = game.Level.AllLocs().First(l => game.Level.IsEmptyFloor(l) && l.DistanceTo(game.Player.Position) > 3);
        var again = new MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster("the_shade_of_the_stair")!, spot);
        Assert.True(game.DamageMonster(again, 100_000));
        Assert.Equal("done", state.Stage);
    }

    [Fact]
    public void The_board_posts_bounties_on_uniques_and_scouting_too()
    {
        var q = Start(level: 12);
        var game = q.Game;
        var kinds = new HashSet<string>();
        for (var i = 0; i < 40 && !(kinds.Contains("bounty") && kinds.Contains("scout")); i++)
        {
            q.Jump(8);
            q.EnterShop("inn"); // coming back up renews the untaken postings
            foreach (var job in game.AvaQuests.Board) kinds.Add(job.Kind);
            var bounty = game.AvaQuests.Board.FirstOrDefault(j => j.Kind == "bounty");
            if (bounty is not null)
            {
                var race = game.Data.Monster(bounty.Target)!;
                Assert.True(race.IsUnique);
                Assert.False(race.Has(MonsterFlags.Questor));
                Assert.StartsWith("Bounty: ", game.JobTitle(bounty));
            }
            if (game.AvaQuests.Board.FirstOrDefault(j => j.Kind == "scout") is { } scout)
            {
                Assert.True(scout.Count > game.Player.MaxDepth);
                Assert.Matches(@"^Scout down to \d+ ft$", game.JobTitle(scout));
            }
        }
        Assert.Contains("bounty", kinds);
        Assert.Contains("scout", kinds);
    }
}
