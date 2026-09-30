// Deep-dungeon speed check: a warrior who cannot die holds for 1000 turns at depths 30, 60 and 99
// (monsters, spells and fights all around); best of five runs, after warm-up.
// Run: dotnet run -c Release --project tools/perf
using System.Diagnostics;
using Angband.Core.Game;
using Angband.Data;
var data = DataLoader.Load(DataLoader.DefaultDataDirectory);
var turns = 1000;
foreach (var depth in new[] { 30, 60, 99 })
{
    var best = double.MaxValue; long gt = 0;
    for (var rep = 0; rep < 5; rep++)
    {
        // (PERF_RACE=high_elf: a race whose abilities run every turn, the High-Elf's light aura.)
        var game = Environment.GetEnvironmentVariable("PERF_RACE") is { Length: > 0 } race
            ? GameSession.NewGame(data, (ulong)depth * 7 + 1, CharacterSpec.Default(race, "warrior"))
            : GameSession.NewGame(data, (ulong)depth * 7 + 1, "warrior");
        game.Execute(new DebugJumpCommand(depth));
        game.Player.MaxHp = game.Player.Hp = 1_000_000;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < turns; i++) { game.Player.Hp = game.Player.MaxHp; game.Execute(new HoldCommand()); }
        best = Math.Min(best, sw.Elapsed.TotalMilliseconds / turns); gt = game.GameTurn;
    }
    Console.WriteLine($"depth {depth}: best {best:F2} ms per turn (game turn {gt})");
}
