using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Magic;
using Angband.Core.Monsters;
using Angband.Core.Randomness;
using Angband.Core.Time;

namespace Angband.Core.Game;

/// <summary>What a hireling does in a fight: stands and swings, or keeps back and shoots.</summary>
public enum HirelingRole { Warrior, Archer }

/// <summary>
/// The orders a hireling takes (<see cref="HirelingOrderCommand"/>): follow close (and fight what comes
/// near), hold this spot, go for your target — and, in town, go home.
/// </summary>
public enum HirelingOrder { Follow, Hold, Attack, Dismiss }

/// <summary>
/// A sword for hire from the Prancing Pony (AVABand's own; kept in the save): who they are, what they
/// cost, and how far they have come with you. Their body on the level is an ordinary monster marked
/// <see cref="Monster.IsAlly"/>, of a race made from this record (<see cref="GameSession.HirelingRace"/>).
/// </summary>
public sealed class Hireling
{
    /// <summary>The race id the hireling's body is saved under (made from this record, never from the data).</summary>
    public const string RaceId = "hireling";

    public string Name { get; set; } = "";
    /// <summary>What folk they come from: human, dwarf, elf or hobbit.</summary>
    public string Look { get; set; } = "human";
    public HirelingRole Role { get; set; }
    public int Level { get; set; } = 1;
    public long Experience { get; set; }
    /// <summary>What hiring them cost, paid up front.</summary>
    public long Fee { get; set; }
    /// <summary>Their share, in percent, of the gold you pick up.</summary>
    public int Share { get; set; }
    /// <summary>The gold their share has come to so far.</summary>
    public long Earned { get; set; }
    public int Kills { get; set; }
    public HirelingOrder Order { get; set; }
    /// <summary>The monster they were told to go for (its id on this level), under <see cref="HirelingOrder.Attack"/>.</summary>
    public int AttackTarget { get; set; }
    /// <summary>Badly hurt and keeping out of the fight, until they have healed to half.</summary>
    public bool Fleeing { get; set; }
    public long HiredTurn { get; set; }
    /// <summary>The deepest level you have been on together.</summary>
    public int DeepestTogether { get; set; }
    /// <summary>The deepest of those you have come back up to town from, together.</summary>
    public int DeepestReturned { get; set; }

    /// <summary>Their body on the level (not saved: found again among the level's monsters).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Monster? Body { get; set; }

    public string RoleName => Role == HirelingRole.Archer ? "archer" : "warrior";

    /// <summary>"Bergil the archer".</summary>
    public string Title => $"{Name} the {RoleName}";

    // --- What a level makes of them (AVABand's own numbers) ------------------------------------------

    public static int MaxHpFor(HirelingRole role, int level) => role == HirelingRole.Archer ? 10 + 5 * level : 15 + 7 * level;

    public static int ArmourFor(HirelingRole role, int level) => role == HirelingRole.Archer ? 8 + 3 * level / 2 : 12 + 2 * level;

    /// <summary>Their skill with a blade (against a monster's armour, as the player's is).</summary>
    public static int MeleeSkillFor(HirelingRole role, int level) => role == HirelingRole.Archer ? 30 + 2 * level : 50 + 3 * level;

    /// <summary>An archer's skill with the bow (less a point a square of range, as yours is).</summary>
    public static int BowSkillFor(int level) => 50 + 3 * level;

    /// <summary>Blows a turn: a warrior two, and another at levels 15 and 30; an archer one.</summary>
    public static int BlowsFor(HirelingRole role, int level) => role == HirelingRole.Archer ? 1 : Math.Min(4, 2 + level / 15);

    /// <summary>A blow's dice (a warrior 2d4, an archer's knife 1d6) and the bonus their level adds to it.</summary>
    public static (Dice Dice, int Bonus) BlowFor(HirelingRole role, int level) =>
        role == HirelingRole.Archer ? (new Dice(1, 6), level / 4) : (new Dice(2, 4), level / 3);

    /// <summary>An arrow: 1d9 plus a third of their level, times the bow's might (x2, a long bow's x3 from level 20).</summary>
    public static (Dice Dice, int Bonus, int Multiplier) ArrowFor(int level) => (new Dice(1, 9), level / 3, level >= 20 ? 3 : 2);

    /// <summary>How far an archer shoots.</summary>
    public static int RangeFor(int level) => level >= 20 ? 12 : 10;

    /// <summary>A point of speed every ten levels.</summary>
    public static int SpeedFor(int level) => level / 10;

    /// <summary>
    /// The experience to reach a level: twice the player's table (they learn slowly, and only from the
    /// fights they are in).
    /// </summary>
    public static long ExperienceFor(int level) =>
        level <= 1 ? 0 : level > StatTables.MaxLevel ? long.MaxValue : (long)StatTables.ExperienceForLevel[level - 2] * 2;

    /// <summary>
    /// What hiring one costs up front: 30 gold a level and 4 more a level squared, a fifth more for an
    /// archer, and more the smaller the share they ask (x1.2 at 10%, x0.8 at 20%).
    /// </summary>
    public static long FeeFor(HirelingRole role, int level, int share)
    {
        long fee = 30L * level + 4L * level * level;
        if (role == HirelingRole.Archer) fee = fee * 6 / 5;
        return Math.Max(10, fee * (160 - 4 * share) / 100);
    }
}

/// <summary>A hireling who fell in your service (AVABand's own; kept in the save, shown on the Knowledge screen).</summary>
public sealed class FallenCompanion
{
    public string Name { get; set; } = "";
    public string Look { get; set; } = "";
    public HirelingRole Role { get; set; }
    public int Level { get; set; }
    public int Kills { get; set; }
    /// <summary>Where they fell.</summary>
    public int Depth { get; set; }
    public string KilledBy { get; set; } = "";
    public long Turn { get; set; }
    /// <summary>How long they were with you, in game turns.</summary>
    public long Served { get; set; }
}

// AVABand's own: a hireling. At the Prancing Pony a sword for hire (a warrior or an archer, a human,
// dwarf, elf or hobbit, no higher in level than you) will follow you down for a fee and a share of the
// gold you pick up. They keep close, fight what comes near (and what you tell them to), take blows
// meant for you, heal slowly, fall back when badly hurt, learn slowly from the fights they are in — and
// can die for good. Only one at a time; birth option "Hirelings" (birth_ava_hirelings), never in the
// daily dungeon, the weekly challenge or the tutorial. Their turn and the fighting are in GameSession.HirelingFight.cs.
public sealed partial class GameSession
{
    /// <summary>The candidates the inn offers in a day: two, and a third one time in two.</summary>
    public const int HireCandidatesMin = 2;

    /// <summary>Their share of the gold you pick up: 10% to 20%.</summary>
    public const int HireShareMin = 10, HireShareMax = 20;

    /// <summary>How much lower than yours a candidate's level may be.</summary>
    public const int HireLevelSpread = 3;

    /// <summary>Whether this character can hire a sword (the birth option; never in the daily dungeon, the weekly challenge or the tutorial).</summary>
    public bool HirelingsOn => Options[OptionIds.Hirelings] && !Player.IsSeededRun && !IsTutorial;

    /// <summary>The sword in your hire, if any.</summary>
    public Hireling? Hireling { get; private set; }

    /// <summary>Hirelings who fell in your service, the first first.</summary>
    public List<FallenCompanion> FallenCompanions { get; private set; } = [];

    /// <summary>The hireling's body, while it is on this level.</summary>
    public Monster? HirelingHere =>
        Hireling?.Body is { IsRemoved: false } body && body.IsActive && Level.Monsters[body.Id] == body ? body : null;

    internal void RestoreHireling(Hireling? hireling, IEnumerable<FallenCompanion>? fallen)
    {
        Hireling = hireling;
        FallenCompanions = fallen?.ToList() ?? [];
    }

    /// <summary>Loading a save: the hireling's body among a level's monsters (saved under <see cref="Game.Hireling.RaceId"/>).</summary>
    internal void AttachHireling(Monster body)
    {
        body.IsAlly = true;
        if (Hireling is { } h) h.Body = body;
    }

    /// <summary>
    /// The race a hireling's body is: a person (or dwarf, elf or hobbit) of their level, drawn in the
    /// ally's light green, unique (so nothing tramples, polymorphs or banishes it) and worth nothing.
    /// </summary>
    public MonsterRaceDef HirelingRace(Hireling h)
    {
        var (dice, bonus) = Hireling.BlowFor(h.Role, h.Level);
        var blow = new MonsterBlowDef { Method = "hit", Effect = "hurt", Damage = new Dice(dice.Count, dice.Sides, bonus) };
        return new MonsterRaceDef
        {
            Id = Hireling.RaceId, Name = h.Name, Glyph = h.Look == "human" ? 'p' : 'h', Base = h.Look == "human" ? "person" : "humanoid",
            Color = "LightGreen", Depth = h.Level, Speed = Hireling.SpeedFor(h.Level), HitPoints = Hireling.MaxHpFor(h.Role, h.Level),
            Armour = Hireling.ArmourFor(h.Role, h.Level), Hearing = 20, Blows = [.. Enumerable.Repeat(blow, Hireling.BlowsFor(h.Role, h.Level))],
            Flags = new HashSet<string> { MonsterFlags.Unique, MonsterFlags.OpenDoor, MonsterFlags.NoConf, MonsterFlags.NoSleep },
            Description = $"{h.Title}, {Article(h.Look)} {h.RoleName} in your hire.",
        };
    }

    /// <summary>The monster race whose picture a hireling borrows, by their folk and role (tilesets have these).</summary>
    public static string HirelingPicture(Hireling h) => (h.Look, h.Role) switch
    {
        ("dwarf", HirelingRole.Warrior) => "stonefoot_warrior",
        ("dwarf", _) => "eastern_dwarf",
        ("elf", HirelingRole.Warrior) => "gallant",
        ("elf", _) => "green_elf_archer",
        ("hobbit", _) => "scruffy_looking_hobbit",
        (_, HirelingRole.Archer) => "ranger",
        _ => "warrior",
    };

    // --- At the inn: the candidates ----------------------------------------------------------------

    private static readonly Dictionary<string, string[]> HirelingNames = new()
    {
        ["human"] = ["Bergil", "Hirgon", "Damrod", "Halbarad", "Targon", "Iorlas", "Baranor", "Derufin", "Duilin", "Anborn", "Grimbold", "Elfhelm"],
        ["dwarf"] = ["Frerin", "Borin", "Nali", "Fror", "Thrar", "Floi", "Loni", "Narvi", "Farin", "Grerin"],
        ["elf"] = ["Lindir", "Orophin", "Rumil", "Gelmir", "Arminas", "Edrahil", "Galdor", "Saeros", "Beleg", "Mablung"],
        ["hobbit"] = ["Tolman", "Wilcome", "Robin", "Holman", "Hob", "Merimac", "Odo", "Ferdibrand", "Carl", "Mungo"],
    };

    private static readonly string[] HirelingLooks = ["human", "dwarf", "elf", "hobbit"];

    /// <summary>
    /// Today's swords for hire: two or three, near your level (never above it), each with a fee and a
    /// share. They come from dice of their own, seeded from the game and the day, so asking again the
    /// same day shows the same faces — and nothing else in the game shifts.
    /// </summary>
    public IReadOnlyList<Hireling> HireCandidates()
    {
        var day = GameTurn / Math.Max(1, Data.Constants.DayLength);
        var dice = new GameRandom(Seed ^ 0x48_1E_E1_17UL ^ (ulong)(day + 1) * 0x9E3779B97F4A7C15UL);
        var taken = FallenCompanions.Select(f => f.Name).Append(Hireling?.Name ?? "").ToHashSet(StringComparer.Ordinal);
        var count = HireCandidatesMin + (dice.OneIn(2) ? 1 : 0);
        var list = new List<Hireling>();
        for (var tries = 0; list.Count < count && tries < 20; tries++)
        {
            var look = HirelingLooks[dice.RandInt0(HirelingLooks.Length)];
            var name = HirelingNames[look][dice.RandInt0(HirelingNames[look].Length)];
            var role = dice.OneIn(2) ? HirelingRole.Archer : HirelingRole.Warrior;
            var level = Math.Max(1, Player.Level - dice.RandInt0(HireLevelSpread + 1));
            var share = dice.RandRange(HireShareMin, HireShareMax);
            if (!taken.Add(name)) continue;
            list.Add(new Hireling
            {
                Name = name, Look = look, Role = role, Level = level, Experience = Hireling.ExperienceFor(level), Share = share,
                Fee = Hireling.FeeFor(role, level, share),
            });
        }
        return list;
    }

    /// <summary>"Bergil, a human archer of level 7: 640 gold now, then 12% of the gold you find."</summary>
    public static string DescribeCandidate(Hireling h) =>
        $"{h.Name}, {Article(h.Look)} {h.RoleName} of level {h.Level}: {h.Fee} gold now, then {h.Share}% of the gold you find";

    /// <summary>The inn's word on hiring: the candidates, or the one you have (to part with, in town).</summary>
    private void HireMenu()
    {
        var back = AvaQuestsOn ? "inn:" : "none"; // (without the quests, there's nothing else at the inn)
        if (Hireling is { } h)
        {
            AskQuest("Your hireling",
                $"{h.Title} ({h.Look}, level {h.Level}) is with you, for {h.Share}% of the gold you find. They have earned {h.Earned} gold "
                + $"and slain {h.Kills} {(h.Kills == 1 ? "foe" : "foes")} at your side. Only one sword at a time.",
                ("hire:dismiss", $"Part ways with {h.Name} (they go home)"), (back, AvaQuestsOn ? "Back" : "Leave"));
            return;
        }
        var candidates = HireCandidates();
        var choices = candidates.Select((c, i) => ($"hire:take:{i}", DescribeCandidate(c))).ToList();
        choices.Add((back, AvaQuestsOn ? "Back" : "Leave"));
        AskQuest("Swords for hire",
            "A few hard-faced folk by the fire would go down with you, for a fee now and a share of the gold you find (taken as you "
            + "pick it up). They follow you, fight at your side, learn as they go — and can die for good. Only one at a time. "
            + $"You have {Player.Gold} gold.",
            [.. choices]);
    }

    private void HireChoice(string[] parts)
    {
        switch (parts.ElementAtOrDefault(1))
        {
            case "take" when int.TryParse(parts.ElementAtOrDefault(2), out var i):
                Hire(i);
                break;
            case "dismiss":
                DismissHireling();
                break;
        }
    }

    /// <summary>Hires today's candidate <paramref name="index"/>, if you can pay; they join you where you stand.</summary>
    private void Hire(int index)
    {
        if (!HirelingsOn || Hireling is not null) return;
        var candidates = HireCandidates();
        if (index < 0 || index >= candidates.Count) return;
        var h = candidates[index];
        if (Player.Gold < h.Fee)
        {
            Publish(new MessageEvent($"{h.Name} shakes their head. \"{h.Fee} gold, and not a copper less.\""));
            HireMenu();
            return;
        }
        Player.Gold -= h.Fee;
        Join(h);
        AddHistory($"Hired {h.Title} ({h.Look}, level {h.Level})");
        Publish(new MessageEvent($"{h.Title} will follow you down, for {h.Share}% of the gold you find."));
        Publish(new HirelingHiredEvent(h.Name));
    }

    /// <summary>A hireling joins you, beside you where you stand (hiring them; tests).</summary>
    internal void Join(Hireling h)
    {
        h.HiredTurn = GameTurn;
        Hireling = h;
        PlaceHireling();
        UpdateView();
    }

    /// <summary>In town: the hireling is paid off and goes home.</summary>
    private void DismissHireling()
    {
        if (Hireling is not { } h) return;
        if (Player.Depth != 0)
        {
            Publish(new MessageEvent($"Not down here: {h.Name} won't find the way back alone. Part ways in town."));
            return;
        }
        if (HirelingHere is { } body) Level.Monsters.Remove(body);
        Hireling = null;
        AddHistory($"Parted ways with {h.Title}");
        Publish(new MessageEvent($"{h.Title} shakes your hand and heads home."));
    }

    // --- Orders ----------------------------------------------------------------------------------------

    /// <summary>Gives the hireling an order (takes no time). Returns 0.</summary>
    private int OrderHireling(HirelingOrder order)
    {
        if (Hireling is not { } h)
        {
            Publish(new MessageEvent("You have no hireling."));
            return 0;
        }
        switch (order)
        {
            case HirelingOrder.Dismiss:
                DismissHireling();
                return 0;
            case HirelingOrder.Attack:
                if (TargetMonster is not { IsAlly: false } foe || !TargetOkay())
                {
                    Publish(new MessageEvent($"Target a monster first (*), then tell {h.Name} to go for it."));
                    return 0;
                }
                h.Order = HirelingOrder.Attack;
                h.AttackTarget = foe.Id;
                Publish(new MessageEvent($"{h.Name} goes for {MonsterName(foe)}!"));
                return 0;
            case HirelingOrder.Hold:
                h.Order = HirelingOrder.Hold;
                Publish(new MessageEvent($"{h.Name} holds this spot."));
                return 0;
            default:
                h.Order = HirelingOrder.Follow;
                Publish(new MessageEvent($"{h.Name} falls in close behind you."));
                return 0;
        }
    }

    /// <summary>What looking at the hireling says: "Bergil the archer, your hireling (level 7, wounded, following)".</summary>
    private static string HirelingLook(Hireling h, int percent)
    {
        var health = percent >= 100 ? "unhurt" : percent >= 60 ? "somewhat wounded" : percent >= 25 ? "wounded" : percent >= 10 ? "badly wounded" : "almost dead";
        return $"{h.Title}, your hireling (level {h.Level}, {health}, {HirelingDoing(h)})";
    }

    /// <summary>What the hireling is about: following, holding, attacking — or falling back.</summary>
    public static string HirelingDoing(Hireling h) =>
        h.Fleeing ? "falling back" : h.Order switch { HirelingOrder.Hold => "holding", HirelingOrder.Attack => "attacking", _ => "following" };

    /// <summary>The hireling's health for the sidebar, as the monster health bar shows a monster's; null with none here.</summary>
    public HealthBar? HirelingHealthBar()
    {
        if (Hireling is not { } h || HirelingHere is not { } body) return null;
        var pct = (int)(100L * Math.Max(0, body.Hp) / Math.Max(1, body.MaxHp));
        var stars = pct < 10 ? 1 : pct < 90 ? pct / 10 + 1 : 10;
        var color = h.Fleeing ? "Violet" : pct >= 100 ? "LightGreen" : pct >= 60 ? "Yellow" : pct >= 25 ? "Orange" : pct >= 10 ? "LightRed" : "Red";
        return new HealthBar($"{h.Title} (level {h.Level}, {HirelingDoing(h)})", stars, color, Unknown: !body.IsVisible);
    }

    // --- Gold, experience and levels -------------------------------------------------------------------

    /// <summary>Gold picked up: the hireling takes their share of it at once. Returns what is left for you.</summary>
    private long TakeHirelingShare(long gold)
    {
        if (Hireling is not { } h || gold <= 0) return gold;
        var share = gold * h.Share / 100;
        if (share <= 0) return gold;
        h.Earned += share;
        Publish(new MessageEvent($"{h.Name} takes a share: {share} gold."));
        return gold - share;
    }

    /// <summary>
    /// A monster died: the hireling learns from it — all it is worth to them (Angband's exp × level ÷
    /// their level) if they made the kill, a third if they had a hand in it.
    /// </summary>
    private void HirelingKillCredit(MonsterRaceDef race, bool killed, bool helped)
    {
        if (Hireling is not { } h || (!killed && !helped)) return;
        var worth = (long)race.Experience * race.Depth / Math.Max(1, h.Level);
        if (killed) h.Kills++;
        GainHirelingExperience(killed ? worth : worth / 3);
    }

    /// <summary>Experience for the hireling, and the levels it brings — never past your own level.</summary>
    internal void GainHirelingExperience(long amount)
    {
        if (Hireling is not { } h || amount <= 0) return;
        h.Experience += amount;
        while (h.Level < Math.Min(StatTables.MaxLevel, Player.Level) && h.Experience >= Hireling.ExperienceFor(h.Level + 1))
        {
            h.Level++;
            Publish(new MessageEvent($"{h.Title} has reached level {h.Level}."));
            if (h.Level % 10 == 0) AddHistory($"{h.Title} reached level {h.Level}");
        }
        RefreshHirelingBody();
    }

    /// <summary>The body keeps up with the record: its race (blows, armour, speed) and hit points for its level.</summary>
    private void RefreshHirelingBody()
    {
        if (Hireling is not { Body: { } body } h) return;
        var max = Hireling.MaxHpFor(h.Role, h.Level);
        if (body.MaxHp != max)
        {
            body.Hp += max - body.MaxHp;
            body.MaxHp = max;
        }
        body.Race = HirelingRace(h);
        body.Speed = body.Race.Speed;
    }

    // --- Coming and going with you ---------------------------------------------------------------------

    /// <summary>Leaving a level: the hireling comes too (taken off it, before the level is kept or thrown away).</summary>
    private void TakeHirelingAlong()
    {
        if (HirelingHere is { } body) Level.Monsters.Remove(body);
        if (Hireling is { } h)
        {
            h.AttackTarget = 0;
            if (h.Order == HirelingOrder.Attack) h.Order = HirelingOrder.Follow;
        }
    }

    /// <summary>
    /// The hireling arrives beside you (the nearest open square, the first in reading order of those):
    /// on a new level, and when hired. Back in town, their wounds are seen to.
    /// </summary>
    private void PlaceHireling()
    {
        if (Hireling is not { } h || HirelingHere is not null) return;
        var me = Player.Position;
        Loc? spot = null;
        for (var r = 1; r <= 10 && spot is null; r++)
            foreach (var p in Level.AllLocs().Where(p => p.ChebyshevTo(me) == r))
                if (Level.InBoundsFully(p) && Level.IsPassable(p) && Level[p].Monster == 0 && !Level.Has(p, TerrainFlags.Fiery)
                    && Level.FeatureAt(p).Shop is null && Combat.ProjectionPath.Projectable(Level, me, p, r + 1))
                {
                    spot = p;
                    break;
                }
        if (spot is not { } at) return; // (nowhere near: they catch up on the next level)
        var oldHp = h.Body?.Hp;
        var race = HirelingRace(h);
        var body = new Monster(Level.Monsters.NextId, race, at, race.HitPoints, race.Speed) { IsAlly = true, Aware = true };
        body.GroupId = body.Id;
        if (oldHp is { } hp && Player.Depth > 0) body.Hp = Math.Clamp(hp, 0, body.MaxHp);
        if (Player.Depth == 0) h.Fleeing = false;
        Level.Monsters.Add(body);
        Scheduler.Add(body);
        h.Body = body;
        h.DeepestTogether = Math.Max(h.DeepestTogether, Player.Depth);
    }

    /// <summary>Arriving on a level (after it is made and peopled): back in town, the deepest you came up from together counts.</summary>
    private void HirelingArrives(int fromDepth)
    {
        if (Hireling is not { } h) return;
        if (Player.Depth == 0 && fromDepth > 0) h.DeepestReturned = Math.Max(h.DeepestReturned, h.DeepestTogether);
        PlaceHireling();
    }

    // --- The end ---------------------------------------------------------------------------------------

    /// <summary>The hireling dies: for good. A line in the history and among the fallen companions.</summary>
    private void HirelingDies(string killedBy)
    {
        if (Hireling is not { } h) return;
        if (h.Body is { } body && Level.Monsters[body.Id] == body) Level.Monsters.Remove(body);
        Hireling = null;
        FallenCompanions.Add(new FallenCompanion
        {
            Name = h.Name, Look = h.Look, Role = h.Role, Level = h.Level, Kills = h.Kills, Depth = Player.Depth,
            KilledBy = killedBy, Turn = GameTurn, Served = GameTurn - h.HiredTurn,
        });
        Publish(new MessageEvent($"{h.Title} has fallen."));
        AddHistory($"{h.Title} fell, killed by {killedBy}");
        Publish(new HirelingFallenEvent(h.Name, killedBy));
        Disturb();
    }
}
