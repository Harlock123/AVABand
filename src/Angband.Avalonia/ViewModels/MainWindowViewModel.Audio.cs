using Angband.Audio;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>Audio services handed to the view model by the app (null in tests = silent).</summary>
public sealed record AudioServices(IAudioEngine Engine, SoundDirector Director, IReadOnlyList<SoundPack> Packs);

/// <summary>Sound settings: packs, volumes, muting.</summary>
public sealed partial class MainWindowViewModel
{
    private AudioServices? _audio;
    private bool _loadingAudio;

    public IReadOnlyList<SoundPack> EffectPacks => _audio?.Packs.Where(p => p.HasSounds).ToList() ?? [];
    public IReadOnlyList<SoundPack> MusicPacks => _audio?.Packs.Where(p => p.HasMusic).ToList() ?? [];
    public bool AudioAvailable => _audio?.Engine.IsAvailable == true;
    public string AudioStatus => _audio is null ? "Audio is off."
        : _audio.Engine.IsAvailable ? "Audio device open." : "No audio device could be opened; the game is silent.";

    [ObservableProperty] private SoundPack? _selectedSoundPack;
    [ObservableProperty] private SoundPack? _selectedMusicPack;
    [ObservableProperty] private bool _effectsEnabled = true;
    [ObservableProperty] private bool _musicEnabled = true;
    [ObservableProperty] private bool _muted;
    [ObservableProperty] private double _masterVolume = 80;
    [ObservableProperty] private double _effectsVolume = 80;
    [ObservableProperty] private double _musicVolume = 50;

    /// <summary>Connects audio (called once by the app); applies the saved settings.</summary>
    public void UseAudio(AudioServices audio)
    {
        _audio = audio;
        _loadingAudio = true;
        var saved = _settings;
        EffectsEnabled = saved.EffectsEnabled;
        MusicEnabled = saved.MusicEnabled;
        Muted = saved.Muted;
        MasterVolume = saved.MasterVolume;
        EffectsVolume = saved.EffectsVolume;
        MusicVolume = saved.MusicVolume;
        SelectedSoundPack = EffectPacks.FirstOrDefault(p => p.Id == saved.SoundPackId) ?? EffectPacks.FirstOrDefault();
        SelectedMusicPack = MusicPacks.FirstOrDefault(p => p.Id == saved.MusicPackId) ?? MusicPacks.FirstOrDefault();
        _loadingAudio = false;
        OnPropertyChanged(nameof(EffectPacks));
        OnPropertyChanged(nameof(MusicPacks));
        OnPropertyChanged(nameof(AudioStatus));
        ApplyAudio();
        audio.Director.Attach(_game);
    }

    partial void OnSelectedSoundPackChanged(SoundPack? value) => AudioChanged();
    partial void OnSelectedMusicPackChanged(SoundPack? value) => AudioChanged();
    partial void OnEffectsEnabledChanged(bool value) => AudioChanged();
    partial void OnMusicEnabledChanged(bool value) => AudioChanged();
    partial void OnMutedChanged(bool value) => AudioChanged();
    partial void OnMasterVolumeChanged(double value) => AudioChanged();
    partial void OnEffectsVolumeChanged(double value) => AudioChanged();
    partial void OnMusicVolumeChanged(double value) => AudioChanged();

    [RelayCommand]
    private void ToggleMute()
    {
        Muted = !Muted;
        LastMessage = Muted ? "Sound muted." : "Sound on.";
    }

    [RelayCommand]
    private void TestSound()
    {
        if (_audio is null || SelectedSoundPack is not { } pack) return;
        var files = pack.Sounds.GetValueOrDefault("LEVEL") ?? pack.Sounds.Values.FirstOrDefault();
        if (files is { Count: > 0 }) _audio.Engine.PlayEffect(pack.Resolve(files[0]));
    }

    private void AudioChanged()
    {
        if (_loadingAudio) return;
        _settings.EffectsEnabled = EffectsEnabled;
        _settings.MusicEnabled = MusicEnabled;
        _settings.Muted = Muted;
        _settings.MasterVolume = MasterVolume;
        _settings.EffectsVolume = EffectsVolume;
        _settings.MusicVolume = MusicVolume;
        _settings.SoundPackId = SelectedSoundPack?.Id ?? _settings.SoundPackId;
        _settings.MusicPackId = SelectedMusicPack?.Id ?? _settings.MusicPackId;
        _saveSettings?.Invoke(_settings);
        ApplyAudio();
    }

    private void ApplyAudio()
    {
        if (_audio is null) return;
        var master = Muted ? 0f : (float)(MasterVolume / 100);
        _audio.Engine.SetVolumes(master, (float)(EffectsVolume / 100), (float)(MusicVolume / 100));
        _audio.Director.Effects = SelectedSoundPack;
        _audio.Director.EffectsEnabled = EffectsEnabled && !Muted;
        _audio.Director.MusicEnabled = MusicEnabled && !Muted;
        _audio.Director.MusicPack = SelectedMusicPack;
    }

    /// <summary>Re-points the sound director at a freshly started game.</summary>
    private void AttachAudio() => _audio?.Director.Attach(_game);
}
