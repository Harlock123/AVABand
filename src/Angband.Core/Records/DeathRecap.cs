using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;

namespace Angband.Core.Records;

/// <summary>The tip a death recap gives, chosen by what happened (see <see cref="DeathRecap.ChooseTip"/>).</summary>
public enum DeathTip
{
    /// <summary>Starved to death.</summary>
    Starvation,
    /// <summary>Killed by poison or bleeding, after the fight.</summary>
    PoisonOrBleeding,
    /// <summary>Died well below the character level's depth.</summary>
    TooDeep,
    /// <summary>Died with a quick escape (Phase Door, Teleportation...) in the pack.</summary>
    UnusedEscape,
    /// <summary>Died with a healing potion (or staff or rod) in the pack.</summary>
    UnusedHealing,
    /// <summary>Killed by a breath.</summary>
    Breath,
    /// <summary>Killed by a unique.</summary>
    Unique,
    /// <summary>Stayed in the fight with a third of the hit points or less.</summary>
    FoughtOnLow,
    /// <summary>Killed in the town.</summary>
    Town,
    /// <summary>None of the others.</summary>
    General,
}

/// <summary>
/// AVABand's own death recap, shown on the game-over window: the last few hurts that led to a death,
/// the escapes and healing still in the pack (only of kinds the player knows, so no flavour is
/// spoiled), and one plain tip chosen by what happened.
/// </summary>
public sealed class DeathRecap
{
    /// <summary>How many of the last hurts a recap shows.</summary>
    public const int HurtsShown = 5;

    /// <summary>The last hurts taken, oldest first; the last of them killed.</summary>
    public required IReadOnlyList<HurtRecord> Hurts { get; init; }

    /// <summary>The escapes in the pack at death, as the pack names them ("3 Scrolls of Phase Door").</summary>
    public required IReadOnlyList<string> Escapes { get; init; }

    /// <summary>The healing in the pack at death, as the pack names it.</summary>
    public required IReadOnlyList<string> Healing { get; init; }

    /// <summary>Which tip was chosen.</summary>
    public required DeathTip TipKind { get; init; }

    /// <summary>The tip, in a sentence or two.</summary>
    public required string Tip { get; init; }

    /// <summary>The recap of a dead character; null for the living, and for winners (who retire rather than die).</summary>
    public static DeathRecap? For(GameSession game)
    {
        var p = game.Player;
        if (!p.IsDead || p.IsWinner) return null;
        var hurts = game.RecentHurts.TakeLast(HurtsShown).ToList();
        var escapes = UsableKnown(game, IsEscape).OrderBy(i => EscapeRank(i.Kind)).ToList();
        var healing = UsableKnown(game, IsHealing).ToList();
        var (kind, tip) = ChooseTip(game, hurts, escapes.FirstOrDefault(i => EscapeRank(i.Kind) <= 1), healing.FirstOrDefault());
        return new DeathRecap
        {
            Hurts = hurts,
            Escapes = [.. escapes.Select(i => game.Describe(i))],
            Healing = [.. healing.Select(i => game.Describe(i))],
            TipKind = kind,
            Tip = tip,
        };
    }

    /// <summary>One hurt as a line: "a Cave orc: 12 damage, 3 hit points left".</summary>
    public static string HurtLine(HurtRecord h)
    {
        var what = h.Breath && h.Element is not null ? $"{h.Killer}'s breath" : h.Killer;
        var times = h.Count > 1 ? $" ({h.Count} times)" : "";
        var left = h.HpAfter < 0 ? "dead" : $"{h.HpAfter} hit point{(h.HpAfter == 1 ? "" : "s")} left";
        return $"{Capitalize(what)}: {h.Damage} damage{times}, {left}";
    }

    /// <summary>The recap as plain lines (for the character dump).</summary>
    public IEnumerable<string> Lines()
    {
        foreach (var h in Hurts) yield return HurtLine(h);
        if (Escapes.Count > 0) yield return "Escapes unused: " + string.Join(", ", Escapes);
        if (Healing.Count > 0) yield return "Healing unused: " + string.Join(", ", Healing);
        yield return "Tip: " + Tip;
    }

    /// <summary>
    /// Whether a kind gets you away: teleporting (yourself, the monster, or off the level), Deep Descent
    /// or Word of Recall — by the effects it has, so AVABand's own items count too.
    /// </summary>
    public static bool IsEscape(ObjectKindDef kind) => EscapeRank(kind) < int.MaxValue;

    /// <summary>Whether a kind heals a useful amount (20 hit points or more, as Cure Light Wounds does).</summary>
    public static bool IsHealing(ObjectKindDef kind) =>
        kind.Effect is { } effect && ItemEffects.Parse(effect).Any(e => e.Name == "heal" && e.Int(0) >= 20);

    /// <summary>How quick an escape is: 0 a teleport, 1 sending the monster away, 2 off the level, 3 a delayed one; else not one.</summary>
    private static int EscapeRank(ObjectKindDef kind)
    {
        if (kind.Effect is not { } effect) return int.MaxValue;
        var rank = int.MaxValue;
        foreach (var e in ItemEffects.Parse(effect))
            rank = Math.Min(rank, e.Name switch
            {
                "teleport" => 0,
                "teleport_other" => 1,
                "teleport_level" => 2,
                "deep_descent" or "recall" => 3,
                _ => int.MaxValue,
            });
        return rank;
    }

    /// <summary>The pack's items of a sort, of kinds the player knows, that could be used now (charged, not recharging).</summary>
    private static IEnumerable<Item> UsableKnown(GameSession game, Func<ObjectKindDef, bool> sort) =>
        game.Player.Inventory.Pack.Where(i => sort(i.Kind) && game.Knowledge.KnowsKind(i)
            && (i.Base.Id is not ("staff" or "wand") || i.Charges > 0)
            && (!i.IsRod || i.NumberCharging < i.Number));

    /// <summary>
    /// The tip, first match winning: starvation; poison or bleeding; too deep for the character level
    /// (more than four levels below it, from 250 ft); a quick escape unused; healing unused; a breath;
    /// a unique; fighting on at a third of the hit points or less; the town; and otherwise a general one.
    /// </summary>
    public static (DeathTip Kind, string Text) ChooseTip(GameSession game, IReadOnlyList<HurtRecord> hurts, Item? escape, Item? healing)
    {
        var p = game.Player;
        var killedBy = p.KilledBy ?? hurts.LastOrDefault()?.Killer ?? "";
        var last = hurts.LastOrDefault();
        var race = last?.RaceId is { } id ? game.MonsterRace(id) : null;

        if (killedBy == "starvation")
            return (DeathTip.Starvation, "Hunger killed you. Carry a few rations, and eat when the status bar says Hungry: Weak and Faint come quickly after.");
        if (killedBy is "poison" or "a fatal wound")
            return (DeathTip.PoisonOrBleeding, "Poison and deep cuts go on hurting after the fight. A Potion of Cure Light Wounds stops bleeding, and Cure Serious Wounds cures poison too.");
        if (p.Depth >= 5 && p.Depth > p.Level + 4)
            return (DeathTip.TooDeep, $"You were at {p.Depth * game.Data.Constants.FeetPerLevel} ft at character level {p.Level}. 50 ft for each character level is a rough guide; deeper than that, monsters outpace you fast.");
        if (escape is not null)
            return (DeathTip.UnusedEscape, $"You died with {game.Describe(escape)} unused. An escape is for when a fight turns, not after: use it while you still have hit points to spare.");
        if (healing is not null)
            return (DeathTip.UnusedHealing, $"You died with {game.Describe(healing)} unused. Heal at half your hit points, not at the end: a monster can strike twice before you act.");
        if (last is { Breath: true, Element: { } element })
        {
            var el = game.Data.Element(element);
            var name = el?.Name ?? element;
            var resisted = p.Resists.GetValueOrDefault(el?.ResistedAs ?? element) > 0;
            return (DeathTip.Breath, resisted
                ? $"Even resisted, a breath of {name} from a healthy monster hurts. Keep your hit points well above what it can breathe, and wound it from afar first."
                : $"A breath of {name} killed you. Breaths are strongest from a healthy monster; resistance to {name} takes much of the sting out of it.");
        }
        if (race is { IsUnique: true } || game.Data.AllMonsters.Any(m => m.IsUnique && m.Name == killedBy))
        {
            var name = race?.Name ?? killedBy;
            return (DeathTip.Unique, $"{name} is a unique, far tougher than its kin. Look at it (x or l) to read what you know of it, and leave it for later if it's beyond you.");
        }
        if (last is not null && hurts.Take(hurts.Count - 1).Any(h => h.HpAfter >= 0 && h.HpAfter * 3 <= h.MaxHp && h.Turn < last.Turn))
            return (DeathTip.FoughtOnLow, "You stayed in the fight with a third of your hit points or less. That's the moment to heal, or to leave by stairs or scroll.");
        if (p.Depth == 0)
            return (DeathTip.Town, "Even the town has teeth, at night most of all. A few Flasks of oil to throw make short work of what lurks there early on.");
        return (DeathTip.General, "Look at a monster (x or l) before you fight it: its recall says how hard it hits. Leave the ones you can't beat for later.");
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
