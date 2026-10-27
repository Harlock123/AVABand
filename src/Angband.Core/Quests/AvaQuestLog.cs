namespace Angband.Core.Quests;

/// <summary>Where one of AVABand's quests stands (saved as it is).</summary>
public sealed class AvaQuestState
{
    public string Id { get; set; } = "";
    /// <summary>The journal stage (a key into the quest's <c>stages</c>): "hunt", "key", "done"...</summary>
    public string Stage { get; set; } = "";
    /// <summary>Its numbers: depths, counts, flags ("keydepth", "found", "hallowed"...).</summary>
    public Dictionary<string, int> Numbers { get; set; } = new();
    /// <summary>Its words: the thief's name, the clues found...</summary>
    public Dictionary<string, string> Texts { get; set; } = new();
    /// <summary>The stages it has passed through, oldest first, for the quest log (empty in older saves).</summary>
    public List<string> History { get; set; } = [];

    public bool IsDone => Stage is "done" or "delivered" or "exposed" or "burned" or "lost" or "returned" or "kept" or "relieved" or "given" or "keep";
    public int N(string key) => Numbers.GetValueOrDefault(key);
}

/// <summary>A job on the Prancing Pony's notice board: hunt so many of a monster, or bring so many of a thing.</summary>
public sealed class BoardJob
{
    public int Id { get; set; }
    /// <summary>
    /// "hunt" (kill <see cref="Count"/> of race <see cref="Target"/>), "gather" (bring that many of kind
    /// <see cref="Target"/>), "bounty" (kill the unique <see cref="Target"/>) or "scout" (reach depth <see cref="Count"/>).
    /// </summary>
    public string Kind { get; set; } = "";
    public string Target { get; set; } = "";
    public int Count { get; set; }
    public int Progress { get; set; }
    public long Reward { get; set; }
    public bool Taken { get; set; }
    /// <summary>A rescue's deadline (game turn); its traveller's square (-1: not placed) and the level it's on.</summary>
    public long Deadline { get; set; }
    public int AtX { get; set; } = -1;
    public int AtY { get; set; } = -1;
    public ulong LevelSeed { get; set; }
}

/// <summary>A character's AVABand quests: the story quests taken or found, the notice board, and what the town thinks of them.</summary>
public sealed class AvaQuestLog
{
    public Dictionary<string, AvaQuestState> Quests { get; set; } = new();
    public List<BoardJob> Board { get; set; } = [];
    public int NextJobId { get; set; } = 1;
    public int JobsDone { get; set; }
    /// <summary>The stage the town has grown to with your renown (AVABand's own; 0 in older saves, as the town was).</summary>
    public int TownStage { get; set; }
    /// <summary>The raid on the town under way while you're there, if there is one (AVABand's own).</summary>
    public TownRaid? Raid { get; set; }
    /// <summary>Raids on the town you drove off (each one renown), and raids that got the better of it.</summary>
    public int RaidsRepelled { get; set; }
    public int RaidsLost { get; set; }
    /// <summary>The last return to town was raided (the next never is).</summary>
    public bool LastReturnRaided { get; set; }
    /// <summary>Returns to the grown town counted (a raid's dice are its own for each).</summary>
    public int TownReturns { get; set; }
    /// <summary>
    /// A raid got the better of the town: its newest improvements are spoiled until its renown reaches
    /// this (0: the town is as its stage has it).
    /// </summary>
    public int SetbackUntil { get; set; }
    /// <summary>Percent added to a store's prices (negative: a discount), for what you did for or against it.</summary>
    public Dictionary<string, int> PriceAdjust { get; set; } = new();
    /// <summary>Quest items left behind on a level (kind, tag), which turn up at the Prancing Pony.</summary>
    public List<LostQuestItem> LostAndFound { get; set; } = [];

    /// <summary>A merchant caravan in the town square, if one is camped there (AVABand's own).</summary>
    public Caravan? Caravan { get; set; }

    /// <summary>The bounty on a unique posted on the notice board, if there is one (none in older saves).</summary>
    public UniqueBounty? Bounty { get; set; }

    /// <summary>The uniques whose bounties this character has claimed (race ids, oldest first).</summary>
    public List<string> BountiesClaimed { get; set; } = [];

    public AvaQuestState? Get(string id) => Quests.GetValueOrDefault(id);
}

/// <summary>
/// A bounty on a living unique, posted on the notice board: who, the pay, the rough depth the poster
/// gives, whether you've taken it, whether you've slain it (and have the pay to collect), and how many
/// more of your returns to town it stays up untaken.
/// </summary>
public sealed class UniqueBounty
{
    public string Race { get; set; } = "";
    public long Reward { get; set; }
    /// <summary>The shallowest level of the poster's hint ("said to haunt the levels around..."); the hint spans four.</summary>
    public int HintFrom { get; set; }
    public bool Taken { get; set; }
    public bool Slain { get; set; }
    public int VisitsLeft { get; set; }
}

/// <summary>A merchant caravan: where it's camped, how many more of your returns it stays, and its wares (as saves write items) and prices.</summary>
public sealed class Caravan
{
    public int X { get; set; }
    public int Y { get; set; }
    public int VisitsLeft { get; set; }
    public List<string> Stock { get; set; } = [];
    public List<long> Prices { get; set; } = [];
}

/// <summary>
/// A raid on the town (AVABand's own): the raiders by their ids on the town level (the leader first),
/// how many came, and how long they have held the square unopposed.
/// </summary>
public sealed class TownRaid
{
    public int LeaderId { get; set; }
    public List<int> RaiderIds { get; set; } = [];
    /// <summary>The leader's race and the side of town they came from ("east"), as the news tells it.</summary>
    public string Leader { get; set; } = "";
    public string From { get; set; } = "";
    /// <summary>Turns the raiders have stood in the square with no one to stop them.</summary>
    public int Held { get; set; }
}

/// <summary>A quest item left behind on a level: what it was and which quest it belongs to.</summary>
public sealed class LostQuestItem
{
    public string Kind { get; set; } = "";
    public string Tag { get; set; } = "";
}
