using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Data;
using Angband.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

public sealed class LoreUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "avaband-lore-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private (MainWindow Window, MainWindowViewModel Vm, RecordStore Records) Open()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42);
        var records = new RecordStore(_dir);
        vm.UseRecords(records);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm, records);
    }

    private static Angband.Core.Monsters.Monster PlaceVisible(MainWindowViewModel vm, string race)
    {
        var game = vm.Game;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        var p = game.Player.Position;
        var spot = game.Level.AllLocs().First(l => l.DistanceTo(p) is > 2 and < 5 && game.Level.IsEmptyFloor(l)
                                                  && game.Level[l].Has(Angband.Core.World.SquareFlags.Seen));
        var monster = new Angband.Core.Monsters.MonsterSpawner(game.Data)
            .Place(game.Level, game.Rng, game.Data.Monster(race)!, spot, asleep: true);
        game.Scheduler.Add(monster);
        game.UpdateView();
        return monster;
    }

    [AvaloniaFact]
    public void X_LooksAtAMonster_AndR_RecallsIt()
    {
        var (window, vm, _) = Open();
        var dog = PlaceVisible(vm, "jackal");

        window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.None);
        Assert.True(vm.IsLooking);
        Assert.Same(dog, vm.LookedAt);
        Assert.StartsWith("The jackal (unhurt, asleep).", vm.LastMessage);

        window.KeyPressQwerty(PhysicalKey.R, RawInputModifiers.None);
        var recall = (CharacterSheetViewModel)window.OwnedWindows.OfType<CharacterSheetWindow>().Last().DataContext!;
        Assert.Equal("Monster recall: jackal", recall.Title);
        Assert.StartsWith("The jackal (C)", recall.Text);
        Assert.False(recall.CanSave);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(vm.IsLooking);
    }

    /// <summary>A lit, empty room of granite with the player's spot in it.</summary>
    private static (Angband.Core.World.Level Level, Angband.Core.Geometry.Loc Eye) Room(Angband.Core.Game.GameSession game, int width, int height)
    {
        var t = game.Data.Terrain;
        var level = new Angband.Core.World.Level(t, width, height, 3);
        foreach (var p in level.AllLocs())
        {
            var edge = p.X == 0 || p.Y == 0 || p.X == width - 1 || p.Y == height - 1;
            level[p].Feature = edge ? t.Ids.Granite : t.Ids.Floor;
            if (!edge) level[p].Flags |= Angband.Core.World.SquareFlags.Glow | Angband.Core.World.SquareFlags.Room;
        }
        return (level, new Angband.Core.Geometry.Loc(3, height / 2));
    }

    [AvaloniaFact]
    public void X_LooksAtEverythingOfInterest_NotJustMonsters()
    {
        var (window, vm, _) = Open();
        var game = vm.Game;
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        game.UpdateView();
        // In town, with no monsters about, the shops and the stairs are still worth a look.
        var spots = game.LookSpots();
        Assert.NotEmpty(spots);
        Assert.All(spots, p => Assert.True(game.Level.FeatureAt(p).Has(Angband.Core.Definitions.TerrainFlags.Interesting)
                                           || game.Known.RememberedObject(p) is not null));
        window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.None);
        Assert.True(vm.IsLooking);
        Assert.StartsWith("You see", vm.LastMessage);
        Assert.Equal(spots[0], vm.Cursor);
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        if (spots.Count > 1) Assert.Equal(spots[1], vm.Cursor);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(vm.IsLooking);
    }

    [AvaloniaFact]
    public void X_WithNothingOfInterest_StartsOnYourOwnSquare()
    {
        var (window, vm, _) = Open();
        var game = vm.Game;
        // A bare room: nothing to look at, so the cursor starts free where you stand (Angband).
        var (level, eye) = Room(game, 7, 5);
        game.UseLevel(level, eye);
        window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.None);
        Assert.True(vm.IsLooking);
        Assert.Equal(game.Player.Position, vm.Cursor);
        Assert.StartsWith("You are on", vm.LastMessage);
    }

    [AvaloniaFact]
    public void Look_DescribesATrapYouKnowOf_AndAnObject()
    {
        var (window, vm, _) = Open();
        var game = vm.Game;
        var (level, eye) = Room(game, 9, 5);
        game.UseLevel(level, eye);
        var trapAt = eye + new Angband.Core.Geometry.Loc(2, 0);
        game.Level[trapAt].Trap = game.Data.Traps.First(t => !t.Web).Index;
        game.Level[trapAt].Flags |= Angband.Core.World.SquareFlags.TrapVisible;
        var daggerAt = eye + new Angband.Core.Geometry.Loc(-1, 1);
        game.Level.Objects.Add(daggerAt, game.Objects.Create("dagger"));
        game.UpdateView();
        Assert.Equal([daggerAt, trapAt], game.LookSpots());
        window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.None);
        Assert.StartsWith("You see a Dagger", vm.LastMessage);
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Assert.StartsWith("You see a", vm.LastMessage);
        Assert.Contains(game.VisibleTrapAt(trapAt)!.Name, vm.LastMessage);
    }

    [AvaloniaFact]
    public void Slash_IdentifiesASymbol_AndOffersTheRecallOfThoseMet()
    {
        var (window, vm, _) = Open();
        var game = vm.Game;
        vm.HandleAction(InputAction.IdentifySymbol);
        Assert.True(vm.IsChoosingGlyph);
        Assert.True(vm.ChooseGlyph("Z"));
        Assert.Equal("Z - Zephyr Hound.", vm.LastMessage); // none met: just the name

        game.Lore.For("cave_orc").Sights++;
        game.Lore.For("snaga").Sights++;
        vm.HandleAction(InputAction.IdentifySymbol);
        vm.ChooseGlyph("o");
        Assert.Equal("o - Orc.  Recall details? (y/n)", vm.LastMessage);
        KnowledgeViewModel? shown = null;
        vm.KnowledgeRequested += k => shown = k;
        window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
        Assert.NotNull(shown);
        var met = new[] { "cave_orc", "snaga" }.Select(id => game.Data.Monster(id)!).OrderBy(r => r.Depth).Select(r => r.Id);
        Assert.Equal(met, shown!.Monsters.Rows.Select(r => r.Race.Id)); // only the orcs met, shallowest first
    }

    [AvaloniaFact]
    public void Knowledge_ListsWhatYouHaveMet_AndMemoryIsSaved()
    {
        var (window, vm, records) = Open();
        var dog = PlaceVisible(vm, "jackal");
        vm.Game.DamageMonster(dog, 10_000);

        vm.HandleAction(InputAction.MonsterKnowledge);
        var knowledgeWindow = Assert.IsType<KnowledgeWindow>(window.OwnedWindows.Last());
        var knowledge = ((KnowledgeViewModel)knowledgeWindow.DataContext!).Monsters;
        var row = Assert.Single(knowledge.Rows, r => r.Race.Id == "jackal");
        Assert.Equal("1 slain", row.KillsText);
        knowledge.Selected = row;
        Assert.Contains("You have killed 1 of these creatures.", knowledge.RecallText);
        TileRenderingTests.Save(knowledgeWindow, "monster-knowledge");

        vm.TrySave();
        Assert.Equal(1, records.LoadLore().Find("jackal")!.TotalKills);

        // A new character inherits the memory, but not the kill count.
        vm.StartGame(43);
        var fresh = vm.CreateMonsterKnowledge().Rows.Single(r => r.Race.Id == "jackal");
        Assert.Equal(0, fresh.Kills);
        Assert.Equal(1, fresh.TotalKills);
    }

    [AvaloniaFact]
    public void Recall_ShowsSpellsInTheColourOfTheirDanger()
    {
        var (window, vm, _) = Open();
        var game = vm.Game;
        var dragon = game.Data.Monster("baby_red_dragon")!;
        var lore = game.Lore.For(dragon.Id);
        lore.Sights = 3;
        lore.TotalKills = 1;
        lore.SpellsSeen.Add("BR_FIRE");
        lore.CastsInnate = 4;
        lore.FlagsKnown.UnionWith(["IM_FIRE", "DRAGON", "EVIL"]);
        lore.FlagsLacking.Add("IM_COLD");

        var monsters = vm.CreateMonsterKnowledge();
        monsters.Selected = monsters.Rows.Single(r => r.Race == dragon);
        Assert.Contains("does not resist cold", monsters.RecallText);
        var fire = monsters.RecallRuns!.First(r => r.Text.StartsWith("fire ("));
        Assert.Equal(global::Avalonia.Media.Color.FromUInt32(0xFFFF8000), ((global::Avalonia.Media.ISolidColorBrush)fire.Brush!).Color); // Orange: unresisted

        vm.ShowRecall(dragon);
        var sheet = Assert.IsType<CharacterSheetWindow>(window.OwnedWindows.Last());
        var text = global::Avalonia.Controls.ControlExtensions.FindControl<global::Avalonia.Controls.SelectableTextBlock>(sheet, "SheetText")!;
        Assert.Contains(text.Inlines!, i => i is global::Avalonia.Controls.Documents.Run { Text: "breathe " } run && run.Foreground is not null);
        TileRenderingTests.Save(sheet, "monster-recall-spells");
    }
}
