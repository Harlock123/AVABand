using Angband.Core.Game;
using Angband.Core.Persistence;

namespace Angband.Tests;

/// <summary>The quest log (AVABand's own), and holding many quests and jobs at once.</summary>
public partial class AvaQuestTests
{
    private static void TakeEveryJob(Quester q)
    {
        q.EnterShop("inn");
        q.ChooseLabel("Read the notice board");
        while (q.Last.Choices.FirstOrDefault(c => c.Id.StartsWith("board:take")) is { } take) q.Choose(take.Id);
    }

    [Fact]
    public void The_board_posts_five_jobs_and_all_five_can_be_taken()
    {
        var q = Start(level: 15);
        q.Jump(5);
        TakeEveryJob(q);
        Assert.Equal(GameSession.BoardPostings, q.Game.AvaQuests.Board.Count(j => j.Taken));
        Assert.Equal(5, GameSession.BoardTakenMax);
        Assert.Contains("as many jobs as you can manage", q.Last.Text);
    }

    [Fact]
    public void The_log_lists_every_quest_and_job_under_way_before_those_done()
    {
        var q = Start(level: 25);
        var game = q.Game;
        q.Jump(5);
        TakeEveryJob(q);
        q.TakeQuest("sealed_door");
        q.TakeQuest("cartographer");

        var log = game.QuestLog();
        Assert.Equal(7, log.Count(e => !e.Done));                       // five jobs and two story quests at once
        var door = log.Single(e => e.Name == "The Sealed Door");
        Assert.Equal("The Prancing Pony", door.Source);
        Assert.Equal("Step 1 of 3", door.Status);
        Assert.Equal(game.QuestText("sealed_door"), Assert.Single(door.Steps).Text);

        var job = game.AvaQuests.Board.First(j => j.Kind == "hunt" || j.Kind == "gather");
        var entry = log.Single(e => e.Name == game.JobTitle(job));
        Assert.Equal("The notice board", entry.Source);
        Assert.True(entry.HasCount);
        Assert.Equal((0, job.Count), (entry.Have, entry.Need));
        Assert.Equal($"{job.Reward} gold", entry.Reward);
        Assert.EndsWith(job.Kind == "hunt" ? "killed" : "carried", entry.Status);
        Assert.Equal("Collect your pay from Butterbur at the Prancing Pony (9).", entry.Steps[^1].Text);
    }

    [Fact]
    public void A_job_done_says_to_collect_the_pay()
    {
        var q = Start(level: 15);
        q.Jump(5);
        TakeEveryJob(q);
        var hunt = q.Game.AvaQuests.Board.FirstOrDefault(j => j.Kind == "hunt");
        Assert.NotNull(hunt);
        hunt.Progress = hunt.Count;
        var entry = q.Game.QuestLog().Single(e => e.Name == q.Game.JobTitle(hunt));
        Assert.Equal("Ready: collect your pay at the Prancing Pony", entry.Status);
        Assert.Equal(entry.Need, entry.Have);
        Assert.True(entry.Steps[0].Done);
    }

    /// <summary>A story quest's steps: those behind you ticked, the one at hand last; done, it goes to the end.</summary>
    [Fact]
    public void The_log_shows_a_quests_steps_as_it_goes()
    {
        var q = Start(level: 25);
        var game = q.Game;
        q.EnterShop("artificer");
        q.Choose("artificer:trouble");
        q.Choose("accept:chisel");
        var state = game.AvaQuests.Get("chisel")!;
        q.Jump(state.N("depth"));
        game.Player.Position = game.Level.Objects.All.Single(o => o.Item.Kind.Id == "star_forged_chisel").Loc;
        game.Execute(new PickupCommand());

        var entry = game.QuestLog().Single(e => e.Name == "The Artificer's Chisel");
        Assert.Equal("The Arcane Artificer", entry.Source);
        Assert.Equal("Step 2 of 3", entry.Status);
        Assert.Equal([true, false], entry.Steps.Select(s => s.Done));
        Assert.Equal(["find"], state.History);

        q.TakeQuest("sealed_door");
        q.EnterShop("artificer");
        q.Choose("chisel:return");
        var log = game.QuestLog();
        entry = log.Single(e => e.Name == "The Artificer's Chisel");
        Assert.True(entry.Done);
        Assert.Equal("Complete", entry.Status);
        Assert.Equal(3, entry.Steps.Count);
        Assert.All(entry.Steps, s => Assert.True(s.Done));
        Assert.Same(entry, log[^1]);                                      // done: after those under way
        Assert.False(log[0].Done);
    }

    [Fact]
    public void A_quests_steps_are_kept_in_the_save()
    {
        var q = Start(level: 25);
        q.TakeQuest("sealed_door");
        q.Game.AvaQuests.Get("sealed_door")!.History.Add("hunt");
        using var stream = new MemoryStream();
        SaveGame.Save(q.Game, stream);
        stream.Position = 0;
        Assert.Equal(["hunt"], SaveGame.Load(TestData.Game, stream).AvaQuests.Get("sealed_door")!.History);
    }
}
