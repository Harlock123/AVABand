using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Data;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>The map's animations: projections and damage numbers (Angband's projection graphics, show_damage).</summary>
public class MapEffectsTests
{
    private static MapEffects Effects() => new(name => name == "Red" ? 0xFFC00000u : 0xFFFFFFFFu);

    private static readonly Loc Start = new(5, 5);

    private static ProjectionEvent Bolt(string? element, params Loc[] path) =>
        new(Start, path, [], element, ProjectionKind.Bolt);

    [Fact]
    public void ABolt_FliesSquareBySquare_PointingTheWayItGoes()
    {
        var fx = Effects();
        fx.AddProjection(Bolt("fire", new(6, 5), new(7, 6), new(7, 7)), _ => true);
        Assert.True(fx.IsActive);
        Assert.Equal([new EffectGlyph(new(6, 5), '-', "Red", 0xFFC00000u)], fx.Glyphs);
        fx.Advance(MapEffects.StepMs);
        Assert.Equal('\\', fx.Glyphs.Single().Glyph);
        fx.Advance(MapEffects.StepMs);
        Assert.Equal('|', fx.Glyphs.Single().Glyph);
        fx.Advance(MapEffects.StepMs);
        Assert.False(fx.IsActive);
        Assert.Empty(fx.Glyphs);
    }

    [Fact]
    public void OnlyWhatThePlayerCanSee_IsShown()
    {
        var fx = Effects();
        fx.AddProjection(Bolt(null, new(6, 5), new(7, 5), new(8, 5)), p => p.X != 7);
        Assert.Equal(new Loc(6, 5), fx.Glyphs.Single().Loc);
        fx.Advance(MapEffects.StepMs);
        Assert.Equal(new Loc(8, 5), fx.Glyphs.Single().Loc);

        var hidden = Effects();
        hidden.AddProjection(Bolt(null, new Loc(6, 5)), _ => false);
        Assert.False(hidden.IsActive);
    }

    [Fact]
    public void ABall_FliesThenSpreadsRingByRing_AndABeamLeavesATrail()
    {
        var fx = Effects();
        var burst = new List<Loc> { new(8, 5), new(9, 5), new(7, 5), new(10, 5) };
        fx.AddProjection(new ProjectionEvent(Start, [new(6, 5), new(7, 5), new(8, 5)], burst, "fire", ProjectionKind.Ball), _ => true);
        fx.Advance(3 * MapEffects.StepMs);
        Assert.Equal([new Loc(8, 5)], fx.Glyphs.Select(g => g.Loc));                     // the centre
        Assert.All(fx.Glyphs, g => Assert.Equal('*', g.Glyph));
        fx.Advance(MapEffects.BurstStepMs);
        Assert.Equal(3, fx.Glyphs.Count);                                               // one square out
        fx.Advance(MapEffects.BurstStepMs);
        Assert.Equal(4, fx.Glyphs.Count);                                               // two out

        var beam = Effects();
        beam.AddProjection(new ProjectionEvent(Start, [new(6, 5), new(7, 5), new(8, 5)], [], "elec", ProjectionKind.Beam), _ => true);
        beam.Advance(2 * MapEffects.StepMs);
        Assert.Equal(3, beam.Glyphs.Count);
    }

    [Fact]
    public void DamageNumbers_WaitForWhatBringsThem_ThenRiseAndFade()
    {
        var fx = Effects();
        fx.AddProjection(Bolt(null, new(6, 5), new(7, 5)), _ => true);
        fx.AddDamage(new Loc(7, 5), 12);
        Assert.Empty(fx.Numbers);                          // the arrow is still flying
        fx.Advance(2 * MapEffects.StepMs);
        var number = Assert.Single(fx.Numbers);
        Assert.Equal("12", number.Text);
        fx.Advance(MapEffects.FloatMs / 2);
        Assert.InRange(fx.Numbers.Single().Age, 0.4, 0.6);
        fx.Advance(MapEffects.FloatMs);
        Assert.False(fx.IsActive);

        // With nothing flying, a blow's number shows at once.
        fx.AddDamage(new Loc(6, 5), 3);
        Assert.Single(fx.Numbers);
    }

    [Fact]
    public void Clear_StopsEverything_AndABusyQueuePlaysFaster()
    {
        var fx = Effects();
        var path = Enumerable.Range(6, 60).Select(x => new Loc(x, 5)).ToArray();
        fx.AddProjection(Bolt(null, path), _ => true);           // 60 squares: 1320 ms
        fx.AddProjection(Bolt(null, new(6, 6), new(7, 6)), _ => true);
        fx.Advance(60 * MapEffects.StepMs);
        Assert.Equal(new Loc(6, 6), fx.Glyphs.Single().Loc);
        fx.Advance(MapEffects.StepMs / 3);                       // the second one at triple speed
        Assert.Equal(new Loc(7, 6), fx.Glyphs.Single().Loc);

        fx.AddDamage(new Loc(1, 1), 5);
        fx.Clear();
        Assert.False(fx.IsActive);
    }

    [AvaloniaFact]
    public void CastingAMagicMissile_AnimatesIt_AndShowDamageFloatsTheNumber()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.StartGame(42, "mage");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var game = vm.Game;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        vm.SetOption(OptionIds.ShowDamage, true);

        var target = game.Player.Position + new Loc(3, 0);
        if (!game.Level.IsEmptyFloor(target)) target = game.Level.AllLocs().First(l => l.DistanceTo(game.Player.Position) is >= 2 and <= 4
            && game.Level.IsEmptyFloor(l) && Angband.Core.Combat.ProjectionPath.Projectable(game.Level, game.Player.Position, l, 20));
        var monster = new Angband.Core.Monsters.MonsterSpawner(game.Data).Place(game.Level, game.Rng, game.Data.Monster("jackal")!, target, asleep: true)!;
        monster.Hp = monster.MaxHp = 10_000;
        game.UpdateView();

        game.Player.LearnedSpells.Add("magic_missile");
        game.Player.Mana = game.Player.MaxMana = 50;
        for (var i = 0; i < 20 && !vm.Effects.IsActive; i++) vm.Execute(new CastCommand("magic_missile", target));
        Assert.True(vm.Effects.IsActive);
        Assert.NotEmpty(vm.Effects.Glyphs);
        Assert.Empty(vm.Effects.Numbers);
        for (var t = 0; t < 200 && vm.Effects.Numbers.Count == 0; t++) vm.Effects.Advance(10); // the missile arrives
        var number = Assert.Single(vm.Effects.Numbers);
        Assert.Equal(target, number.Loc);
        Assert.True(int.Parse(number.Text) > 0);

        // A new command cuts the show short.
        vm.Execute(new CastCommand("magic_missile", target));
        vm.Execute(new HoldCommand());
        Assert.Empty(vm.Effects.Glyphs);
    }
}
