using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Data;
using Angband.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

/// <summary>AVABand's rival adventurers in the interface: on the map, and the Knowledge screen's Rivals page.</summary>
public class RivalUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, GameSession Game) Open()
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
        var game = vm.Game;
        game.Rivals!.Members[0] = new RivalMember { Name = "Brannor", RaceId = "dwarf", Race = "Dwarf", ClassId = "warrior", Class = "Warrior" };
        game.Rivals.Temper = RivalTemper.Friendly;
        vm.Refresh();
        return (window, vm, game);
    }

    [AvaloniaFact]
    public void The_Rivals_page_tells_the_band_its_members_and_the_news()
    {
        var (window, vm, game) = Open();
        var band = game.Rivals!;
        band.Depth = 12;
        band.Members[2].Dead = true;
        band.Members[2].Fate = "slain by Bramble at 500 ft";
        band.Deeds.Add(new RivalDeed { Turn = 10, Depth = 2, Text = "Brannor's band slew Grip, Farmer Maggot's Dog at 100 ft", Told = true });
        band.Deeds.Add(new RivalDeed { Turn = 20, Depth = 5, Text = "Brannor's band found the Dagger 'Narthanc'", Told = false });

        var page = vm.CreateKnowledge().Rivals!;
        Assert.Equal("Brannor's band: about 600 ft down", page.Summary);
        Assert.Equal("Brannor's band", page.Rows[0].Name);
        Assert.Contains("They are friendly toward you.", page.Rows[0].Describe());
        Assert.Equal("Brannor the Dwarf Warrior", page.Rows[1].Name);
        Assert.Equal("alive", page.Rows[1].Note);
        Assert.Equal("fallen", page.Rows[3].Note);
        Assert.Contains("Slain by Bramble at 500 ft.", page.Rows[3].Describe());
        // Only the news told so far.
        Assert.Equal("They say Brannor's band slew Grip, Farmer Maggot's Dog at 100 ft", Assert.Single(page.Rows.Skip(4)).Name);

        // The browser's last tab.
        vm.HandleAction(InputAction.MonsterKnowledge);
        var knowledge = window.OwnedWindows.OfType<KnowledgeWindow>().Last();
        var shown = (KnowledgeViewModel)knowledge.DataContext!;
        var tabs = knowledge.GetVisualDescendants().OfType<TabControl>().First();
        var index = tabs.Items.Count - 1;
        Assert.Equal("Rivals", ((TabItem)tabs.Items[index]!).Header);
        shown.SelectedTab = index;
        Assert.Same(tabs.Items[index], tabs.SelectedItem);
        TileRenderingTests.Save(knowledge, "knowledge-rivals");
        knowledge.Close();
    }

    [AvaloniaFact]
    public void Without_rivals_the_page_says_why()
    {
        var (_, vm, game) = Open();
        game.UseRivals(null);
        var page = vm.CreateKnowledge().Rivals!;
        Assert.Empty(page.Rows);
        Assert.Equal("This character was made without rival adventurers.", page.EmptyText);
        game.Player.DailyDate = "2026-10-26";
        game.UseRivals(new RivalBand());
        Assert.Contains("daily dungeon", vm.CreateKnowledge().Rivals!.EmptyText);
    }

    [AvaloniaFact]
    public void A_rival_met_is_drawn_as_a_person_of_their_class()
    {
        var (window, vm, game) = Open();
        foreach (var m in game.Level.Monsters.All.ToList()) game.Level.Monsters.Remove(m);
        var me = game.Player.Position;
        var at = game.Level.AllLocs().Where(p => game.Level.IsEmptyFloor(p) && p.ChebyshevTo(me) is >= 2 and <= 4)
            .OrderBy(p => p.DistanceTo(me)).First();
        var body = game.MeetRival(0, at, new Angband.Core.Randomness.GameRandom(5))!;
        game.UpdateView();
        vm.Refresh();
        var cell = vm.GetCell(at.X, at.Y);
        Assert.Equal('p', cell.Glyph);
        Assert.Equal("Brannor", body.Race.Name);
        TileRenderingTests.Save(window, "rival-on-map");
    }
}
