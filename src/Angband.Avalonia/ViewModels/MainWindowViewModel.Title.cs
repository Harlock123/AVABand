using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Angband.Avalonia.ViewModels;

/// <summary>One button on the title screen.</summary>
public sealed record TitleChoice(string Letter, string Label, string? Detail, Action Act);

// The title screen (AVABand's own): Thangorodrim, the logo and the title music, with what to do —
// continue the character saved last (or, if it died, play it again), make a new one, load another,
// the high scores, or leave. It stays up while a dialog it opened is showing, and goes when a game
// starts or loads. Its pictures can be replaced like the scenes' (title.png / title-logo.png in the
// art folder).
public sealed partial class MainWindowViewModel
{
    [ObservableProperty] private bool _isShowingTitle;
    [ObservableProperty] private int _titleSelectedIndex;

    public ObservableCollection<TitleChoice> TitleChoices { get; } = [];

    /// <summary>The picture behind the title (the player's own title.png/jpg, or the bundled one).</summary>
    public Bitmap? TitleBackground => LoadBitmap(PictureFor("title"));

    public Bitmap? TitleLogo => LoadBitmap(PictureFor("title-logo"));

    public string TitleFooter =>
        $"AVABand {typeof(MainWindowViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? ""}"
        + "  ·  a clone of Angband 4.2.5";

    private static Bitmap? LoadBitmap(string? path)
    {
        if (path is null) return null;
        try { return new Bitmap(path); }
        catch (Exception) { return null; }
    }

    /// <summary>Opens the title screen, its choices fitted to what there is to continue.</summary>
    public void ShowTitle()
    {
        TitleSelectedIndex = -1; // (so the highlight comes back on the first choice below)
        TitleChoices.Clear();
        var choices = new List<(string Label, string? Detail, Action Act)>();
        var p = _game.Player;
        if (_loadedAtStartup && !p.IsDead)
        {
            var where = p.Depth == 0 ? "in the town" : $"at {p.Depth * 50} ft";
            var detail = $"the level {p.Level} {p.Race?.Name} {p.Class?.Name}, {where}";
            if (_crashResume is { } saved) detail += $" — from the save made {Ago(DateTime.UtcNow - saved)}";
            choices.Add(($"Continue {p.Name}", detail, ContinueFromTitle));
        }
        else if (!IsFirstRun && LastCharacter() is { } last)
        {
            // Nobody to continue (the last one died): a fresh game with them is already set up behind.
            var dead = _records?.LoadScores().Entries.OrderByDescending(e => e.DateUtc).FirstOrDefault() is { IsAlive: false } d
                ? $" — last killed by {d.KilledBy}" : "";
            choices.Add(($"Play again as {last.Name}", $"the {_data.Race(last.RaceId)?.Name} {_data.Class(last.ClassId)?.Name}, starting afresh{dead}",
                () =>
                {
                    CloseTitle();
                    AddMessage("A new adventure begins.");
                }));
        }
        choices.Add((IsFirstRun ? "Begin your first adventure" : "Create a new character", "choose a race, a class and your stats",
            RequestNewCharacter));
        if (_saves?.List().Any(e => !e.Summary.IsDead) == true)
            choices.Add(("Load a saved character", null, () => LoadRequested?.Invoke()));
        if (_records is not null) choices.Add(("High scores", null, ShowHighScores));
        if (_records?.LoadGraveyard().Fallen.Count > 0) choices.Add(("The graveyard", "those who went before", ShowGraveyard));
        choices.Add(("Exit", null, () => ExitRequested?.Invoke()));
        for (var i = 0; i < choices.Count; i++)
            TitleChoices.Add(new TitleChoice(((char)('a' + i)).ToString(), choices[i].Label, choices[i].Detail, choices[i].Act));
        TitleSelectedIndex = 0;
        OnPropertyChanged(nameof(TitleBackground));
        OnPropertyChanged(nameof(TitleLogo));
        IsShowingTitle = true;
        if (_audio is not null) _audio.Director.TitleScreen = true;
    }

    private void ContinueFromTitle()
    {
        CloseTitle();
        if (_crashResume is not null)
        {
            _crashResume = null;
            AddMessage("Resumed from the last save; anything after it was lost.");
        }
    }

    /// <summary>The title goes: the game's own music takes over.</summary>
    public void CloseTitle()
    {
        if (!IsShowingTitle) return;
        IsShowingTitle = false;
        ShouldOfferStartMenu = false;
        if (_audio is not null) _audio.Director.TitleScreen = false;
        Refresh();
    }

    /// <summary>A letter picks a choice; Up/Down move; Enter takes the highlighted one.</summary>
    public bool TitleKey(char key)
    {
        if (!IsShowingTitle) return false;
        var choice = TitleChoices.FirstOrDefault(c => c.Letter[0] == char.ToLowerInvariant(key));
        choice?.Act();
        return choice is not null;
    }

    public void MoveTitleSelection(int delta)
    {
        if (TitleChoices.Count == 0) return;
        TitleSelectedIndex = ((TitleSelectedIndex + delta) % TitleChoices.Count + TitleChoices.Count) % TitleChoices.Count;
    }

    public void ChooseSelectedTitle()
    {
        if (TitleChoices.ElementAtOrDefault(TitleSelectedIndex) is { } choice) choice.Act();
    }
}
