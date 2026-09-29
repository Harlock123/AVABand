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
}
