using Angband.Core.Definitions;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Quests;

namespace Angband.Core.Game;

// The Prancing Pony's notice board: five postings at a time — hunt so many of a monster, bring so
// many of a thing found in the dungeon (never one the shops sell), a bounty on a unique you could
// face, or scouting a depth no deeper than a few levels past your deepest — renewed whenever you come
// back up to town. All five can be taken at once. A "bring" job counts what's carried and what's
// in the Home, and can be handed in a little at a time (Progress counts what Butterbur has had); hunts count from when they're taken, and the pay is collected
// at the inn.
public sealed partial class GameSession
{
    public const int BoardPostings = 5;
    public const int BoardTakenMax = 5;

    /// <summary>Back in town from the dungeon: the postings nobody took are replaced.</summary>
    private void RenewBoard()
    {
        if (!AvaQuestsOn) return;
        CheckRescueDeadlines();
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
        // Now and then: a bounty on a unique, or a scouting job.
        if (Rng.OneIn(3))
            return Rng.RandInt0(4) switch
            {
                0 => MakeBounty(deepest), 1 => MakeScouting(deepest), 2 => MakeParcel(deepest), _ => MakeRescue(deepest),
            };
        // Things the dungeon gives up that no shop sells.
        var stocked = Data.Stores.SelectMany(s => s.Staples.Concat(s.Stocked)).ToHashSet();
        var kinds = Data.Objects.Where(k => k.Commonness > 0 && (k.MinDepth ?? k.Level) <= deepest + 2 && k.Level <= deepest + 2
                                            && k.Base is "potion" or "scroll" or "mushroom" or "food" && !stocked.Contains(k.Id) && k.Cost > 0).ToList();
        if (kinds.Count == 0) return null;
        var kind = kinds[Rng.RandInt0(kinds.Count)];
        if (AvaQuests.Board.Any(j => j.Target == kind.Id)) return null;
        var n = 2 + Rng.RandInt0(3);
        return new BoardJob { Id = AvaQuests.NextJobId++, Kind = "gather", Target = kind.Id, Count = n, Reward = 50 + kind.Cost * n * 3 };
    }

    /// <summary>A bounty on a living unique no deeper than a little past your deepest (not a quest's own).</summary>
    private BoardJob? MakeBounty(int deepest)
    {
        var uniques = Data.Monsters.Where(r => r.IsUnique && !r.Has(MonsterFlags.Questor) && r.Depth >= 1 && r.Depth <= deepest + 2
                                               && r.Depth >= deepest - 10 && !KilledUniques.Contains(r.Id)
                                               && !Suspects.Contains(r.Id) // (the Black Market thief's suspects are a quest's)
                                               && AvaQuests.Board.All(j => j.Target != r.Id)).ToList();
        if (uniques.Count == 0) return null;
        var race = uniques[Rng.RandInt0(uniques.Count)];
        return new BoardJob { Id = AvaQuests.NextJobId++, Kind = "bounty", Target = race.Id, Count = 1, Reward = 100 + 3L * race.Experience * Math.Max(1, race.Depth) / 10 };
    }

    /// <summary>Scouting: reach a depth three to five levels past your deepest, and come back to tell of it.</summary>
    private BoardJob? MakeScouting(int deepest)
    {
        var target = Math.Min(Data.Constants.MaxDepth - 1, deepest + 3 + Rng.RandInt0(3));
        if (target <= deepest || AvaQuests.Board.Any(j => j.Kind == "scout")) return null;
        return new BoardJob { Id = AvaQuests.NextJobId++, Kind = "scout", Target = target.ToString(System.Globalization.CultureInfo.InvariantCulture), Count = target, Reward = 100 + 40L * target };
    }

    /// <summary>A posting's name, e.g. "Hunt 6 cave spiders", "Bring 3 Potions of Resist Poison", "Bounty: Grip, Farmer Maggot's Dog", "Scout down to 1250 ft".</summary>
    public string JobTitle(BoardJob job)
    {
        if (job.Kind == "bounty") return $"Bounty: {Data.Monster(job.Target)?.Name ?? job.Target}";
        if (job.Kind == "scout") return $"Scout down to {job.Count * Data.Constants.FeetPerLevel} ft";
        if (job.Kind == "parcel") return $"Deliver a parcel to the {ShopName(job.Target)}";
        if (job.Kind == "rescue") return $"Rescue a traveller trapped at {job.Count * Data.Constants.FeetPerLevel} ft";
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

    public string JobText(BoardJob job) => job.Kind switch
    {
        "hunt" => $"{JobTitle(job)} ({job.Progress} so far), for {job.Reward} gold at the Prancing Pony.",
        "bounty" => $"{JobTitle(job)} ({(job.Progress > 0 ? "done" : $"found about {Data.Monster(job.Target)?.Depth * Data.Constants.FeetPerLevel} ft")}), for {job.Reward} gold at the Prancing Pony.",
        "scout" => $"{JobTitle(job)} ({(JobComplete(job) ? "done" : $"deepest so far {Player.MaxDepth * Data.Constants.FeetPerLevel} ft")}), for {job.Reward} gold at the Prancing Pony.",
        "parcel" => $"{JobTitle(job)} ({(job.Progress > 0 ? "delivered" : "not yet")}), for {job.Reward} gold at the Prancing Pony.",
        "rescue" => $"{JobTitle(job)} ({(job.Progress > 0 ? "rescued" : RescueTimeLeft(job))}), for {job.Reward} gold at the Prancing Pony.",
        _ => $"{JobTitle(job)} to the Prancing Pony ({GatherSoFar(job)}), for {job.Reward} gold.",
    };

    private int CarriedCount(string kindId) => Player.Inventory.Pack.Where(i => i.Kind.Id == kindId).Sum(i => i.Number);

    /// <summary>How many of a kind lie in the Home.</summary>
    private int AtHome(string kindId) => HomeStock?.Where(i => i.Kind.Id == kindId).Sum(i => i.Number) ?? 0;

    private List<Item>? HomeStock => _stores.Values.FirstOrDefault(s => s.IsHome)?.Stock;

    /// <summary>A "bring" job's things to hand: carried, and in the Home.</summary>
    public int GatherOnHand(BoardJob job) => CarriedCount(job.Target) + AtHome(job.Target);

    /// <summary>"2 brought in, 1 carried, 1 at home" (the parts there are).</summary>
    private string GatherSoFar(BoardJob job)
    {
        var parts = new List<string>();
        if (job.Progress > 0) parts.Add($"{job.Progress} brought in");
        if (CarriedCount(job.Target) is > 0 and var carried) parts.Add($"{carried} carried");
        if (AtHome(job.Target) is > 0 and var home) parts.Add($"{home} at home");
        return parts.Count == 0 ? "none yet" : string.Join(", ", parts);
    }

    /// <summary>Takes up to <paramref name="wanted"/> of a job's things, from the pack first, then the Home; how many it took.</summary>
    private int TakeForJob(BoardJob job, int wanted)
    {
        var left = wanted;
        foreach (var stack in Player.Inventory.Pack.Where(i => i.Kind.Id == job.Target).ToList())
        {
            if (left == 0) break;
            var take = Math.Min(left, stack.Number);
            Player.Inventory.Remove(stack, take, () => Objects.NextSerial++);
            left -= take;
        }
        if (HomeStock is { } home)
            foreach (var stack in home.Where(i => i.Kind.Id == job.Target).ToList())
            {
                if (left == 0) break;
                var take = Math.Min(left, stack.Number);
                if (take == stack.Number) home.Remove(stack);
                else stack.Number -= take;
                left -= take;
            }
        return wanted - left;
    }

    private bool JobComplete(BoardJob job) => job.Kind switch
    {
        "hunt" or "bounty" => job.Progress >= job.Count,
        "scout" => Player.MaxDepth >= job.Count,
        "parcel" or "rescue" => job.Progress >= 1,
        _ => job.Progress + GatherOnHand(job) >= job.Count,
    };

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
                TakeErrand(job);
                Publish(new MessageEvent($"You take the note down: {JobTitle(job)}."));
                ShowBoard();
                break;
            case "drop" when job.Taken:
                AvaQuests.Board.Remove(job);
                DropErrand(job);
                Publish(new MessageEvent($"You pin the note back up for someone else: {JobTitle(job)}."));
                ShowBoard();
                break;
            case "collect" when job.Taken && JobComplete(job):
                if (job.Kind == "gather") job.Progress += TakeForJob(job, job.Count - job.Progress);
                PayJob(job);
                break;
            case "deliver" when job.Taken && job.Kind == "gather" && job.Progress < job.Count && GatherOnHand(job) > 0:
            {
                var given = TakeForJob(job, job.Count - job.Progress);
                job.Progress += given;
                if (job.Progress >= job.Count)
                {
                    PayJob(job);
                    break;
                }
                var thing = Data.Object(job.Target) is { } kind ? ItemNaming.Describe(Objects.Create(kind.Id, given), Knowledge, withArticle: false) : job.Target;
                Publish(new MessageEvent($"Butterbur takes the {thing} and makes a mark on the note. \"{job.Count - job.Progress} more and the pay's yours.\""));
                InnChoice("");
                break;
            }
        }
    }

    private void PayJob(BoardJob job)
    {
        Player.Gold += job.Reward;
        AvaQuests.Board.Remove(job);
        AvaQuests.JobsDone++;
        Publish(new MessageEvent($"Butterbur counts out {job.Reward} gold. \"Well done. There's always more work.\""));
        InnChoice("");
    }

    private void BoardMonsterKilled(Monster monster)
    {
        foreach (var job in AvaQuests.Board.Where(j => j.Taken && j.Kind is "hunt" or "bounty" && j.Target == monster.Race.Id && j.Progress < j.Count))
        {
            job.Progress++;
            if (job.Progress == job.Count)
                Publish(new MessageEvent(job.Kind == "bounty" ? $"The bounty is yours: {JobTitle(job)}. Collect at the Prancing Pony."
                    : $"That's the last of them: {JobTitle(job)} is done. Collect at the Prancing Pony."));
        }
    }
}
