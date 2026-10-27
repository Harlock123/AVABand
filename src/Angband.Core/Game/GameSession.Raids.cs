using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Quests;
using Angband.Core.Randomness;

namespace Angband.Core.Game;

/// <summary>A raid on the town begins: its leader (for the scene), and what is said of it.</summary>
public sealed record TownRaidEvent(Monster Leader, string Caption, string Subtitle) : IGameEvent;

// AVABand's own: raids on the town. Once the town has grown to its second stage, now and then (one
// return in eight, never two returns running, never the first return to a new stage) a band comes
// over the town wall while you're there — a leader and its followers of one kind (kobolds, jackals,
// orcs, wolves, later ogres and trolls), from the game's monsters, of a depth suited to your level —
// and makes for the square while the townsfolk run indoors. Kill the leader, or more than half of
// them, and the rest flee: renown (toward the town's stages), gold from the townsfolk, a line in the
// history and the feats. Leave the town while they're in it, or let them hold the square unopposed,
// and the raid succeeds: the town's newest improvements are spoiled, and the shops stock a little
// less, until your next renown (never below the first stage; the Home and the strongroom are never
// touched). The shops bar their doors while raiders are at them. The dice are the raid's own; the
// birth option "Town raids" turns them off; never in the daily dungeon, the weekly challenge, the
// tutorial, or a replay recorded before there were raids.
public sealed partial class GameSession
{
    /// <summary>One return to town in this many is raided (when one can be).</summary>
    public const int RaidChance = 8;

    /// <summary>The stage the town must have grown to before anyone thinks it worth raiding.</summary>
    public const int RaidFromStage = 2;

    /// <summary>The least character level a raid comes for (a beginner's town is left alone).</summary>
    public const int RaidMinLevel = 5;

    /// <summary>How long (player turns) the raiders must hold the square unopposed to sack the town.</summary>
    public const int RaidHoldTurns = 50;

    /// <summary>How far from the middle of town counts as the square.</summary>
    public const int RaidSquareRadius = 3;

    /// <summary>Within this of the square, you are there to oppose them.</summary>
    public const int RaidOpposeRange = 8;

    /// <summary>The kinds of monster that raid the town (their bases), the weakest first.</summary>
    private static readonly string[] RaiderBases = ["kobold", "canine", "orc", "ogre", "troll", "giant"];

    /// <summary>Whether raids can come: the town can grow, the birth option is on, and the replay is new enough.</summary>
    public bool RaidsOn => TownGrowthOn && Options[OptionIds.AvaRaids] && !RaidsHeld;

    /// <summary>A replay recorded before there were raids plays back without them (ReplayPlayer sets it).</summary>
    internal bool RaidsHeld { get; set; }

    /// <summary>The raid under way, while you're in town.</summary>
    public TownRaid? Raid => AvaQuests.Raid;

    /// <summary>The depth of the band a character of this level faces: about half its level.</summary>
    public static int RaidDepth(int level) => Math.Clamp((level + 1) / 2, 1, 40);

    /// <summary>How many follow the leader: two, and one more for every eight levels (seven at most).</summary>
    public static int RaidFollowers(int level) => Math.Clamp(2 + level / 8, 2, 7);

    /// <summary>The gold the grateful townsfolk give for a raid driven off.</summary>
    public long RaidReward => 30L * Player.Level + 100L * Math.Max(1, TownStage);

    private Loc TownMiddle => new(Level.Width / 2, Level.Height / 2);

    /// <summary>The raiders still in town, the leader first.</summary>
    public IEnumerable<Monster> Raiders(TownRaid raid) =>
        raid.RaiderIds.Select(id => Level.Monsters[id]).OfType<Monster>().Where(m => !m.IsRemoved);

    /// <summary>Whether a monster is one of the raiders in town.</summary>
    public bool IsRaider(Monster monster) => Raid is { } raid && Player.Depth == 0 && raid.RaiderIds.Contains(monster.Id) && !monster.IsRemoved;

    /// <summary>
    /// Back in town from the dungeon (once the town is peopled): now and then, a raid. Never two
    /// returns running, never the first return to a new stage, never before the town's second stage.
    /// </summary>
    internal void RaidOnReturn()
    {
        var grew = _townGrewOnReturn;
        _townGrewOnReturn = false;
        if (!RaidsOn || Level.ProfileId != "town") return;
        var last = AvaQuests.LastReturnRaided;
        AvaQuests.LastReturnRaided = false;
        AvaQuests.TownReturns++;
        if (AvaQuests.TownStage < RaidFromStage || last || grew || Player.Level < RaidMinLevel || AvaQuests.Raid is not null) return;
        var dice = new GameRandom(GameRandom.DeriveSeed(Seed, GameTurn, AvaQuests.TownReturns, 0x52_41_49_44));
        if (!dice.OneIn(RaidChance)) return;
        if (StartRaid(dice) is not null) AvaQuests.LastReturnRaided = true;
    }

    /// <summary>
    /// The band for a character of this level: a kind of monster with a leader of about its depth (up to
    /// two deeper) and followers no deeper — the strongest that fits leads, the followers are picked as
    /// the dungeon picks monsters. Null if there's none.
    /// </summary>
    public (MonsterRaceDef Leader, List<MonsterRaceDef> Followers)? RaidBand(GameRandom dice, int level)
    {
        var depth = RaidDepth(level);
        bool Fits(MonsterRaceDef r) => r.Depth > 0 && !r.IsUnique && !r.Has(MonsterFlags.Questor) && !r.Has(MonsterFlags.NeverMove)
                                       && !r.Has(MonsterFlags.Multiply);
        var bands = RaiderBases.Select(b => (
                Leaders: Data.Monsters.Where(r => r.Base == b && Fits(r) && r.Depth <= depth + 2 && r.Depth >= depth - 10).ToList(),
                Followers: Data.Monsters.Where(r => r.Base == b && Fits(r) && r.Depth <= depth && r.Depth >= depth - 12).ToList()))
            .Where(x => x.Leaders.Count > 0 && x.Followers.Count > 0).ToList();
        if (bands.Count == 0) return null;
        var (leaders, followers) = bands[dice.RandInt0(bands.Count)];
        var leader = leaders.OrderByDescending(r => r.Depth).ThenBy(r => r.Id, StringComparer.Ordinal).First();
        var band = new List<MonsterRaceDef>();
        for (var i = 0; i < RaidFollowers(level); i++)
            if (dice.PickWeighted(followers, r => Math.Max(1, 100 / Math.Max(1, r.Rarity))) is { } follower) band.Add(follower);
        return (leader, band);
    }

    /// <summary>
    /// A raid begins: the band comes over the wall at the town's edge, well away from you, awake and
    /// making for the square; the townsfolk run indoors (those you can see flee). Null if there was
    /// nowhere to put them.
    /// </summary>
    internal TownRaid? StartRaid(GameRandom dice)
    {
        if (RaidBand(dice, Player.Level) is not { } band) return null;
        var (leaderRace, followers) = band;
        var edge = Level.AllLocs().Where(p => Level.IsEmptyFloor(p) && IsTownEdge(p) && p != Player.Position && p.DistanceTo(Player.Position) > 12).ToList();
        if (edge.Count == 0) return null;
        var start = edge[dice.RandInt0(edge.Count)];
        var leader = _spawner.Place(Level, dice, leaderRace, start, asleep: false);
        Scheduler.Add(leader);
        var raid = new TownRaid { LeaderId = leader.Id, RaiderIds = [leader.Id], Leader = leaderRace.Name, From = SideOfTown(start) };
        foreach (var race in followers)
        {
            var spot = Level.AllLocs().Where(p => Level.IsEmptyFloor(p) && p != Player.Position && p.ChebyshevTo(start) <= 4)
                .OrderBy(p => p.DistanceTo(start)).ThenBy(p => p.Y).ThenBy(p => p.X).Select(p => (Loc?)p).FirstOrDefault();
            if (spot is not { } at) break;
            var follower = _spawner.Place(Level, dice, race, at, asleep: false);
            follower.GroupId = leader.Id;
            Scheduler.Add(follower);
            raid.RaiderIds.Add(follower.Id);
        }
        AvaQuests.Raid = raid;
        _raidPaths = null;

        // The townsfolk run for their doors: those out of sight are gone indoors, the rest flee.
        foreach (var m in Level.Monsters.All.Where(m => m.Race.Depth == 0 && !m.IsAlly && !raid.RaiderIds.Contains(m.Id)).ToList())
            if (m.IsVisible) m.Fear = Math.Max(m.Fear, 100);
            else Level.Monsters.Remove(m);

        var kind = followers.GroupBy(r => r.Id).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.First()).FirstOrDefault() ?? leaderRace;
        var plural = kind.Plural ?? kind.Name + (kind.Name.EndsWith('s') ? "es" : "s");
        var more = raid.RaiderIds.Count - 1;
        Publish(new MessageEvent($"Raiders! A band of {plural} led by {Article(leaderRace.Name)} comes over the town wall from the {raid.From}."));
        Publish(new MessageEvent("The townsfolk run for their doors. Drive the raiders off — kill their leader, or most of them — before they sack the square."));
        Publish(new TownRaidEvent(leader, "Raiders in the town!",
            $"{Capitalize(Article(leaderRace.Name))} and {more} more come over the wall from the {raid.From}. Drive them off!"));
        return raid;
    }

    /// <summary>The street within two squares of the town wall.</summary>
    private bool IsTownEdge(Loc p) => p.X <= 2 || p.Y <= 2 || p.X >= Level.Width - 3 || p.Y >= Level.Height - 3;

    /// <summary>Which side of town a square is on ("east"), as the town is wider than it is tall.</summary>
    private string SideOfTown(Loc p)
    {
        var dx = p.X - TownMiddle.X;
        var dy = (p.Y - TownMiddle.Y) * 3;
        return Math.Abs(dx) >= Math.Abs(dy) ? dx < 0 ? "west" : "east" : dy < 0 ? "north" : "south";
    }

    /// <summary>The way to the square: each street's steps from it (the town level the map was made for).</summary>
    private (World.Level Level, Dictionary<Loc, int> Steps)? _raidPaths;

    private Dictionary<Loc, int> RaidPaths()
    {
        if (_raidPaths is { } cached && cached.Level == Level) return cached.Steps;
        bool Open(Loc p) => Level.InBounds(p) && Level.IsPassable(p) && !Level.FeatureAt(p).Has(TerrainFlags.Shop);
        var steps = new Dictionary<Loc, int>();
        var queue = new Queue<Loc>();
        foreach (var p in Level.AllLocs().Where(p => p.DistanceTo(TownMiddle) <= RaidSquareRadius && Open(p)))
        {
            steps[p] = 0;
            queue.Enqueue(p);
        }
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            foreach (var n in Level.Neighbors(at))
                if (!steps.ContainsKey(n) && Open(n))
                {
                    steps[n] = steps[at] + 1;
                    queue.Enqueue(n);
                }
        }
        _raidPaths = (Level, steps);
        return steps;
    }

    /// <summary>
    /// Every game turn (with the world's): raiders that haven't caught wind of you march a step toward
    /// the square; the raid is driven off, or the square held long enough unopposed and the town sacked.
    /// No dice are rolled.
    /// </summary>
    internal void RaidUpkeep()
    {
        if (AvaQuests.Raid is not { } raid || Player.Depth != 0 || Level.ProfileId != "town" || Player.IsDead) return;
        if (RaidDrivenOff(raid)) return;
        var steps = RaidPaths();
        var inSquare = false;
        foreach (var m in Raiders(raid).ToList())
        {
            if (!m.Active && m.Sleep == 0 && m.Held == 0 && m.Fear == 0) MarchOn(m, steps);
            if (steps.TryGetValue(m.Position, out var d) && d == 0) inSquare = true;
        }
        if (!inSquare || Player.Position.DistanceTo(TownMiddle) <= RaidOpposeRange) return;
        raid.Held++;
        if (raid.Held == 1) Publish(new MessageEvent("The raiders have reached the square! Drive them out before they sack it."));
        else if (raid.Held == RaidHoldTurns / 2) Publish(new MessageEvent("Smoke rises from the square: the raiders are breaking open the stalls."));
        if (raid.Held >= RaidHoldTurns) RaidLost(left: false);
    }

    /// <summary>A raider's step toward the square: to the open, empty neighbour nearest it.</summary>
    private void MarchOn(Monster m, Dictionary<Loc, int> steps)
    {
        if (!steps.TryGetValue(m.Position, out var here) || here == 0) return;
        Loc? best = null;
        var bestSteps = here;
        foreach (var d in Ddd)
        {
            var n = m.Position + Keypad[d];
            if (!steps.TryGetValue(n, out var s) || s >= bestSteps || n == Player.Position || Level.Monsters.At(n) is not null) continue;
            best = n;
            bestSteps = s;
        }
        if (best is { } to) Level.Monsters.Move(m, to);
    }

    /// <summary>A raider slain: perhaps the raid is driven off.</summary>
    private void RaiderKilled(Monster monster)
    {
        if (AvaQuests.Raid is { } raid && Player.Depth == 0 && raid.RaiderIds.Contains(monster.Id)) RaidDrivenOff(raid);
    }

    /// <summary>
    /// The leader dead, or more than half the band: the rest flee over the wall, and the town thanks
    /// you — renown, gold, the history. True if so.
    /// </summary>
    private bool RaidDrivenOff(TownRaid raid)
    {
        var alive = Raiders(raid).ToList();
        var leaderDown = alive.All(m => m.Id != raid.LeaderId);
        var fallen = raid.RaiderIds.Count - alive.Count;
        if (!leaderDown && fallen * 2 <= raid.RaiderIds.Count) return false;
        AvaQuests.Raid = null;
        foreach (var m in alive) Level.Monsters.Remove(m);
        AvaQuests.RaidsRepelled++;
        var gold = RaidReward;
        Player.Gold += gold;
        Publish(new MessageEvent(leaderDown
            ? $"With their {raid.Leader} fallen, the raiders break and flee back over the wall!"
            : "The raiders have had enough: the rest break and flee back over the wall!"));
        Publish(new MessageEvent($"The townsfolk come out cheering. Butterbur presses a purse on you: \"From all of us.\" ({gold} gold; your renown grows.)"));
        AddHistory($"Drove raiders led by {Article(raid.Leader)} out of the town");
        return true;
    }

    /// <summary>What a raid spoils, by the stage the town had reached.</summary>
    private static readonly string[] RaidDamage =
    [
        "",
        "",
        "the well is fouled and rubble chokes the streets again, and the shops have a little less on their shelves",
        "the market stalls are smashed, and the shops have less on their shelves",
        "the fountain is broken, the lamps by the shop doors are out, and the shops have less on their shelves",
        "your statue is pulled down, and the shops have less on their shelves",
    ];

    /// <summary>
    /// The raid succeeded — you left the town to it, or they held the square: the raiders go with their
    /// loot, and the town's newest improvements are spoiled until your next renown. Nothing of yours
    /// is touched.
    /// </summary>
    private void RaidLost(bool left)
    {
        if (AvaQuests.Raid is not { } raid) return;
        AvaQuests.Raid = null;
        foreach (var m in Raiders(raid).ToList()) Level.Monsters.Remove(m);
        AvaQuests.RaidsLost++;
        var already = AvaQuests.SetbackUntil > 0;
        AvaQuests.SetbackUntil = TownRenown + 1;
        Publish(new MessageEvent(left
            ? "Behind you, the raiders sack the town, and are gone over the wall with their loot."
            : "The raiders have the square: they loot the stalls and are gone over the wall before anyone can stop them."));
        var stage = Math.Clamp(AvaQuests.TownStage, 0, RaidDamage.Length - 1);
        Publish(new MessageEvent(already || stage <= 1
            ? "The town will take that much longer to mend: it's put right with your next renown."
            : $"The town is set back: {RaidDamage[stage]}, until your renown grows again (your next quest or job done)."));
        AddHistory($"Raiders led by {Article(raid.Leader)} sacked the town");
    }

    /// <summary>Leaving town while raiders are in it: the raid succeeds (going nowhere but the town again, it simply ends).</summary>
    private void RaidLeftBehind(int toDepth)
    {
        if (AvaQuests.Raid is not { } raid) return;
        if (toDepth != 0)
        {
            RaidLost(left: true);
            return;
        }
        foreach (var m in Raiders(raid).ToList()) Level.Monsters.Remove(m);
        AvaQuests.Raid = null;
    }

    /// <summary>A shop's door, barred while raiders are at it (the Home is your own, and never barred). True if so.</summary>
    private bool ShopBarredByRaid()
    {
        if (AvaQuests.Raid is not { } raid || Player.Depth != 0 || Level.FeatureAt(Player.Position).Shop is not { } shop || shop == "home") return false;
        if (!Raiders(raid).Any(m => m.Position.ChebyshevTo(Player.Position) <= 2)) return false;
        Publish(new MessageEvent("The door is barred. \"Not while there's raiders in the street!\" someone shouts from inside."));
        return true;
    }
}
