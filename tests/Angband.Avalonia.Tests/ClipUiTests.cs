using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using Angband.Core.Geometry;
using Angband.Core.Persistence;
using Angband.Data;
using Angband.Data.Tiles;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Angband.Avalonia.Tests;

/// <summary>Clips: "Save a clip" makes an animated GIF of a character's last moments, from their replay.</summary>
public sealed class ClipUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "avaband-clip-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private (MainWindow Window, MainWindowViewModel Vm) Open(bool tiles)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        MainWindow.OpenSavedCards = false;
        var settings = new AppSettings { UseTiles = tiles, TilesetId = "gervais", Muted = true };
        settings.Options[DisplayOptions.Hints] = false;
        settings.Options[DisplayOptions.Scenes] = false;
        var tilesets = tiles ? TilesetCatalog.Discover([TilesetCatalog.DefaultDirectory]) : [];
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), tilesets, settings, save: null)
        {
            ReplayDirectory = Path.Combine(_dir, "replays"),
        };
        vm.UseRecords(new RecordStore(Path.Combine(_dir, "records")));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm);
    }

    /// <summary>Waits (letting the window run) for the clip under way to be made; returns the GIF read back.</summary>
    private static GifEncoderTests.DecodedGif WaitForClip(MainWindow window, Func<string> saying, out string path, out long bytes)
    {
        var clip = window.ClipUnderWay ?? throw new InvalidOperationException("no clip under way");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!clip.IsCompleted && watch.Elapsed < TimeSpan.FromMinutes(2))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(2);
        }
        Assert.True(clip.IsCompletedSuccessfully);
        var said = saying();
        Assert.StartsWith("Clip saved: ", said);
        path = said["Clip saved: ".Length..];
        Assert.True(File.Exists(path), said);
        Assert.Equal(".gif", Path.GetExtension(path));
        var data = File.ReadAllBytes(path);
        bytes = data.Length;
        return GifEncoderTests.Decode(data);
    }

    /// <summary>When AVABAND_SCREENSHOTS is set, saves a frame of a clip as a PNG there, to look at.</summary>
    private static void SaveFrame(GifEncoderTests.DecodedGif gif, int frame, string name)
    {
        if (Environment.GetEnvironmentVariable("AVABAND_SCREENSHOTS") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        using var bitmap = new WriteableBitmap(new global::Avalonia.PixelSize(gif.Width, gif.Height), new global::Avalonia.Vector(96, 96),
            global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Opaque);
        using (var buffer = bitmap.Lock())
        {
            var pixels = gif.Frames[frame].Pixels.Select(c => unchecked((int)0xFF000000) | c).ToArray();
            for (var y = 0; y < gif.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(pixels, y * gif.Width, buffer.Address + y * buffer.RowBytes, gif.Width);
        }
        bitmap.Save(Path.Combine(dir, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    /// <summary>
    /// Walks about the level for a number of turns (holding now and then): to a square not yet
    /// stood on where there is one, so the clip has somewhere to go.
    /// </summary>
    private static void Wander(MainWindowViewModel vm, int turns)
    {
        var been = new HashSet<Loc>();
        Direction[] ways = [Direction.East, Direction.SouthEast, Direction.South, Direction.SouthWest, Direction.West,
            Direction.NorthWest, Direction.North, Direction.NorthEast];
        for (var i = 0; i < turns && !vm.Game.Player.IsDead; i++)
        {
            var g = vm.Game;
            var at = g.Player.Position;
            been.Add(at);
            if (i % 5 == 4)
            {
                vm.Execute(new HoldCommand());
                continue;
            }
            var open = ways.Where(d => g.Level.InBounds(at + d.Offset()) && g.Level.IsPassable(at + d.Offset())
                                       && g.Level.Monsters.At(at + d.Offset()) is null).ToList();
            var way = open.FirstOrDefault(d => !been.Contains(at + d.Offset()), open.FirstOrDefault());
            vm.Execute(way == Direction.None ? new HoldCommand() : new WalkCommand(way));
        }
    }

    [AvaloniaFact]
    public void TheGraveyard_AndTheGameOverMenu_SaveAClip_OfTheirLastMoments_InTiles()
    {
        var (window, vm) = Open(tiles: true);
        vm.StartGame(77, "warrior");                         // (recorded from the start)
        var game = vm.Game;
        for (var i = 0; i < 5; i++) vm.Execute(new HoldCommand());
        game.MarkDebugUsed();                                // (a debug jump: it would bar the grave, so take it off again)
        vm.Execute(new DebugJumpCommand(3));
        game.ToggleDebugMark();
        vm.SkipScenes();
        Wander(vm, 30);
        game.Player.Hp = 1;
        game.TakeHit(100_000, "a cave spider");

        // The game-over menu offers one, for the character who just died.
        var menu = (GameOverMenuViewModel)window.OwnedWindows.OfType<GameOverWindow>().Last().DataContext!;
        var offer = Assert.Single(menu.Choices, c => c.Label.StartsWith("Save a clip", StringComparison.Ordinal));
        Assert.Equal("Exit", menu.Choices[^1].Label);
        menu.Choose(offer);
        WaitForClip(window, () => vm.LastMessage, out var fromMenu, out _);
        Assert.StartsWith(vm.ClipDirectory!, fromMenu);

        // And the graveyard, beside "Watch their last moments".
        var yard = vm.CreateGraveyard();
        Assert.True(yard.SaveClipCommand.CanExecute(null));
        yard.SaveClipCommand.Execute(null);
        Assert.Equal("Making a clip of their last moments…", yard.ClipNote);
        var gif = WaitForClip(window, () => yard.ClipNote, out var path, out var bytes);
        Assert.NotEqual(fromMenu, path);
        Assert.StartsWith(game.Player.Name, Path.GetFileName(path));
        Assert.Equal((ClipMaker.Width, ClipMaker.Height), (gif.Width, gif.Height));
        Assert.Equal(0, gif.Loops);
        Assert.InRange(gif.Frames.Count, 6, ClipMaker.MaxFrames);
        Assert.True(bytes < 5_000_000, $"{bytes:N0} bytes");
        Assert.True(gif.Frames[^1].DelayCs >= 300, "the end is held");
        // The frames differ (they walked), and the map isn't black.
        Assert.NotEqual(gif.Frames[1].Pixels, gif.Frames[^2].Pixels);
        Assert.True(gif.Frames[0].Pixels.Count(c => c != 0) > gif.Width * 50);
        SaveFrame(gif, gif.Frames.Count / 2, "clip-tiles");
        SaveFrame(gif, gif.Frames.Count - 1, "clip-tiles-end");
        TestContext.Current.TestOutputHelper?.WriteLine($"Clip (tiles): {gif.Frames.Count} frames, {bytes:N0} bytes");
    }

    /// <summary>A replay recorded by hand (a recorder on a game, ~30 commands, then a fatal hit) makes a clip in ASCII.</summary>
    [AvaloniaFact]
    public void AReplayFile_MakesAClip_InLetters()
    {
        var (window, vm) = Open(tiles: false);
        vm.ReplayDirectory = null;
        vm.StartGame(1234, "mage");
        var game = vm.Game;
        game.Recorder = new ReplayRecorder(game);
        game.MarkDebugUsed();
        vm.Execute(new DebugJumpCommand(2));
        vm.SkipScenes();
        Wander(vm, 30);
        game.TakeHit(100_000, "Grip, Farmer Maggot's Dog");
        var replay = Path.Combine(_dir, "by-hand" + ReplayFile.Extension);
        game.Recorder.ToFile(game).Write(replay);

        Assert.True(vm.SaveClip(replay));
        var gif = WaitForClip(window, () => vm.LastMessage, out _, out var bytes);
        Assert.InRange(gif.Frames.Count, 6, ClipMaker.MaxFrames);
        Assert.True(bytes < 5_000_000);
        SaveFrame(gif, gif.Frames.Count - 1, "clip-ascii-end");
        TestContext.Current.TestOutputHelper?.WriteLine($"Clip (ASCII): {gif.Frames.Count} frames, {bytes:N0} bytes");
    }

    [AvaloniaFact]
    public void WithoutAReplay_ThereIsNoClip()
    {
        var (_, vm) = Open(tiles: false);
        Assert.False(vm.SaveClip(Path.Combine(_dir, "missing" + ReplayFile.Extension)));
        Assert.StartsWith("Could not open the replay", vm.LastMessage);
    }
}
