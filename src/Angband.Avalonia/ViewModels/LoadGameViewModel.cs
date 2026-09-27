using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>The "Load a character" dialog: the saved characters, newest first.</summary>
public sealed partial class LoadGameViewModel : ObservableObject
{
    private readonly SaveStore _saves;

    public LoadGameViewModel(SaveStore saves)
    {
        _saves = saves;
        Reload();
    }

    public ObservableCollection<SaveEntry> Saves { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand), nameof(DeleteCommand))]
    private SaveEntry? _selected;

    /// <summary>Delete asks twice: the first press arms it ("Really delete?"), the second deletes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteText))]
    private bool _confirmingDelete;

    public string DeleteText => ConfirmingDelete ? "Really delete?" : "Delete";

    partial void OnSelectedChanged(SaveEntry? value) => ConfirmingDelete = false;

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
