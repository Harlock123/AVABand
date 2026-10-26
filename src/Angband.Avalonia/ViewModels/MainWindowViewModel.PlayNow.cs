using Angband.Core.Game;

namespace Angband.Avalonia.ViewModels;

// Play now (AVABand's own): straight into the town with a character chosen for you — one of a few
// friendly race and class pairs, a random name, the Classic options and the standard kit. It goes
// through the creation screen's own choices and Start, without showing the screen.
public sealed partial class MainWindowViewModel
{
    /// <summary>Picks Play now's character (tests choose it).</summary>
    public Func<IReadOnlyList<PlayNowCombo>, PlayNowCombo> PlayNowPick { get; set; } = combos => combos[Random.Shared.Next(combos.Count)];

    /// <summary>Starts a game with a character made for you, and says who you are.</summary>
    public void StartPlayNow()
    {
        var combo = PlayNowPick(PlayNow.Combos);
        var creation = new CharacterCreationViewModel(_data, last: null);
        creation.ChooseForPlayNow(combo);
        var started = false;
        creation.Started += spec =>
        {
            started = true;
            StartCharacter(spec);
        };
        creation.StartCommand.Execute(null);
        if (!started) return;
        var p = _game.Player;
        var who = $"{p.Race?.Name} {p.Class?.Name}";
        AddMessage($"You are {p.Name}, {PlayNow.Article(who)} {who}: {combo.Blurb}.");
        AddMessage("Next time, Create a new character (Ctrl+N) lets you choose everything yourself.");
        Refresh();
    }
}
