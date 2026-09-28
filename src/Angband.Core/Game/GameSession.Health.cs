using Angband.Core.Monsters;

namespace Angband.Core.Game;

/// <summary>What the health bar shows (Angband prt_health): stars out of ten, in a colour; or unknown.</summary>
public sealed record HealthBar(string Name, int Stars, string Color, bool Unknown);

// The monster health bar (Angband ui-display.c prt_health, monster_health_attr): the monster last
// targeted, looked at, struck in melee or hit by a bolt or missile is tracked, and the sidebar shows
// how hurt it is — and whether it sleeps, is held, stunned, confused or afraid.
public sealed partial class GameSession
{
    private Monster? _healthTracked;

    /// <summary>Angband health_track: follow this monster's health in the bar.</summary>
    public void TrackHealth(Monster? monster) => _healthTracked = monster;

    /// <summary>The tracked monster, while it is still on the level (Angband stops tracking the dead).</summary>
    public Monster? HealthTracked =>
        _healthTracked is { IsRemoved: false } m && Level.Monsters.All.Contains(m) ? m : null;

    /// <summary>The bar for the tracked monster; null when nothing is tracked.</summary>
    public HealthBar? HealthBarFor()
    {
        if (HealthTracked is not { } m) return null;
        if (!m.IsVisible || m.Camouflaged || IsHallucinating || m.Hp < 0) return new HealthBar("", 0, "White", Unknown: true);
        var name = MonsterName(m);
        var pct = (int)(100L * m.Hp / Math.Max(1, m.MaxHp));
        var stars = pct < 10 ? 1 : pct < 90 ? pct / 10 + 1 : 10;
        var color = pct >= 100 ? "LightGreen" : pct >= 60 ? "Yellow" : pct >= 25 ? "Orange" : pct >= 10 ? "LightRed" : "Red";
        // Its state outranks its health, in Angband's order.
        if (m.Fear > 0) color = "Violet";
        if (ReferenceEquals(Commanded, m)) color = "LightPurple";
        if (m.Confused > 0) color = "Umber";
        if (m.Stun > 0) color = "LightBlue";
        if (m.Sleep > 0 || m.Held > 0) color = "Blue";
        return new HealthBar(name, stars, color, Unknown: false);
    }
}
