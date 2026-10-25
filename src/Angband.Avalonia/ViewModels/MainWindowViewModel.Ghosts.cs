using Angband.Core.Game;
using Angband.Core.Records;

namespace Angband.Avalonia.ViewModels;

// AVABand's ghosts of fallen characters in the UI: each game started or loaded is handed the graveyard's
// fallen (before its replay starts recording, so the replay has them and never needs the graveyard), and
// a ghost laid to rest is marked on its headstone, so it walks for no one again. Not for replays, the
// tutorial or the daily dungeon; a cheat's ghost-laying doesn't count.
public sealed partial class MainWindowViewModel
{
    /// <summary>Hands the game just attached the graveyard's fallen, and listens for a ghost laid to rest.</summary>
    private void UseGraveyardGhosts()
    {
        if (_records is null || _game.IsReplay || _game.IsTutorial || !_game.GhostsOn) return;
        _game.UseGraveyard(FallenGhosts.FromGraveyard(_records.LoadGraveyard()));
        _game.Events.Subscribe<GhostLaidToRestEvent>(OnGhostLaidToRest);
    }

    private void OnGhostLaidToRest(GhostLaidToRestEvent e)
    {
        if (_records is null || !Feats.Counts(_game)) return;
        var p = _game.Player;
        _records.LayToRest(e.Key, $"{p.Name} the {p.Race?.Name} {p.Class?.Name}".Replace("  ", " "));
    }
}
