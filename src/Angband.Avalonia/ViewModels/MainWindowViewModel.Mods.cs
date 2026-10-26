using System.Collections.ObjectModel;
using Angband.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// A mod folder on the Settings → Mods tab: its <c>mod.json</c>, the files it changes and how, any
/// problems, and its on/off switch (kept in the settings; it takes effect at the next start).
/// </summary>
public sealed partial class ModRow : ObservableObject
{
    private readonly Action<ModRow>? _changed;
    private readonly ModLoadResult? _atStart;

    public ModRow(ModInfo info, ModLoadResult? atStart, bool enabled, Action<ModRow>? changed)
    {
        Info = info;
        _atStart = atStart;
        _isEnabled = enabled;
        _changed = changed;
        Changes = [.. info.Files.Select(f => f.Summary)];
        var problems = new List<string>(info.Errors);
        if (atStart is { State: ModLoadState.Failed, Error: { } error }) problems.Add(error);
        Problems = problems;
        Notes = info.Notes;
    }

    public ModInfo Info { get; }
    /// <summary>The folder's name (what switches it off, and what a modded character's dump names).</summary>
    public string Name => Info.Name;
    public string Title => Info.Title;

    /// <summary>"by Someone · version 1.0", from <c>mod.json</c>.</summary>
    public string Byline => string.Join("  ·  ", new[]
    {
        string.IsNullOrWhiteSpace(Info.Manifest?.Author) ? null : "by " + Info.Manifest!.Author,
        string.IsNullOrWhiteSpace(Info.Manifest?.Version) ? null : "version " + Info.Manifest!.Version,
    }.OfType<string>());
    public bool HasByline => Byline.Length > 0;
    public string Description => Info.Manifest?.Description ?? "";
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>Each data file and what it does: "ava_monsters.json: adds 1, replaces 0".</summary>
    public IReadOnlyList<string> Changes { get; }
    /// <summary>Unreadable files, and the loader's complaint if the mod broke the data at start.</summary>
    public IReadOnlyList<string> Problems { get; }
    public bool HasProblems => Problems.Count > 0;
    public IReadOnlyList<string> Notes { get; }
    public bool HasNotes => Notes.Count > 0;

    /// <summary>Whether the game started with this mod in play (or tried to: a failed mod was on).</summary>
    public bool OnAtStart => _atStart is { State: ModLoadState.Loaded or ModLoadState.Failed };

    /// <summary>Its switch differs from how the game started: a restart is wanted.</summary>
    public bool Changed => IsEnabled != OnAtStart;

    [ObservableProperty] private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        _changed?.Invoke(this);
    }

    /// <summary>Where it stands: loaded, off, or waiting for a restart.</summary>
    public string StatusText => (_atStart?.State, IsEnabled) switch
    {
        (ModLoadState.Failed, true) => "Not loaded: it broke the game's data (see below). Fix it and restart.",
        (ModLoadState.Failed, false) => "Off (it failed to load).",
        (ModLoadState.Loaded, true) => "Loaded.",
        (ModLoadState.Loaded, false) => "In play now; off from the next start.",
        (ModLoadState.Disabled, true) => "On from the next start.",
        (null, true) => "New: loads at the next start.",
        _ => "Off.",
    };
}

/// <summary>
/// Settings → Mods (AVABand's own): the mod folders in the user mods folder, what each changes, any load
/// errors, and a switch for each. Mods are read when the game starts, so changes say "Restart AVABand to
/// apply". Also opens the folder and writes an example mod to copy from.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>The mods folder being listed; null until <see cref="UseMods"/> (the tab then lists nothing).</summary>
    private string? _modsRoot;
    private IReadOnlyList<ModLoadResult> _modsAtStart = [];

    public ObservableCollection<ModRow> ModRows { get; } = [];

    /// <summary>The user mods folder (made by "Open the mods folder" if it isn't there yet).</summary>
    public string ModsFolder => _modsRoot ?? DataLoader.UserModsRoot;

    public bool HasMods => ModRows.Count > 0;

    /// <summary>A mod's switch differs from how the game started.</summary>
    [ObservableProperty] private bool _modsNeedRestart;

    /// <summary>What was just done (the example made, the folder's whereabouts...).</summary>
    [ObservableProperty] private string _modsStatus = "";

    /// <summary>"2 mods: 1 loaded, 1 off" — the tab's heading line.</summary>
    public string ModsSummary
    {
        get
        {
            if (ModRows.Count == 0) return "No mods yet.";
            var loaded = _modsAtStart.Count(m => m.State == ModLoadState.Loaded);
            var failed = _modsAtStart.Count(m => m.State == ModLoadState.Failed);
            var off = ModRows.Count(r => !r.IsEnabled);
            var parts = new List<string> { $"{loaded} in play" };
            if (failed > 0) parts.Add($"{failed} failed to load");
            if (off > 0) parts.Add($"{off} off");
            return $"{ModRows.Count} mod{(ModRows.Count == 1 ? "" : "s")}: {string.Join(", ", parts)}.";
        }
    }

    /// <summary>The mods folder and what became of each mod at startup (App, from <see cref="DataLoader.LoadDefault(IEnumerable{string}, out IReadOnlyList{ModLoadResult})"/>).</summary>
    public void UseMods(string root, IReadOnlyList<ModLoadResult> atStart)
    {
        _modsRoot = root;
        _modsAtStart = atStart;
        OnPropertyChanged(nameof(ModsFolder));
    }

    /// <summary>Looks in the mods folder again (the Settings window does on opening, and "Look again").</summary>
    [RelayCommand]
    public void RefreshMods()
    {
        ModRows.Clear();
        if (_modsRoot is { } root)
        {
            IReadOnlyList<ModInfo> found;
            try { found = ModCatalog.Discover(root); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                found = [];
                ModsStatus = $"Couldn't read the mods folder: {ex.Message}";
            }
            var disabled = new HashSet<string>(_settings.DisabledMods, StringComparer.OrdinalIgnoreCase);
            foreach (var info in found)
            {
                var atStart = _modsAtStart.FirstOrDefault(m => string.Equals(m.Name, info.Name, StringComparison.OrdinalIgnoreCase));
                ModRows.Add(new ModRow(info, atStart, !disabled.Contains(info.Name), ModSwitched));
            }
        }
        UpdateModsState();
    }

    private void ModSwitched(ModRow row)
    {
        _settings.DisabledMods.RemoveAll(n => string.Equals(n, row.Name, StringComparison.OrdinalIgnoreCase));
        if (!row.IsEnabled) _settings.DisabledMods.Add(row.Name);
        _saveSettings?.Invoke(_settings);
        UpdateModsState();
    }

    private void UpdateModsState()
    {
        // A mod that has gone since the start counts too: the next start won't have it.
        var gone = _modsAtStart.Any(m => m.State != ModLoadState.Disabled
                                         && ModRows.All(r => !string.Equals(r.Name, m.Name, StringComparison.OrdinalIgnoreCase)));
        ModsNeedRestart = gone || ModRows.Any(r => r.Changed);
        OnPropertyChanged(nameof(HasMods));
        OnPropertyChanged(nameof(ModsSummary));
    }

    /// <summary>Writes <c>example-mod</c> (a new monster and a new item) into the mods folder, switched on.</summary>
    [RelayCommand]
    public void MakeExampleMod()
    {
        try
        {
            Directory.CreateDirectory(ModsFolder);
            ModsStatus = ModCatalog.WriteExample(ModsFolder) is null
                ? $"There is already a folder called {ModCatalog.ExampleName} in the mods folder; it was left as it is."
                : $"Made {ModCatalog.ExampleName}: the mud newt and the miner's pick-knife. Restart AVABand to play with it (Help → Mods explains its files).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ModsStatus = $"Couldn't make the example mod: {ex.Message}";
        }
        _modsRoot ??= ModsFolder;
        RefreshMods();
    }
}
