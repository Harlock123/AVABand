using Angband.Core.Definitions;
using Angband.Core.Items;
using Angband.Core.Generation;
using Angband.Core.Geometry;
using Angband.Core.Monsters;
using Angband.Core.Randomness;
using Angband.Core.Sight;
using Angband.Core.Time;
using Angband.Core.World;

namespace Angband.Core.Game;

/// <summary>
/// A running game: owns the RNG, current level, player and scheduler, and executes commands.
/// Everything flows from <see cref="Seed"/> and the command sequence, so a game can be replayed.
/// Split across files: this one (commands, movement, levels), <c>.Combat</c> (player attacks),
/// <c>.Monsters</c> (monster turns), <c>.Status</c> (damage, timed effects, world upkeep).
/// </summary>
public sealed partial class GameSession : ITurnHandler
{
    private readonly DungeonGenerator _generator;
    private readonly MonsterSpawner _spawner;

    private GameSession(GameData data, ulong seed, GameOptions? options = null)
    {
        Data = data;
        Seed = seed;
        Options = options ?? new GameOptions();
        ApplyOptionDefaults();
        Rng = new GameRandom(seed);
        TownSeed = Rng.NextULong();
        _generator = new DungeonGenerator(data);
        _spawner = new MonsterSpawner(data);
        InitItems();
        InitStores();
    }

    /// <summary>An empty session for loading a save into (no starting kit, stores or level).</summary>
    private GameSession(GameData data, ulong seed, ulong townSeed)
    {
        Data = data;
        Seed = seed;
        TownSeed = townSeed;
        Rng = new GameRandom(seed);
        _generator = new DungeonGenerator(data);
        _spawner = new MonsterSpawner(data);
        Objects = new ObjectFactory(data);
        Knowledge = new PlayerKnowledge(data, seed) { IgnoredCheck = IsMarkedIgnored };
        Player.Inventory = new Inventory(data.Constants.PackSize, data.Constants.QuiverSlotSize, data.Constants.QuiverSize);
    }

    internal static GameSession CreateForLoad(GameData data, ulong seed, ulong townSeed) => new(data, seed, townSeed);

    /// <summary>Installs a restored level and derived state (loading a save).</summary>
    internal void RestoreLevel(Level level, KnownMap known)
    {
        Level = level;
        Known = known;
    }

    internal void RestoreStore(Store store) => _stores[store.Id] = store;

    public GameData Data { get; }
    public ulong Seed { get; }
    /// <summary>The town layout seed, fixed for the whole game.</summary>
    public ulong TownSeed { get; }
    public GameRandom Rng { get; }
    public GameEventBus Events { get; } = new();
    public TurnScheduler Scheduler { get; } = new();
    public Player Player { get; } = new();
    public Level Level { get; private set; } = null!;
    /// <summary>What the player remembers of the current level.</summary>
    public KnownMap Known { get; private set; } = null!;
    public VisionSystem Vision { get; } = new();
    /// <summary>The player's noise flow, which monsters follow to hunt the player.</summary>
    public NoiseMap Noise { get; } = new();
    /// <summary>Uniques killed this game, or currently alive on this level; they will not be generated.</summary>
    public HashSet<string> KilledUniques { get; } = [];

    public bool IsGameOver => Player.IsDead;

    /// <summary>Daytime is the first half of each day (Angband: turn % day_length &lt; day_length / 2).</summary>
    public bool IsDaytime => GameTurn % Data.Constants.DayLength < Data.Constants.DayLength / 2;

    public long GameTurn => Scheduler.GameTurn;
    /// <summary>Player turns at normal speed (Angband's displayed turn count).</summary>
    public long NormalTurns => Scheduler.GameTurn / EnergyTable.GameTurnsPerNormalTurn;

    /// <summary>Quick start: a human of the given class (default warrior) with every stat at 15.</summary>
    public static GameSession NewGame(GameData data, ulong seed, string? classId = null)
    {
        var cls = classId ?? (data.Class("warrior") ?? data.Classes.FirstOrDefault())?.Id;
        if (cls is null) return NewGame(data, seed, (CharacterSpec?)null);
        if (data.Class(cls) is null) throw new ArgumentException($"Unknown class '{classId}'.", nameof(classId));
        var race = data.Race("human")?.Id ?? data.Races.FirstOrDefault()?.Id ?? "human";
        return NewGame(data, seed, CharacterSpec.Default(race, cls));
    }

    /// <summary>Starts a game with a character from the creation screen.</summary>
    public static GameSession NewGame(GameData data, ulong seed, CharacterSpec? spec)
    {
        var game = new GameSession(data, seed, spec?.Options is { } options ? new GameOptions(options) : null);
        if (spec is not null)
        {
            var cls = data.Class(spec.ClassId) ?? throw new ArgumentException($"Unknown class '{spec.ClassId}'.", nameof(spec));
            var race = data.Race(spec.RaceId);
            if (race is null && data.Races.Count > 0) throw new ArgumentException($"Unknown race '{spec.RaceId}'.", nameof(spec));
            game.ApplyCharacter(spec, race, cls);
        }
        game.ChangeLevel(0, StairArrival.None);
        game.Publish(new MessageEvent("Welcome to AVABand. Find the stairs down ('>') to enter the dungeon."));
        return game;
    }

    /// <summary>
    /// Executes a player command. Returns true if it used game time, in which case monsters and the
    /// world advance until the player can act again. Commands that fail take no time.
    /// </summary>
    public bool Execute(GameCommand command) => command switch
    {
        CountedCommand counted => Repeat(counted.Command, Math.Clamp(counted.Count, 1, MaxCommandCount)),
        // Angband cmd-core.c: opening, disarming (and tunnelling) repeat up to 99 times by themselves.
        OpenCommand or DisarmCommand => Repeat(command, AutoRepeatCount),
        _ => ExecuteOnce(command),
    };

    private bool ExecuteOnce(GameCommand command)
    {
        if (IsGameOver) return false;
        foreach (var m in Level.Monsters.All) m.IsDetected = false; // detection lasts until the next action
        if (RefusedByShape(command)) return false;
        if (CommandedAction(command) is { } commandedEnergy)
        {
            if (commandedEnergy <= 0) return false;
            SpendAndAdvance(commandedEnergy);
            return true;
        }
        if (command is RestCommand) return Rest();
        if (command is FeelingCommand)
        {
            ShowFeeling();
            return false;
        }
        if (command is TunnelCommand tunnel) return Tunnel(tunnel.Direction, TunnelRepeats);
        if (command is RetireCommand)
        {
            Retire();
            return Player.IsDead;
        }
        if (command is TravelCommand travel) return Travel(travel.Target);
        if (command is RunCommand run) return Run(run.Direction);

        var energy = command switch
        {
            WalkCommand walk => Walk(walk.Direction),
            HoldCommand => Hold(),
            OpenCommand open => OpenAt(Player.Position.Step(open.Direction)),
            DisarmCommand disarm => Disarm(disarm.Direction),
            StealCommand steal => Steal(steal.Direction),
            InscribeCommand inscribe => Inscribe(inscribe.Item, inscribe.Text),
            IgnoreCommand ignore => IgnoreItem(ignore.Item, ignore.Choice),
            ToggleUnignoreCommand => ToggleUnignore(),
            UninscribeCommand uninscribe => Uninscribe(uninscribe.Item),
            CloseCommand close => CloseDoor(Player.Position.Step(close.Direction)),
            TakeStairsCommand stairs => TakeStairs(stairs.Down),
            FireCommand fire => Fire(fire.Target, fire.Ammo),
            PickupCommand pickup => Pickup(pickup.Item),
            DropCommand drop => Drop(drop.Item, drop.Count),
            WieldCommand wield => Wield(wield.Item),
            TakeOffCommand takeOff => TakeOff(takeOff.Item),
            UseCommand use => WithGlyph(use.Glyph, () => Use(use.Item, use.Target, use.Direction)),
            ActivateCommand activate => WithGlyph(activate.Glyph, () => Activate(activate.Item, activate.Target, activate.Direction)),
            ResumeShapeCommand => ResumeNormalShape(),
            ThrowCommand throwCmd => Throw(throwCmd.Item, throwCmd.Target),
            RefuelCommand refuel => Refuel(refuel.Fuel),
            EnterStoreCommand => EnterStore(),
            LeaveStoreCommand => LeaveStore(),
            BuyCommand buy => Buy(buy.Item, buy.Count),
            SellCommand sell => Sell(sell.Item, sell.Count),
            StudyCommand study => Study(study.SpellId, study.Book),
            CastCommand cast => Cast(cast.SpellId, cast.Target, cast.Direction, cast.AllowOverexert),
            DebugJumpCommand jump => DebugJump(jump.Depth),
            _ => 0,
        };
        if (energy <= 0) return false;

        SpendAndAdvance(energy);
        IgnoreDrop(); // anything that learning about has made ignorable goes
        return true;
    }

    /// <summary>Uses the player's energy, lets the world run, then refreshes the view.</summary>
    private void SpendAndAdvance(int energy)
    {
        Player.Energy -= energy;
        if (!Player.Timed.Has("covertracks")) Scent.Lay(Level, Player.Position); // Angband: no scent while covering tracks
        UpdateView(); // monsters react to where the player is now
        Scheduler.Advance(this);

        // Paralysed or knocked-out players lose their turns until they recover (or die).
        for (var guard = 0; Player.IsIncapacitated && !Player.IsDead && guard < 10_000; guard++)
        {
            Player.Energy -= EnergyTable.MoveEnergy;
            Scheduler.Advance(this);
        }
        UpdateView();
        SenseOre(); // a dwarf's sense for treasure, at the start of each turn
    }

    bool ITurnHandler.NeedsInput(IActor actor) => actor is Player;

    int ITurnHandler.TakeTurn(IActor actor) =>
        actor is Monster monster ? MonsterTurn(monster) : EnergyTable.MoveEnergy;

    void ITurnHandler.OnWorldTick(long gameTurn) => WorldTick(gameTurn);

    /// <summary>Recomputes the player's view, light, monster visibility and noise flow.</summary>
    public void UpdateView()
    {
        // A necromancer's unlight lets them see nearby squares without light (Angband UNLIGHT).
        var radius = Math.Max(Player.LightRadius, UnlightRadius);
        var lights = radius > 0 ? [new LightSource(Player.Position, radius)] : Array.Empty<LightSource>();
        Vision.Update(Level, Known, Player.Position, Data.Constants.MaxSight, lights, Player.IsBlind);
        Noise.Update(Level, Player.Position);
        foreach (var monster in Level.Monsters.All)
        {
            monster.IsVisible = MonsterVisible(monster);
            if (monster.IsVisible) NoteSighting(monster);
        }
        foreach (var p in Vision.Viewed)
            if (Level[p].Has(SquareFlags.Seen))
            {
                if (Level.FeelSquares.Count > 0) NoticeFeelingSquare(p);
                Known.RememberObject(p, ObjectShownAt(p));
                foreach (var item in Level.Objects.At(p)) Knowledge.See(item);
            }
    }

    /// <summary>
    /// Town lighting (Angband cave_illuminate): by day everything is lit and known; by night only the
    /// shop entrances glow, though the player still remembers the buildings and walls.
    /// </summary>
    private void ApplyTownLighting()
    {
        var day = IsDaytime;
        Level.IsLit = day;
        foreach (var p in Level.AllLocs())
        {
            ref var sq = ref Level[p];
            var feature = Level.FeatureAt(p);
            var glow = day || feature.Has(TerrainFlags.Shop);
            sq.Flags = glow ? sq.Flags | SquareFlags.Glow : sq.Flags & ~SquareFlags.Glow;
            if (day || !feature.Has(TerrainFlags.Floor)) Known.Remember(Level, p);
        }
        UpdateView();
    }

    private int Walk(Direction dir, bool confuse = true)
    {
        // Angband: in a web, trying to move clears it instead (and takes the turn).
        if (IsWebbed(Player.Position))
        {
            ClearWeb(Player.Position);
            Publish(new MessageEvent("You clear the web."));
            return EnergyTable.MoveEnergy;
        }
        if (confuse && Player.Timed.Has(Effects.TimedIds.Confused) && Rng.RandInt0(100) < 40)
        {
            dir = Rng.Pick(DirectionExtensions.Compass);
            Publish(new MessageEvent("You are confused."));
        }

        var target = Player.Position.Step(dir);
        if (!Level.InBounds(target)) return 0;

        if (Level.Monsters.At(target) is { Camouflaged: true } hidden)
        {
            Reveal(hidden); // bumping into it gives it away (and uses the turn)
            return EnergyTable.MoveEnergy;
        }
        if (Level.Monsters.At(target) is { } monster) return PlayerMelee(monster);

        var feature = Level.FeatureAt(target);
        if (feature.Has(TerrainFlags.DoorClosed)) return OpenDoor(target, announceFailure: true);

        if (!feature.Has(TerrainFlags.Passable))
        {
            var what = feature.Has(TerrainFlags.Rubble) ? "a pile of rubble"
                : feature.Mimic is { } mimic ? Data.Terrain[mimic].Name
                : feature.Name;
            Publish(new MessageEvent($"There is {Article(what)} in the way."));
            return 0;
        }

        var from = Player.Position;
        Player.Position = target;
        Publish(new PlayerMovedEvent(from, target));

        if (feature.Shop is { } shopId)
        {
            if (_stores.TryGetValue(shopId, out var store)) Publish(new ShopEnteredEvent(store.Id, store.IsHome));
            else Publish(new MessageEvent($"The {Data.Shops.FirstOrDefault(s => s.Id == shopId)?.Name ?? shopId} is closed."));
        }
        else if (feature.Has(TerrainFlags.Stair))
            Publish(new MessageEvent($"There is {Article(feature.Name)} here."));

        // Angband do_autopickup: each object picked up costs a tenth of a turn (at most a turn).
        var picked = Level.Objects.Any(target) ? NoticeFloorObjects() : 0;

        if (Level[target].Trap != 0) HitTrap(target);
        if (Player.Position == target && !IsGameOver) Search(); // secret doors beside you are always found
        return EnergyTable.MoveEnergy + Math.Min(EnergyTable.MoveEnergy, picked * EnergyTable.MoveEnergy / 10);
    }

    /// <summary>'o': a chest there (or underfoot) is opened first, otherwise a door.</summary>
    private int OpenAt(Loc p)
    {
        if (Level.InBounds(p) && ChestAt(p) is { } chest) return OpenChest(chest, p);
        if (Level.InBounds(p) && Level.Objects.At(p).Any(i => i.IsChest))
        {
            Publish(new MessageEvent("The chest is empty."));
            return 0;
        }
        return OpenDoor(p, announceFailure: true);
    }

    private int OpenDoor(Loc p, bool announceFailure)
    {
        if (!Level.InBounds(p) || !Level.Has(p, TerrainFlags.DoorClosed))
        {
            if (announceFailure) Publish(new MessageEvent("You see nothing there to open."));
            return 0;
        }

        ref var sq = ref Level[p];
        if (sq.LockPower > 0)
        {
            // Angband: chance = disarm skill - 4 * lock power, at least 2%.
            var chance = Math.Max(2, EffectiveDisarmSkill - 4 * sq.LockPower); // hurt by blindness, dark, confusion, hallucination
            if (!Rng.Percent(chance))
            {
                Publish(new LockPickFailedEvent(p));
                Publish(new MessageEvent("You failed to pick the lock."));
                _more = true;
                return EnergyTable.MoveEnergy;
            }
            sq.LockPower = 0;
            Publish(new LockPickedEvent(p));
            Publish(new MessageEvent("You have picked the lock."));
        }

        sq.Feature = Data.Terrain.Ids.OpenDoor;
        Publish(new DoorOpenedEvent(p));
        return EnergyTable.MoveEnergy;
    }

    private int CloseDoor(Loc p)
    {
        if (!Level.InBounds(p) || !Level.Has(p, TerrainFlags.Closable))
        {
            Publish(new MessageEvent("You see nothing there to close."));
            return 0;
        }
        if (p == Player.Position || Level[p].Monster != 0)
        {
            Publish(new MessageEvent("Something is in the way."));
            return 0;
        }
        Level[p].Feature = Data.Terrain.Ids.ClosedDoor;
        Publish(new DoorClosedEvent(p));
        return EnergyTable.MoveEnergy;
    }

    private int TakeStairs(bool down)
    {
        var flag = down ? TerrainFlags.DownStair : TerrainFlags.UpStair;
        if (!Level.Has(Player.Position, flag))
        {
            Publish(new MessageEvent($"I see no {(down ? "down" : "up")} staircase here."));
            return 0;
        }
        if (!down && ForceDescend)
        {
            Publish(new MessageEvent("Nothing happens!"));
            return 0;
        }

        Publish(new StairsTakenEvent(down));
        Publish(new MessageEvent(down ? "You enter a maze of down staircases." : "You enter a maze of up staircases."));
        ChangeLevel(down ? DescentTarget(Player.Depth) : Player.Depth - 1, down ? StairArrival.Descended : StairArrival.Ascended);
        return EnergyTable.MoveEnergy;
    }

    private int DebugJump(int depth)
    {
        MarkDebugUsed();
        depth = Math.Clamp(depth, 0, Data.Constants.MaxDepth);
        ChangeLevel(depth, StairArrival.None);
        return EnergyTable.MoveEnergy;
    }

    private void ChangeLevel(int depth, StairArrival arrival)
    {
        var seed = depth == 0 ? TownSeed : Rng.NextULong();
        if (Level is not null) PreserveUnfoundArtifacts();
        ArenaReturn = null; // a new level ends any duel (and any command) for good
        Commanded = null;
        var generated = _generator.Generate(new LevelRequest(depth, seed, arrival, ConnectStairs: Options[OptionIds.ConnectStairs]));

        if (Level is not null) Vision.Reset(Level);
        ClearTarget();
        Level = generated.Level;
        Known = new KnownMap(Level.Width, Level.Height);
        Scent.Reset(Level);
        Player.Depth = depth;
        Player.MaxDepth = Math.Max(Player.MaxDepth, depth);
        Player.Position = generated.PlayerStart;

        Scheduler.Clear();
        Scheduler.Add(Player);
        Player.Energy = EnergyTable.MoveEnergy; // Angband: the player always gets the first move on a new level

        // Uniques that are dead stay dead; the rest may appear on each new level.
        var unavailable = new HashSet<string>(KilledUniques);
        _spawner.Populate(Level, Rng, Player.Position, unavailable);
        foreach (var monster in Level.Monsters.All) Scheduler.Add(monster);
        PopulateObjects();
        DisguiseMonsters();
        PrepareQuestLevel();
        PrepareFeeling();
        CheatPeek();

        foreach (var p in Level.AllLocs())
            if (Level[p].Has(SquareFlags.Mark)) Known.Remember(Level, p);
        if (depth == 0)
        {
            ApplyTownLighting();
            if (StoreDays > 0) UpdateStores();
        }
        _arriving = true;
        try
        {
            UpdateView();
        }
        finally
        {
            _arriving = false;
        }
        if (depth > 0) ShowFeeling(); // Angband announces it in the dungeon only (Ctrl+F works in town too)
        Search(); // Angband on_new_level: a secret door beside the arrival spot is found at once
        SenseOre();

        Publish(new LevelChangedEvent(depth, Level.ProfileId));
    }

    /// <summary>Puts the player on an existing level (tests now; loading saved games later).</summary>
    internal void UseLevel(Level level, Loc start)
    {
        if (Level is not null) Vision.Reset(Level);
        Level = level;
        Known = new KnownMap(level.Width, level.Height);
        Scent.Reset(level);
        Player.Depth = level.Depth;
        Player.Position = start;
        Scheduler.Clear();
        Scheduler.Add(Player);
        Player.Energy = EnergyTable.MoveEnergy;
        foreach (var monster in level.Monsters.All) Scheduler.Add(monster);
        UpdateView();
    }

    private void Publish<T>(T evt) where T : IGameEvent => Events.Publish(evt);

    private static string Article(string noun) =>
        noun.Length > 0 && "aeiouAEIOU".Contains(noun[0]) ? $"an {noun}" : $"a {noun}";

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
