using Angband.Avalonia.Controls;
using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Data;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>The Grimoire (Help → The Grimoire): every spell in every book, realm by realm, with pictures.</summary>
public class GrimoireUiTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open(string cls)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory));
        vm.StartGame(42, cls);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 860 };
        window.Show();
        return (window, vm);
    }

    [AvaloniaFact]
    public void EverySpell_EveryBook_AndEveryRealm_HasItsPicture()
    {
        var data = DataLoader.Load(DataLoader.DefaultDataDirectory);
        Assert.All(data.Spells, s => Assert.True(GrimoireArt.Spell(s.Name) is not null, $"no picture for {s.Name} ({GrimoireArt.SpellFile(s.Name)}.png)"));
        Assert.All(data.Spells.Select(s => s.Book).Distinct(), b => Assert.NotNull(GrimoireArt.Book(b)));
        Assert.All(data.Realms, r => Assert.NotNull(GrimoireArt.Realm(r.Id)));
        Assert.NotNull(GrimoireArt.Parchment);
        Assert.Equal("find_traps_doors_stairs", GrimoireArt.SpellFile("Find Traps, Doors & Stairs"));
        Assert.Equal("spear_of_orome", GrimoireArt.SpellFile("Spear of Oromë"));
    }

    [AvaloniaFact]
    public void ItOpensAtYourRealm_TownBooksFirst_EverySpellOnItsBooksPage()
    {
        var (window, vm) = Open("mage");
        var grimoire = vm.CreateGrimoire();
        Assert.Equal(["Arcane", "Divine", "Nature", "Necromantic"], grimoire.Realms.Select(r => r.Name));
        var arcane = grimoire.SelectedRealm!;
        Assert.Equal("arcane", arcane.Id);
        Assert.Equal("first_spells", arcane.SelectedBook!.Id);
        Assert.Equal("Sold in town, at the Bookseller.", arcane.SelectedBook.Where);
        Assert.Equal("wizards_tome_of_power", arcane.Books[^1].Id);              // the deepest book last
        Assert.StartsWith("Found in the dungeon, from about", arcane.Books[^1].Where);
        Assert.Equal(vm.Game.Data.Spells.Count, grimoire.Realms.Sum(r => r.Books.Sum(b => b.Spells.Count)));

        var missile = arcane.SelectedBook.Spells.Single(s => s.Name == "Magic Missile");
        Assert.NotNull(missile.Icon);
        Assert.Contains(missile.Classes, c => c.IsYours && c.Text.StartsWith("★ Mage: level 1 · 1 mana"));
        Assert.DoesNotContain(missile.Classes, c => c.Text.StartsWith("Rogue: "));     // (not a rogue's spell)
        Assert.Contains(arcane.SelectedBook.Spells.Single(s => s.Name == "Object Detection").Classes, c => c.Text.StartsWith("Rogue: "));
        Assert.Contains(missile.Classes, c => c.Text.StartsWith("Warrior/Mage: level 1"));
        Assert.Equal("You could learn this now (G, with its book).", missile.YourNote);
        Assert.Contains("Read by Mage, Rogue, Warrior/Mage", arcane.SelectedBook.Readers);
        Assert.Equal("A", arcane.SelectedBook.DropCap);                             // "Arcane magic is learned…"

        vm.ShowGrimoire();
        var shown = Assert.IsType<GrimoireWindow>(window.OwnedWindows.Last());
        TileRenderingTests.Save(shown, "grimoire");
        ((GrimoireViewModel)shown.DataContext!).SelectedRealm = ((GrimoireViewModel)shown.DataContext!).Realms[2];
        TileRenderingTests.Save(shown, "grimoire-nature");
        shown.Close();
    }

    [AvaloniaFact]
    public void Search_FindsSpellsInEveryBook()
    {
        var (window, vm) = Open("priest");
        var grimoire = vm.CreateGrimoire();
        Assert.Equal("divine", grimoire.SelectedRealm!.Id);
        grimoire.Search = "heal";
        Assert.True(grimoire.IsSearching);
        Assert.Contains(grimoire.SearchResults, s => s.Name == "Minor Healing");
        Assert.Contains(grimoire.SearchResults, s => s.Name == "Herbal Curing" || s.Description.Contains("heal", StringComparison.OrdinalIgnoreCase));
        vm.ShowGrimoire();
        var shown = Assert.IsType<GrimoireWindow>(window.OwnedWindows.Last());
        ((GrimoireViewModel)shown.DataContext!).Search = "fire";
        TileRenderingTests.Save(shown, "grimoire-search");
        grimoire.Search = "zzzz";
        Assert.True(grimoire.NoResults);
        grimoire.ClearSearchCommand.Execute(null);
        Assert.False(grimoire.IsSearching);
    }

    [AvaloniaFact]
    public void AWarrior_SeesItAll_WithNothingMarked()
    {
        var (_, vm) = Open("warrior");
        var grimoire = vm.CreateGrimoire();
        Assert.StartsWith("20 books and ", grimoire.Subtitle);
        Assert.DoesNotContain(grimoire.Realms.SelectMany(r => r.Books).SelectMany(b => b.Spells), s => s.HasYourNote);
    }
}
