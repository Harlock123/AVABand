using Angband.Core.Definitions;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Quests;

namespace Angband.Core.Game;

// The Prancing Pony's notice board: three postings at a time — hunt so many of a monster, or bring so
// many of a thing found in the dungeon (never one the shops sell) — renewed whenever you come back up
// to town. Two can be taken at once; hunts count from when they're taken, and the pay is collected
// at the inn.
public sealed partial class GameSession
{
    private const int BoardPostings = 3;
    private const int BoardTakenMax = 2;

    /// <summary>Back in town from the dungeon: the postings nobody took are replaced.</summary>
    private void RenewBoard()
    {
        if (!AvaQuestsOn) return;
        AvaQuests.Board.RemoveAll(j => !j.Taken);
        for (var tries = 0; AvaQuests.Board.Count(j => !j.Taken) < BoardPostings && tries < 20; tries++)
            if (MakeJob() is { } job) AvaQuests.Board.Add(job);
    }

    private BoardJob? MakeJob()
    {
        var deepest = Math.Max(Player.MaxDepth, 1);
        if (Rng.OneIn(2))
        {
            var unavailable = new HashSet<string>(KilledUniques);
            var race = _spawner.PickRace(Rng, Math.Max(1, deepest - Rng.RandInt0(4)), unavailable,
                r => !r.IsUnique && !r.Has(MonsterFlags.Questor) && !r.Has(MonsterFlags.NeverMove) && r.Depth >= Math.Max(1, deepest - 8),
                allowOutOfDepth: false);
            if (race is null || AvaQuests.Board.Any(j => j.Target == race.Id)) return null;
            var count = 4 + Rng.RandInt0(7);
            return new BoardJob
            {
                Id = AvaQuests.NextJobId++, Kind = "hunt", Target = race.Id, Count = count,
                Reward = count * (10 + (long)race.Experience * Math.Max(1, race.Depth) / 10),
            };
        }
        // Things the dungeon gives up that no shop sells.
        var stocked = Data.Stores.SelectMany(s => s.Staples.Concat(s.Normal)).ToHashSet();
        var kinds = Data.Objects.Where(k => k.Commonness > 0 && (k.MinDepth ?? k.Level) <= deepest + 2 && k.Level <= deepest + 2
                                            && k.Base is "potion" or "scroll" or "mushroom" or "food" && !stocked.Contains(k.Id) && k.Cost > 0).ToList();
        if (kinds.Count == 0) return null;
        var kind = kinds[Rng.RandInt0(kinds.Count)];
        if (AvaQuests.Board.Any(j => j.Target == kind.Id)) return null;
        var n = 2 + Rng.RandInt0(3);
        return new BoardJob { Id = AvaQuests.NextJobId++, Kind = "gather", Target = kind.Id, Count = n, Reward = 50 + kind.Cost * n * 3 };
    }

    /// <summary>A posting's name, e.g. "Hunt 6 cave spiders", "Bring 3 Potions of Resist Poison".</summary>
    public string JobTitle(BoardJob job)
    {
        if (job.Kind == "hunt")
        {
            var race = Data.Monster(job.Target);
            var name = race is null ? job.Target : job.Count == 1 ? race.Name : race.Plural ?? race.Name + "s";
            return $"Hunt {job.Count} {name}";
        }
        var kind = Data.Object(job.Target);
        var sample = kind is null ? null : Objects.Create(kind.Id, job.Count);
        return $"Bring {(sample is null ? job.Target : ItemNaming.Describe(sample, Knowledge, withArticle: false))}";
    }

    public string JobText(BoardJob job) => job.Kind == "hunt"
        ? $"{JobTitle(job)} ({job.Progress} so far), for {job.Reward} gold at the Prancing Pony."
        : $"{JobTitle(job)} to the Prancing Pony ({Math.Min(CarriedCount(job.Target), job.Count)} carried), for {job.Reward} gold.";

    private int CarriedCount(string kindId) => Player.Inventory.Pack.Where(i => i.Kind.Id == kindId).Sum(i => i.Number);

    private bool JobComplete(BoardJob job) => job.Kind == "hunt" ? job.Progress >= job.Count : CarriedCount(job.Target) >= job.Count;

    private void ShowBoard()
    {
        if (AvaQuests.Board.Count == 0) RenewBoard();
        var choices = new List<(string, string)>();
        var taken = AvaQuests.Board.Count(j => j.Taken);
        foreach (var job in AvaQuests.Board)
        {
            if (job.Taken) choices.Add(($"board:drop:{job.Id}", $"Give up: {JobTitle(job)} ({job.Reward} gold)"));
            else if (taken < BoardTakenMax) choices.Add(($"board:take:{job.Id}", $"Take: {JobTitle(job)} ({job.Reward} gold)"));
        }
        choices.Add(("inn:", "Back"));
        AskQuest("The notice board", taken >= BoardTakenMax
                ? "Notes pinned three deep, most of them years old. You already have as many jobs as you can manage."
                : "Notes pinned three deep, most of them years old. A few are fresh.",
            [.. choices]);
    }

    private void BoardChoice(string[] parts)
    {
        if (parts.Length < 3 || !int.TryParse(parts[2], out var id) || AvaQuests.Board.FirstOrDefault(j => j.Id == id) is not { } job) return;
        switch (parts[1])
        {
            case "take" when !job.Taken && AvaQuests.Board.Count(j => j.Taken) < BoardTakenMax:
                job.Taken = true;
                Publish(new MessageEvent($"You take the note down: {JobTitle(job)}."));
                ShowBoard();
                break;
            case "drop" when job.Taken:
                AvaQuests.Board.Remove(job);
                Publish(new MessageEvent($"You pin the note back up for someone else: {JobTitle(job)}."));
                ShowBoard();
                break;
            case "collect" when job.Taken && JobComplete(job):
                if (job.Kind == "gather")
                {
                    var left = job.Count;
                    foreach (var stack in Player.Inventory.Pack.Where(i => i.Kind.Id == job.Target).ToList())
                    {
                        var take = Math.Min(left, stack.Number);
                        Player.Inventory.Remove(stack, take, () => Objects.NextSerial++);
                        left -= take;
                        if (left == 0) break;
                    }
                }
                Player.Gold += job.Reward;
                AvaQuests.Board.Remove(job);
                AvaQuests.JobsDone++;
                Publish(new MessageEvent($"Butterbur counts out {job.Reward} gold. \"Well done. There's always more work.\""));
                InnChoice("");
                break;
        }
    }

    private void BoardMonsterKilled(Monster monster)
    {
        foreach (var job in AvaQuests.Board.Where(j => j.Taken && j.Kind == "hunt" && j.Target == monster.Race.Id && j.Progress < j.Count))
        {
            job.Progress++;
            if (job.Progress == job.Count) Publish(new MessageEvent($"That's the last of them: {JobTitle(job)} is done. Collect at the Prancing Pony."));
        }
    }
}
