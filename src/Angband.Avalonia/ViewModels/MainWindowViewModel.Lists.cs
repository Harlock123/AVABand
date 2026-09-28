using System.Collections.ObjectModel;
using Angband.Core.Game;
using Angband.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Angband.Avalonia.ViewModels;

/// <summary>A line of the monster or object list, ready to draw.</summary>
public sealed record ListRow(string Glyph, IBrush GlyphBrush, string Text, IBrush TextBrush, string Location, bool IsHeading)
{
    public FontWeight Weight => IsHeading ? FontWeight.SemiBold : FontWeight.Normal;
}

// The monster list ('[') and object list (']'), Angband's ui-mon-list.c and ui-obj-list.c: shown over
// the map until a key is pressed ('x' re-sorts the monsters by experience), and optionally kept up
// to date in the sidebar, as Angband's subwindows are.
public sealed partial class MainWindowViewModel
{
    private enum ListKind { None, Monsters, Objects }
    private ListKind _shownList;
    private bool _listByExperience;

    [ObservableProperty] private bool _isShowingList;
    [ObservableProperty] private string _listFooter = "";
    public ObservableCollection<ListRow> ListRows { get; } = [];

    /// <summary>Sidebar lists (View menu), remembered in the settings.</summary>
    [ObservableProperty] private bool _showMonsterPanel;
    [ObservableProperty] private bool _showObjectPanel;
    public ObservableCollection<ListRow> MonsterPanelRows { get; } = [];
    public ObservableCollection<ListRow> ObjectPanelRows { get; } = [];

    /// <summary>'[': the monsters you can see or sense.</summary>
    public void ShowMonsterList()
    {
        _shownList = ListKind.Monsters;
        _listByExperience = false;
        FillShownList();
    }

    /// <summary>']': the objects you can see or know of.</summary>
    public void ShowObjectList()
    {
        _shownList = ListKind.Objects;
        FillShownList();
    }

    private void FillShownList()
    {
        Fill(ListRows, _shownList == ListKind.Monsters ? _game.MonsterList(_listByExperience) : _game.ObjectList());
        ListFooter = _shownList == ListKind.Monsters
            ? $"Press 'x' to turn {(_listByExperience ? "OFF" : "ON")} 'sort by exp'; any other key to close."
            : "Press any key to close.";
        IsShowingList = true;
    }

    /// <summary>While a list is up, 'x' re-sorts the monster list and anything else closes it (Angband).</summary>
    private bool HandleListAction(InputAction action)
    {
        if (!IsShowingList) return false;
        if (_shownList == ListKind.Monsters && action == InputAction.Look)
        {
            _listByExperience = !_listByExperience;
            FillShownList();
            return true;
        }
        CloseList();
        return true;
    }

    public void CloseList()
    {
        IsShowingList = false;
        _shownList = ListKind.None;
        ListRows.Clear();
    }

    [RelayCommand]
    private void ToggleMonsterPanel() => ShowMonsterPanel = !ShowMonsterPanel;

    [RelayCommand]
    private void ToggleObjectPanel() => ShowObjectPanel = !ShowObjectPanel;

    // --- The recall panel (Angband's monster recall subwindow, PW_MONSTER) --------------------------

    [ObservableProperty] private bool _showRecallPanel;
    [ObservableProperty] private string _recallPanelTitle = "";
    [ObservableProperty] private string _recallPanelText = "";
    [ObservableProperty] private IReadOnlyList<ColoredRun>? _recallPanelRuns;
    [ObservableProperty] private bool _hasRecallPanel;
    private Angband.Core.Definitions.MonsterRaceDef? _recallPanelRace;

    [RelayCommand]
    private void ToggleRecallPanel() => ShowRecallPanel = !ShowRecallPanel;

    partial void OnShowRecallPanelChanged(bool value)
    {
        _settings.ShowRecallPanel = value;
        _saveSettings?.Invoke(_settings);
        RefreshRecallPanel();
    }

    /// <summary>
    /// What the recall panel shows: the monster under the look cursor, else the one tracked (last
    /// targeted, looked at or struck: Angband's monster_race_track). Not while hallucinating.
    /// </summary>
    private void RefreshRecallPanel()
    {
        var race = !ShowRecallPanel || _game.IsHallucinating ? null
            : LookedAt?.Race ?? (_game.HealthTracked is { IsVisible: true, Camouflaged: false } m ? m.Race : null);
        HasRecallPanel = race is not null;
        if (race is null)
        {
            _recallPanelRace = null;
            return;
        }
        var text = _game.Recall(race);
        if (race == _recallPanelRace && text == RecallPanelText) return; // unchanged (what is known can grow in a fight)
        _recallPanelRace = race;
        RecallPanelTitle = race.Name;
        RecallPanelText = text;
        RecallPanelRuns = ColoredText.FromMarked(_game.RecallMarked(race), _cells);
    }

    partial void OnShowMonsterPanelChanged(bool value)
    {
        _settings.ShowMonsterPanel = value;
        _saveSettings?.Invoke(_settings);
        RefreshListPanels();
    }

    partial void OnShowObjectPanelChanged(bool value)
    {
        _settings.ShowObjectPanel = value;
        _saveSettings?.Invoke(_settings);
        RefreshListPanels();
    }

    /// <summary>Keeps the sidebar lists current (called with every refresh of the screen).</summary>
    private void RefreshListPanels()
    {
        if (ShowMonsterPanel) Fill(MonsterPanelRows, _game.MonsterList());
        else MonsterPanelRows.Clear();
        if (ShowObjectPanel) Fill(ObjectPanelRows, _game.ObjectList());
        else ObjectPanelRows.Clear();
        RefreshRecallPanel();
    }

    private void Fill(ObservableCollection<ListRow> target, IReadOnlyList<VisibleListRow> rows)
    {
        target.Clear();
        foreach (var r in rows)
            target.Add(new ListRow(r.Glyph?.ToString() ?? "", Brush(r.GlyphColor ?? "White"), r.Text, Brush(r.Color), r.Location, r.IsHeading));
    }

    private IBrush Brush(string color) => new ImmutableSolidColorBrush(Color.FromUInt32(_cells.Color(color)));
}
