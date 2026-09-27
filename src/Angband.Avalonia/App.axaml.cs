using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Core.Game;
using System.Diagnostics;
using Angband.Audio;
using Angband.Input;
using Angband.Avalonia.Input;
using Avalonia.Threading;
using Angband.Data;
using Angband.Data.Tiles;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Angband.Avalonia;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var data = DataLoader.LoadDefault();
            var problems = new List<string>();
            var tilesets = TilesetCatalog.DiscoverDefault(problems);
            var vm = new MainWindowViewModel(data, tilesets, AppSettings.Load(), s => s.Save());

            // Audio is optional: without a device (or with --no-audio) the game is simply silent.
            var engine = desktop.Args?.Contains("--no-audio") == true ? new NullAudioEngine() : OpenAlAudioEngine.CreateOrSilent();
            var director = new SoundDirector(engine);
            vm.UseAudio(new AudioServices(engine, director, SoundPackCatalog.DiscoverDefault(problems)));
            desktop.Exit += (_, _) => { director.Dispose(); engine.Dispose(); };

            // Input: saved key/button bindings; gamepads via SDL (optional; --no-gamepad disables).
            var bindings = InputBindings.Load();
            var pad = desktop.Args?.Contains("--no-gamepad") == true ? null : SdlGamepadProvider.TryCreate(bindings);
            var gamepad = pad is null ? null : new GamepadService(pad);
            vm.UseInput(bindings, gamepad, b => b.Save());
            if (pad is not null)
            {
                var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) =>
                {
                    pad.Poll();
                    vm.RefreshGamepadStatus();
                });
                timer.Start();
                desktop.Exit += (_, _) => { timer.Stop(); pad.Dispose(); };
            }
            // Saved characters: continue the most recent living one (unless a seed was asked for).
            vm.UseRecords(RecordStore.Default());
            vm.UseSaves(SaveStore.Default(), resume: !(desktop.Args ?? []).Contains("--seed"));
            foreach (var problem in problems) Trace.WriteLine(problem);
            ApplyDebugArguments(vm, desktop.Args ?? []);
            var mainWindow = new MainWindow { DataContext = vm };
            // Through the window, so the B button can close dialogs and the game ignores the pad behind them.
            if (pad is not null) pad.ActionTriggered += mainWindow.HandleGamepadAction;
            desktop.MainWindow = mainWindow;
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Supports <c>--seed N</c> and <c>--depth N</c> for reproducing a game or level.</summary>
    private static void ApplyDebugArguments(MainWindowViewModel vm, string[] args)
    {
        string? Value(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        if (ulong.TryParse(Value("--seed"), out var seed)) vm.StartGame(seed);
        if (int.TryParse(Value("--depth"), out var depth)) vm.Execute(new DebugJumpCommand(depth));
    }
}
