using Angband.Core.Game;
using Angband.Core.Records;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// The game-over window's last message (AVABand's messages from the fallen): a ready-made phrase or the
/// player's own words, at most <see cref="FallenNotes.MaxLength"/> long, carved on the headstone.
/// </summary>
public sealed partial class LastWordsPrompt(IReadOnlyList<string> phrases, Func<string, string?> carve) : ObservableObject
{
    /// <summary>The ready-made phrases to pick from.</summary>
    public IReadOnlyList<string> Phrases { get; } = phrases;

    /// <summary>The longest the words may be.</summary>
    public int MaxLength => FallenNotes.MaxLength;

    /// <summary>The words as written so far.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCarve))]
    private string _text = "";

    /// <summary>A phrase picked from the list: it goes in the box, to leave as it is or change.</summary>
    [ObservableProperty] private string? _phrase;

    partial void OnPhraseChanged(string? value)
    {
        if (value is not null) Text = value;
    }

    /// <summary>What was carved, once it was ("Carved on the headstone: ..."); empty until then.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCarved))]
    [NotifyPropertyChangedFor(nameof(CanCarve))]
    private string _carved = "";

    public bool IsCarved => Carved.Length > 0;

    public bool CanCarve => !IsCarved && FallenNotes.Clean(Text) is not null;

    /// <summary>Carves the words on the headstone (once); false if there were none to carve.</summary>
    public bool Carve()
    {
        if (!CanCarve || carve(Text) is not { } words) return false;
        Carved = $"Carved on the headstone: '{words}'";
        return true;
    }
}

// AVABand's messages from the fallen in the UI: each game started or loaded is handed the graveyard's last
// words (before its replay starts recording, as the ghosts are), and a character that dies may leave its own
// on its headstone from the game-over window. Not for replays, cheats, the tutorial, the daily dungeon or
// the weekly challenge.
public sealed partial class MainWindowViewModel
{
    /// <summary>The headstone the last death was laid under (null for a cheat's, or without a record store).</summary>
    private FallenRecord? _buried;

    /// <summary>Hands the game just attached the graveyard's last words.</summary>
    private void UseGraveyardNotes()
    {
        if (_records is null || _game.IsReplay || !_game.FallenNotesOn) return;
        _game.UseGraveyardNotes(FallenNotes.FromGraveyard(_records.LoadGraveyard()));
    }

    /// <summary>The last-message prompt for the game-over window after a death, or null where none may be left.</summary>
    private LastWordsPrompt? LastWordsPromptFor(GameSession game)
    {
        var p = game.Player;
        if (_records is null || _buried is null || p.IsWinner || game.IsTutorial || p.IsSeededRun) return null;
        return new LastWordsPrompt(FallenNotes.Phrases(p.KilledBy ?? ""), LeaveLastWords);
    }

    /// <summary>
    /// The dead character's last words: on its headstone, its character dump and sheet. Returns the words as
    /// kept (null if there were none).
    /// </summary>
    public string? LeaveLastWords(string text)
    {
        if (_records is null || _buried is null || FallenNotes.Clean(text) is not { } words) return null;
        _game.LastWords = words;
        _buried.LastWords = words;
        _records.SetLastWords(_buried.Key, words);
        try
        {
            if (_buried.DumpPath is { } dump && File.Exists(dump)) File.WriteAllText(dump, CharacterDump.WithLastWords(File.ReadAllText(dump), words));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The headstone has them; the dump goes without.
        }
        AddMessage($"Your last words are carved on your headstone: '{words}'");
        return words;
    }
}
