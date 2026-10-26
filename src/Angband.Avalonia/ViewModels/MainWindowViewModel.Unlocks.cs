using Angband.Core.Game;
using Angband.Core.Records;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// Unlocks (AVABand's own): rewards earned through feats that change nothing about the game's balance —
/// more title-screen scenes, frames for character cards (Settings → Display), and other starting kits
/// on the creation screen. Knowledge (~) → Unlocks lists them all, earned or not, and what earns each.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>The unlocks the feats done so far earn.</summary>
    public IReadOnlyList<UnlockDef> EarnedUnlocks => Unlocks.Earned(_featBook);

    /// <summary>The card frames earned, in the page's order.</summary>
    private IReadOnlyList<CardFrameStyle> EarnedFrames =>
        [.. EarnedUnlocks.Where(u => u.Kind == UnlockKind.CardFrame).Select(u => CardFrames.Find(u.Key)).OfType<CardFrameStyle>()];

    /// <summary>Settings → Display, "Card frame": plain, then each frame earned.</summary>
    public IReadOnlyList<string> CardFrameChoices => ["Plain", .. EarnedFrames.Select(f => f.Name)];

    /// <summary>The frame chosen for character cards (0: plain).</summary>
    public int CardFrameIndex
    {
        get => EarnedFrames.ToList().FindIndex(f => f.Id == _settings.CardFrame) + 1;
        set
        {
            var frames = EarnedFrames;
            if (value < 0 || value > frames.Count) return;
            var id = value == 0 ? null : frames[value - 1].Id;
            if (id == _settings.CardFrame) return;
            _settings.CardFrame = id;
            _saveSettings?.Invoke(_settings);
            OnPropertyChanged();
        }
    }

    /// <summary>The frame cards are drawn with: the one chosen, if it is still earned.</summary>
    public CardFrameStyle? ChosenCardFrame => EarnedFrames.FirstOrDefault(f => f.Id == _settings.CardFrame);

    /// <summary>The kits the creation screen may offer besides the class's own.</summary>
    public IReadOnlyList<StartingKitDef> EarnedKits =>
        [.. EarnedUnlocks.Where(u => u.Kind == UnlockKind.StartingKit).Select(u => StartingKits.Find(u.Key)).OfType<StartingKitDef>()];

    /// <summary>
    /// The pictures the title screen takes in turn: Thangorodrim, then each scene earned — those that
    /// can be found (the player's own of the same name first, as for the scenes).
    /// </summary>
    public IReadOnlyList<string> TitleScenes =>
        ["title", .. EarnedUnlocks.Where(u => u.Kind == UnlockKind.TitleScene && PictureFor(u.Key) is not null).Select(u => u.Key)];

    /// <summary>The picture behind the title this time (one of <see cref="TitleScenes"/>).</summary>
    public string TitleScene
    {
        get
        {
            var scenes = TitleScenes;
            return scenes[(int)((uint)_settings.TitleShows % (uint)scenes.Count)];
        }
    }

    /// <summary>The title screen shows again: the next scene earned (when there is more than one).</summary>
    private void NextTitleScene()
    {
        if (TitleScenes.Count < 2) return;
        _settings.TitleShows++;
        _saveSettings?.Invoke(_settings);
    }

    /// <summary>A feat was done: what it unlocked shows in the settings and on the title.</summary>
    private void UnlocksChanged()
    {
        OnPropertyChanged(nameof(CardFrameChoices));
        OnPropertyChanged(nameof(CardFrameIndex));
        OnPropertyChanged(nameof(EarnedUnlocks));
    }

    private static string KindName(UnlockKind kind) => kind switch
    {
        UnlockKind.TitleScene => "Title scene",
        UnlockKind.CardFrame => "Card frame",
        _ => "Starting kit",
    };

    /// <summary>Knowledge → Unlocks: every unlock, earned or not, with what earns it and where it is used.</summary>
    public KnowledgeCategoryViewModel CreateUnlocksPage()
    {
        var earned = EarnedUnlocks;
        var rows = Unlocks.All.Select(u =>
        {
            var has = earned.Contains(u);
            var use = u.Kind switch
            {
                UnlockKind.TitleScene => "The title screen shows it in turn with the others earned.",
                UnlockKind.CardFrame => "Choose it in Settings → Display, Card frame; it is drawn round the character cards you save.",
                _ => "The creation screen offers it as a starting kit, for every class (the daily dungeon always uses the standard kit).",
            };
            var kit = StartingKits.Find(u.Kind == UnlockKind.StartingKit ? u.Key : null);
            return new KnowledgeRow(has ? "*" : "·", _cells.Color(has ? "Yellow" : "Slate"), u.Name, KindName(u.Kind),
                () => $"{u.Name} ({KindName(u.Kind).ToLowerInvariant()})\n\n{char.ToUpperInvariant(u.Description[0])}{u.Description[1..]}."
                      + (kit is null ? "" : $"\n\n{kit.Description} The gold left over is the same or a little more.")
                      + $"\n\n{u.Requirement(FeatDefs)}\n\n" + (has ? $"Unlocked. {use}" : "Not yet unlocked."));
        }).ToList();
        return new KnowledgeCategoryViewModel("Unlocks", $"{earned.Count} of {rows.Count} unlocked  ·  earned through feats, for every character", "", rows);
    }
}
