using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Records;
using Angband.Data;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>The museum window (AVABand's own): artifacts, uniques and the bestiary across every character.</summary>
public class MuseumUiTests
{
    [AvaloniaFact]
    public void Finds_go_into_the_museum_and_its_rooms_show_them_with_their_pictures()
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var folder = Path.Combine(Path.GetTempPath(), $"avaband-museum-{Guid.NewGuid():N}");
        try
        {
            var tilesets = Angband.Data.Tiles.TilesetCatalog.Discover([Angband.Data.Tiles.TilesetCatalog.DefaultDirectory]);
            var settings = new AppSettings { UseTiles = true, TilesetId = "dcss", TileScale = 1 };
            settings.Options[DisplayOptions.Hints] = false;
            settings.Options[DisplayOptions.Scenes] = false;
            var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), tilesets, settings, save: null);
            var records = new RecordStore(folder);
            vm.UseRecords(records);
            vm.StartGame(42, "warrior");
            var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
            window.Show();

            // An artifact found and a unique slain: said, recorded and saved after the next command.
            var data = vm.Game.Data;
            var art = data.Artifacts.FirstOrDefault(a => a.Id is "phial_of_galadriel" or "the_phial_of_galadriel") ?? data.Artifacts[0];
            vm.Game.Knowledge.See(vm.Game.Objects.CreateArtifact(art));
            var unique = Museum.UniqueRaces(data).OrderBy(r => r.Depth).First();
            vm.Game.KilledUniques.Add(unique.Id);
            foreach (var race in data.Monsters.Where(r => r.Depth <= 5)) vm.Game.Lore.For(race.Id).Sights = 1;
            vm.Execute(new HoldCommand());
            Assert.Contains(vm.Messages, m => m.Contains($"A first for the museum: {art.Name}"));
            Assert.Contains(unique.Id, Museum.Load(records.MuseumPath).Uniques.Keys);

            var museum = vm.CreateMuseum();
            Assert.Equal(1, museum.Artifacts.FoundCount);
            Assert.Equal(data.Artifacts.Count, museum.Artifacts.Pieces.Count);
            Assert.Equal(1, museum.Uniques.FoundCount);
            Assert.True(museum.Bestiary.FoundCount >= data.Monsters.Count(r => r.Depth <= 5) - 5);
            Assert.True(museum.Artifacts.Selected!.IsFound);
            Assert.Contains("Found by ", museum.Artifacts.Selected.Detail);
            museum.Artifacts.OnlyFound = true;
            Assert.Single(museum.Artifacts.Pieces);

            var shown = new MuseumWindow { DataContext = museum };
            shown.Show();
            shown.CaptureRenderedFrame();
            TileRenderingTests.Save(shown, "museum-artifacts");
            var rooms = shown.FindControl<TabControl>("Rooms")!;
            rooms.SelectedIndex = 2;
            shown.CaptureRenderedFrame();
            TileRenderingTests.Save(shown, "museum-bestiary");
            shown.Close();
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
