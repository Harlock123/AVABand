using System.Text;
using Angband.Core.Geometry;
using Angband.Core.Records;
using Angband.Core.World;

namespace Angband.Core.Game;

/// <summary>
/// A fallen character's last words, as a message scratched into the floor needs them (AVABand's messages
/// from the fallen): copied from its headstone into the save, so loading the game and playing back its
/// replay never need the graveyard.
/// </summary>
public sealed class FallenNote
{
    /// <summary>Which end it was (<see cref="FallenRecord.Key"/>).</summary>
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Race { get; set; } = "";
    public string Class { get; set; } = "";
    /// <summary>The depth it fell at: where its words may be found.</summary>
    public int Depth { get; set; }
    public string KilledBy { get; set; } = "";
    /// <summary>What it left to say.</summary>
    public string Words { get; set; } = "";
    public DateTime DateUtc { get; set; }

    public static FallenNote From(FallenRecord f) => new()
    {
        Key = f.Key, Name = f.Name, Race = f.Race, Class = f.Class, Depth = f.Depth, KilledBy = f.KilledBy, Words = f.LastWords ?? "",
        DateUtc = f.DateUtc,
    };
}

/// <summary>A character's messages from the fallen (saved with it): the last words it may find, and those it has read.</summary>
public sealed class FallenNoteLog
{
    /// <summary>The fallen the graveyard told of when the game was last started or loaded (newest first), and any on a level now.</summary>
    public List<FallenNote> Fallen { get; set; } = [];
    /// <summary>The notes this character has read (each is found once).</summary>
    public List<string> Read { get; set; } = [];
}

/// <summary>
/// AVABand's messages from the fallen: which headstones' last words may be found, how they read, and the
/// ready-made phrases the game-over window offers.
/// </summary>
public static class FallenNotes
{
    /// <summary>One time in this many, an eligible note is scratched into a new level at its depth.</summary>
    public const int Chance = 3;

    /// <summary>The most fallen a save carries (the newest with last words).</summary>
    public const int MaxKept = 40;

    /// <summary>The longest last words may be.</summary>
    public const int MaxLength = 80;

    /// <summary>The terrain a note is: walkable floor with the words scratched into it.</summary>
    public const string Terrain = "fallen_note";

    /// <summary>
    /// The headstones whose last words may be found: those that left some, not a winner's (they retired)
    /// nor one who died in the town — the newest <see cref="MaxKept"/>.
    /// </summary>
    public static List<FallenNote> FromGraveyard(Graveyard yard, int max = MaxKept) =>
        [.. yard.Fallen.Where(f => !f.Won && f.Depth > 0 && Clean(f.LastWords) is not null).Take(max).Select(FallenNote.From)];

    /// <summary>Last words as they are kept: on one line, trimmed, at most <see cref="MaxLength"/> long; null for none.</summary>
    public static string? Clean(string? words)
    {
        if (words is null) return null;
        var sb = new StringBuilder();
        foreach (var c in words.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
            }
            else if (!char.IsControl(c)) sb.Append(c);
        }
        var text = sb.ToString().Trim();
        if (text.Length > MaxLength) text = text[..MaxLength].TrimEnd();
        return text.Length == 0 ? null : text;
    }

    /// <summary>What a note says, as reading it shows it (and the message log keeps it).</summary>
    public static string Text(FallenNote n) =>
        $"Scratched into the floor: '{n.Words}' — {n.Name} the {n.Race} {n.Class}, killed here by {n.KilledBy}.";

    /// <summary>What killed it, to put after "Beware": "a cave orc" becomes "the cave orc"; a name stays as it is.</summary>
    public static string Foe(string killedBy)
    {
        var name = killedBy.Trim();
        foreach (var article in new[] { "a ", "an ", "some " })
            if (name.StartsWith(article, StringComparison.OrdinalIgnoreCase)) return "the " + name[article.Length..];
        return name.Length == 0 ? "the dark" : name;
    }

    /// <summary>The ready-made last words the game-over window offers (the first two name what killed you).</summary>
    public static IReadOnlyList<string> Phrases(string killedBy)
    {
        var foe = Foe(killedBy);
        string[] phrases =
        [
            $"Beware {foe}.",
            $"Trust not {foe}.",
            "Turn back. The way is shut.",
            "Treasure ahead.",
            "Fly, you fools!",
            "Go carefully, friend. I did not.",
            "Bring more light than I did.",
            "Not all who wander are lost. I was.",
            "The road goes ever on. Mine ended here.",
            "Elbereth! Gilthoniel!",
            "Despair not. Go on.",
            "Remember me.",
        ];
        return [.. phrases.Select(p => Clean(p)!)];
    }
}

// AVABand's messages from the fallen (the birth option birth_ava_fallen_notes): a character that dies may
// leave a last message on its headstone, and the app hands each game the graveyard's last words as it is
// started or loaded (before its replay starts recording, so the replay has them), and the save keeps them.
// A new level at the depth one of them fell at may — one time in three for each, one a level — have its
// words scratched into the floor of a room: a square of its own terrain, read by stepping on it or looking
// at it, after which this character never finds it again. Not in the daily dungeon, the weekly challenge
// or the tutorial; a replay has only the notes its game had (and none before replay version 4).
public sealed partial class GameSession
{
    /// <summary>The last words this character may find scratched into the floor, and those it has read (saved with it).</summary>
    public FallenNoteLog FloorNotes { get; private set; } = new();

    /// <summary>This character's own last words, once it has died and left some (for its character dump; not saved).</summary>
    public string? LastWords { get; set; }

    /// <summary>Whether no notes are found (when playing back a replay recorded before there were any).</summary>
    internal bool FallenNotesOff { get; set; }

    /// <summary>Whether the fallen's notes are found by this character: the birth option, and not in the daily dungeon, the weekly challenge or the tutorial.</summary>
    public bool FallenNotesOn => Options[OptionIds.AvaFallenNotes] && !Player.IsSeededRun && !IsTutorial && !FallenNotesOff;

    /// <summary>
    /// The graveyard's last words (newest first), handed in as the game is started or loaded — before its
    /// replay starts recording. Those already scratched into a level stay; those read, and this character's
    /// own, are left out.
    /// </summary>
    public void UseGraveyardNotes(IEnumerable<FallenNote> notes)
    {
        var placed = NotesOnLevels();
        var kept = FloorNotes.Fallen.Where(n => placed.Contains(n.Key)).ToList();
        var self = $"{Seed:x}-";
        var fresh = !FallenNotesOn ? [] : notes.Where(n => !(n.Name == Player.Name && n.Key.StartsWith(self, StringComparison.Ordinal))
                                                         && !FloorNotes.Read.Contains(n.Key)).Take(FallenNotes.MaxKept).ToList();
        FloorNotes.Fallen = [.. kept, .. fresh.Where(f => kept.All(k => k.Key != f.Key))];
    }

    /// <summary>Loading a save: its notes.</summary>
    internal void RestoreFallenNotes(FallenNoteLog? log) => FloorNotes = log ?? new FallenNoteLog();

    /// <summary>The notes scratched into this level and those kept in a persistent dungeon (by headstone key).</summary>
    private HashSet<string> NotesOnLevels()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (Level is not null) keys.UnionWith(Level.FallenNotes.Values);
        foreach (var stored in _storedLevels.Values) keys.UnionWith(stored.Level.FallenNotes.Values);
        return keys;
    }

    /// <summary>The fallen character whose words are scratched into a square of this level, if any are.</summary>
    public FallenNote? FallenNoteAt(Loc at) =>
        Level.InBounds(at) && Level.FallenNotes.TryGetValue(at, out var key) && Level.FeatureAt(at).Id == FallenNotes.Terrain
            ? FloorNotes.Fallen.FirstOrDefault(n => n.Key == key) : null;

    /// <summary>
    /// A new level is made and peopled: perhaps the last words of someone who fell at this depth, in a room.
    /// No dice are rolled unless a note could be found here, so a game without them plays as before.
    /// </summary>
    private void PlaceFallenNote()
    {
        if (!FallenNotesOn || Level.Depth <= 0) return;
        var placed = NotesOnLevels();
        var here = FloorNotes.Fallen.Where(n => n.Depth == Level.Depth && !FloorNotes.Read.Contains(n.Key) && !placed.Contains(n.Key)).ToList();
        foreach (var note in here)
        {
            if (!Rng.OneIn(FallenNotes.Chance)) continue;
            if (NoteSpot() is not { } at) return;
            Level.SetFeature(at, Data.Terrain[FallenNotes.Terrain].Index);
            Level.FallenNotes[at] = note.Key;
            return;
        }
    }

    /// <summary>Where a note is scratched: bare floor in a room (anywhere on the floor, on a level without rooms), not in a vault, and not where you stand.</summary>
    private Loc? NoteSpot()
    {
        bool Bare(Loc p) => Level.FeatureAt(p).Index == Data.Terrain.Ids.Floor && Level[p].Trap == 0 && !Level.Objects.Any(p)
                            && !Level[p].Has(SquareFlags.Vault) && p != Player.Position && Level.InBoundsFully(p);
        var spots = Level.AllLocs().Where(p => Bare(p) && Level[p].Has(SquareFlags.Room)).ToList();
        if (spots.Count == 0) spots = [.. Level.AllLocs().Where(Bare)];
        return spots.Count == 0 ? null : spots[Rng.RandInt0(spots.Count)];
    }

    /// <summary>Having stepped onto a square: a note scratched there is read (if you can see). True if there was one.</summary>
    private bool StepOnFallenNote(Loc at)
    {
        if (Level.FeatureAt(at).Id != FallenNotes.Terrain) return false;
        if (Player.IsBlind)
        {
            Publish(new MessageEvent("You feel words scratched into the floor, but you cannot see to read them."));
            return true;
        }
        ReadFallenNote(at);
        return true;
    }

    /// <summary>
    /// Looking at a note you can see (from the app's look mode): it is read, and the message log keeps it.
    /// Takes no time; recorded for a replay, as the notes read change what is found later.
    /// </summary>
    public void LookAtFallenNote(Loc at)
    {
        if (!Level.InBounds(at) || Level.FeatureAt(at).Id != FallenNotes.Terrain || Player.IsBlind || !Level[at].Has(SquareFlags.Seen)) return;
        if (FallenNoteAt(at) is not { } note || FloorNotes.Read.Contains(note.Key)) return; // (read already, or worn away: look mode says so)
        using var _ = Recorded("read-note", at.X, at.Y);
        ReadFallenNote(at);
    }

    /// <summary>A note read: what it says (the message log keeps it), and this character never finds it again.</summary>
    private void ReadFallenNote(Loc at)
    {
        if (FallenNoteAt(at) is not { } note)
        {
            Publish(new MessageEvent("Something was scratched into the floor here, but it is too worn to read."));
            return;
        }
        Publish(new MessageEvent(FallenNotes.Text(note)));
        if (FloorNotes.Read.Contains(note.Key)) return;
        FloorNotes.Read.Add(note.Key);
        AddHistory($"Read the last words of {note.Name}");
    }
}
