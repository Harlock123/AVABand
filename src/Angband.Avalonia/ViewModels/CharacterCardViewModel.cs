using Avalonia.Media.Imaging;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// A character card (AVABand's own): a picture of a character to share — a living one's name,
/// deeds and state, or a fallen one's headstone words — over a scene that suits it.
/// </summary>
public sealed record CharacterCardViewModel(string Name, string Title, IReadOnlyList<string> Lines, string? Epitaph, Bitmap? Background,
    bool Fallen, string Footer)
{
    public bool HasEpitaph => Epitaph is not null;
    public string Mark => Fallen ? "Here lies" : "Still adventuring";
}

public sealed partial class MainWindowViewModel
{
    /// <summary>Raised with a card to render and save (the view does the drawing).</summary>
    public event Action<CharacterCardViewModel, string>? CardRequested;

    /// <summary>Where cards are saved: cards/ beside the records (null without them, as in tests).</summary>
    public string? CardDirectory => _records is null ? null : Path.Combine(_records.Directory, "cards");

    private string CardFooter => "AVABand  ·  a clone of Angband 4.2.5"
                                 + (_featBook.Earned.Count > 0 ? $"  ·  {_featBook.Earned.Count} feats done across every character" : "");

    /// <summary>Game → Save a character card: the character in play, now.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void SaveCharacterCard()
    {
        var p = _game.Player;
        var where = p.Depth == 0 ? "in the town" : $"at {p.Depth * 50} ft";
        var lines = new List<string>
        {
            $"Level {p.Level}  ·  deepest {p.MaxDepth * 50} ft  ·  now {where}",
            $"{Angband.Core.Records.Scoring.Points(p):N0} points  ·  {_game.NormalTurns:N0} turns  ·  {_game.KilledUniques.Count} uniques slain",
        };
        var done = _game.AvaQuests.Quests.Values.Count(q => q.IsDone && q.Stage != "lost");
        if (done > 0) lines.Add($"{done} of the Prancing Pony's quests done");
        if (p.HeroicBirth) lines.Add("Heroic");
        var picture = LevelPicture();
        RequestCard(new CharacterCardViewModel(p.Name, $"the {p.Race?.Name} {p.Class?.Name}", lines, null, LoadBitmap(picture), false, CardFooter));
    }

    /// <summary>A card for a fallen character, from the graveyard.</summary>
    public void SaveFallenCard(Angband.Core.Records.FallenRecord f)
    {
        var lines = new List<string>
        {
            $"Level {f.Level}  ·  deepest {f.MaxDepth * 50} ft  ·  {f.Points:N0} points",
            f.Won ? "Retired victorious" : $"Slain by {f.KilledBy}{(f.Depth == 0 ? " in the town" : $" at {f.Depth * 50} ft")}",
            f.DateUtc.ToLocalTime().ToString("D", System.Globalization.CultureInfo.CurrentCulture),
        };
        RequestCard(new CharacterCardViewModel(f.Name, $"the {f.Race} {f.Class}" + (f.Heroic ? " [Heroic]" : ""), lines, f.Epitaph,
            LoadBitmap(PictureFor("death")), true, CardFooter));
    }

    private void RequestCard(CharacterCardViewModel card)
    {
        if (CardDirectory is not { } folder)
        {
            AddMessage("Character cards are saved beside your records; there are none here.");
            return;
        }
        var safe = string.Concat(card.Name.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var stem = $"{(safe.Length == 0 ? "character" : safe)}{(card.Fallen ? "-headstone" : "")}-{DateTime.Now:yyyyMMdd-HHmmss}";
        var path = Path.Combine(folder, stem + ".png");
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{stem}-{n}.png");
        CardRequested?.Invoke(card, path);
    }

    /// <summary>The picture of where the character is: the level's kind, or the stairs, or the town.</summary>
    private string? LevelPicture() => _game.Player.Depth == 0 ? PictureFor("stairs-up-town")
        : _game.Level.ProfileId switch
        {
            "cavern" => PictureFor("level-cavern"),
            "labyrinth" => PictureFor("level-labyrinth"),
            "moria" => PictureFor("level-moria"),
            "lair" => PictureFor("level-lair"),
            "gauntlet" => PictureFor("level-gauntlet"),
            "hard_centre" => PictureFor("level-fortress"),
            _ => PictureFor("stairs-down"),
        };

    /// <summary>The view saved a card: say where.</summary>
    public void CardSaved(string path) => AddMessage($"Character card saved: {path}");
}
