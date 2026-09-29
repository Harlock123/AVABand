using Angband.Avalonia.ViewModels;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>Remove Curse asks which item, then which curse, showing each curse's strength (Angband ui-curse.c).</summary>
public class RemoveCurseUiTests
{
    [AvaloniaFact]
    public void Reading_Remove_Curse_asks_for_the_item_then_the_curse()
    {
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        TestKit.Give(vm);
        var game = vm.Game;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);

        var dagger = game.Objects.Create("dagger");
        Assert.True(game.Objects.AddCurse(new Angband.Core.Randomness.GameRandom(1), dagger, game.Data.Curse("vulnerability")!, 7));
        game.Knowledge.LearnRune(RuneIds.Curse("vulnerability"));
        game.Player.Inventory.Add(dagger);
        var scroll = game.Player.Inventory.Add(game.Objects.Create("scroll_of_remove_curse"))!;
        game.Knowledge.LearnKind(scroll.Kind);
        vm.Refresh();

        vm.HandleAction(InputAction.Read);
        vm.PromptKey(vm.PromptRows.Single(r => r.Item == scroll).Letter[0]);
        Assert.Equal("Uncurse which item?", vm.PromptTitle);
        vm.PromptKey('a');
        Assert.Equal("Remove which curse (spell strength 50+d50)?", vm.PromptTitle);
        Assert.Equal(["vulnerability (curse strength 7)"], vm.MenuLabels);
        vm.PromptKey('a');
        Assert.False(dagger.IsCursed);
        Assert.False(game.Player.Inventory.Contains(scroll));
    }
}
