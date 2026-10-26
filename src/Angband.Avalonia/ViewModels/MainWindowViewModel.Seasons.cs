using Angband.Core.Game;

namespace Angband.Avalonia.ViewModels;

// Seasonal events (AVABand's own; Seasons in core): a character made while one runs — by the player's own
// date — is born in it and keeps it for life, unless Settings → Display → "Seasonal events" is off. While
// one runs the title screen wears it too: over Thangorodrim, the player's own picture of it
// (title-<id>.png or .jpg in the art folder), or else a painting of it (SeasonalTitleOverlay).
public sealed partial class MainWindowViewModel
{
    /// <summary>The clock <see cref="SeasonClock"/> starts with: the player's own (the tests fix it, out of season).</summary>
    public static Func<DateTime> DefaultSeasonClock { get; set; } = () => DateTime.Now;

    /// <summary>The date seasonal events go by.</summary>
    public Func<DateTime> SeasonClock { get; set; } = DefaultSeasonClock;

    /// <summary>Settings → Display, "Seasonal events": whether new characters are born in the event running (on by default).</summary>
    public bool SeasonalEvents
    {
        get => _settings.SeasonalEvents;
        set
        {
            if (value == _settings.SeasonalEvents) return;
            _settings.SeasonalEvents = value;
            _saveSettings?.Invoke(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SeasonToday));
            TitleSeasonChanged();
        }
    }

    /// <summary>The seasonal event running today, by the player's own date — null out of season, or with events off.</summary>
    public SeasonDef? SeasonToday => _settings.SeasonalEvents ? Seasons.At(DateOnly.FromDateTime(SeasonClock())) : null;

    /// <summary>What a character born in an event is told as the game begins.</summary>
    private string SeasonWelcome(SeasonDef season) =>
        $"You are born at {season.Name}: its creatures will walk the dungeon for you all your days. "
        + (_game.IsDaytime ? season.TownByDay : season.TownByNight);

    /// <summary>The event the title screen wears now: today's, behind Thangorodrim (not behind an unlocked scene).</summary>
    public SeasonDef? TitleSeason => TitleScene == "title" ? SeasonToday : null;

    /// <summary>The event to paint over the title picture (its id), when the player has no picture of their own for it.</summary>
    public string? TitleSeasonPainted => TitleSeason is { } season && PictureFor(SeasonPicture(season)) is null ? season.Id : null;

    /// <summary>The name of an event's own title picture, in the art folder: "title-halloween".</summary>
    private static string SeasonPicture(SeasonDef season) => "title-" + season.Id;

    private void TitleSeasonChanged()
    {
        OnPropertyChanged(nameof(TitleSeason));
        OnPropertyChanged(nameof(TitleSeasonPainted));
        OnPropertyChanged(nameof(TitleBackground));
    }
}
