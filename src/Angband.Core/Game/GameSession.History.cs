using Angband.Core.Items;

namespace Angband.Core.Game;

/// <summary>A line of the player's history: when (game turn), where (dungeon level) and what.</summary>
public sealed record HistoryEntry(long Turn, int Depth, string Text);

// The player's history (Angband player-history.c): the start of the quest, each character level
// reached, each unique killed, each artifact found, and the player's own notes (':'). The
// character dump lists it under [Player history].
public sealed partial class GameSession
{
    private readonly List<HistoryEntry> _history = [];

    public IReadOnlyList<HistoryEntry> History => _history;

    public void AddHistory(string text) => _history.Add(new HistoryEntry(GameTurn, Player.Depth, text));

    internal void RestoreHistory(IEnumerable<HistoryEntry> entries)
    {
        _history.Clear();
        _history.AddRange(entries);
    }

    /// <summary>
    /// Angband do_cmd_note (':'): a note of your own in the history ("/say words" and "/me does
    /// something" are written as speech and action). Empty notes, or ones starting with a space, are
    /// not kept. True if it was kept.
    /// </summary>
    public bool AddNote(string text)
    {
        if (string.IsNullOrEmpty(text) || text[0] == ' ') return false;
        var note = text.StartsWith("/say ", StringComparison.Ordinal) ? $"-- {Player.Name} says: \"{text[5..]}\""
            : text.StartsWith("/me", StringComparison.Ordinal) ? $"-- {Player.Name}{text[3..]}"
            : $"-- Note: {text}";
        Publish(new MessageEvent(note[3..]));
        AddHistory(note);
        return true;
    }

    /// <summary>Sees an object (Angband object_see), noting an artifact in the history the first time it is found.</summary>
    private void SeeObject(Item item)
    {
        var newArtifact = item.Artifact is { } art && !Knowledge.SeenArtifacts.Contains(art.Id);
        Knowledge.See(item);
        if (newArtifact && Knowledge.SeenArtifacts.Contains(item.Artifact!.Id))
            AddHistory($"Found {Describe(item)}"); // Angband history_find_artifact
    }
}
