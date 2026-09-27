using Angband.Core.Items;
using Angband.Core.Monsters;
using Angband.Core.Geometry;

namespace Angband.Core.Game;

// Mimics and lurkers (Angband UNAWARE monsters): until found out they look like an object (creeping
// coins, potion/scroll/ring mimics) or like bare floor (lurkers, trappers). They lie in wait —
// no moving, no spells — and are revealed when the player bumps into them, they attack, or
// anything hurts them.
public sealed partial class GameSession
{
    /// <summary>Gives every camouflaged mimic without a disguise the object it poses as.</summary>
    public void DisguiseMonsters()
    {
        foreach (var m in Level.Monsters.All.Where(m => m.Camouflaged && m.MimicItem is null && m.Race.Mimics.Count > 0))
        {
            var kind = Data.Object(Rng.Pick(m.Race.Mimics));
            if (kind is null) continue;
            var disguise = Objects.Create(kind);
            if (disguise.IsGold) disguise.GoldValue = MakeLevelGold(Level.Depth).GoldValue;
            if (disguise.IsChest) disguise.ChestState = Objects.PickChestTraps(Rng, kind);
            m.MimicItem = disguise;
        }
    }

    /// <summary>The object the player would see at a square: the top of the pile, or a mimic's disguise.</summary>
    public Item? ObjectShownAt(Loc p)
    {
        var pile = Level.Objects.At(p);
        if (pile.Count > 0) return pile[0];
        return Level.Monsters.At(p) is { Camouflaged: true, MimicItem: { } disguise } ? disguise : null;
    }

    /// <summary>A mimic or lurker is found out (Angband become_aware).</summary>
    public void Reveal(Monster m)
    {
        if (!m.Camouflaged) return;
        m.Camouflaged = false;
        var disguise = m.MimicItem;
        m.MimicItem = null;
        Known.RememberObject(m.Position, Level.Objects.At(m.Position) is { Count: > 0 } pile ? pile[0] : null);
        m.IsVisible = MonsterVisible(m);
        Publish(new MessageEvent(disguise is not null
            ? $"The {Describe(disguise, withArticle: false)} was really a monster!"
            : $"You have found {Article(m.Race.Name)}!"));
        Publish(new MonsterRevealedEvent(m.Id, m.Race.Id));
        m.Sleep = 0;
        UpdateView();
    }

    /// <summary>A camouflaged monster's turn: strike if the player is in reach (giving itself away), else wait.</summary>
    private int LurkingTurn(Monster m)
    {
        if (m.Position.ChebyshevTo(Player.Position) == 1 && !m.Race.Has(Definitions.MonsterFlags.NeverBlow)
            && PassesWarding(m, Player.Position))
        {
            Reveal(m);
            MonsterMelee(m);
        }
        return Time.EnergyTable.MoveEnergy;
    }
}
