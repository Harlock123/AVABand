using Angband.Core.Game;
using Angband.Core.Geometry;

namespace Angband.Tests;

/// <summary>The monster list ('[', Angband mon-list.c) and object list (']', obj-list.c).</summary>
public class VisibleListTests
{
    // Two rooms: the player's, and one behind a wall, out of line of sight.
    private static GameSession Game()
    {
        var game = Arena.Create(5,
            "#####################",
            "#,,,,,,,,,,#,,,,,,,,#",
            "#,,@,,,,,,,#,,,,,,,,#",
            "#,,,,,,,,,,#,,,,,,,,#",
            "#####################");
        TestGames.ClearMonsters(game);
        game.Player.Hp = game.Player.MaxHp = 100_000;
        return game;
    }

    private static Loc At(GameSession game, int dx, int dy) => game.Player.Position + new Loc(dx, dy);

    private static List<string> Lines(IReadOnlyList<VisibleListRow> rows) =>
        rows.Select(r => r.Location.Length > 0 ? $"{r.Text} | {r.Location}" : r.Text).ToList();

    [Fact]
    public void NothingAround_SaysSo()
    {
        var game = Game();
        Assert.Equal(["You can see no monsters."], Lines(game.MonsterList()));
        Assert.Equal(["You can see no objects."], Lines(game.ObjectList()));
    }

    [Fact]
    public void Monsters_AreCountedByRace_WithSleepers_AndALoneOneSaysWhereItIs()
    {
        var game = Game();
        Arena.AddMonster(game, "jackal", At(game, 3, 0), awake: false);
        Arena.AddMonster(game, "jackal", At(game, 4, 1));
        Arena.AddMonster(game, "floating_eye", At(game, 4, -1), awake: false);
        var lines = Lines(game.MonsterList());
        Assert.Equal("You can see 3 monsters:", lines[0]);
        Assert.Contains("  2 jackals (1 asleep)", lines);
        Assert.Contains("  1 floating eye (asleep) | 1 N 4 E", lines);
    }

    [Fact]
    public void SensedMonsters_AreListedApart_AsOthers()
    {
        var game = Game();
        Arena.AddMonster(game, "jackal", At(game, 2, 0));
        var grip = Arena.AddMonster(game, "grip", new Loc(15, 2));
        grip.IsVisible = false;
        grip.IsDetected = true;
        var rows = game.MonsterList();
        var lines = Lines(rows);
        Assert.Equal("", lines[2]);
        Assert.Equal("You are aware of 1 other monster:", lines[3]);
        Assert.StartsWith("[U] Grip, Farmer Maggot's Dog | 0 N", lines[4]);
        Assert.Equal("Violet", rows[4].Color); // uniques stand out
    }

    [Fact]
    public void UnseenAndDisguisedMonsters_AreLeftOut()
    {
        var game = Game();
        var jackal = Arena.AddMonster(game, "jackal", At(game, 2, 0));
        var mimic = Arena.AddMonster(game, "jackal", At(game, 3, 0));
        jackal.IsVisible = false;
        mimic.Camouflaged = true;
        Assert.Equal(["You can see no monsters."], Lines(game.MonsterList()));
    }

    [Fact]
    public void DeeperMonstersComeFirst_InRed_OrByExperience()
    {
        var game = Game();
        game.Player.Depth = 1;
        Arena.AddMonster(game, "jackal", At(game, 2, 0));
        Arena.AddMonster(game, "cave_orc", At(game, 3, 0)); // level 7, out of depth here
        Arena.AddMonster(game, "floating_eye", At(game, 4, 0));
        var rows = game.MonsterList();
        Assert.Contains("cave orc", rows[1].Text);
        Assert.Equal("Red", rows[1].Color);
        Assert.Equal("White", rows[2].Color);

        var byExp = game.MonsterList(byExperience: true);
        var exp = byExp.Skip(1).Select(r => game.Data.Monsters.First(m => r.Text.EndsWith(m.Name)))
            .Select(m => (long)m.Experience * m.Depth / game.Player.Level).ToList();
        Assert.Equal(exp.OrderByDescending(x => x), exp);
    }

    [Fact]
    public void Names_FollowGetMonName()
    {
        var data = TestData.Game;
        Assert.Equal("  1 jackal", GameSession.MonsterListName(data.Monster("jackal")!, 1));
        Assert.Equal(" 12 jackals", GameSession.MonsterListName(data.Monster("jackal")!, 12));
        Assert.StartsWith("[U] ", GameSession.MonsterListName(data.Monster("grip")!, 1));
    }

    [Fact]
    public void Objects_InView_AndRemembered_WithWhereTheyAre()
    {
        var game = Game();
        var potions = game.Objects.Create("cure_light_wounds", 3);
        game.Knowledge.LearnKind(potions.Kind);
        game.Level.Objects.Add(At(game, 2, 1), potions);
        game.Level.Objects.Add(At(game, 1, 0), game.Objects.Create("dagger"));
        game.Level.Objects.Add(At(game, 3, 0), game.Objects.MakeGold(game.Rng, 5)); // gold isn't listed

        // Something seen earlier behind the wall: remembered, so "aware of".
        var far = new Loc(15, 2);
        var arrow = game.Objects.Create("arrow", 7);
        game.Level.Objects.Add(far, arrow);
        game.Known.RememberObject(far, arrow);
        game.UpdateView();

        var lines = Lines(game.ObjectList());
        Assert.Equal("You can see 2 objects:", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("  3 Potions of Cure Light Wounds") && l.EndsWith("| 1 S 2 E"));
        Assert.Contains(lines, l => l.StartsWith("  a Dagger") && l.EndsWith("| 0 N 1 E"));
        Assert.Equal("You are aware of 1 other object:", lines[4]);
        Assert.StartsWith("  7 Arrows", lines[5]);
        Assert.EndsWith($"| 0 N {15 - game.Player.Position.X} E", lines[5]);
    }

    [Fact]
    public void IgnoredObjects_AreLeftOut_UntilUnignored()
    {
        var game = Game();
        var dagger = game.Objects.Create("dagger");
        dagger.Ignored = true;
        game.Level.Objects.Add(At(game, 1, 0), dagger);
        game.UpdateView();
        Assert.Equal(["You can see no objects."], Lines(game.ObjectList()));
        game.Execute(new ToggleUnignoreCommand());
        Assert.Equal("You can see 1 object:", Lines(game.ObjectList())[0]);
    }

    [Fact]
    public void ArtifactsLead_ThenUnfamiliarThings_InTheirColours()
    {
        var game = Game();
        game.Level.Objects.Add(At(game, 4, 0), game.Objects.Create("dagger"));
        var potion = game.Objects.Create("speed"); // flavour not yet known
        game.Level.Objects.Add(At(game, 3, 0), potion);
        var phial = game.Objects.CreateArtifact(game.Data.Artifacts.Single(a => a.Id == "galadriel"));
        foreach (var rune in phial.Runes()) game.Knowledge.LearnRune(rune);
        game.Knowledge.LearnKind(phial.Kind);
        game.Level.Objects.Add(At(game, 5, 0), phial);
        game.UpdateView();

        var rows = game.ObjectList();
        Assert.Contains("Galadriel", rows[1].Text);
        Assert.Equal("Violet", rows[1].Color);
        Assert.Equal("LightRed", rows[2].Color);
        Assert.Contains("Dagger", rows[3].Text);
        Assert.Equal("White", rows[3].Color);
    }
}
