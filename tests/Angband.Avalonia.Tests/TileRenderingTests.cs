using Angband.Avalonia.Controls;
using Angband.Avalonia.Rendering;
using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Data;
using Angband.Data.Tiles;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

public class TileRenderingTests
{
    private static readonly IReadOnlyList<TilesetManifest> Tilesets = TilesetCatalog.Discover([TilesetCatalog.DefaultDirectory]);

    private static (MainWindow Window, MainWindowViewModel Vm, AppSettings Saved) Open(bool tiles, string tileset = "adam-bolt")
    {
        var saved = new AppSettings();
        var settings = new AppSettings { UseTiles = tiles, TilesetId = tileset, TileScale = 2 };
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), Tilesets, settings,
            s => { saved.UseTiles = s.UseTiles; saved.TilesetId = s.TilesetId; saved.TileScale = s.TileScale; });
        vm.StartGame(42);
        var window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
        window.Show();
        return (window, vm, saved);
    }

    private static MapView Map(Window w) => w.GetVisualDescendants().OfType<MapView>().First();

    /// <summary>Renders a frame and, when AVABAND_SCREENSHOTS is set, saves it (shared with other UI tests).</summary>
    internal static void Save(Window window, string name) => Capture(window, name);

    /// <summary>Renders a frame; when AVABAND_SCREENSHOTS is set, also saves it there for eyeballing.</summary>
    private static WriteableBitmap Capture(Window window, string name)
    {
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
        if (Environment.GetEnvironmentVariable("AVABAND_SCREENSHOTS") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            frame.Save(Path.Combine(dir, name + ".png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        return frame;
    }

    [AvaloniaFact]
    public void CtrlT_SwitchesBetweenAsciiAndTiles_AndSavesTheChoice()
    {
        var (window, vm, saved) = Open(tiles: false);
        Assert.IsType<AsciiRenderer>(Map(window).Renderer);

        window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Control);

        Assert.True(vm.UseTiles);
        Assert.True(saved.UseTiles);
        Assert.IsType<TileRenderer>(Map(window).Renderer);

        window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Control);
        Assert.IsType<AsciiRenderer>(Map(window).Renderer);
    }

    [AvaloniaTheory]
    [InlineData("adam-bolt", 16)]
    [InlineData("gervais", 32)]
    [InlineData("dcss", 32)]
    public void EachTileset_RendersAtItsNativeSizeTimesScale(string id, int size)
    {
        var (window, vm, _) = Open(tiles: true, id);
        vm.Execute(new Angband.Core.Game.DebugJumpCommand(3));
        vm.ShowWholeMap = true;

        var renderer = Assert.IsType<TileRenderer>(Map(window).Renderer);
        Assert.Equal(size * 2, renderer.CellSize.Width);
        Capture(window, $"game-{id}");
    }

    [AvaloniaFact]
    public void Zoom_StepsThroughTileScales()
    {
        var (window, vm, _) = Open(tiles: true);
        window.KeyPressQwerty(PhysicalKey.Equal, RawInputModifiers.Control);
        Assert.Equal(3, vm.TileScale);
        window.KeyPressQwerty(PhysicalKey.Minus, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.Minus, RawInputModifiers.Control);
        Assert.Equal(1.5, vm.TileScale);
    }

    [AvaloniaFact]
    public void AsciiMode_StillRenders()
    {
        var (window, _, _) = Open(tiles: false);
        Capture(window, "game-ascii");
    }

    [AvaloniaFact]
    public void SettingsWindow_ListsTilesets_AndPreviewsTheSelection()
    {
        var (window, vm, saved) = Open(tiles: true);
        var settings = window.OpenSettings();
        settings.Width = 820;
        settings.Height = 560;

        var list = settings.GetVisualDescendants().OfType<ListBox>().First();
        Assert.Equal(9, list.ItemCount);

        foreach (var set in Tilesets)
        {
            vm.SelectedTileset = set;
            Assert.Equal(set.Id, saved.TilesetId);
            Capture(settings, $"settings-{set.Id}");
        }
    }

    public static TheoryData<string> TilesetIds()
    {
        var data = new TheoryData<string>();
        foreach (var s in Tilesets) data.Add(s.Id);
        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(TilesetIds))]
    public void EveryBundledTileset_DrawsTheMap(string id)
    {
        var (window, _, _) = Open(tiles: true, tileset: id);
        var renderer = Assert.IsType<TileRenderer>(Map(window).Renderer);
        var set = Tilesets.Single(s => s.Id == id);
        Assert.Equal(set.TileWidth * 2, renderer.CellSize.Width);   // non-square cells (Nomad's 8x16) keep their shape
        Assert.Equal(set.TileHeight * 2, renderer.CellSize.Height);
        using var frame = Capture(window, $"map-{id}");
        using var pixels = frame.Lock();
        var raw = new int[pixels.RowBytes / 4 * pixels.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(pixels.Address, raw, 0, raw.Length);
        var distinct = raw.Where((_, i) => i % 97 == 0).ToHashSet();
        Assert.True(distinct.Count > 50, $"{id}: the map looks blank ({distinct.Count} colours)");
    }

    [Fact]
    public void One_monster_in_a_thousand_has_a_rare_look_that_stays_put()
    {
        var rare = Enumerable.Range(0, 200_000).Count(id => MapCellBuilder.HasRareLook(id, 12345));
        Assert.InRange(rare, 150, 250);
        Assert.Equal(MapCellBuilder.HasRareLook(77, 9), MapCellBuilder.HasRareLook(77, 9));
    }

    [AvaloniaFact]
    public void A_rare_blue_yeek_is_Platino_in_DawnLike_and_a_plain_yeek_elsewhere()
    {
        var data = DataLoader.Load(DataLoader.DefaultDataDirectory);
        var cell = new MapCellBuilder(data).Monster(data.Monster("blue_yeek")!, MapCell.Unknown, rare: true);
        Assert.Equal("monster-rare:blue_yeek", cell.TileKey);
        Assert.Equal("monster:blue_yeek", cell.AltTileKey); // what sets without a rare look draw

        var dawnlike = new TileAtlas(Tilesets.Single(s => s.Id == "dawnlike"));
        Assert.True(dawnlike.TryResolve(cell.TileKey, TileLighting.Lit, out _, out _));
        foreach (var other in Tilesets.Where(s => s.Id != "dawnlike"))
        {
            var atlas = new TileAtlas(other);
            Assert.False(atlas.TryResolve(cell.TileKey, TileLighting.Lit, out _, out _));
            Assert.True(atlas.TryResolve(cell.AltTileKey!, TileLighting.Lit, out _, out _)
                        || atlas.TryResolve(TileRenderer.GlyphKey('y'), TileLighting.Lit, out _, out _), other.Id);
        }
    }

    [AvaloniaFact]
    public void UnmappedKeys_FallBackToAscii()
    {
        var set = Tilesets.Single(s => s.Id == "dcss");
        var atlas = new TileAtlas(set);
        Assert.False(atlas.TryResolve("monster:no_such_thing", TileLighting.Lit, out _, out _));
        Assert.True(atlas.TryResolve("player", TileLighting.Lit, out var layers, out _));
        Assert.Equal(4, layers.Length); // paper doll: body, armour, hair, weapon
    }
}
