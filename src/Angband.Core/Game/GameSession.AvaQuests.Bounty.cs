using Angband.Core.Definitions;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Quests;

namespace Angband.Core.Game;

// Bounties on uniques (AVABand's own): the notice board keeps one poster up beside its jobs — a bounty
// on a living unique whose native depth is from your deepest to five levels past it (never a quest's
// own, Sauron or Morgoth, nor one already slain), with a rough hint at where it's found. Take it, slay
// it, and Butterbur pays at the inn: gold by the unique's level, and a trophy of it (its own kind of
// trophy if it leaves one, a dragon's scale or a troll's hide; a Hunter's Trophy otherwise). If it dies
// some other way the bounty lapses. A poster nobody takes comes down after a few returns to town, and a
// new one goes up after each is claimed. Never in the daily dungeon, the weekly challenge or the
// tutorial, nor in a replay recorded before there were bounties (version 2 or older).
public sealed partial class GameSession
{
    /// <summary>How many of your returns to town a bounty stays posted if nobody takes it.</summary>
    public const int BountyVisits = 3;

    /// <summary>How far past your deepest a bounty's unique may be found (by its native depth).</summary>
    public const int BountyReach = 5;

    /// <summary>The trophy a bounty pays when the unique leaves no trophy of its own kind.</summary>
    public const string HuntersTrophy = "hunters_trophy";

    /// <summary>Set when playing back a replay recorded before bounties (version 2 or older): the board is as it was.</summary>
    internal bool BeforeBounties { get; set; }

    /// <summary>Whether the notice board posts bounties on uniques for this character.</summary>
    public bool BountiesOn => AvaQuestsOn && !BeforeBounties && !Player.IsSeededRun && !IsTutorial;

    /// <summary>The bounty posted now, if any.</summary>
    public UniqueBounty? Bounty => BountiesOn ? AvaQuests.Bounty : null;

    /// <summary>
    /// The uniques a bounty could be posted on now: living, not a quest's own (nor one of Sauron's other
    /// shapes, nor the Black Market thief's suspects), and natively from your deepest to
    /// <see cref="BountyReach"/> levels past it.
    /// </summary>
    public IReadOnlyList<MonsterRaceDef> BountyCandidates()
    {
        var deepest = Math.Max(Player.MaxDepth, 1);
        var questShapes = Data.Monsters.Where(r => r.Has(MonsterFlags.Questor)).SelectMany(r => r.Shapes).ToHashSet();
        return [.. Data.Monsters.Where(r => r.IsUnique && !r.Has(MonsterFlags.Questor) && !questShapes.Contains(r.Id)
                                            && r.Depth >= deepest && r.Depth <= deepest + BountyReach
                                            && !KilledUniques.Contains(r.Id) && !Suspects.Contains(r.Id)
                                            && AvaQuests.Board.All(j => j.Target != r.Id))];
    }

    /// <summary>A bounty's pay: gold by the unique's level and worth.</summary>
    public static long BountyReward(MonsterRaceDef race) => 100 + 3L * race.Experience * Math.Max(1, race.Depth) / 10;

    /// <summary>Puts up a new poster (or none, if no unique fits).</summary>
    private void PostBounty()
    {
        var candidates = BountyCandidates();
        if (candidates.Count == 0)
        {
            AvaQuests.Bounty = null;
            return;
        }
        var race = candidates[Rng.RandInt0(candidates.Count)];
        AvaQuests.Bounty = new UniqueBounty
        {
            Race = race.Id, Reward = BountyReward(race), HintFrom = Math.Max(1, race.Depth - Rng.RandInt0(3)), VisitsLeft = BountyVisits,
        };
    }

    /// <summary>Back in town from the dungeon: a bounty whose unique died some other way lapses; one nobody took comes down in time.</summary>
    private void BountyOnReturn()
    {
        if (!BountiesOn) return;
        if (AvaQuests.Bounty is not { } bounty)
        {
            PostBounty();
            return;
        }
        if (bounty.Slain) return; // (the pay is waiting at the inn)
        if (KilledUniques.Contains(bounty.Race))
        {
            if (bounty.Taken)
                Publish(new MessageEvent($"Word has reached Bree that {BountyName(bounty)} is dead — but not by your hand. The bounty lapses."));
            PostBounty();
            return;
        }
        if (!bounty.Taken && --bounty.VisitsLeft <= 0) PostBounty();
    }

    private string BountyName(UniqueBounty bounty) => Data.Monster(bounty.Race)?.Name ?? bounty.Race;

    /// <summary>"Bounty: Grip, Farmer Maggot's Dog".</summary>
    public string BountyTitle(UniqueBounty bounty) => $"Bounty: {BountyName(bounty)}";

    /// <summary>The poster's hint: "the levels around 600–750 ft".</summary>
    public string BountyHint(UniqueBounty bounty)
    {
        var feet = Data.Constants.FeetPerLevel;
        return $"the levels around {bounty.HintFrom * feet}–{(bounty.HintFrom + 3) * feet} ft";
    }

    /// <summary>What the bounty pays, in words: "340 gold and a trophy".</summary>
    public static string BountyPay(UniqueBounty bounty) => $"{bounty.Reward} gold and a trophy";

    /// <summary>The poster, as the board shows it.</summary>
    private string BountyPoster(UniqueBounty bounty) =>
        bounty.Slain ? $"Your bounty poster has \"CLAIMED\" scrawled across it: {BountyName(bounty)} is dead. Butterbur has your pay."
        : $"A fresh poster is nailed over the rest: WANTED — {BountyName(bounty)}, said to haunt {BountyHint(bounty)}. "
          + $"Reward: {BountyPay(bounty)}.{(bounty.Taken ? " You've taken it on." : "")}";

    /// <summary>The bounty's lines on the board: the poster's choice (take it, or give it up).</summary>
    private void AddBountyChoices(List<(string Id, string Label, string? Picture)> choices)
    {
        if (Bounty is not { Slain: false } bounty) return;
        var picture = $"monster:{bounty.Race}";
        if (bounty.Taken) choices.Add(("board:bounty:drop", $"Give up: {BountyTitle(bounty)} ({BountyPay(bounty)})", picture));
        else choices.Add(("board:bounty:take", $"Take: {BountyTitle(bounty)} ({BountyPay(bounty)})", picture));
    }

    private void BountyChoice(string what)
    {
        if (Bounty is not { } bounty) return;
        switch (what)
        {
            case "take" when !bounty.Taken && !bounty.Slain:
                bounty.Taken = true;
                Publish(new MessageEvent($"You tear the poster down: {BountyTitle(bounty)}."));
                ShowBoard();
                break;
            case "drop" when bounty.Taken && !bounty.Slain:
                bounty.Taken = false;
                Publish(new MessageEvent($"You nail the poster back up for someone else: {BountyTitle(bounty)}."));
                ShowBoard();
                break;
            case "collect" when bounty.Slain:
                ClaimBounty(bounty);
                break;
        }
    }

    /// <summary>The unique slain with its bounty taken: the pay waits at the inn.</summary>
    private void BountyMonsterKilled(Monster monster)
    {
        var race = monster.OriginalRace ?? monster.Race;
        if (Bounty is not { Taken: true, Slain: false } bounty || bounty.Race != race.Id) return;
        bounty.Slain = true;
        Publish(new MessageEvent($"The bounty is yours: {BountyTitle(bounty)}. Collect at the Prancing Pony."));
    }

    /// <summary>The trophy a bounty pays: the unique's own kind of trophy if it leaves one, else a Hunter's Trophy (inscribed with its name).</summary>
    private Item? BountyTrophy(MonsterRaceDef race)
    {
        var (kinds, _) = TrophiesOf(race);
        var kind = kinds.FirstOrDefault(k => Data.Object(k) is not null) ?? (Data.Object(HuntersTrophy) is not null ? HuntersTrophy : null);
        if (kind is null) return null;
        var trophy = Objects.Create(kind);
        trophy.Note = race.Name;
        Knowledge.LearnKind(trophy.Kind);
        return trophy;
    }

    private void ClaimBounty(UniqueBounty bounty)
    {
        var race = Data.Monster(bounty.Race);
        Player.Gold += bounty.Reward;
        AvaQuests.Bounty = null;
        AvaQuests.BountiesClaimed.Add(bounty.Race);
        AvaQuests.JobsDone++;
        AddHistory($"Claimed the bounty on {BountyName(bounty)}");
        Publish(new MessageEvent($"Butterbur counts out {bounty.Reward} gold. \"{BountyName(bounty)}! There's folk in Bree will sleep easier for that.\""));
        if (race is not null && BountyTrophy(race) is { } trophy) GiveQuestItem(trophy);
        PostBounty();
        InnChoice("");
    }

    /// <summary>The bounty taken, for the quest log.</summary>
    private QuestLogEntry BountyEntry(UniqueBounty bounty)
    {
        var name = BountyName(bounty);
        var status = bounty.Slain ? "Ready: collect your pay at the Prancing Pony" : "0 of 1 slain";
        List<QuestLogStep> steps =
        [
            new($"Slay {name}, said to haunt {BountyHint(bounty)}. If it dies some other way, the bounty lapses.", bounty.Slain),
            new("Collect your pay from Butterbur at the Prancing Pony (9).", false),
        ];
        return new QuestLogEntry(BountyTitle(bounty), "The notice board", false, status, steps, bounty.Slain ? 1 : 0, 1, BountyPay(bounty));
    }
}
