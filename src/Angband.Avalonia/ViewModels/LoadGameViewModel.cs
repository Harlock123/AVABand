using System.Collections.ObjectModel;
using Angband.Core.Definitions;
using Angband.Core.Persistence;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// The "Load a character" dialog: the saved characters, newest first — and moving them between
/// computers: Export copies the selected save anywhere you choose; Import brings one in (checked, and
/// replacing the same character's save only when you say so, the one replaced kept among its earlier saves).
/// </summary>
public sealed partial class LoadGameViewModel : ObservableObject
{
    private readonly SaveStore _saves;
    private readonly GameData? _data;

    public LoadGameViewModel(SaveStore saves, GameData? data = null)
    {
        _saves = saves;
        _data = data;
        Reload();
    }

    /// <summary>The saves folder (for "Open saves folder").</summary>
    public string SavesFolder => _saves.Directory;

    /// <summary>The window's file picker for a save to import (null: cancelled).</summary>
    public Func<Task<string?>>? PickImport { get; set; }

    /// <summary>The window's file picker for where to export a save, given a suggested file name (null: cancelled).</summary>
    public Func<string, Task<string?>>? PickExport { get; set; }

    /// <summary>What was just done (imported, exported), shown under the list.</summary>
    [ObservableProperty] private string _status = "";

    /// <summary>An import waiting on "Replace?": the same character is here already.</summary>
    private ImportPlan? _pendingImport;

    [ObservableProperty] private bool _confirmingImport;
    [ObservableProperty] private string _importQuestion = "";

    public bool CanImport => _data is not null;

    [RelayCommand]
    private async Task Import()
    {
        if (PickImport is null || await PickImport() is not { } path) return;
        BeginImport(path);
    }

    /// <summary>Checks the chosen save; imports it, or asks first if it would replace this character's save.</summary>
    public void BeginImport(string path)
    {
        Error = Status = "";
        CancelImport();
        if (_data is null) return;
        ImportPlan plan;
        try { plan = _saves.PlanImport(_data, path); }
        catch (Exception ex) when (ex is SaveGameException or IOException or UnauthorizedAccessException)
        {
            Error = $"Can't import {Path.GetFileName(path)}: {ex.Message}";
            return;
        }
        if (plan.AlreadyHere)
        {
            Select(plan.Target);
            Status = $"{plan.Summary.Name} is already in your saves.";
            return;
        }
        if (plan.Existing is { } here)
        {
            _pendingImport = plan;
            var newer = plan.Summary.SavedAtUtc > here.Summary.SavedAtUtc ? "newer" : plan.Summary.SavedAtUtc < here.Summary.SavedAtUtc ? "older" : "the same age";
            ImportQuestion = $"{here.Summary.Name} is already here: level {here.Summary.Level}, turn {here.Summary.GameTurn}, saved {here.Summary.SavedAtUtc.ToLocalTime():g}. "
                             + $"The one you chose is {newer}: level {plan.Summary.Level}, turn {plan.Summary.GameTurn}, saved {plan.Summary.SavedAtUtc.ToLocalTime():g}. "
                             + "Replace it? The one here is kept among its earlier saves.";
            ConfirmingImport = true;
            return;
        }
        FinishImport(plan);
    }

    [RelayCommand]
    private void ReplaceOnImport()
    {
        if (_pendingImport is not { } plan) return;
        CancelImport();
        FinishImport(plan);
    }

    [RelayCommand]
    private void CancelImport()
    {
        _pendingImport = null;
        ConfirmingImport = false;
        ImportQuestion = "";
    }

    private void FinishImport(ImportPlan plan)
    {
        try
        {
            var entry = _saves.Import(plan);
            Reload();
            Select(entry.Path);
            Status = $"Imported {plan.Summary.Name}. Press Load to play.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = $"Could not import it: {ex.Message}";
        }
    }

    private void Select(string path) =>
        Selected = Saves.FirstOrDefault(e => string.Equals(Path.GetFullPath(e.Path), Path.GetFullPath(path), StringComparison.Ordinal)) ?? Selected;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Export()
    {
        if (Selected is not { } entry || PickExport is null) return;
        if (await PickExport($"{Path.GetFileNameWithoutExtension(_saves.PathFor(entry))}{SaveGame.Extension}") is not { } destination) return;
        ExportTo(destination);
    }

    /// <summary>Copies the selected save to <paramref name="destination"/>.</summary>
    public void ExportTo(string destination)
    {
        if (Selected is not { } entry) return;
        Error = Status = "";
        try
        {
            _saves.Export(entry.Path, destination);
            Status = $"Copied {entry.Summary.Name} to {destination}. On the other computer: Load character, then Import a save.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = $"Could not export it: {ex.Message}";
        }
    }

    public ObservableCollection<SaveEntry> Saves { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand), nameof(DeleteCommand), nameof(ExportCommand))]
    private SaveEntry? _selected;

    /// <summary>Delete asks twice: the first press arms it ("Really delete?"), the second deletes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteText))]
    private bool _confirmingDelete;

    public string DeleteText => ConfirmingDelete ? "Really delete?" : "Delete";

    partial void OnSelectedChanged(SaveEntry? value)
    {
        ConfirmingDelete = false;
        Backups.Clear();
        if (value is not null)
            foreach (var backup in _saves.Backups(value.Path)) Backups.Add(backup);
        SelectedBackup = null;
        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(BackupsHeader));
    }

    /// <summary>The selected character's earlier saves (saves/backups), newest first.</summary>
    public ObservableCollection<SaveEntry> Backups { get; } = [];

    public bool HasBackups => Backups.Count > 0;

    public string BackupsHeader => $"Earlier saves of this character ({Backups.Count})";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private SaveEntry? _selectedBackup;

    private bool HasBackupSelected => SelectedBackup is not null;

    /// <summary>
    /// Goes back to an earlier save: it becomes the character's save (the one it replaces is kept as
    /// a backup, so this can be undone) and is loaded.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasBackupSelected))]
    private void Restore()
    {
        if (SelectedBackup is not { } backup) return;
        if (LoadRequested?.Invoke(backup) == true) Loaded?.Invoke();
    }

    [ObservableProperty] private string _error = "";

    public bool IsEmpty => Saves.Count == 0;

    /// <summary>Raised with the chosen save; the owner loads it (and reports failures through <see cref="Error"/>).</summary>
    public event Func<SaveEntry, bool>? LoadRequested;

    /// <summary>Raised once a save has been loaded, so the window can close.</summary>
    public event Action? Loaded;

    private bool HasSelection => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Load()
    {
        if (Selected is not { } entry) return;
        if (LoadRequested?.Invoke(entry) == true) Loaded?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        if (Selected is not { } entry) return;
        if (!ConfirmingDelete)
        {
            ConfirmingDelete = true;
            return;
        }
        ConfirmingDelete = false;
        _saves.Delete(entry.Path);
        Reload();
    }

    private void Reload()
    {
        Saves.Clear();
        foreach (var entry in _saves.List()) Saves.Add(entry);
        Selected = Saves.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
    }
}
