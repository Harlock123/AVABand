using Angband.Core.Quests;

namespace Angband.Core.Game;

/// <summary>A step of a quest in the quest log: its journal words, and whether it's behind you.</summary>
public sealed record QuestLogStep(string Text, bool Done);

/// <summary>
/// A quest in the quest log (AVABand's own): a story quest or a notice-board job — who gave it, where it
/// stands ("Step 2 of 3", "4 of 6 killed"), its steps so far, and, for a job, the count and the pay.
/// </summary>
public sealed record QuestLogEntry(string Name, string Source, bool Done, string Status, IReadOnlyList<QuestLogStep> Steps,
    int Have = 0, int Need = 0, string Reward = "")
{
    /// <summary>Whether it has a count to show as a bar (a job's kills, things carried, depth reached).</summary>
    public bool HasCount => Need > 0;
}

// AVABand's quest log: every quest under way (the board's jobs and the story quests), then those done.
public sealed partial class GameSession
{
    public IReadOnlyList<QuestLogEntry> QuestLog()
    {
        var log = new List<QuestLogEntry>();
        foreach (var job in AvaQuests.Board.Where(j => j.Taken)) log.Add(JobEntry(job));
        foreach (var state in AvaQuests.Quests.Values.Reverse())
            if (QuestDef(state.Id) is not null) log.Add(StoryEntry(state));
        return [.. log.Where(e => !e.Done), .. log.Where(e => e.Done)];
    }

    private QuestLogEntry StoryEntry(AvaQuestState state)
    {
        var def = QuestDef(state.Id)!;
        var source = def.Source.Length > 0 ? def.Source : def.MinLevel > 0 ? "The Prancing Pony" : "Found in the dungeon";
        var steps = state.History
            .Select(stage => new QuestLogStep(Fill(def.Stages.GetValueOrDefault(stage, ""), state), true))
            .Where(s => s.Text.Length > 0)
            .Append(new QuestLogStep(QuestText(state.Id), state.IsDone))
            .ToList();
        // The steps along the way, and one ending (a quest can end more than one way).
        var total = def.Stages.Keys.Count(k => !new AvaQuestState { Stage = k }.IsDone) + 1;
        var status = state.IsDone ? "Complete" : $"Step {Math.Min(steps.Count, total - 1)} of {total}";
        return new QuestLogEntry(def.Name, source, state.IsDone, status, steps);
    }

    private QuestLogEntry JobEntry(BoardJob job)
    {
        var complete = JobComplete(job);
        var (have, need, unit) = job.Kind switch
        {
            "hunt" => (Math.Min(job.Progress, job.Count), job.Count, "killed"),
            "bounty" => (Math.Min(job.Progress, 1), 1, "slain"),
            "scout" => (Math.Min(Player.MaxDepth, job.Count), job.Count, "levels down"),
            "parcel" => (job.Progress, 1, "delivered"),
            "rescue" => (job.Progress, 1, "rescued"),
            _ => (Math.Min(job.Progress + GatherOnHand(job), job.Count), job.Count, "brought in, carried or at home"),
        };
        var status = complete ? "Ready: collect your pay at the Prancing Pony" : $"{have} of {need} {unit}";
        List<QuestLogStep> steps =
        [
            new(JobObjective(job), complete),
            new("Collect your pay from Butterbur at the Prancing Pony (9).", false),
        ];
        return new QuestLogEntry(JobTitle(job), "The notice board", false, status, steps, have, need, $"{job.Reward} gold");
    }

    /// <summary>What a job asks, for its first step in the log.</summary>
    private string JobObjective(BoardJob job)
    {
        var feet = Data.Constants.FeetPerLevel;
        return job.Kind switch
        {
            "hunt" => $"{JobTitle(job)} — only those killed since you took the note count.",
            "bounty" => $"Slay {Data.Monster(job.Target)?.Name ?? job.Target}, found about {Data.Monster(job.Target)?.Depth * feet} ft.",
            "scout" => $"Reach {job.Count * feet} ft (your deepest so far: {Player.MaxDepth * feet} ft).",
            "parcel" => $"Take the sealed parcel (in your quest satchel) to the {ShopName(job.Target)}: walking in hands it over.",
            "rescue" => $"Find the traveller trapped at {job.Count * feet} ft and free them ({RescueTimeLeft(job)}) — you'll hear them when you arrive.",
            _ => $"{JobTitle(job)} ({GatherSoFar(job)}) — no shop sells them; the dungeon has them. What you carry or keep in your Home counts, and you can hand them in a few at a time.",
        };
    }
}
