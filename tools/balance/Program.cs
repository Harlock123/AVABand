// Balance check: what each depth holds, and how hard it is, over many generated levels.
//
// Default mode — for depths 1 to 99: objects on the floor (and how many are egos, artifacts, cursed),
// gold, monsters, traps; and, for a warrior of the depth's level (clvl = depth, at most 50) with the
// hit points that brings, how often they die holding still for 300 turns there.
//
// `play` mode — a bot plays each depth: a warrior of the depth's level, with a good weapon and body
// armour made for that depth and a stock of healing potions and Phase Door, explores the level,
// fights what it meets (walking up to it and hitting it), quaffs when hurt and blinks when nearly
// dead (and cures blindness and confusion, and blinks away when afraid), for up to 1000 turns. It
// reports how often it lives (and how often it is sent off the level alive — a trap door, a
// monster's teleport-level), what it kills, the experience it gains, how much of the level it sees
// and the potions it drinks. It rests when nothing awake is in view, backs into a corridor when
// several foes come at it in the open, and shoots them as they come if it has a launcher. `play
// [runs] mage` plays a mage instead: its books, the spells of its level learned, and its strongest
// bolt or ball it can afford cast at what it sees. `one <depth> <seed> [class]` plays a single run
// (BOT_TRACE=1 shows its end); BOT_PLAIN=1 plays as the first bot did (no resting, corridors or
// launcher), to compare.
//
// `soak [decisions]` — for CI: a warrior, a mage and a ranger of level 50, five fixed seeds each, played by the bot
// for that many decisions (default 4000), jumping deeper whenever a level is done, while a replay
// records the game. It fails on an exception, on a decision that takes more than 10 seconds or a
// game that hangs, and if the replay doesn't play back to exactly the same end; a failing game's
// replay is written to soak-failures/ (watch it with Game > Watch a replay...).
//
// Both are kept to the game's plainest API, so the same program runs on older commits to compare.
// Run: dotnet run -c Release --project tools/balance [levels per depth, default 30]
//      dotnet run -c Release --project tools/balance play [runs per depth, default 20]
using System.Globalization;
using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Persistence;
using Angband.Core.Randomness;
using Angband.Data;

var data = DataLoader.Load(DataLoader.DefaultDataDirectory);
var play = args.Length > 0 && args[0] == "play";
var count = args.Length > (play ? 1 : 0) && int.TryParse(args[play ? 1 : 0], CultureInfo.InvariantCulture, out var n) ? n : play ? 20 : 30;
var depths = new[] { 1, 5, 10, 20, 30, 40, 50, 60, 70, 80, 90, 99 };
string Num(double x) => x.ToString("0.0", CultureInfo.InvariantCulture);

if (args.Length > 0 && args[0] == "soak")
{
    var decisions = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 4000;
    var failures = 0;
    foreach (var cls in new[] { "warrior", "mage", "ranger" })
    foreach (var seed in new[] { 101UL, 202UL, 303UL, 404UL, 505UL })
        if (!Soak.Run(data, cls, seed, decisions)) failures++;
    Console.WriteLine(failures == 0 ? "Soak: every game played and replayed cleanly." : $"Soak: {failures} game(s) failed.");
    Environment.Exit(failures == 0 ? 0 : 1);
}

if (args.Length > 0 && args[0] == "one")
{
    var one = Bot.Play(data, int.Parse(args[1], CultureInfo.InvariantCulture), ulong.Parse(args[2], CultureInfo.InvariantCulture),
        args.Length > 3 ? args[3] : "warrior");
    Console.WriteLine(one);
    return;
}

if (play)
{
    var cls = args.Length > 2 ? args[2] : "warrior";
    Console.WriteLine($"{count} runs per depth; a clvl-matched {cls} with depth-made gear and potions plays up to {Bot.MaxTurns} turns"
                      + (Bot.Plain ? " (plain bot)" : ""));
    Console.WriteLine("depth | survived% | left level% | turns | kills | exp gained | level seen% | potions | blinks | rests | shots/casts");
    foreach (var depth in depths)
    {
        double lived = 0, left = 0, turns = 0, kills = 0, exp = 0, seen = 0, potions = 0, blinks = 0, rests = 0, shots = 0;
        for (var run = 0; run < count; run++)
        {
            var r = Bot.Play(data, depth, (ulong)(depth * 1000 + run), cls);
            if (r.Survived) lived++;
            if (r.LeftLevel) left++;
            turns += r.Turns;
            kills += r.Kills;
            exp += r.Experience;
            seen += r.SeenPercent;
            potions += r.Potions;
            blinks += r.Blinks;
            rests += r.Rests;
            shots += r.Shots;
        }
        Console.WriteLine($"{depth,5} | {Num(100 * lived / count),9} | {Num(100 * left / count),11} | {Num(turns / count),5} | {Num(kills / count),5} | "
                          + $"{Num(exp / count),10} | {Num(seen / count),11} | {Num(potions / count),7} | {Num(blinks / count),6} | {Num(rests / count),5} | {Num(shots / count),11}");
    }
    return;
}

const int holdTurns = 300;
Console.WriteLine($"{count} levels per depth; deaths: a clvl-matched warrior holding {holdTurns} turns");
Console.WriteLine("depth | objects | egos% | artifacts | cursed% | gold/level | monsters | traps | deaths%");
foreach (var depth in depths)
{
    double objects = 0, egos = 0, artifacts = 0, cursed = 0, gold = 0, monsters = 0, traps = 0, deaths = 0;
    for (var run = 0; run < count; run++)
    {
        var game = GameSession.NewGame(data, (ulong)(depth * 1000 + run), "warrior");
        game.MarkDebugUsed();
        game.Player.Hp = game.Player.MaxHp = 1_000_000;
        game.Execute(new DebugJumpCommand(depth));
        foreach (var (_, item) in game.Level.Objects.All)
        {
            if (item.IsGold) { gold += item.GoldValue; continue; }
            objects++;
            if (item.Ego is not null) egos++;
            if (item.IsArtifact) artifacts++;
            if (item.IsCursed) cursed++;
        }
        monsters += game.Level.Monsters.All.Count();
        traps += game.Level.AllLocs().Count(p => game.Level[p].Trap != 0 && game.Data.TrapByIndex(game.Level[p].Trap) is { Warding: false, Web: false });

        // The danger: a warrior of the depth's level, as tough as that makes them.
        var clvl = Math.Clamp(depth, 1, 50);
        if (clvl > 1) game.GainExperience(game.ExperienceForLevel(clvl - 1) - game.Player.Experience);
        game.Player.Hp = game.Player.MaxHp = Math.Max(20, clvl * 12);
        for (var i = 0; i < holdTurns && !game.IsGameOver; i++) game.Execute(new HoldCommand());
        if (game.Player.IsDead) deaths++;
    }
    string F(double x) => (x / count).ToString("0.0", CultureInfo.InvariantCulture);
    string P(double part, double whole) => whole == 0 ? "-" : (100 * part / whole).ToString("0.0", CultureInfo.InvariantCulture);
    Console.WriteLine($"{depth,5} | {F(objects),7} | {P(egos, objects),5} | {F(artifacts),9} | {P(cursed, objects),7} | {F(gold),10} | {F(monsters),8} | {F(traps),5} | {P(deaths, count),7}");
}

/// <summary>What one run of the bot came to.</summary>
internal readonly record struct BotResult(bool Survived, bool LeftLevel, int Turns, int Kills, long Experience, double SeenPercent, int Potions, int Blinks,
    int Rests, int Shots, string? KilledBy = null);

/// <summary>
/// A simple player: explore, fight what comes (from a corridor if several come, shooting or casting
/// as they close), quaff when hurt, blink when nearly dead, rest when it is quiet.
/// </summary>
internal static class Bot
{
    public const int MaxTurns = 1000;

    /// <summary>Play as the first bot did: no resting, corridors, launcher or spells.</summary>
    public static readonly bool Plain = Environment.GetEnvironmentVariable("BOT_PLAIN") is { Length: > 0 };

    // Potion ids, as one commit or another spells them.
    private static readonly string[][] Healing =
    [
        ["cure_light_wounds", "potion_of_cure_light_wounds"], ["cure_serious_wounds", "potion_of_cure_serious_wounds"],
        ["potion_of_cure_critical_wounds", "cure_critical_wounds"], ["potion_of_healing", "healing"],
    ];

    /// <summary>A mage's attack spells, strongest first (the bot casts the first it knows and can afford).</summary>
    private static readonly string[] MageAttacks = ["mana_storm", "mana_bolt", "fire_ball", "acid_spray", "frost_bolt", "magic_missile"];

    private static string? Kind(GameData data, string[] ids) => ids.FirstOrDefault(id => data.Object(id) is not null);

    internal sealed class Tally
    {
        public int Potions, Blinks, Rests, Shots, Waits;
    }

    /// <summary>A character of the class at the depth's level, equipped for it, standing on a new level there.</summary>
    public static GameSession Setup(GameData data, int depth, ulong seed, string cls, int? level = null)
    {
        var game = GameSession.NewGame(data, seed, cls);
        game.MarkDebugUsed();
        var clvl = level ?? Math.Clamp(depth, 1, 50);
        if (clvl > 1) game.GainExperience(game.ExperienceForLevel(clvl - 1) - game.Player.Experience);
        Equip(game, Math.Max(depth, clvl), seed);
        Give(game, Kind(data, Healing[depth < 20 ? 0 : depth < 40 ? 1 : 2]), 5 + depth / 10);
        Give(game, "phase_door", 5);
        if (!Plain) LearnSpells(game);
        game.Player.Hp = game.Player.MaxHp;
        // (Items given straight into the pack: count their weight, as a loaded game would.)
        game.RecalculateBonuses();
        game.Player.Hp = game.Player.MaxHp;
        game.Player.Mana = game.Player.MaxMana;
        game.Execute(new DebugJumpCommand(depth));
        return game;
    }

    public static BotResult Play(GameData data, int depth, ulong seed, string cls = "warrior")
    {
        var game = Setup(data, depth, seed, cls);
        if (Environment.GetEnvironmentVariable("BOT_TRACE") is { Length: > 0 })
            Console.WriteLine($"{cls} clvl {game.Player.Level} hp {game.Player.Hp}/{game.Player.MaxHp} sp {game.Player.Mana} ac {game.Player.Armour} "
                + $"blows {game.Player.Blows} weapon {(game.Player.Inventory.Weapon is { } w ? game.Describe(w) : "none")} "
                + $"bow {(game.Player.Inventory.Bow is { } bw ? game.Describe(bw) : "none")} spells {string.Join(",", game.Player.LearnedSpells)}");

        var log = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => log.Add($"[{game.Player.Hp}] {m.Text}"));
        var kills = 0;
        game.Events.Subscribe<MonsterKilledEvent>(_ => kills++);
        var tally = new Tally();
        var startExp = game.Player.MaxExperience;
        var turns = 0;
        var explorer = new Explorer();
        for (; turns < MaxTurns && !game.IsGameOver && game.Player.Depth == depth; turns++)
        {
            explorer.Visited.Add(game.Player.Position);
            if (!Act(game, explorer, tally)) game.Execute(new HoldCommand());
        }

        if (Environment.GetEnvironmentVariable("BOT_TRACE") is { Length: > 0 })
            foreach (var line in log.TakeLast(60)) Console.WriteLine(line);
        var passable = game.Level.AllLocs().Count(p => game.Level.IsPassable(p));
        var known = game.Level.AllLocs().Count(p => game.Level.IsPassable(p) && game.Known.IsKnown(p));
        return new BotResult(!game.Player.IsDead, !game.Player.IsDead && game.Player.Depth != depth, turns, kills, game.Player.MaxExperience - startExp,
            passable == 0 ? 0 : 100.0 * known / passable, tally.Potions, tally.Blinks, tally.Rests, tally.Shots,
            game.Player.IsDead ? game.Player.KilledBy : null);
    }

    /// <summary>The books of the class's spells up to its level, and every spell it can learn from them.</summary>
    private static void LearnSpells(GameSession game)
    {
        var books = game.ClassSpells.Where(s => game.SpellInfo(s)!.Level <= game.Player.Level).Select(s => s.Book).Distinct().ToList();
        if (books.Count == 0) return;
        foreach (var book in books) Give(game, book, 1);
        for (var tries = 0; tries < 100 && game.StudyableSpells().FirstOrDefault() is { } spell; tries++)
        {
            var learned = game.ChoosesSpells ? game.Execute(new StudyCommand(spell.Id)) : game.Execute(new StudyCommand(Book: spell.Book));
            if (!learned) break;
        }
        game.RecalculateMana();
    }

    /// <summary>The bot's state from one decision to the next.</summary>
    public sealed class Mind
    {
        internal Explorer Explorer { get; set; } = new();
        internal Tally Tally { get; } = new();
        internal int Depth { get; set; } = -1;
    }

    /// <summary>One decision for a game the caller drives (a new level starts a new exploration). False when there was nothing to do.</summary>
    public static bool Decide(GameSession game, Mind mind)
    {
        if (game.Player.Depth != mind.Depth)
        {
            mind.Explorer = new Explorer();
            mind.Depth = game.Player.Depth;
        }
        mind.Explorer.Visited.Add(game.Player.Position);
        return Act(game, mind.Explorer, mind.Tally);
    }

    /// <summary>One decision. False when there was nothing to do (the level is explored and quiet).</summary>
    private static bool Act(GameSession game, Explorer explorer, Tally tally)
    {
        var p = game.Player;
        var hurt = p.Hp * 100 / Math.Max(1, p.MaxHp);
        // What it goes for: anything next to it; otherwise anything awake, and sleepers no deeper than
        // its own level — but not breeders (a worm mass is a waste of time unless it's in the way).
        var foes = game.Level.Monsters.All
            .Where(m => m.IsVisible && (m.Position.DistanceTo(p.Position) <= 1
                || (!m.Race.Has("MULTIPLY") && (m.Sleep == 0 || m.Race.Depth <= p.Level))))
            .OrderBy(m => m.Position.DistanceTo(p.Position)).ToList();
        var afraid = p.Timed.Has("afraid") || p.Timed.Has("terror");

        if (hurt < 20 && foes.Count > 0 && Find(game, "phase_door") is { } phase)
        {
            tally.Blinks++;
            return game.Execute(new UseCommand(phase));
        }
        // Blind or confused, it can't fight or read: a cure potion clears both (and heals).
        var dazed = p.Timed.Has("blind") || p.Timed.Has("confused");
        if ((hurt < 50 || dazed) && Healing.Select(h => Kind(game.Data, h) is { } id ? Find(game, id) : null).FirstOrDefault(i => i is not null) is { } cure)
        {
            tally.Potions++;
            return game.Execute(new UseCommand(cure));
        }
        // Afraid, it can't fight hand to hand: blink away if anything is close, else keep exploring.
        if (afraid && foes.FirstOrDefault() is { } near && near.Position.DistanceTo(p.Position) <= 2
            && !p.Timed.Has("blind") && Find(game, "phase_door") is { } escape)
        {
            tally.Blinks++;
            return game.Execute(new UseCommand(escape));
        }

        var awake = foes.Where(m => m.Sleep == 0).ToList();
        if (!Plain)
        {
            // Quiet and hurt (or low on mana): rest.
            if (awake.Count == 0 && (hurt < 70 || (p.MaxMana > 0 && p.Mana * 2 < p.MaxMana)) && !dazed)
            {
                var before = (p.Hp, p.Mana);
                if (game.Execute(new RestCommand()) || (p.Hp, p.Mana) != before)
                {
                    tally.Rests++;
                    return true;
                }
            }
            // Something awake at range and in the line of fire: a spell, or a shot.
            if (awake.FirstOrDefault(m => m.Position.DistanceTo(p.Position) > 1
                    && ProjectionPath.Projectable(game.Level, p.Position, m.Position, 20)) is { } mark && !p.Timed.Has("blind"))
            {
                if (AttackSpell(game) is { } spell && !p.Timed.Has("confused"))
                {
                    tally.Shots++;
                    tally.Waits = 0;
                    if (game.Execute(new CastCommand(spell, mark.Position))) return true;
                }
                if (p.Inventory.Bow is { } bow && p.Inventory.Quiver.Any(q => q.Base.AmmoClass == bow.Base.AmmoClass)
                    && mark.Position.DistanceTo(p.Position) <= 6 + 2 * bow.Multiplier)
                {
                    tally.Shots++;
                    tally.Waits = 0;
                    if (game.Execute(new FireCommand(mark.Position))) return true;
                }
            }
            // Several awake and coming, nothing yet at hand, and it stands in the open: back into a corridor.
            var coming = awake.Where(m => m.Position.DistanceTo(p.Position) <= 7).ToList();
            if (coming.Count >= 2 && coming[0].Position.DistanceTo(p.Position) > 1 && !afraid)
            {
                if (Openness(game, p.Position) > 2 && Corridor(game, coming) is { } hole)
                    return game.Execute(new WalkCommand(Toward(p.Position, hole)));
                // In the corridor already: let them come, a few turns at most.
                if (Openness(game, p.Position) <= 2 && tally.Waits++ < 5) return game.Execute(new HoldCommand());
            }
        }
        if (foes.FirstOrDefault() is { } foe && !afraid)
        {
            tally.Waits = 0;
            if (foe.Position.DistanceTo(p.Position) <= 1) return game.Execute(new WalkCommand(Toward(p.Position, foe.Position)));
            if (game.FindPath(p.Position, foe.Position) is { Count: > 0 } path)
                return game.Execute(new WalkCommand(Toward(p.Position, path[0])));
        }
        // Explore: keep going to the chosen spot at the edge of the known, then choose the next.
        if (explorer.Target is { } t && (t == p.Position || explorer.Visited.Contains(t))) explorer.Target = null;
        explorer.Target ??= Frontier(game, explorer.Visited);
        if (explorer.Target is { } target && game.FindPath(p.Position, target) is { Count: > 0 } way)
            return game.Execute(new WalkCommand(Toward(p.Position, way[0])));
        explorer.Target = null;
        return false;
    }

    /// <summary>The strongest attack spell learned that the bot has the mana for and fails no more than one time in four.</summary>
    private static string? AttackSpell(GameSession game) =>
        MageAttacks.FirstOrDefault(id => game.Player.LearnedSpells.Contains(id) && game.Data.Spells.FirstOrDefault(s => s.Id == id) is { } s
            && game.SpellInfo(s) is { } info && info.Mana <= game.Player.Mana && game.SpellFailChance(s) <= 25);

    /// <summary>How many of a square's eight neighbours are open.</summary>
    private static int Openness(GameSession game, Loc at) => game.Level.Neighbors(at).Count(n => game.Level.IsPassable(n));

    /// <summary>
    /// The first step toward the nearest known corridor square (two open neighbours or fewer) within
    /// eight steps that is no nearer the foes than the bot is now.
    /// </summary>
    private static Loc? Corridor(GameSession game, List<Monster> foes)
    {
        var level = game.Level;
        var start = game.Player.Position;
        int Nearest(Loc at) => foes.Min(m => m.Position.DistanceTo(at));
        var now = Nearest(start);
        var first = new Dictionary<Loc, Loc> { [start] = start };
        var steps = new Dictionary<Loc, int> { [start] = 0 };
        var queue = new Queue<Loc>([start]);
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            if (at != start && Openness(game, at) <= 2 && Nearest(at) >= now) return first[at];
            if (steps[at] >= 8) continue;
            foreach (var n in level.Neighbors(at))
            {
                if (first.ContainsKey(n) || !game.Known.IsKnown(n) || !level.IsPassable(n) || level.Monsters.At(n) is not null) continue;
                if (Nearest(n) < 2) continue;
                first[n] = at == start ? n : first[at];
                steps[n] = steps[at] + 1;
                queue.Enqueue(n);
            }
        }
        return null;
    }

    /// <summary>Where the bot is headed, and where it has been (a spot it has stood on is explored).</summary>
    internal sealed class Explorer
    {
        public HashSet<Loc> Visited { get; } = [];
        public Loc? Target { get; set; }
    }

    /// <summary>The nearest known open square beside the unknown that the bot hasn't stood on.</summary>
    private static Loc? Frontier(GameSession game, HashSet<Loc> visited)
    {
        var level = game.Level;
        var start = game.Player.Position;
        var prev = new Dictionary<Loc, Loc> { [start] = start };
        var queue = new Queue<Loc>([start]);
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            if (at != start && !visited.Contains(at) && level.Neighbors(at).Any(n => !game.Known.IsKnown(n))) return at;
            foreach (var n in level.Neighbors(at))
            {
                if (prev.ContainsKey(n) || !game.Known.IsKnown(n)) continue;
                var feature = game.Data.Terrain[game.Known.Feature(n)];
                if (!feature.Has(TerrainFlags.Passable) && !feature.Has(TerrainFlags.DoorClosed)) continue;
                if (feature.Has(TerrainFlags.Fiery)) continue;
                if (level[n].Trap != 0 && level[n].Has(Angband.Core.World.SquareFlags.TrapVisible)) continue;
                prev[n] = at;
                queue.Enqueue(n);
            }
        }
        return null;
    }

    private static Direction Toward(Loc from, Loc to) =>
        DirectionExtensions.FromOffset(Math.Sign(to.X - from.X), Math.Sign(to.Y - from.Y));

    private static Item? Find(GameSession game, string kind) => game.Player.Inventory.Pack.FirstOrDefault(i => i.Kind.Id == kind);

    private static void Give(GameSession game, string? kind, int number)
    {
        if (kind is null || game.Data.Object(kind) is null) return;
        var item = game.Objects.Create(kind, number);
        game.Knowledge.LearnKind(item.Kind);
        game.Player.Inventory.Add(item);
    }

    /// <summary>
    /// The best weapon (most damage a blow) and body armour (most armour) among 30 good objects of
    /// each made for the depth, and (unless plain) the launcher with the most might and 40 plain
    /// missiles for it.
    /// </summary>
    private static void Equip(GameSession game, int depth, ulong seed)
    {
        var rng = new GameRandom(seed ^ 0x5EED);
        var slots = Plain ? new[] { EquipSlot.Weapon, EquipSlot.Body } : [EquipSlot.Weapon, EquipSlot.Body, EquipSlot.Bow];
        foreach (var slot in slots)
        {
            var found = new List<Item>();
            for (var i = 0; i < 3000 && found.Count < 30; i++)
                if (game.Objects.Make(rng, depth, good: true) is { } item && item.Base.Slot == slot && !item.IsCursed
                    && (slot != EquipSlot.Weapon || item.Weight <= 200)) // one a warrior can swing well enough
                    found.Add(item);
            var best = slot switch
            {
                EquipSlot.Weapon => found.OrderByDescending(i => i.Damage.Count * (i.Damage.Sides + 1) / 2.0 + i.ToDam).FirstOrDefault(),
                EquipSlot.Bow => found.OrderByDescending(i => i.Multiplier).ThenByDescending(i => i.ToDam).FirstOrDefault(),
                _ => found.OrderByDescending(i => i.Armour + i.ToAc).FirstOrDefault(),
            };
            if (best is null) continue;
            game.Player.Inventory.Add(best);
            game.Execute(new WieldCommand(best));
            if (slot == EquipSlot.Bow && game.Data.Objects
                    .Where(k => game.Data.ObjectBase(k.Base) is { IsAmmo: true } b && b.AmmoClass == best.Base.AmmoClass)
                    .OrderBy(k => k.Level).FirstOrDefault() is { } ammo)
                Give(game, ammo.Id, 40);
        }
    }
}

/// <summary>Long bot-played games, recorded and replayed, for CI: crashes, hangs and nondeterminism.</summary>
internal static class Soak
{
    private const int DecisionsPerLevel = 250;
    private static readonly TimeSpan SlowDecision = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GameTimeout = TimeSpan.FromMinutes(10);

    public static bool Run(GameData data, string cls, ulong seed, int decisions)
    {
        var game = Bot.Setup(data, 1, seed, cls, level: 50);
        game.Recorder = new ReplayRecorder(game);
        var mind = new Bot.Mind();
        var done = 0;
        var maxDepth = 1;
        string? failure = null;
        var slowest = TimeSpan.Zero;
        var marks = new List<(int Steps, ReplayEnd State)>();

        var thread = new Thread(() =>
        {
            try
            {
                var onLevel = 0;
                var clock = new System.Diagnostics.Stopwatch();
                for (; done < decisions && !game.IsGameOver; done++)
                {
                    var depth = game.Player.Depth;
                    clock.Restart();
                    var acted = Bot.Decide(game, mind);
                    if (!acted) game.Execute(new HoldCommand());
                    onLevel = game.Player.Depth == depth ? onLevel + 1 : 0;
                    // Explored, or long enough here: on down (a debug jump, so the replay has it).
                    if (!acted || onLevel >= DecisionsPerLevel)
                    {
                        game.Execute(new DebugJumpCommand(Math.Min(game.Player.Depth + 6, 98)));
                        onLevel = 0;
                    }
                    clock.Stop();
                    marks.Add((game.Recorder.StepCount, ReplayCodec.EndOf(game)));
                    if (clock.Elapsed > slowest) slowest = clock.Elapsed;
                    if (clock.Elapsed > SlowDecision)
                    {
                        failure = $"decision {done} at {depth * 50} ft took {clock.Elapsed.TotalSeconds:0.0}s";
                        return;
                    }
                    maxDepth = Math.Max(maxDepth, game.Player.Depth);
                }
            }
            catch (Exception e)
            {
                failure = $"decision {done}: {e}";
            }
        }, 64 * 1024 * 1024) { IsBackground = true };
        thread.Start();
        if (!thread.Join(GameTimeout))
        {
            Console.WriteLine($"{cls} {seed}: HUNG after {done} decisions at {game.Player.Depth * 50} ft");
            Save(game, cls, seed);
            return false;
        }

        var file = game.Recorder.ToFile(game);
        var end = $"{done} decisions, to {maxDepth * 50} ft, {(game.Player.IsDead ? "killed by " + game.Player.KilledBy : "alive")}, "
                  + $"{file.Steps.Count} steps, slowest decision {slowest.TotalMilliseconds:0} ms";
        if (failure is not null)
        {
            Console.WriteLine($"{cls} {seed}: FAILED — {failure}\n  ({end})");
            Save(game, cls, seed);
            return false;
        }

        // Back through a file, as a player would watch it: the same end, the same history.
        var path = Path.Combine(Path.GetTempPath(), $"avaband-soak-{cls}-{seed}{ReplayFile.Extension}");
        file.Write(path);
        var player = new ReplayPlayer(data, ReplayFile.Read(path));
        File.Delete(path);
        var next = 0;
        string? divergence = null;
        while (player.Step())
        {
            while (next < marks.Count && marks[next].Steps < player.Position) next++;
            if (divergence is null && next < marks.Count && marks[next].Steps == player.Position
                && ReplayCodec.EndOf(player.Game) != marks[next].State)
            {
                var from = Math.Max(0, (next > 0 ? marks[next - 1].Steps : 0));
                divergence = $"first differs after step {player.Position}: recorded {marks[next].State}, replayed {ReplayCodec.EndOf(player.Game)}; "
                             + $"steps {from}..{player.Position - 1}: "
                             + string.Join(" | ", file.Steps.Skip(from).Take(player.Position - from).Select(x => x!.ToJsonString()));
            }
        }
        var same = player.Matches == true
                   && player.Game.History.Select(h => h.Text).SequenceEqual(game.History.Select(h => h.Text));
        if (!same)
        {
            Console.WriteLine($"{cls} {seed}: REPLAY DIFFERS — recorded {file.End}, replayed {ReplayCodec.EndOf(player.Game)}\n  ({end})\n  {divergence}");
            Save(game, cls, seed);
            return false;
        }
        Console.WriteLine($"{cls} {seed}: ok — {end}; replay matches");
        return true;
    }

    private static void Save(GameSession game, string cls, ulong seed)
    {
        Directory.CreateDirectory("soak-failures");
        var path = Path.Combine("soak-failures", $"{cls}-{seed}{ReplayFile.Extension}");
        game.Recorder!.ToFile(game).Write(path);
        Console.WriteLine($"  replay written to {path}");
    }
}
