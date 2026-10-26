using System.Runtime.InteropServices;
using Angband.Avalonia.Controls;
using Angband.Avalonia.Services;
using Angband.Avalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Angband.Avalonia.Views;

/// <summary>
/// Films a clip (AVABand's own): a <see cref="ClipJob"/>'s steps played one by one, each drawn by a map
/// view of its own (unseen, laid out in a panel of the window so it takes the app's fonts) with the
/// game's latest words above and who they were below, into a bitmap whose pixels go to the
/// <see cref="GifEncoder"/>. Playing the game up to the clip and writing the GIF are done off the UI
/// thread; the frames are drawn on it, one at a time, letting the window get on between them.
/// </summary>
public static class ClipMaker
{
    public const int Width = 640;
    public const int MapHeight = 384;
    private const int MessageHeight = 22;
    private const int CaptionHeight = 28;
    public const int Height = MessageHeight + MapHeight + CaptionHeight;

    /// <summary>How long each step is shown (about 7 a second), the first a little longer, and the end held.</summary>
    public const int StepMs = 140;
    private const int FirstMs = 700;
    private const int EndMs = 3500;

    /// <summary>The most frames a clip has (it has fewer: a frame a step, and steps that change nothing are left out).</summary>
    public const int MaxFrames = 120;

    /// <summary>Squares are drawn this many pixels a side in tiles (26 by 16 of them: near enough to make out who is who).</summary>
    private const int TileSize = 24;

    /// <summary>Makes the clip and writes it to <paramref name="path"/>; returns how many frames it has.</summary>
    public static async Task<int> SaveAsync(ClipJob job, string path, Panel host)
    {
        await Task.Run(job.FastForward);
        var gif = await Film(job, host);
        var bytes = await Task.Run(gif.ToArray);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes);
        return gif.FrameCount;
    }

    private static async Task<GifEncoder> Film(ClipJob job, Panel host)
    {
        IBrush Brush(uint argb) => new SolidColorBrush(Color.FromUInt32(argb));
        var mono = host.TryFindResource("MonoFont", out var m) && m is FontFamily mf ? mf : FontFamily.Default;
        var title = host.TryFindResource("TitleFont", out var t) && t is FontFamily tf ? tf : FontFamily.Default;
        var map = new MapView
        {
            Source = job.Map,
            UseTiles = job.UseTiles,
            Tileset = job.Tileset,
            TileScale = job.Tileset is { TileWidth: > 0 } set
                ? set.TileWidth >= TileSize ? TileSize / (double)set.TileWidth : Math.Floor(TileSize / (double)set.TileWidth)
                : 1,
            CellFontSize = job.FontSize,
            // The view moves on by a panel when they near its edge, as Angband's does, rather than keeping
            // them in the middle: every step would move the whole picture, and a GIF keeps only what changed.
            CenterPlayer = false,
            RunsByItself = false,
            Width = Width,
            Height = MapHeight,
        };
        var message = new TextBlock
        {
            FontFamily = mono, FontSize = 12, Foreground = Brush(0xFFE6DDD0), TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0),
        };
        var caption = new TextBlock
        {
            Text = job.Caption, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brush(0xFFE2D3AE),
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0),
        };
        var mark = new TextBlock
        {
            Text = "AVABand", FontFamily = title, FontSize = 12, Foreground = Brush(0xFF8A7A62),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0),
        };
        var ending = new Border
        {
            IsVisible = false, Background = Brush(0xB0080608), Padding = new Thickness(28, 10), CornerRadius = new CornerRadius(6),
            // (high on the map, so where they fell still shows)
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 48, 0, 0),
            Child = new TextBlock { Text = job.Ending, FontFamily = title, FontSize = 34, Foreground = Brush(0xFFE04A3A) },
        };
        var foot = new DockPanel { Background = Brush(0xFF1A1512), Height = CaptionHeight };
        DockPanel.SetDock(mark, Dock.Right);
        foot.Children.Add(mark);
        foot.Children.Add(caption);
        var top = new Border { Background = Brush(0xFF0C0A0C), Height = MessageHeight, Child = message };
        var frame = new DockPanel { Width = Width, Height = Height, Background = Brushes.Black };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(foot, Dock.Bottom);
        frame.Children.Add(top);
        frame.Children.Add(foot);
        frame.Children.Add(new Panel { Children = { map, ending } });

        var gif = new GifEncoder(Width, Height);
        host.Children.Add(frame);
        try
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(Width, Height));
            var pixels = new byte[Width * Height * 4];
            var last = new byte[pixels.Length];
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                // Draws the frame; false if it looks just as the one before.
                bool Draw()
                {
                    var size = new Size(Width, Height);
                    frame.Measure(size);
                    frame.Arrange(new Rect(size));
                    frame.UpdateLayout();
                    bitmap.Render(frame);
                    bitmap.CopyPixels(new PixelRect(0, 0, Width, Height), handle.AddrOfPinnedObject(), pixels.Length, Width * 4);
                    if (bitmap.Format == global::Avalonia.Platform.PixelFormat.Rgba8888)
                        for (var i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
                    if (pixels.AsSpan().SequenceEqual(last)) return false;
                    pixels.CopyTo(last, 0);
                    return true;
                }

                map.RecentreCount++;
                Draw();
                gif.AddFrame(pixels, Width * 4, FirstMs);
                while (gif.FrameCount < MaxFrames - 1 && job.Step())
                {
                    if (job.TakeArrival())
                    {
                        // They came to the level they fell on: the clip starts again from there.
                        gif = new GifEncoder(Width, Height);
                        map.RecentreCount++;
                        Array.Clear(last);
                    }
                    if (job.TakeMessages() is { Length: > 0 } said) message.Text = said;
                    if (Draw()) gif.AddFrame(pixels, Width * 4, gif.FrameCount == 0 ? FirstMs : StepMs);
                    // (the window gets on with things between frames)
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                }
                if (job.Ending.Length > 0)
                {
                    ending.IsVisible = true;
                    Draw();
                    gif.AddFrame(pixels, Width * 4, EndMs);
                }
                else gif.Hold(EndMs);
            }
            finally
            {
                handle.Free();
            }
        }
        finally
        {
            host.Children.Remove(frame);
        }
        return gif;
    }
}
