using Angband.Core.Definitions;
using Angband.Core.Geometry;
using Angband.Core.Time;

namespace Angband.Core.Monsters;

/// <summary>A live monster on the current level.</summary>
public sealed class Monster : IActor
{
    public Monster(int id, MonsterRaceDef race, Loc position, int hp, int speed)
    {
        Id = id;
        Race = race;
        Position = position;
        MaxHp = Hp = Math.Max(1, hp);
        Speed = speed;
    }

    /// <summary>Slot id on the level (1-based); also its scheduler actor id.</summary>
    public int Id { get; }
    /// <summary>What it is now (a shapechanged monster's current form).</summary>
    public MonsterRaceDef Race { get; set; }
    /// <summary>Its own race while shapechanged (Angband original_race).</summary>
    public MonsterRaceDef? OriginalRace { get; set; }
    public Loc Position { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }

    public int ActorId => Id;
    /// <summary>Base speed; hasted monsters act at +10 (see <see cref="Fast"/>).</summary>
    public int Speed { get; set; }
    int IActor.Speed => Speed + (Fast > 0 ? 10 : 0) - (Slow > 0 ? 10 : 0);
    public int Energy { get; set; }
    public bool IsActive => Hp >= 0 && !IsRemoved;
    public bool IsRemoved { get; set; }

    // Monster timed effects (Angband mon_tmd): counted down every 10 game turns.
    public int Sleep { get; set; }
    public int Stun { get; set; }
    /// <summary>Disenchanted (Angband MON_TMD_DISEN): its spells fail half as often again.</summary>
    public int Disenchanted { get; set; }
    public int Confused { get; set; }
    public int Fear { get; set; }
    public int Held { get; set; }

    /// <summary>
    /// Angband MON_GROUP_BODYGUARD: the id of the leader this monster guards, while it lives (then
    /// the monster is an ordinary member of its group).
    /// </summary>
    public int? BodyguardOf { get; set; }

    /// <summary>
    /// What this monster has seen of the player's resistances and protections (Angband
    /// <c>known_pstate</c>): an element or protection id and its level as last observed.
    /// </summary>
    public Dictionary<string, int> KnownPlayer { get; set; } = [];
    /// <summary>Hasted (Angband mon_tmd FAST): +10 speed while it lasts.</summary>
    public int Fast { get; set; }
    /// <summary>Slowed (Angband mon_tmd SLOW): -10 speed while it lasts.</summary>
    public int Slow { get; set; }

    /// <summary>Not yet noticed: a mimic seen as <see cref="MimicItem"/>, or a lurker seen as floor.</summary>
    public bool Camouflaged { get; set; }
    /// <summary>The object a camouflaged mimic appears to be (not on the floor; it goes when the mimic is found).</summary>
    public Items.Item? MimicItem { get; set; }

    /// <summary>What it has stolen (dropped when it dies).</summary>
    public List<Items.Item> Carried { get; } = [];

    /// <summary>Its treasure has been rolled into <see cref="Carried"/> (for a thief to steal).</summary>
    public bool LootRolled { get; set; }

    public bool IsAsleep => Sleep > 0;
    public bool IsAfraid => Fear > 0;

    /// <summary>Counted in the monster memory as seen (each monster counts once).</summary>
    public bool EverSeen { get; set; }

    /// <summary>Whether the player can currently see it (refreshed with the view).</summary>
    public bool IsVisible { get; set; }

    /// <summary>Shown by a detection effect until the player's next action.</summary>
    public bool IsDetected { get; set; }

    /// <summary>Where it is ambling to while it has no idea where the player is.</summary>
    public Loc? WanderTarget { get; set; }
    /// <summary>Failed steps toward <see cref="WanderTarget"/>; a new target is picked when stuck.</summary>
    public int WanderStuck { get; set; }

    public override string ToString() => $"{Race.Id}#{Id}@{Position}";
}
