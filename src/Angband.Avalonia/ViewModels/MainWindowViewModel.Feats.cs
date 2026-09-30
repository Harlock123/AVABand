using System.Globalization;
using Angband.Core.Records;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// Feats (AVABand's own): milestones across every character, checked after each command and kept in
/// feats.json beside the scores; a new one is said in the messages, and Knowledge (~) → Feats lists
/// them all, done or not.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private FeatBook _featBook = new();
    private IReadOnlyList<FeatDef>? _featDefs;

    private IReadOnlyList<FeatDef> FeatDefs => _featDefs ??= Feats.All(_data);

    /// <summary>The feats done so far (for tests and the page).</summary>
    public FeatBook FeatBook => _featBook;

    /// <summary>After a command: any feat newly done is recorded, said, and saved.</summary>
    private void CheckFeats()
    {
        var fresh = _featBook.Check(_game, FeatDefs);
        if (fresh.Count == 0) return;
        foreach (var feat in fresh) AddMessage($"Feat: {feat.Name}! ({feat.Description})");
        _records?.SaveFeats(_featBook);
    }

    /// <summary>Knowledge → Feats: every feat, done or not, and who did it first.</summary>
    public KnowledgeCategoryViewModel CreateFeatsPage()
    {
        var rows = FeatDefs.Select(f =>
        {
            var done = _featBook.Earned.GetValueOrDefault(f.Id);
            var when = done?.WhenUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
            return new KnowledgeRow(done is null ? "·" : "*", _cells.Color(done is null ? "Slate" : "Yellow"), f.Name, done is null ? "" : when!,
                () => $"{f.Name}\n\n{f.Description}\n\n" + (done is null ? "Not yet done." : $"First done by {done.Character}, {when}."));
        }).ToList();
        var count = _featBook.Earned.Keys.Count(k => FeatDefs.Any(f => f.Id == k));
        var note = Feats.Counts(_game) ? "" : "  ·  this character can't earn them (debug, cheats, tutorial or replay)";
        return new KnowledgeCategoryViewModel("Feats", $"{count} of {rows.Count} feats done{note}", "", rows);
    }
}
