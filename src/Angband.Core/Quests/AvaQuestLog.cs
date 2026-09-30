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

    public bool IsDone => Stage is "done" or "delivered" or "exposed" or "burned" or "lost";
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
}

/// <summary>A character's AVABand quests: the story quests taken or found, the notice board, and what the town thinks of them.</summary>
public sealed class AvaQuestLog
{
    public Dictionary<string, AvaQuestState> Quests { get; set; } = new();
    public List<BoardJob> Board { get; set; } = [];
    public int NextJobId { get; set; } = 1;
    public int JobsDone { get; set; }
    /// <summary>Percent added to a store's prices (negative: a discount), for what you did for or against it.</summary>
    public Dictionary<string, int> PriceAdjust { get; set; } = new();
    /// <summary>Quest items left behind on a level (kind, tag), which turn up at the Prancing Pony.</summary>
    public List<LostQuestItem> LostAndFound { get; set; } = [];

    public AvaQuestState? Get(string id) => Quests.GetValueOrDefault(id);
}

/// <summary>A quest item left behind on a level: what it was and which quest it belongs to.</summary>
public sealed class LostQuestItem
{
    public string Kind { get; set; } = "";
    public string Tag { get; set; } = "";
}
