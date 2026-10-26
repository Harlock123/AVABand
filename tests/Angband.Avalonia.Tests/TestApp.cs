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
        // Out of season, so no test's character is born in a seasonal event because of the day it runs (SeasonUiTests sets its own).
        Angband.Avalonia.ViewModels.MainWindowViewModel.DefaultSeasonClock = () => new DateTime(2026, 6, 15, 12, 0, 0);
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
