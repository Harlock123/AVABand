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

    [AvaloniaFact]
    public void X_WithNothingInView_SaysSo()
    {
        var (window, vm, _) = Open();
        foreach (var m in vm.Game.Level.Monsters.All.ToList()) vm.Game.Level.Monsters.Remove(m);
        vm.Game.UpdateView();
        window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.None);
        Assert.False(vm.IsLooking);
        Assert.Equal("You see no monsters.", vm.LastMessage);
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
