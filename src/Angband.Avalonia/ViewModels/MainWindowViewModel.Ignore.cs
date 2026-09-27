using System.Collections.ObjectModel;
using Angband.Core.Game;
using Angband.Core.Items;

namespace Angband.Avalonia.ViewModels;

/// <summary>One line of a choice menu (the ignore menu).</summary>
public sealed record ChoiceRow(string Letter, string Text);

// Ignoring in the UI (Angband ui-object.c): Ctrl+D picks an item, then offers "This item only",
// "All <kind>", "All <type> <ego>", "All <quality> <type>"; 'K' shows ignored items again.
public sealed partial class MainWindowViewModel
{
    private Item? _ignoring;
    private IReadOnlyList<IgnoreOption> _ignoreOptions = [];

    public ObservableCollection<ChoiceRow> ChoiceRows { get; } = [];

    private void BeginIgnoreMenu(Item item)
    {
        _ignoring = item;
        _ignoreOptions = _game.IgnoreOptions(item);
        ChoiceRows.Clear();
        PromptRows.Clear();
        SpellPromptRows.Clear();
        for (var i = 0; i < _ignoreOptions.Count; i++) ChoiceRows.Add(new ChoiceRow(((char)('a' + i)).ToString(), _ignoreOptions[i].Label));
        PromptTitle = $"Ignore {_game.Describe(item)}:";
        IsPrompting = true;
    }

    /// <summary>A letter in the ignore menu; anything else cancels.</summary>
    private void ChooseIgnore(char key)
    {
        var index = key - 'a';
        var item = _ignoring;
        ChoiceRows.Clear();
        _ignoring = null;
        if (item is null || index < 0 || index >= _ignoreOptions.Count)
        {
            LastMessage = "Cancelled.";
            return;
        }
        Execute(new IgnoreCommand(item, _ignoreOptions[index].Choice));
    }

    /// <summary>Is the object hidden by ignoring (not shown on the map or in menus)?</summary>
    private bool Hidden(Item item) => _game.IsIgnored(item);
}
