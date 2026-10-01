// AVABand's quests, played by the bot: `quests [seeds]` plays each quest end to end with a few
// classes and reports how it went (for balance); `soak` also plays one of each, recorded and replayed.
//
// The bot takes the quest at the Prancing Pony (walking in and answering, as a player does), goes
// to each level the quest names (a debug jump, recorded), and heads for what the quest wants there —
// Durgash, the sealed door, the Seal, the forge, a shard, the altar, Hathol, the hermit's door, a
// ledger page's carrier, the thief, the trapped apprentice, the nearest cold brazier and then the
// Shade, the Heart of the Mountain, Grishnag, the palantír — walking there square by square (the Cartographer, which waits on rare kinds of level, is
// left to the tests); it fights, heals and flees as
// the plain bot does whenever something awake comes near or it is hurt. It answers each question
// the quest asks as a player finishing the quest would (the Letter's way depends on the seed).
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Persistence;
using Angband.Data;

internal static class QuestBot
{
    public sealed record Outcome(string Quest, string Cls, ulong Seed, bool Done, int Decisions, bool Died, int Potions, int Levels, string Note,
        string? KilledBy = null);

    /// <summary>The character level each quest is played at (about what its depths call for).</summary>
    private static readonly Dictionary<string, int> Levels = new()
    {
        ["sealed_door"] = 16, ["burden"] = 22, ["broken_blade"] = 24, ["consecration"] = 30, ["letter"] = 18, ["thief"] = 22, ["board"] = 15,
        ["apprentice"] = 22, ["warden"] = 32, ["heart"] = 38, ["watch"] = 40, ["stone"] = 45, ["chisel"] = 26,
    };

    public const int Budget = 6000;

    private sealed class Run
    {
        public required GameSession Game { get; init; }
        public Bot.Mind Mind { get; } = new();
        public QuestPromptEvent? Prompt { get; set; }
        public int Decisions { get; set; }
        public int Potions { get; set; }
        public int LevelsSeen { get; set; }
        public List<(int Steps, ReplayEnd State)> Marks { get; } = [];
        public bool OutOfTime => Decisions >= Budget || Game.IsGameOver;
    }

    public static Outcome Play(GameData data, string quest, string cls, ulong seed, bool record = false, List<(int, ReplayEnd)>? marks = null,
        Action<GameSession>? started = null)
    {
        // (QUEST_LEVEL=n plays it at another character level: e.g. the lowest the inn offers it at.)
        var level = int.TryParse(Environment.GetEnvironmentVariable("QUEST_LEVEL"), out var chosen) ? chosen : Levels[quest];
        var game = Bot.Setup(data, 1, seed, cls, level: level);
        if (record) game.Recorder = new ReplayRecorder(game);
        started?.Invoke(game);
        var run = new Run { Game = game };
        game.Events.Subscribe<QuestPromptEvent>(p => run.Prompt = p);
        game.Events.Subscribe<ItemUsedEvent>(u => { if (u.Verb == "quaff") run.Potions++; });
        var trace = Environment.GetEnvironmentVariable("QUEST_TRACE") is { Length: > 0 } ? new List<string>() : null;
        if (trace is not null) game.Events.Subscribe<MessageEvent>(m => trace.Add($"[{game.Player.Depth} {game.Player.Hp}/{game.Player.MaxHp}] {m.Text}"));
        var done = false;
        var note = "";
        try
        {
            done = quest switch
            {
                "sealed_door" => SealedDoor(run),
                "burden" => Burden(run),
                "broken_blade" => BrokenBlade(run),
                "consecration" => Consecration(run),
                "letter" => Letter(run, (int)(seed % 3), out note),
                "thief" => Thief(run),
                "board" => Board(run, out note),
                "apprentice" => Apprentice(run),
                "warden" => Warden(run),
                "heart" => Heart(run),
                "watch" => Watch(run),
                "stone" => Stone(run),
                "chisel" => Chisel(run),
                _ => false,
            };
        }
        finally
        {
            if (marks is not null) marks.AddRange(run.Marks);
        }
        if (trace is not null) foreach (var line in trace.TakeLast(int.Parse(Environment.GetEnvironmentVariable("QUEST_TRACE")!))) Console.WriteLine(line);
        return new Outcome(quest, cls, seed, done, run.Decisions, game.Player.IsDead, run.Potions, run.LevelsSeen, note,
            game.Player.IsDead ? game.Player.KilledBy : null);
    }

    // --- The quests --------------------------------------------------------------------------------

    private static bool SealedDoor(Run r)
    {
        if (!Take(r, "sealed_door")) return false;
        var s = r.Game.AvaQuests.Get("sealed_door")!;
        for (var tries = 0; tries < 4 && s.Stage == "hunt" && !r.OutOfTime; tries++)
        {
            Jump(r, s.N("keydepth"));
            Pursue(r, () => s.Stage != "hunt", () => Monster(r, "durgash_the_keybearer"), attack: true);
        }
        for (var tries = 0; tries < 4 && s.Stage == "key" && !r.OutOfTime; tries++)
        {
            Jump(r, s.N("doordepth"));
            Pursue(r, () => s.Stage != "key", () => Feature(r, "sealed_door"), bump: true, answer: "door:unlock");
        }
        return s.Stage == "done";
    }

    private static bool Burden(Run r)
    {
        var g = r.Game;
        for (var d = g.BurdenDepth; d < g.BurdenDepth + 12 && g.AvaQuests.Get("burden") is null && !r.OutOfTime; d++)
        {
            Jump(r, d);
            Pursue(r, () => g.AvaQuests.Get("burden") is not null, () => FloorItem(r, "seal_of_angmar"), pickup: true, patience: 600);
        }
        if (g.AvaQuests.Get("burden") is not { } s) return false;
        // Worn, as a player tempted by it would.
        if (g.Player.Inventory.Pack.FirstOrDefault(i => i.Kind.Id == "seal_of_angmar") is { } seal) Do(r, new WieldCommand(seal));
        for (var tries = 0; tries < 4 && s.Stage == "carry" && !r.OutOfTime; tries++)
        {
            Jump(r, s.N("forgedepth"));
            Pursue(r, () => s.Stage != "carry", () => Feature(r, "dwarven_forge"), step: true, answer: "forge:unmake");
        }
        return s.Stage == "done";
    }

    private static bool BrokenBlade(Run r)
    {
        if (!Take(r, "broken_blade")) return false;
        var g = r.Game;
        var s = g.AvaQuests.Get("broken_blade")!;
        foreach (var i in new[] { 1, 2, 3 })
            for (var tries = 0; tries < 4 && s.N($"got{i}") == 0 && !r.OutOfTime; tries++)
            {
                Jump(r, s.N($"depth{i}"));
                Pursue(r, () => s.N($"got{i}") != 0, () => FloorItem(r, "shard_of_the_"), pickup: true);
            }
        if (s.Stage != "reforge") return false;
        Town(r);
        GoInto(r, "weaponsmith");
        if (r.Prompt?.Choices.FirstOrDefault(c => c.Id.StartsWith("blade:pick:")) is not { } first) return false;
        Answer(r, first.Id);
        if (r.Prompt?.Choices.FirstOrDefault(c => c.Id.StartsWith("blade:make:")) is not { } second) return false;
        Answer(r, second.Id);
        return s.Stage == "done";
    }

    private static bool Consecration(Run r)
    {
        if (!Take(r, "consecration")) return false;
        var s = r.Game.AvaQuests.Get("consecration")!;
        for (var tries = 0; tries < 4 && s.Stage != "done" && !r.OutOfTime; tries++)
        {
            Jump(r, s.N("depth"));
            if (s.Stage == "hallow")
                Pursue(r, () => s.Stage != "hallow", () => Feature(r, "quest_altar"), step: true, answer: "altar:hallow", fightBack: false);
            if (s.Stage == "slay")
                Pursue(r, () => s.Stage == "done", () => Monster(r, "hathol_lord_of_the_barrow"), attack: true);
        }
        return s.Stage == "done";
    }

    private static bool Letter(Run r, int way, out string note)
    {
        note = way switch { 0 => "delivered", 1 => "exposed", _ => "burned" };
        if (!Take(r, "letter")) return false;
        var g = r.Game;
        var s = g.AvaQuests.Get("letter")!;
        if (way >= 1)
        {
            Do(r, new UseCommand(g.Player.Inventory.Pack.First(i => i.Kind.Id == "sealed_letter")));
            Answer(r, way == 1 ? "letter:read" : "letter:burn");
        }
        if (way == 1)
        {
            GoInto(r, "alchemist");
            Answer(r, "letter:expose");
        }
        for (var tries = 0; tries < 4 && way == 0 && s.Stage == "deliver" && !r.OutOfTime; tries++)
        {
            Jump(r, s.N("depth"));
            Pursue(r, () => s.Stage != "deliver", () => Feature(r, "hermitage"), step: true, answer: "hermit:give");
        }
        return s.Stage == note;
    }

    private static bool Thief(Run r)
    {
        if (!Take(r, "thief")) return false;
        var g = r.Game;
        var s = g.AvaQuests.Get("thief")!;
        var thief = g.Data.Monster(s.Texts["thief"])!;
        for (var level = 0; level < 40 && s.Stage == "hunt" && !r.OutOfTime; level++)
        {
            // Pages first (down to 1000 ft), then the thief at about his depth.
            Jump(r, s.N("pages") < 3 ? 3 + level % 16 : Math.Max(1, thief.Depth - 2 + level % 5));
            Pursue(r, () => s.Stage != "hunt" || g.Level.Monsters.All.All(m => !Wanted(m, s)),
                () => g.Level.Monsters.All.Where(m => Wanted(m, s)).OrderBy(m => m.Position.DistanceTo(g.Player.Position)).FirstOrDefault()?.Position,
                attack: true, patience: 500);
        }
        if (s.Stage != "return") return false;
        GoInto(r, "black_market");
        Answer(r, "thief:return");
        return s.Stage == "done";
    }

    /// <summary>A monster worth chasing for the thief: one carrying a page (while pages are wanted), or the thief himself.</summary>
    private static bool Wanted(Monster m, Angband.Core.Quests.AvaQuestState s) =>
        (s.N("pages") < 3 && m.Carried.Any(i => i.Kind.Id == "journal_page")) || m.Race.Id == s.Texts["thief"];

    private static bool Apprentice(Run r)
    {
        if (!Take(r, "apprentice")) return false;
        var s = r.Game.AvaQuests.Get("apprentice")!;
        // (Every class starts with a Scroll of Word of Recall: it goes to the apprentice.)
        for (var tries = 0; tries < 4 && s.Stage == "find" && !r.OutOfTime; tries++)
        {
            Jump(r, s.N("depth"));
            Pursue(r, () => s.Stage != "find", () => Feature(r, "trapped_apprentice"), step: true, answer: "apprentice:recall");
        }
        if (s.Stage != "rescued") return false;
        GoInto(r, "alchemist");
        Answer(r, "apprentice:reward");
        return s.Stage == "done";
    }

    /// <summary>The Artificer's Chisel: asked of SlatriBartSlow, the chisel fetched from his workshop and given back, the free socket taken.</summary>
    private static bool Chisel(Run r)
    {
        GoInto(r, "artificer");
        Answer(r, "artificer:trouble");
        Answer(r, "accept:chisel");
        if (r.Game.AvaQuests.Get("chisel") is not { } s) return false;
        for (var tries = 0; tries < 4 && s.Stage == "find" && !r.OutOfTime; tries++)
        {
            Jump(r, s.N("depth"));
            Pursue(r, () => s.Stage != "find", () => FloorItem(r, "star_forged_chisel"), pickup: true, patience: 2500);
        }
        if (s.Stage != "found") return false;
        GoInto(r, "artificer");
        Answer(r, "chisel:return");
        if (r.Prompt?.Choices.FirstOrDefault(c => c.Id.StartsWith("artificer:cut:", StringComparison.Ordinal)) is { } cut) Answer(r, cut.Id);
        return s.Stage == "returned";
    }

    private static bool Warden(Run r)
    {
        if (!Take(r, "warden")) return false;
        var g = r.Game;
        var s = g.AvaQuests.Get("warden")!;
        for (var tries = 0; tries < 4 && s.Stage != "done" && !r.OutOfTime; tries++)
        {
            Jump(r, s.N("depth"));
            // The nearest cold fire each time (the Shade puts them out behind you), then the Shade.
            if (s.Stage == "light")
                Pursue(r, () => s.Stage != "light",
                    () => g.Level.AllLocs().Where(l => g.Level.FeatureAt(l).Id == "cold_brazier")
                        .OrderBy(l => l.DistanceTo(g.Player.Position)).Select(l => (Loc?)l).FirstOrDefault(),
                    step: true, fightBack: false, patience: 2000); // (no use fighting the Shade yet)
            if (s.Stage == "slay")
                Pursue(r, () => s.Stage == "done", () => Monster(r, "the_shade_of_the_stair"), attack: true);
        }
        return s.Stage == "done";
    }

    private static bool Heart(Run r)
    {
        if (!Take(r, "heart")) return false;
        var s = r.Game.AvaQuests.Get("heart")!;
        for (var tries = 0; tries < 4 && s.Stage == "hunt" && !r.OutOfTime; tries++)
        {
            Jump(r, s.N("depth"));
            Pursue(r, () => s.Stage != "hunt", () => FloorItem(r, "heart_of_the_mountain"), pickup: true, patience: 2500);
        }
        if (s.Stage != "choose") return false;
        GoInto(r, "inn");
        Answer(r, "heart:return");
        return s.Stage == "returned";
    }

    private static bool Watch(Run r)
    {
        if (!Take(r, "watch")) return false;
        var s = r.Game.AvaQuests.Get("watch")!;
        // Holds the tower by going for the warchief: either ends it (the siege counts meanwhile).
        for (var tries = 0; tries < 4 && s.Stage == "hold" && !r.OutOfTime; tries++)
        {
            Jump(r, s.N("depth"));
            Pursue(r, () => s.Stage != "hold", () => Monster(r, "grishnag_the_warchief"), attack: true, patience: 3000);
        }
        return s.Stage == "relieved";
    }

    private static bool Stone(Run r)
    {
        if (!Take(r, "stone")) return false;
        var g = r.Game;
        var s = g.AvaQuests.Get("stone")!;
        for (var tries = 0; tries < 4 && s.Stage == "seek" && !r.OutOfTime; tries++)
        {
            Jump(r, s.N("depth"));
            Pursue(r, () => s.Stage != "seek", () => FloorItem(r, "palantir"), pickup: true, patience: 2500);
        }
        if (s.Stage != "carry") return false;
        // One look into it, as anyone would.
        if (g.Player.Inventory.Pack.FirstOrDefault(i => i.Kind.Id == "palantir") is { } stone) Do(r, new UseCommand(stone));
        GoInto(r, "bookseller");
        Answer(r, "stone:give");
        return s.Stage == "given";
    }

    private static bool Board(Run r, out string note)
    {
        note = "";
        var g = r.Game;
        Jump(r, 3);
        GoInto(r, "inn");
        Answer(r, "inn:board");
        if (r.Prompt?.Choices.FirstOrDefault(c => c.Id.StartsWith("board:take")) is not { } take) return false;
        Answer(r, take.Id);
        var job = g.AvaQuests.Board.Single(j => j.Taken);
        note = g.JobTitle(job);
        for (var level = 0; level < 20 && job.Progress < job.Count && job.Kind == "hunt" && !r.OutOfTime; level++)
        {
            var race = g.Data.Monster(job.Target)!;
            Jump(r, Math.Max(1, race.Depth + level % 3));
            Pursue(r, () => job.Progress >= job.Count || g.Level.Monsters.All.All(m => m.Race.Id != job.Target),
                () => g.Level.Monsters.All.Where(m => m.Race.Id == job.Target).OrderBy(m => m.Position.DistanceTo(g.Player.Position)).FirstOrDefault()?.Position,
                attack: true, patience: 500);
        }
        if (job.Kind == "gather") return true; // (the dungeon's luck; the board itself is tested)
        if (job.Progress < job.Count) return false;
        GoInto(r, "inn");
        if (r.Prompt?.Choices.FirstOrDefault(c => c.Id.StartsWith("board:collect")) is not { } collect) return false;
        var gold = g.Player.Gold;
        Answer(r, collect.Id);
        return g.Player.Gold > gold;
    }

    // --- Moving and acting ---------------------------------------------------------------------------

    private static bool Take(Run r, string quest)
    {
        GoInto(r, "inn");
        Answer(r, "inn:work");
        Answer(r, $"offer:{quest}");
        Answer(r, $"accept:{quest}");
        return r.Game.AvaQuests.Get(quest) is not null;
    }

    private static void Do(Run r, GameCommand command)
    {
        r.Game.Execute(command);
        r.Decisions++;
        if (r.Game.Recorder is { } rec) r.Marks.Add((rec.StepCount, ReplayCodec.EndOf(r.Game)));
    }

    private static void Answer(Run r, string choice)
    {
        if (r.Prompt?.Choices.Any(c => c.Id == choice) != true) return;
        r.Prompt = null;
        Do(r, new QuestChoiceCommand(choice));
    }

    private static void Jump(Run r, int depth)
    {
        Do(r, new DebugJumpCommand(depth));
        Do(r, new DebugCureAllCommand()); // (fresh for each level, as the soak's bots are)
        r.LevelsSeen++;
        r.Mind.Explorer = new Bot.Explorer();
    }

    private static void Town(Run r)
    {
        if (r.Game.Player.Depth != 0) Jump(r, 0);
    }

    /// <summary>Walks into a town building (the Prancing Pony, a shop), answering nothing yet.</summary>
    private static void GoInto(Run r, string shopId)
    {
        Town(r);
        var g = r.Game;
        var door = g.Level.AllLocs().First(l => g.Level.FeatureAt(l).Shop == shopId);
        r.Prompt = null;
        Pursue(r, () => g.Player.Position == door || r.Prompt is not null, () => door, step: true, fightBack: true, patience: 400);
    }

    private static Loc? Monster(Run r, string race) => r.Game.Level.Monsters.All.FirstOrDefault(m => m.Race.Id == race)?.Position;

    private static Loc? Feature(Run r, string id) =>
        r.Game.Level.AllLocs().Where(l => r.Game.Level.FeatureAt(l).Id == id).Select(l => (Loc?)l).FirstOrDefault();

    private static Loc? FloorItem(Run r, string kindPrefix) =>
        r.Game.Level.Objects.All.Where(o => o.Item.Kind.Id.StartsWith(kindPrefix)).Select(o => (Loc?)o.Loc).FirstOrDefault();

    /// <summary>
    /// Heads for the target until the goal is met: step by step along the shortest way (through doors,
    /// secret ones included — they're found on the way), fighting, healing or fleeing as the plain bot
    /// does when something awake is near or it's hurt (unless told not to fight back), and, on
    /// arriving, attacking, walking into, stepping onto or picking up the target and answering what it asks.
    /// </summary>
    private static void Pursue(Run r, Func<bool> done, Func<Loc?> target, bool attack = false, bool bump = false, bool step = false,
        bool pickup = false, string? answer = null, bool fightBack = true, int patience = 1500)
    {
        var g = r.Game;
        var startDepth = g.Player.Depth;
        for (var i = 0; i < patience && !done() && !r.OutOfTime && g.Player.Depth == startDepth; i++)
        {
            if (answer is not null && r.Prompt?.Choices.Any(c => c.Id == answer) == true)
            {
                Answer(r, answer);
                continue;
            }
            var me = g.Player.Position;
            var hurt = g.Player.Hp * 2 < g.Player.MaxHp;
            var goal = target();
            var threat = g.Level.Monsters.All.Any(m => m.IsVisible && m.Sleep == 0 && m.Position.ChebyshevTo(me) <= 2 && m.Position != goal);
            if ((hurt || (fightBack && threat)) && Decide(r)) continue;
            if (goal is not { } t)
            {
                if (!Decide(r)) Do(r, new HoldCommand()); // explore until it turns up
                continue;
            }
            if (me == t)
            {
                if (pickup && g.Level.Objects.At(me).FirstOrDefault(o => o.IsQuestItem) is { } item) Do(r, new PickupCommand(item));
                else if (!Decide(r)) Do(r, new HoldCommand());
                continue;
            }
            if (me.ChebyshevTo(t) == 1 && (attack || bump))
            {
                Do(r, new WalkCommand(DirectionExtensions.FromOffset(t.X - me.X, t.Y - me.Y)));
                continue;
            }
            if (NextStep(g, me, t) is { } next) Do(r, new WalkCommand(next));
            else if (!Decide(r)) Do(r, new HoldCommand());
        }
    }

    private static bool Decide(Run r)
    {
        var acted = Bot.Decide(r.Game, r.Mind);
        r.Decisions++;
        if (r.Game.Recorder is { } rec) r.Marks.Add((rec.StepCount, ReplayCodec.EndOf(r.Game)));
        return acted;
    }

    /// <summary>The first step of the shortest way to the target (or next to it), avoiding monsters and lava.</summary>
    private static Direction? NextStep(GameSession g, Loc from, Loc to)
    {
        var level = g.Level;
        bool Open(Loc p) =>
            level.InBounds(p) && (p == to || (level.Monsters.At(p) is null && !level.Has(p, TerrainFlags.Fiery)
                                              && (level.IsPassable(p) || level.Has(p, TerrainFlags.DoorClosed) || level.Has(p, TerrainFlags.Secret))));
        var cameFrom = new Dictionary<Loc, Loc> { [from] = from };
        var queue = new Queue<Loc>([from]);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (p == to || (p.ChebyshevTo(to) == 1 && !Open(to)))
            {
                var step = p;
                while (cameFrom[step] != from) step = cameFrom[step];
                return step == from ? null : DirectionExtensions.FromOffset(step.X - from.X, step.Y - from.Y);
            }
            foreach (var d in DirectionExtensions.Compass)
            {
                var n = p.Step(d);
                if (cameFrom.ContainsKey(n) || !Open(n)) continue;
                cameFrom[n] = p;
                queue.Enqueue(n);
            }
        }
        return null;
    }

    // --- Reports ------------------------------------------------------------------------------------------

    public static void Report(GameData data, int seeds)
    {
        var quests = new[] { "sealed_door", "burden", "broken_blade", "consecration", "letter", "thief", "apprentice", "warden", "heart", "watch", "stone", "chisel", "board" };
        var classes = new[] { "warrior", "mage", "ranger" };
        Console.WriteLine("quest          done  died  decisions  potions  levels   (per class: warrior / mage / ranger, " + seeds + " seeds each)");
        foreach (var quest in quests)
        {
            var results = new List<Outcome>();
            foreach (var cls in classes)
                for (ulong seed = 1; seed <= (ulong)seeds; seed++)
                    results.Add(Play(data, quest, cls, seed * 101));
            string Per(Func<Outcome, bool> f) => string.Join("/", classes.Select(c => results.Count(o => o.Cls == c && f(o))));
            Console.WriteLine($"{quest,-14} {Per(o => o.Done),5} {Per(o => o.Died),5}  {results.Average(o => o.Decisions),9:0}  {results.Average(o => o.Potions),7:0.0}  {results.Average(o => o.Levels),6:0.0}"
                              + $"   killed by: {string.Join(", ", results.Where(o => o.KilledBy is not null).Select(o => o.KilledBy))}");
        }
    }

    /// <summary>For the soak: one game of each quest, recorded and replayed.</summary>
    public static int Soak(GameData data)
    {
        var failures = 0;
        var cases = new (string Quest, string Cls, ulong Seed)[]
        {
            // (Seeds on which the bot sees each quest through, so every ending is played on every push.)
            ("sealed_door", "warrior", 202), ("burden", "warrior", 102), ("broken_blade", "warrior", 101),
            ("consecration", "warrior", 101), ("letter", "mage", 505), ("letter", "warrior", 506), ("letter", "rogue", 507),
            ("thief", "warrior", 102), ("board", "ranger", 202), ("apprentice", "warrior", 404), ("warden", "warrior", 101),
            ("heart", "warrior", 303), ("watch", "warrior", 101), ("stone", "warrior", 311), ("chisel", "warrior", 101),
        };
        foreach (var (quest, cls, seed) in cases)
        {
            var marks = new List<(int, ReplayEnd)>();
            GameSession? game = null;
            string? failure = null;
            Outcome? outcome = null;
            var thread = new Thread(() =>
            {
                try { outcome = Play(data, quest, cls, seed, record: true, marks, g => game = g); }
                catch (Exception e) { failure = e.ToString(); }
            }, 64 * 1024 * 1024) { IsBackground = true };
            thread.Start();
            if (!thread.Join(TimeSpan.FromMinutes(10)))
            {
                Console.WriteLine($"quest {quest} ({cls} {seed}): HUNG");
                failures++;
                continue;
            }
            if (game is null)
            {
                Console.WriteLine($"quest {quest} ({cls} {seed}): FAILED to start — {failure}");
                failures++;
                continue;
            }
            var end = outcome is null ? "" : $"{(outcome.Done ? "done" : "NOT DONE")}{(outcome.Note.Length > 0 ? $" ({outcome.Note})" : "")}, "
                                             + $"{outcome.Decisions} decisions, {outcome.Levels} levels, {(outcome.Died ? "died" : "alive")}";
            if (!global::Soak.Finish(data, game, $"quest {quest} ({cls} {seed})", $"quest {quest} ({cls} {seed})", marks, end, failure, null, cls, seed))
                failures++;
        }
        return failures;
    }
}
