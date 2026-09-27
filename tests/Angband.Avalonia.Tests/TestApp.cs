using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Angband.Avalonia.Tests.TestAppBuilder))]

namespace Angband.Avalonia.Tests;

public static class TestAppBuilder
{
    // Real Skia rendering (not the stub renderer) so tests can capture and inspect frames.
    public static AppBuilder BuildAvaloniaApp()
    {
        Angband.Avalonia.Views.MainWindow.ShowCreationOnFirstRun = false;
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
