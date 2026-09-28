using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.World;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>Light and shadow on the map: torchlight fading from you, lit rooms steady, memory dim.</summary>
public class LightAndShadowUiTests
{
    [AvaloniaFact]
    public void Torchlight_Fades_LitRoomsStay_AndRememberedSquaresAreDimmer()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var settings = new AppSettings();
        settings.Options[DisplayOptions.Hints] = false;
        settings.Options[DisplayOptions.Scenes] = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, save: null);
        vm.UseInput(InputBindings.Defaults(), null, null);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        vm.Game.MarkDebugUsed();
        vm.Execute(new DebugJumpCommand(3));
        var game = vm.Game;
        Assert.True(vm.LightAndShadow); // on by default

        var me = game.Player.Position;
        var torchlit = game.Level.AllLocs().Where(l => game.Level[l].Has(SquareFlags.Seen) && game.Level[l].Has(SquareFlags.Lit)
            && !game.Level[l].Has(SquareFlags.Glow)).ToList();
        if (torchlit.Count >= 2)
        {
            var near = torchlit.MinBy(l => l.DistanceTo(me));
            var far = torchlit.MaxBy(l => l.DistanceTo(me));
            Assert.True(vm.ShadeAt(near.X, near.Y).Torch);
            Assert.True(vm.ShadeAt(near.X, near.Y).Amount <= vm.ShadeAt(far.X, far.Y).Amount);
        }
        foreach (var l in game.Level.AllLocs().Where(l => game.Level[l].Has(SquareFlags.Seen) && game.Level[l].Has(SquareFlags.Glow)).Take(3))
            Assert.True(vm.ShadeAt(l.X, l.Y).Amount < 0.1);
        var remembered = game.Level.AllLocs().FirstOrDefault(l => !game.Level[l].Has(SquareFlags.Seen) && game.Known.IsKnown(l), new Loc(-1, -1));
        if (remembered.X >= 0) Assert.Equal(0.38f, vm.ShadeAt(remembered.X, remembered.Y).Amount);
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "light-and-shadow");

        vm.SetOption(DisplayOptions.LightAndShadow, false);
        Assert.False(vm.LightAndShadow);
        window.CaptureRenderedFrame();
        TileRenderingTests.Save(window, "light-and-shadow-off");
    }
}
