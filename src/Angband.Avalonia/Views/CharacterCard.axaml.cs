using Angband.Avalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Angband.Avalonia.Views;

public partial class CharacterCard : UserControl
{
    public CharacterCard() => InitializeComponent();

    /// <summary>
    /// Draws a card (1200 x 675) and saves it as a PNG. It is laid out in <paramref name="host"/>
    /// (a panel in the window, unseen) so that it takes the app's styles, then taken away again.
    /// </summary>
    public static void Save(CharacterCardViewModel card, string path, Panel host)
    {
        var control = new CharacterCard { DataContext = card };
        host.Children.Add(control);
        try
        {
            var size = new Size(control.Width, control.Height);
            control.Measure(size);
            control.Arrange(new Rect(size));
            control.UpdateLayout();
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height));
            bitmap.Render(control);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            bitmap.Save(path, PngBitmapEncoderOptions.Default);
        }
        finally
        {
            host.Children.Remove(control);
        }
    }
}
