using Angband.Core.Game;
using Angband.Core.World;

namespace Angband.Avalonia.ViewModels;

/// <summary>
/// The map's animations: projections the player can see, and (with show_damage) the damage done
/// to monsters, queued as the game reports them and played by the map view after the turn.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private MapEffects? _effects;

    /// <summary>What the map is animating.</summary>
    public MapEffects Effects => _effects ??= new MapEffects(_cells.Color);

    private void OnProjection(ProjectionEvent e) =>
        Effects.AddProjection(e, p => _game.Level.InBounds(p) && _game.Level[p].Has(SquareFlags.View));

    private void OnMonsterDamaged(MonsterDamagedEvent e)
    {
        if (!OptionValue(OptionIds.ShowDamage) || !_game.Level.InBounds(e.Loc) || !_game.Level[e.Loc].Has(SquareFlags.View)) return;
        Effects.AddDamage(e.Loc, e.Damage);
    }
}
