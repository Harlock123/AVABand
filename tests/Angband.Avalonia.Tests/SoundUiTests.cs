using Angband.Audio;
using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Data;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Angband.Avalonia.Tests;

public class SoundUiTests
{
    private sealed class FakeEngine : NullAudioEngine
    {
        public float Master = -1, Music = -1;
        public override bool IsAvailable => true;
        public override void SetVolumes(float master, float effects, float music) => (Master, Music) = (master, music);
    }

    private static (MainWindow Window, MainWindowViewModel Vm, FakeEngine Engine, AppSettings Saved) Open()
    {
        var saved = new AppSettings();
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(),
            s => { saved.Muted = s.Muted; saved.MusicVolume = s.MusicVolume; });
        vm.StartGame(42);
        var engine = new FakeEngine();
        vm.UseAudio(new AudioServices(engine, new SoundDirector(engine), SoundPackCatalog.Discover([SoundPackCatalog.DefaultDirectory])));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        return (window, vm, engine, saved);
    }

    [AvaloniaFact]
    public void SoundTab_ListsPacks_AndShowsTheSavedChoices()
    {
        var (window, vm, _, _) = Open();
        var settings = window.OpenSettings();
        settings.Width = 820;
        settings.Height = 560;
        settings.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 1;
        TileRenderingTests.Save(settings, "settings-sound");

        Assert.Equal("angband-dubtrain", vm.SelectedSoundPack?.Id);
        Assert.Equal("cc0-dungeon-music", vm.SelectedMusicPack?.Id);
        Assert.Single(vm.EffectPacks);
        Assert.Single(vm.MusicPacks);
    }

    [AvaloniaFact]
    public void CtrlM_Mutes_AndIsRemembered()
    {
        var (window, vm, engine, saved) = Open();
        Assert.True(engine.Master > 0);

        window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.Control);

        Assert.True(vm.Muted);
        Assert.Equal(0, engine.Master);
        Assert.True(saved.Muted);
    }

    [AvaloniaFact]
    public void MusicVolume_ReachesTheEngine()
    {
        var (_, vm, engine, saved) = Open();
        vm.MusicVolume = 25;
        Assert.Equal(0.25f, engine.Music, 3);
        Assert.Equal(25, saved.MusicVolume);
    }

    [AvaloniaFact]
    public void TheAudioBuffer_IsChosenInTheSoundTab_AndKeptForTheNextStart()
    {
        var settings = new AppSettings();
        var saves = new List<string>();
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, s => saves.Add(s.AudioBuffer));
        vm.StartGame(42);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        window.Show();
        Assert.Equal(AudioBuffer.Automatic, settings.AudioBuffer);
        Assert.Equal(0, vm.AudioBufferIndex);

        var dialog = window.OpenSettings();
        var tabs = dialog.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedIndex = 1; // Sound
        dialog.UpdateLayout();
        var box = dialog.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "AudioBufferBox");
        Assert.Equal(AudioBuffer.Choices.Count, box.ItemCount);
        box.SelectedIndex = 3;
        Assert.Equal(AudioBuffer.Large, settings.AudioBuffer);
        Assert.Equal([AudioBuffer.Large], saves);
        Assert.Contains("No audio device", vm.AudioBufferNote); // no real engine in this test
    }
}
