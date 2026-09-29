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
// and the potions it drinks. `one <depth> <seed>` plays a single run (BOT_TRACE=1 shows its end).
//
// Both are kept to the game's plainest API, so the same program runs on older commits to compare.
// Run: dotnet run -c Release --project tools/balance [levels per depth, default 30]
//      dotnet run -c Release --project tools/balance play [runs per depth, default 20]
using System.Globalization;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Randomness;
using Angband.Data;

var data = DataLoader.Load(DataLoader.DefaultDataDirectory);
var play = args.Length > 0 && args[0] == "play";
var count = args.Length > (play ? 1 : 0) && int.TryParse(args[play ? 1 : 0], CultureInfo.InvariantCulture, out var n) ? n : play ? 20 : 30;
var depths = new[] { 1, 5, 10, 20, 30, 40, 50, 60, 70, 80, 90, 99 };
string Num(double x) => x.ToString("0.0", CultureInfo.InvariantCulture);

if (args.Length > 0 && args[0] == "one")
{
    var one = Bot.Play(data, int.Parse(args[1], CultureInfo.InvariantCulture), ulong.Parse(args[2], CultureInfo.InvariantCulture));
    Console.WriteLine(one);
    return;
}

if (play)
{
    Console.WriteLine($"{count} runs per depth; a clvl-matched warrior with depth-made gear and potions plays up to {Bot.MaxTurns} turns");
    Console.WriteLine("depth | survived% | left level% | turns | kills | exp gained | level seen% | potions | blinks");
    foreach (var depth in depths)
    {
        double lived = 0, left = 0, turns = 0, kills = 0, exp = 0, seen = 0, potions = 0, blinks = 0;
        for (var run = 0; run < count; run++)
        {
            var r = Bot.Play(data, depth, (ulong)(depth * 1000 + run));
            if (r.Survived) lived++;
            if (r.LeftLevel) left++;
            turns += r.Turns;
            kills += r.Kills;
            exp += r.Experience;
            seen += r.SeenPercent;
            potions += r.Potions;
            blinks += r.Blinks;
        }
        Console.WriteLine($"{depth,5} | {Num(100 * lived / count),9} | {Num(100 * left / count),11} | {Num(turns / count),5} | {Num(kills / count),5} | "
                          + $"{Num(exp / count),10} | {Num(seen / count),11} | {Num(potions / count),7} | {Num(blinks / count),6}");
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
    string? KilledBy = null);

/// <summary>A simple player: explore, fight what comes, quaff when hurt, blink when nearly dead.</summary>
internal static class Bot
{
    public const int MaxTurns = 1000;

    // Potion ids, as one commit or another spells them.
    private static readonly string[][] Healing =
    [
        ["cure_light_wounds", "potion_of_cure_light_wounds"], ["cure_serious_wounds", "potion_of_cure_serious_wounds"],
        ["potion_of_cure_critical_wounds", "cure_critical_wounds"], ["potion_of_healing", "healing"],
    ];

    private static string? Kind(GameData data, string[] ids) => ids.FirstOrDefault(id => data.Object(id) is not null);

    public static BotResult Play(GameData data, int depth, ulong seed)
    {
        var game = GameSession.NewGame(data, seed, "warrior");
        game.MarkDebugUsed();
        var clvl = Math.Clamp(depth, 1, 50);
        if (clvl > 1) game.GainExperience(game.ExperienceForLevel(clvl - 1) - game.Player.Experience);
        Equip(game, depth, seed);
        Give(game, Kind(data, Healing[depth < 20 ? 0 : depth < 40 ? 1 : 2]), 5 + depth / 10);
        Give(game, "phase_door", 5);
        game.Player.Hp = game.Player.MaxHp;
        game.Execute(new DebugJumpCommand(depth));
        if (Environment.GetEnvironmentVariable("BOT_TRACE") is { Length: > 0 })
            Console.WriteLine($"clvl {game.Player.Level} hp {game.Player.Hp}/{game.Player.MaxHp} ac {game.Player.Armour} blows {game.Player.Blows} "
                + $"weapon {(game.Player.Inventory.Weapon is { } w ? game.Describe(w) : "none")} body {(game.Player.Inventory.InSlot(EquipSlot.Body) is { } b ? game.Describe(b) : "none")}");

        var log = new List<string>();
        game.Events.Subscribe<MessageEvent>(m => log.Add($"[{game.Player.Hp}] {m.Text}"));
        var kills = 0;
        var potions = 0;
        var blinks = 0;
        game.Events.Subscribe<MonsterKilledEvent>(_ => kills++);
        var startExp = game.Player.MaxExperience;
        var turns = 0;
        var explorer = new Explorer();
        for (; turns < MaxTurns && !game.IsGameOver && game.Player.Depth == depth; turns++)
        {
            explorer.Visited.Add(game.Player.Position);
            if (!Act(game, explorer, ref potions, ref blinks)) game.Execute(new HoldCommand());
        }

        if (Environment.GetEnvironmentVariable("BOT_TRACE") is { Length: > 0 })
            foreach (var line in log.TakeLast(60)) Console.WriteLine(line);
        var passable = game.Level.AllLocs().Count(p => game.Level.IsPassable(p));
        var known = game.Level.AllLocs().Count(p => game.Level.IsPassable(p) && game.Known.IsKnown(p));
        return new BotResult(!game.Player.IsDead, !game.Player.IsDead && game.Player.Depth != depth, turns, kills, game.Player.MaxExperience - startExp,
            passable == 0 ? 0 : 100.0 * known / passable, potions, blinks, game.Player.IsDead ? game.Player.KilledBy : null);
    }

    /// <summary>One decision. False when there was nothing to do (the level is explored and quiet).</summary>
    private static bool Act(GameSession game, Explorer explorer, ref int potions, ref int blinks)
    {
        var p = game.Player;
        var hurt = p.Hp * 100 / Math.Max(1, p.MaxHp);
        // What it goes for: anything next to it; otherwise anything awake, and sleepers no deeper than
        // its own level — but not breeders (a worm mass is a waste of time unless it's in the way).
        var foes = game.Level.Monsters.All
            .Where(m => m.IsVisible && (m.Position.DistanceTo(p.Position) <= 1
                || (!m.Race.Has("MULTIPLY") && (m.Sleep == 0 || m.Race.Depth <= p.Level))))
            .OrderBy(m => m.Position.DistanceTo(p.Position)).ToList();

        if (hurt < 20 && foes.Count > 0 && Find(game, "phase_door") is { } phase)
        {
            blinks++;
            return game.Execute(new UseCommand(phase));
        }
        // Blind or confused, it can't fight or read: a cure potion clears both (and heals).
        var dazed = p.Timed.Has("blind") || p.Timed.Has("confused");
        if ((hurt < 50 || dazed) && Healing.Select(h => Kind(game.Data, h) is { } id ? Find(game, id) : null).FirstOrDefault(i => i is not null) is { } cure)
        {
            potions++;
            return game.Execute(new UseCommand(cure));
        }
        // Afraid, it can't fight: blink away if anything is close, else keep exploring.
        if ((p.Timed.Has("afraid") || p.Timed.Has("terror")) && foes.FirstOrDefault() is { } near && near.Position.DistanceTo(p.Position) <= 2
            && !p.Timed.Has("blind") && Find(game, "phase_door") is { } escape)
        {
            blinks++;
            return game.Execute(new UseCommand(escape));
        }
        if (foes.FirstOrDefault() is { } foe && !p.Timed.Has("afraid") && !p.Timed.Has("terror"))
        {
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

    /// <summary>Where the bot is headed, and where it has been (a spot it has stood on is explored).</summary>
    private sealed class Explorer
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
                var feature = data(game).Terrain[game.Known.Feature(n)];
                if (!feature.Has(TerrainFlags.Passable) && !feature.Has(TerrainFlags.DoorClosed)) continue;
                if (feature.Has(TerrainFlags.Fiery)) continue;
                if (level[n].Trap != 0 && level[n].Has(Angband.Core.World.SquareFlags.TrapVisible)) continue;
                prev[n] = at;
                queue.Enqueue(n);
            }
        }
        return null;

        static GameData data(GameSession g) => g.Data;
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
    /// each made for the depth.
    /// </summary>
    private static void Equip(GameSession game, int depth, ulong seed)
    {
        var rng = new GameRandom(seed ^ 0x5EED);
        foreach (var slot in new[] { EquipSlot.Weapon, EquipSlot.Body })
        {
            var found = new List<Item>();
            for (var i = 0; i < 3000 && found.Count < 30; i++)
                if (game.Objects.Make(rng, depth, good: true) is { } item && item.Base.Slot == slot && !item.IsCursed
                    && (slot != EquipSlot.Weapon || item.Weight <= 200)) // one a warrior can swing well enough
                    found.Add(item);
            var best = slot == EquipSlot.Weapon
                ? found.OrderByDescending(i => i.Damage.Count * (i.Damage.Sides + 1) / 2.0 + i.ToDam).FirstOrDefault()
                : found.OrderByDescending(i => i.Armour + i.ToAc).FirstOrDefault();
            if (best is null) continue;
            game.Player.Inventory.Add(best);
            game.Execute(new WieldCommand(best));
        }
    }
}
