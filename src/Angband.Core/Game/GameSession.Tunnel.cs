using Angband.Core.Definitions;
using Angband.Core.Effects;
using Angband.Core.Geometry;
using Angband.Core.Items;
using Angband.Core.Magic;
using Angband.Core.Time;
using Angband.Core.World;

namespace Angband.Core.Game;

/// <summary>What a square is, for digging (Angband's DIGGING_* classes).</summary>
public enum DiggingKind { None, Rubble, Magma, Quartz, Granite, Door, Permanent }

// Tunnelling (Angband 4.2 do_cmd_tunnel / calc_digging_chances): digging skill from race, Strength
// and the best digger carried; each attempt succeeds with a chance out of 1600 that depends on
// the material; the command repeats until done, disturbed or 99 attempts.
public sealed partial class GameSession
{
    /// <summary>Angband adj_str_dig: digging from Strength (3 ... 18/220).</summary>
    private static readonly int[] AdjStrDig =
        [0, 0, 1, 2, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 10, 12, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100, 100, 100];

    /// <summary>Most attempts one tunnel command makes (Angband repeats digging 99 times).</summary>
    public const int TunnelRepeats = 99;

    /// <summary>
    /// Digging skill: race skill, Strength, and the best tool carried — the wielded weapon or any
    /// digger in the pack (4.2 uses the best digger automatically): +20 per point of tunnelling and
    /// a tenth of its weight — and, as 4.2's calc_bonuses adds every worn object's, +20 for each point
    /// of tunnelling on the rest of what you wear (a Ring of Digging, a Miner's Helm).
    /// </summary>
    public int DiggingSkill
    {
        get
        {
            var p = Player;
            var skill = (p.Race?.Skills.GetValueOrDefault("dig") ?? 0) + (p.Class?.Skills.GetValueOrDefault("dig") ?? 0)
                        + AdjStrDig[StatTables.Index(p.Stats.GetValueOrDefault("str", 15))];
            var tools = p.Inventory.Pack.Where(i => i.Base.Id == "digger").Append(p.Inventory.Weapon).OfType<Item>();
            var best = tools.Select(ToolBonus).DefaultIfEmpty(0).Max();
            var worn = p.Inventory.Equipped.Where(i => i != p.Inventory.Weapon).Sum(i => i.Modifier(ItemModifiers.Tunnel)) * 20;
            return Math.Max(0, skill + best + worn + RaceDigging());
        }
    }

    private static int ToolBonus(Item tool) => tool.Modifier(ItemModifiers.Tunnel) * 20 + tool.Weight / 10;

    /// <summary>The tool that gives <see cref="DiggingSkill"/> its bonus (for messages and the character sheet).</summary>
    public Item? BestDigger =>
        Player.Inventory.Pack.Where(i => i.Base.Id == "digger").Append(Player.Inventory.Weapon).OfType<Item>()
            .OrderByDescending(ToolBonus).ThenBy(i => i.Serial).FirstOrDefault();

    public DiggingKind DiggingKindAt(Loc p)
    {
        var f = Level.FeatureAt(p);
        if (f.Has(TerrainFlags.Permanent) || !Level.InBoundsFully(p)) return DiggingKind.Permanent;
        if (f.Has(TerrainFlags.Rubble)) return DiggingKind.Rubble;
        if (f.Has(TerrainFlags.Secret)) return DiggingKind.Granite;
        if (f.Has(TerrainFlags.DoorClosed)) return DiggingKind.Door;
        if (f.Has(TerrainFlags.Magma)) return DiggingKind.Magma;
        if (f.Has(TerrainFlags.Quartz)) return DiggingKind.Quartz;
        if (f.Has(TerrainFlags.Granite) || f.Has(TerrainFlags.Rock)) return DiggingKind.Granite;
        return DiggingKind.None;
    }

    /// <summary>Chance out of 1600 that one attempt succeeds (Angband calc_digging_chances).</summary>
    public int DiggingChance(DiggingKind kind)
    {
        var skill = DiggingSkill;
        var chance = kind switch
        {
            DiggingKind.Rubble => SmashesRubble ? 1600 : skill * 8,
            DiggingKind.Magma => (skill - 10) * 4,
            DiggingKind.Quartz => (skill - 20) * 2,
            DiggingKind.Granite => skill - 40,
            DiggingKind.Door => (skill * 4 - 119) / 3,
            _ => 0,
        };
        return Math.Max(0, chance);
    }

    /// <summary>Tunnel in a direction, repeating until through, disturbed or out of attempts.</summary>
    private bool Tunnel(Direction dir, int attempts)
    {
        if (Player.Timed.Has(TimedIds.Confused) && Rng.RandInt0(100) < 40)
        {
            dir = Rng.Pick(DirectionExtensions.Compass);
            Publish(new MessageEvent("You are confused."));
        }
        var target = Player.Position.Step(dir);
        if (!Level.InBounds(target) || dir == Direction.Here) return false;

        if (Level.Monsters.At(target) is { Camouflaged: true } hidden)
        {
            Reveal(hidden);
            SpendAndAdvance(EnergyTable.MoveEnergy);
            return true;
        }
        if (Level.Monsters.At(target) is { } monster)
        {
            Publish(new MessageEvent("There is a monster in the way!"));
            SpendAndAdvance(PlayerMelee(monster));
            return true;
        }

        var kind = DiggingKindAt(target);
        var feature = Level.FeatureAt(target);
        switch (kind)
        {
            case DiggingKind.None:
                Publish(new MessageEvent("You see nothing there to tunnel."));
                return false;
            case DiggingKind.Permanent:
                Publish(new MessageEvent("This seems to be permanent rock."));
                return false;
        }

        var chance = DiggingChance(kind);
        var name = Level[target].Has(SquareFlags.Seen) || Known.IsKnown(target) ? feature.Name : "the wall";
        if (chance == 0)
        {
            // Hopeless: one turn of chipping, then give up (Angband doesn't repeat it).
            Publish(new MessageEvent($"You chip away futilely at the {name}."));
            Publish(new DigEvent(target, Done: false));
            SpendAndAdvance(EnergyTable.MoveEnergy);
            return true;
        }

        Publish(new MessageEvent(kind == DiggingKind.Rubble ? "You dig in the rubble." : $"You tunnel into the {name}."));
        var hp = Player.Hp;
        var seen = VisibleMonsters();
        Publish(new DigEvent(target, Done: false));
        for (var attempt = 0; attempt < attempts && !Player.IsDead; attempt++)
        {
            var done = Rng.RandInt0(1600) < chance;
            if (done) FinishTunnel(target, kind);
            SpendAndAdvance(EnergyTable.MoveEnergy);
            if (done) return true;

            // Disturbances stop the digging, as in Angband.
            if (Player.Hp < hp || MonstersDisturb(seen)) break;
            seen = VisibleMonsters();
            if (Player.Position.Step(dir) != target || DiggingKindAt(target) != kind) break;
        }
        if (!Player.IsDead)
            Publish(new MessageEvent(kind == DiggingKind.Rubble
                ? "You dig in the rubble with little effect."
                : $"You fail to make any progress into the {name}."));
        return true;
    }

    private void FinishTunnel(Loc p, DiggingKind kind)
    {
        ref var sq = ref Level[p];
        var feature = Level.FeatureAt(p);
        var gold = feature.Has(TerrainFlags.Gold);
        switch (kind)
        {
            case DiggingKind.Rubble:
                sq.Feature = Data.Terrain.Ids.Floor;
                Publish(new MessageEvent("You have removed the rubble."));
                // Angband: rubble sometimes hides an object.
                if (Rng.OneIn(10) && Objects.Make(Rng, Level.Depth) is { } found)
                {
                    Level.Objects.Add(p, found);
                    Publish(new MessageEvent("You have found something!"));
                }
                break;
            case DiggingKind.Door:
                sq.Feature = Data.Terrain.Ids.BrokenDoor;
                sq.LockPower = 0;
                Publish(new MessageEvent("You have broken through the door."));
                break;
            default:
                if (feature.Has(TerrainFlags.Secret))
                {
                    RevealSecretDoor(p);
                    Publish(new MessageEvent("You have found a secret door."));
                    break;
                }
                sq.Feature = Data.Terrain.Ids.Floor;
                sq.Flags &= ~SquareFlags.AnyWallMarker;
                Publish(new MessageEvent("You have finished the tunnel."));
                if (gold)
                {
                    var veinGold = MakeLevelGold(Level.Depth);
                    VeinGold(veinGold);
                    Level.Objects.Add(p, veinGold);
                    Publish(new MessageEvent("You have found something!"));
                }
                break;
        }
        Publish(new DigEvent(p, Done: true));
        Known.Remember(Level, p);
        UpdateView();
    }
}
