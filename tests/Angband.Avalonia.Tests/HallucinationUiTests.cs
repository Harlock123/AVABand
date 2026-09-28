using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Effects;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Data;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>What a hallucinating player sees (Angband hallucinatory_monster / hallucinatory_object).</summary>
public class HallucinationUiTests
{
    [AvaloniaFact]
    public void MonstersAndObjects_LookLikeAnything_ANewThingEachTurn()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.StartGame(42, "warrior");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var game = vm.Game;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);

        // Three sleeping jackals and a dagger in plain view.
        var p = game.Player.Position;
        var spots = game.Level.AllLocs().Where(l => l.DistanceTo(p) is > 1 and < 5 && game.Level.IsEmptyFloor(l)).Take(4).ToList();
        var spawner = new Angband.Core.Monsters.MonsterSpawner(game.Data);
        foreach (var at in spots.Take(3)) spawner.Place(game.Level, game.Rng, game.Data.Monster("jackal")!, at, asleep: true);
        game.Level.Objects.Add(spots[3], game.Objects.Create("dagger"));
        game.UpdateView();
        Assert.All(spots.Take(3), at => Assert.Equal("monster:jackal", vm.GetCell(at.X, at.Y).TileKey));
        Assert.Equal("object:dagger", vm.GetCell(spots[3].X, spots[3].Y).TileKey);

        game.IncreaseTimed(TimedIds.Image, 100);
        string[] Seen() => [.. spots.Select(at => vm.GetCell(at.X, at.Y).TileKey ?? "")];
        var first = Seen();
        Assert.Contains(first.Take(3), k => k != "monster:jackal");
        Assert.All(first.Take(3), k => Assert.StartsWith("monster:", k));
        Assert.StartsWith("object:", first[3]);
        Assert.Equal(first, Seen()); // steady within a turn: repainting doesn't make it flicker

        vm.Execute(new HoldCommand());
        Assert.NotEqual(first, Seen()); // a new turn, new visions

        vm.ClickCell(spots[3], secondary: true);
        Assert.Equal("You see something strange.", vm.LastMessage);
        Assert.Contains("Halluc", vm.StatusText);
    }
}
