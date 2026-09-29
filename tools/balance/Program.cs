// Balance check: what each depth holds, and how hard it is, over many generated levels.
// For depths 1 to 99: objects on the floor (and how many are egos, artifacts, cursed), gold, monsters,
// traps; and, for a warrior of the depth's level (clvl = depth, at most 50) with the hit points that
// brings, how often they die holding still for 300 turns there. Kept to the game's plainest API, so
// the same program runs on older commits to compare.
// Run: dotnet run -c Release --project tools/balance [levels per depth, default 30]
using System.Globalization;
using Angband.Core.Game;
using Angband.Data;

var data = DataLoader.Load(DataLoader.DefaultDataDirectory);
var perDepth = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 30;
const int holdTurns = 300;
Console.WriteLine($"{perDepth} levels per depth; deaths: a clvl-matched warrior holding {holdTurns} turns");
Console.WriteLine("depth | objects | egos% | artifacts | cursed% | gold/level | monsters | traps | deaths%");
foreach (var depth in new[] { 1, 5, 10, 20, 30, 40, 50, 60, 70, 80, 90, 99 })
{
    double objects = 0, egos = 0, artifacts = 0, cursed = 0, gold = 0, monsters = 0, traps = 0, deaths = 0;
    for (var run = 0; run < perDepth; run++)
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
    string F(double x) => (x / perDepth).ToString("0.0", CultureInfo.InvariantCulture);
    string P(double part, double whole) => whole == 0 ? "-" : (100 * part / whole).ToString("0.0", CultureInfo.InvariantCulture);
    Console.WriteLine($"{depth,5} | {F(objects),7} | {P(egos, objects),5} | {F(artifacts),9} | {P(cursed, objects),7} | {F(gold),10} | {F(monsters),8} | {F(traps),5} | {P(deaths, perDepth),7}");
}
