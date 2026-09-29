using Angband.Avalonia.ViewModels;

namespace Angband.Avalonia.Tests;

/// <summary>
/// A test fixture's kit, on top of the class's own (4.2.5's): the things tests reach for — Cure Light
/// Wounds, Phase Door and flasks of oil, and a sling with iron shots.
/// </summary>
internal static class TestKit
{
    public static void Give(MainWindowViewModel vm)
    {
        var game = vm.Game;
        foreach (var (kind, count) in new[] { ("cure_light_wounds", 2), ("phase_door", 2), ("flask_of_oil", 2), ("iron_shot", 40) })
        {
            var item = game.Objects.Create(kind, count);
            game.Knowledge.LearnKind(item.Kind);
            game.Player.Inventory.Add(item);
        }
        if (game.Player.Inventory.Bow is null)
        {
            var sling = game.Objects.Create("sling");
            game.Knowledge.LearnKind(sling.Kind);
            game.Player.Inventory.Wield(sling, () => game.Objects.NextSerial++);
        }
        game.RecalculateBonuses();
        vm.Refresh();
    }

    /// <summary>
    /// Stands the player on open ground, well away from the town's doors and stairs (which Look would
    /// find before a monster placed nearby).
    /// </summary>
    public static void OpenGround(MainWindowViewModel vm)
    {
        var level = vm.Game.Level;
        var interesting = level.AllLocs().Where(l => level.FeatureAt(l).Has(Angband.Core.Definitions.TerrainFlags.Interesting)).ToList();
        var spot = level.AllLocs().First(l => level.IsEmptyFloor(l) && interesting.All(i => i.DistanceTo(l) >= 6)
                                             && level.AllLocs().Where(n => n.DistanceTo(l) <= 4).All(n => level.IsEmptyFloor(n) || n == l));
        vm.Game.Player.Position = spot;
        vm.Game.UpdateView();
        vm.Refresh();
    }
}
