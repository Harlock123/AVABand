using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Data;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

/// <summary>Settings → Mods (AVABand's own): the mods folder listed, each mod's switch kept in the settings.</summary>
public class ModsUiTests
{
    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("avaband-mods-ui-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private static TabControl ModsTab(SettingsWindow settings)
    {
        var tabs = settings.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => (string?)t.Header == "Mods");
        settings.UpdateLayout();
        return tabs;
    }

    [AvaloniaFact]
    public void TheModsTab_ListsAMod_AndItsSwitchIsKept()
    {
        using var root = new TempDir();
        var mod = Path.Combine(root.Path, "goblin-pack");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, ModCatalog.ManifestFile), """
            { "name": "Goblin pack", "author": "A. Tester", "version": "2.1", "description": "A new goblin for the upper levels." }
            """);
        File.WriteAllText(Path.Combine(mod, DataLoader.AvaMonstersFile), """
            [ { "id": "test_goblin", "name": "test goblin", "glyph": "o", "color": "Green", "base": "orc", "depth": 3,
                "rarity": 1, "hitPoints": 9, "armour": 10, "sleep": 20, "hearing": 10, "experience": 4,
                "blows": [ { "method": "hit", "effect": "hurt", "damage": "1d6" } ], "flags": [ "ORC", "EVIL" ] } ]
            """);
        var data = DataLoader.LoadWithMods(DataLoader.DefaultDataDirectory, [mod], [], out var results);

        MainWindow.ShowCreationOnFirstRun = false;
        var appSettings = new AppSettings();
        var saves = new List<AppSettings>();
        var vm = new MainWindowViewModel(data, [], appSettings, saves.Add);
        vm.UseMods(root.Path, results);
        vm.StartGame(42);
        Assert.Equal(["goblin-pack"], vm.Game.Player.Mods); // a game with the mod is a modded one
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();

        var settings = window.OpenSettings();
        settings.Width = 900;
        settings.Height = 620;
        ModsTab(settings);

        var row = Assert.Single(vm.ModRows);
        Assert.Equal("Goblin pack", row.Title);
        Assert.Equal("by A. Tester  ·  version 2.1", row.Byline);
        Assert.Equal(["ava_monsters.json: adds 1, replaces 0"], row.Changes);
        Assert.Equal("Loaded.", row.StatusText);
        Assert.False(vm.ModsNeedRestart);
        var list = settings.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.Name == "ModList");
        Assert.Equal(1, list.ItemCount);
        var texts = list.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Goblin pack", texts);
        Assert.Contains("ava_monsters.json: adds 1, replaces 0", texts);

        // Switched off: kept in the settings (saved), and a restart is asked for.
        var toggle = list.GetVisualDescendants().OfType<ToggleSwitch>().Single();
        Assert.True(toggle.IsChecked);
        toggle.IsChecked = false;
        Assert.False(row.IsEnabled);
        Assert.Equal(["goblin-pack"], appSettings.DisabledMods);
        Assert.NotEmpty(saves);
        Assert.True(vm.ModsNeedRestart);
        settings.UpdateLayout();
        Assert.True(settings.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ModsRestartNotice").IsVisible);

        // And on again: back as the game started, so no restart wanted.
        toggle.IsChecked = true;
        Assert.Empty(appSettings.DisabledMods);
        Assert.False(vm.ModsNeedRestart);

        // The next start honours the switch.
        row.IsEnabled = false;
        var next = DataLoader.LoadWithMods(DataLoader.DefaultDataDirectory, [mod], appSettings.DisabledMods, out var nextResults);
        Assert.Null(next.Monster("test_goblin"));
        Assert.Equal(ModLoadState.Disabled, nextResults.Single().State);
        row.IsEnabled = true;

        // The example mod: written beside it, listed, and waiting for a restart.
        vm.MakeExampleModCommand.Execute(null);
        Assert.True(Directory.Exists(Path.Combine(root.Path, ModCatalog.ExampleName)));
        Assert.Equal(["example-mod", "goblin-pack"], vm.ModRows.Select(r => r.Name));
        Assert.Equal("New: loads at the next start.", vm.ModRows[0].StatusText);
        Assert.True(vm.ModsNeedRestart);
        Assert.Contains("Made example-mod", vm.ModsStatus);
        settings.UpdateLayout();
        TileRenderingTests.Save(settings, "settings-mods");
    }

    [AvaloniaFact]
    public void TheHelpPage_NamesEveryFileAModCanHold()
    {
        var page = HelpViewModel.LoadBundled().Single(t => t.Title == "Mods");
        var text = string.Concat(page.Blocks.SelectMany(b => b.Spans).Select(s => s.Text + " "));
        Assert.All(ModCatalog.ModdableFiles, file => Assert.Contains(file, text));
        Assert.Contains(ModCatalog.ManifestFile, text);
    }

    [AvaloniaFact]
    public void AModThatFailed_ShowsWhy()
    {
        using var root = new TempDir();
        var mod = Path.Combine(root.Path, "broken");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, DataLoader.AvaTerrainFile), """
            [ { "id": "test_wall", "name": "odd wall", "glyph": "#", "color": "NoSuchColour", "flags": ["Wall"] } ]
            """);
        var data = DataLoader.LoadWithMods(DataLoader.DefaultDataDirectory, [mod], [], out var results);
        Assert.Empty(data.Mods); // the game starts without it

        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(data, [], new AppSettings(), _ => { });
        vm.UseMods(root.Path, results);
        vm.StartGame(42);
        Assert.Empty(vm.Game.Player.Mods);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        var settings = window.OpenSettings();
        ModsTab(settings);

        var row = Assert.Single(vm.ModRows);
        Assert.True(row.HasProblems);
        Assert.Contains(row.Problems, p => p.Contains("NoSuchColour", StringComparison.Ordinal));
        Assert.StartsWith("Not loaded", row.StatusText);
        Assert.Equal("1 mod: 0 in play, 1 failed to load.", vm.ModsSummary);
        TileRenderingTests.Save(settings, "settings-mods-failed");
    }
}
