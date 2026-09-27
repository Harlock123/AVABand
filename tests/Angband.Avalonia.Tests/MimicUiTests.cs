using Angband.Avalonia.ViewModels;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Data;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

public class MimicUiTests
{
    [AvaloniaFact]
    public void A_disguised_mimic_is_drawn_and_described_as_its_object()
    {
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42);
        var game = vm.Game;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        var p = game.Player.Position;
        var spot = game.Level.AllLocs().First(l => l.DistanceTo(p) is > 2 and < 5 && game.Level.IsEmptyFloor(l)
                                                  && game.Level[l].Has(Angband.Core.World.SquareFlags.Seen));
        var spawner = new Angband.Core.Monsters.MonsterSpawner(game.Data);
        var mimic = spawner.Place(game.Level, game.Rng, game.Data.Monster("potion_mimic")!, spot, asleep: false);
        game.Scheduler.Add(mimic);
        game.DisguiseMonsters();
        game.UpdateView();

        var cell = vm.GetCell(spot.X, spot.Y);
        Assert.Equal('!', cell.Glyph);
        Assert.DoesNotContain("monster:", cell.TileKey);

        vm.ClickCell(spot, secondary: true); // look
        Assert.Equal($"You see {game.Describe(mimic.MimicItem!)}.", vm.LastMessage);
    }
}
