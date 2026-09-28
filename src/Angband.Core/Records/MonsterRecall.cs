using Angband.Core.Randomness;
using System.Globalization;
using System.Text;
using Angband.Core.Combat;
using Angband.Core.Definitions;
using Angband.Core.Monsters;

namespace Angband.Core.Records;

/// <summary>
/// Monster recall (Angband lore_description): what the player knows about a race, written out as
/// prose. Everything but the description is earned — by watching, fighting and killing.
/// </summary>
public static class MonsterRecall
{
    /// <summary>Kills after which a race's toughness (armour, life) is known.</summary>
    public const int KillsForToughness = 3;
    /// <summary>Times a blow must be seen before its damage is known.</summary>
    public const int BlowsForDamage = 10;
    /// <summary>Angband lore_update: innate attacks or spells seen more than this many times have a known frequency.</summary>
    public const int CastsForFrequency = 50;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The recall text for <paramref name="race"/> given what <paramref name="lore"/> knows.</summary>
    public static string Describe(GameData data, MonsterRaceDef race, RaceLore? lore, int playerLevel, int playerKills = 0,
        RecallViewer? viewer = null) =>
        RecallMarkup.Strip(DescribeMarked(data, race, lore, playerLevel, playerKills, viewer));

    /// <summary>
    /// The recall with Angband's colours marked (see <see cref="RecallMarkup"/>): spells coloured by
    /// how dangerous they are to <paramref name="viewer"/>, resistances and weaknesses highlighted.
    /// </summary>
    public static string DescribeMarked(GameData data, MonsterRaceDef race, RaceLore? lore, int playerLevel, int playerKills = 0,
        RecallViewer? viewer = null)
    {
        lore ??= new RaceLore();
        viewer ??= RecallViewer.Unprotected;
        if (lore.Probed) lore = Everything(race, lore); // probing tells all (Angband lore_do_probe)
        var sb = new StringBuilder();
        var pronoun = race.Has(MonsterFlags.Male) ? "He" : race.Has(MonsterFlags.Female) ? "She" : "It";
        var name = race.IsUnique ? race.Name : $"The {race.Name}";

        sb.Append(name).Append(" (").Append(race.Glyph).Append(")\n\n");

        // Kills and deaths.
        if (race.IsUnique)
        {
            if (lore.Deaths > 0)
                sb.Append($"{pronoun} has slain {Count(lore.Deaths, "of your ancestors", "of your ancestors")}");
            if (lore.TotalKills > 0)
                sb.Append(lore.Deaths > 0 ? ", but you have avenged them! " : $"You have slain {race.Name}. ");
            else if (lore.Deaths > 0) sb.Append(", who remain unavenged. ");
            else sb.Append("No battles to the death are recalled. ");
        }
        else
        {
            if (lore.Deaths > 0) sb.Append($"{Count(lore.Deaths, "of your ancestors has", "of your ancestors have")} been killed by this creature. ");
            if (playerKills > 0) sb.Append($"You have killed {Count(playerKills, "of these creatures", "of these creatures")}. ");
            else if (lore.TotalKills > 0) sb.Append($"Your ancestors have killed {Count(lore.TotalKills, "of these creatures", "of these creatures")}. ");
            else if (lore.Deaths == 0) sb.Append("No battles to the death are recalled. ");
        }
        if (lore.Sights > 0 && lore.TotalKills == 0 && lore.Deaths == 0 && !race.IsUnique)
            sb.Append($"You have seen {Count(lore.Sights, "of them", "of them")}. ");
        sb.Append("\n\n");

        // Description (always known, as in Angband).
        if (race.Description.Length > 0) sb.Append(race.Description).Append("\n\n");

        // Depth and speed.
        var feet = race.Depth * data.Constants.FeetPerLevel;
        if (lore.TotalKills > 0 || lore.Sights >= 10 || race.IsUnique && lore.Sights > 0)
        {
            sb.Append(race.Depth == 0 ? $"{pronoun} lives in the town" : $"{pronoun} is normally found at depths of {feet} feet (level {race.Depth})");
            sb.Append(", and moves ").Append(Speed(race.Speed)).Append(". ");
        }

        // Experience value.
        if (lore.TotalKills > 0)
        {
            var (whole, fraction) = CombatMath.KillExperience(race.Experience, race.Depth, Math.Max(1, playerLevel));
            var value = whole + fraction / 65536.0;
            sb.Append($"A kill of this creature is worth {value.ToString("0.##", Inv)} point{(value == 1 ? "" : "s")} for a {Ordinal(playerLevel)} level character. ");
        }

        // Toughness.
        if (lore.TotalKills >= KillsForToughness)
            sb.Append($"{pronoun} has an armour rating of {race.Armour}, and a life rating of about {race.HitPoints}. ");
        sb.Append('\n');

        // Nature and resistances.
        var known = lore.FlagsKnown;
        var kinds = new List<string>();
        if (known.Contains("ANIMAL")) kinds.Add("natural creature");
        if (known.Contains("UNDEAD")) kinds.Add("undead");
        if (known.Contains("DEMON")) kinds.Add("demon");
        if (known.Contains("DRAGON")) kinds.Add("dragon");
        if (known.Contains("GIANT")) kinds.Add("giant");
        if (known.Contains("TROLL")) kinds.Add("troll");
        if (known.Contains("ORC")) kinds.Add("orc");
        var evil = known.Contains("EVIL");
        if (kinds.Count > 0 || evil)
        {
            var what = kinds.Count > 0 ? Join(kinds) : "creature";
            sb.Append($"{pronoun} is {(evil ? "an evil " : AOrAn(what) + " ")}{what}. ");
        }
        if (known.Contains("INVISIBLE")) sb.Append($"{pronoun} is invisible. ");
        if (known.Contains("MULTIPLY")) sb.Append($"{pronoun} breeds explosively. ");
        if (known.Contains("REGENERATE")) sb.Append($"{pronoun} regenerates quickly. ");
        if (known.Contains(MonsterFlags.PassWall)) sb.Append($"{pronoun} can pass through walls. ");
        if (known.Contains(MonsterFlags.KillWall)) sb.Append($"{pronoun} can bore through rock. ");
        if (known.Contains(MonsterFlags.NeverMove)) sb.Append($"{pronoun} does not deign to chase intruders. ");

        AppendAbilities(sb, data, race, lore, pronoun);
        sb.Append('\n');

        // Spells (Angband lore_append_spells).
        AppendSpells(sb, data, race, lore, pronoun, viewer);

        // Blows.
        var blows = race.Blows.Select((b, i) => (Blow: b, Index: i)).Where(x => lore.BlowSeen(x.Index) > 0).ToList();
        if (blows.Count > 0)
        {
            var parts = blows.Select(x =>
            {
                var method = data.BlowMethod(x.Blow.Method);
                var verb = method?.Message.Replace(" you", "").Replace("es you", "es") ?? x.Blow.Method;
                var effect = EffectPhrase(x.Blow.Effect);
                var damage = lore.BlowSeen(x.Index) >= BlowsForDamage || lore.TotalKills >= BlowsForDamage
                    ? $" ({x.Blow.Damage})" : "";
                return Verb(verb) + (effect.Length > 0 ? " to " + effect : "") + damage;
            }).ToList();
            sb.Append($"{pronoun} can {Join(parts)}.");
            if (blows.Count < race.Blows.Count) sb.Append(" You may not have seen all of its attacks.");
            sb.Append('\n');
        }
        else if (race.Blows.Count > 0 && !race.Has(MonsterFlags.NeverBlow))
            sb.Append($"Nothing is known about {pronoun switch { "He" => "his", "She" => "her", _ => "its" }} attack.\n");

        // Tidy: no trailing spaces, at most one blank line between paragraphs.
        var lines = sb.ToString().Split('\n').Select(l => l.TrimEnd());
        return System.Text.RegularExpressions.Regex.Replace(string.Join("\n", lines), "\n{3,}", "\n\n").Trim();
    }

    /// <summary>A copy of the lore with everything about the race known (after probing).</summary>
    private static RaceLore Everything(MonsterRaceDef race, RaceLore lore)
    {
        var all = new RaceLore
        {
            Sights = Math.Max(lore.Sights, 10), TotalKills = Math.Max(lore.TotalKills, KillsForToughness), Deaths = lore.Deaths,
            CastsSeen = Math.Max(lore.CastsSeen, CastsForFrequency), MaxItemsDropped = lore.MaxItemsDropped,
            CastsInnate = Math.Max(lore.CastsInnate, CastsForFrequency + 1), CastsSpell = Math.Max(lore.CastsSpell, CastsForFrequency + 1),
            MaxGoldDropped = lore.MaxGoldDropped,
        };
        foreach (var f in race.Flags) all.FlagsKnown.Add(f);
        // Angband rf_setall: every resistance and weakness counts as tested.
        foreach (var f in TestableFlags()) if (!race.Has(f)) all.FlagsLacking.Add(f);
        foreach (var s in race.Spells) all.SpellsSeen.Add(s);
        for (var i = 0; i < race.Blows.Count; i++)
            for (var n = 0; n < BlowsForDamage; n++) all.SeeBlow(i);
        return all;
    }

    /// <summary>Resistances (Angband RFT_RES), by element.</summary>
    private static IEnumerable<(string Flag, string Name)> ResistFlags(GameData data) =>
        data.Elements.Where(e => e.ImmunityFlag is not null).Select(e => (e.ImmunityFlag!, e.Name));

    /// <summary>
    /// Weaknesses: light and rock (Angband RFT_VULN — not being hurt by them counts as resisting),
    /// and fire and cold (RFT_VULN_I, weaknesses paired with a resistance).
    /// </summary>
    private static IEnumerable<(string Flag, string Name, bool Paired)> VulnerabilityFlags(GameData data) =>
        data.Elements.Where(e => e.VulnerabilityFlag is not null)
            .Select(e => (e.VulnerabilityFlag!, e.Name, e.ImmunityFlag is not null))
            .Append((MonsterFlags.HurtRock, "rock remover", false));

    private static IEnumerable<string> TestableFlags() =>
        ["IM_ACID", "IM_ELEC", "IM_FIRE", "IM_COLD", "IM_POIS", "HURT_LIGHT", MonsterFlags.HurtRock, "HURT_FIRE", "HURT_COLD"];

    /// <summary>
    /// Angband lore_append_abilities: what hurts it, what it resists — including light or rock that
    /// don't hurt it — what it has been found not to resist, and what it cannot be made to suffer.
    /// "It is hurt by fire, but resists cold, and does not resist acid or poison, and cannot be slept."
    /// </summary>
    private static void AppendAbilities(StringBuilder sb, GameData data, MonsterRaceDef race, RaceLore lore, string pronoun)
    {
        bool Known(string flag) => lore.FlagsKnown.Contains(flag) && race.Has(flag);
        bool Lacking(string flag) => lore.FlagsLacking.Contains(flag) && !race.Has(flag);
        var prev = false;

        var hurt = VulnerabilityFlags(data).Where(v => Known(v.Flag)).Select(v => v.Name).ToList();
        if (hurt.Count > 0)
        {
            sb.Append($"{pronoun} is hurt by ").Append(Clause(hurt, "and", RecallColors.Hurt));
            prev = true;
        }

        var resists = ResistFlags(data).Where(r => Known(r.Flag)).Select(r => r.Name)
            .Concat(VulnerabilityFlags(data).Where(v => !v.Paired && Lacking(v.Flag)).Select(v => v.Name)).ToList();
        if (resists.Count > 0)
        {
            sb.Append(prev ? ", but resists " : $"{pronoun} resists ").Append(Clause(resists, "and", RecallColors.Resist));
            prev = true;
        }

        // Found not to resist — unless it is known to be hurt by it, which says more.
        var hurtNames = VulnerabilityFlags(data).Where(v => v.Paired && Known(v.Flag)).Select(v => v.Name).ToHashSet();
        var notResisted = ResistFlags(data).Where(r => Lacking(r.Flag) && !hurtNames.Contains(r.Name)).Select(r => r.Name).ToList();
        if (notResisted.Count > 0)
        {
            sb.Append(prev ? ", and does not resist " : $"{pronoun} does not resist ").Append(Clause(notResisted, "or", RecallColors.Resist));
            prev = true;
        }

        var immune = new (string Flag, string Word)[]
        {
            (MonsterFlags.NoFear, "frightened"), (MonsterFlags.NoStun, "stunned"), (MonsterFlags.NoConf, "confused"),
            (MonsterFlags.NoSleep, "slept"), ("NO_HOLD", "held"),
        }.Where(x => Known(x.Flag)).Select(x => x.Word).ToList();
        if (immune.Count > 0)
        {
            sb.Append(prev ? ", and cannot be " : $"{pronoun} cannot be ").Append(Clause(immune, "or", RecallColors.Resist));
            prev = true;
        }
        if (prev) sb.Append(". ");
    }

    /// <summary>
    /// Angband lore_append_spells: innate attacks, then breaths, then spells, each with its damage
    /// (a breath's only once the monster's life is known) in a colour for how dangerous it is to
    /// the player; and how often it uses them — a guess at first, known after 50 have been seen.
    /// </summary>
    private static void AppendSpells(StringBuilder sb, GameData data, MonsterRaceDef race, RaceLore lore, string pronoun, RecallViewer viewer)
    {
        var seen = race.Spells.Where(lore.SpellsSeen.Contains).Select(data.MonsterSpell).OfType<MonsterSpellDef>().ToList();
        if (seen.Count == 0) return;
        var knowHp = lore.TotalKills > 0;
        var innate = seen.Where(s => s.Innate && s.Kind != MonsterSpellKind.Breath).ToList();
        var breaths = seen.Where(s => s.Kind == MonsterSpellKind.Breath).ToList();
        var spells = seen.Where(s => !s.Innate && s.Kind != MonsterSpellKind.Breath).ToList();

        if (innate.Count > 0) sb.Append($"{pronoun} may ").Append(SpellClause(data, race, innate, knowHp, viewer));
        if (breaths.Count > 0)
        {
            sb.Append(innate.Count > 0 ? ", and may " : $"{pronoun} may ")
                .Append(RecallMarkup.Color("breathe ", RecallColors.Verb))
                .Append(SpellClause(data, race, breaths, knowHp, viewer));
        }
        if (innate.Count + breaths.Count > 0) sb.Append(Frequency(race.InnateFrequency, lore.CastsInnate)).Append(". ");

        if (spells.Count > 0)
        {
            sb.Append($"{pronoun} may ").Append(RecallMarkup.Color("cast spells", RecallColors.Verb));
            if (lore.FlagsKnown.Contains(MonsterFlags.Smart)) sb.Append(" intelligently");
            sb.Append(" which ").Append(SpellClause(data, race, spells, knowHp, viewer))
                .Append(Frequency(race.SpellFrequency, lore.CastsSpell)).Append(". ");
        }
        sb.Append('\n');
    }

    /// <summary>"; 1 time in 4" once known, "; about 1 time in 5" as a guess, nothing if never seen.</summary>
    private static string Frequency(int oneIn, int casts)
    {
        if (oneIn <= 0 || casts == 0) return "";
        var green = RecallColors.Number;
        if (casts > CastsForFrequency)
            return $"; {RecallMarkup.Color("1", green)} time in {RecallMarkup.Color(oneIn.ToString(Inv), green)}";
        // Angband: the percentage chance rounded up to a multiple of ten.
        var percent = 100 / oneIn;
        var approx = Math.Max((percent + 9) / 10 * 10, 1);
        return $"; about {RecallMarkup.Color("1", green)} time in {RecallMarkup.Color((100 / approx).ToString(Inv), green)}";
    }

    private static string SpellClause(GameData data, MonsterRaceDef race, IReadOnlyList<MonsterSpellDef> spells, bool knowHp, RecallViewer viewer) =>
        Clause(spells.Select(s =>
        {
            var level = s.LoreFor(race.Power);
            var color = SpellColor(data, s, level, viewer);
            var damage = LoreDamage(s, race, knowHp);
            var text = level?.Text is { Length: > 0 } t ? t : SpellPhrase(data, s);
            return RecallMarkup.Color(damage > 0 ? $"{text} ({damage})" : text, color);
        }).ToList(), "or", null);

    /// <summary>
    /// Angband mon_spell_lore_damage: the most a spell can do — for a breath, what a monster of
    /// average life breathes (known only once one has been killed).
    /// </summary>
    public static int LoreDamage(MonsterSpellDef spell, MonsterRaceDef race, bool knowHp)
    {
        if (spell.Kind == MonsterSpellKind.Breath)
            return knowHp ? Math.Min(spell.BreathCap, race.HitPoints / Math.Max(1, spell.BreathDivisor)) : 0;
        if (spell.PowerScaled) return Math.Max(1, race.Power / 3 * 2) * 5;       // WOUND: (power/3*2)d5
        if (spell.Kind == MonsterSpellKind.Storm) return 70 + 3 * Math.Max(1, race.Power / 3) * 5; // three balls
        if (spell.DamageFormula is { } formula) return DiceFormula.Max(formula, spell.FormulaTerms, race.Power);
        if (spell.Damage.Max <= 0) return 0;
        return spell.Damage.Max + (spell.LevelDivisor > 0 ? race.Depth / spell.LevelDivisor : 0) + race.Depth * spell.LevelPercent / 100;
    }

    /// <summary>
    /// Angband spell_color: a spell's colour for the player as they are known to be — its normal
    /// colour, its "resisted" colour, or its "immune" colour.
    /// </summary>
    public static string SpellColor(GameData data, MonsterSpellDef spell, MonsterSpellLore? level, RecallViewer viewer)
    {
        if (level is null) return "White";
        var resisted = level.ResistColor ?? level.Color;
        var immune = level.ImmuneColor ?? resisted;
        if (level.ResistColor is null && level.ImmuneColor is null) return level.Color;

        if (level.Save)
        {
            if (viewer.SavingThrow >= 100) return level.ImmuneColor ?? resisted;
            if (spell.Kind == MonsterSpellKind.TeleportLevel) return viewer.Resist("nexus") > 0 ? resisted : level.Color;
            if (spell.Timed is not null && (spell.Kind == MonsterSpellKind.Status || level.ImmuneColor is not null))
                return spell.PreventedBy is { } p && viewer.Resist(p) > 0 ? resisted : level.Color;
            return level.Color;
        }

        if (spell.Kind is MonsterSpellKind.Bolt or MonsterSpellKind.Ball or MonsterSpellKind.Breath && spell.Element is { } element)
        {
            switch (element)
            {
                case "sound":
                    return viewer.Resist("sound") > 0 ? immune : viewer.Resist("stun") > 0 ? resisted : level.Color;
                case "nexus":
                    return viewer.Resist("nexus") > 0 ? immune : viewer.SavingThrow >= 100 ? resisted : level.Color;
                default:
                    var res = viewer.Resist(element);
                    return res >= 3 ? immune : res > 0 ? resisted : level.Color;
            }
        }
        return level.Color;
    }

    /// <summary>Angband lore_append_clause: "a", "a and b", "a, b, and c" — each part coloured.</summary>
    private static string Clause(IReadOnlyList<string> parts, string conjunction, string? color)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                if (parts.Count > 2) sb.Append(',');
                if (i == parts.Count - 1) sb.Append(' ').Append(conjunction);
                sb.Append(' ');
            }
            sb.Append(color is null ? parts[i] : RecallMarkup.Color(parts[i], color));
        }
        return sb.ToString();
    }

    private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    private static string Speed(int speed) => speed switch
    {
        0 => "at normal speed",
        > 20 => $"incredibly quickly (+{speed})",
        > 10 => $"very quickly (+{speed})",
        > 0 => $"quickly (+{speed})",
        < -10 => $"very slowly ({speed})",
        _ => $"slowly ({speed})",
    };

    private static string Ordinal(int n) =>
        n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    private static string AOrAn(string word) => "aeiou".Contains(char.ToLowerInvariant(word[0])) ? "an" : "a";

    private static string Join(IReadOnlyList<string> parts, string conjunction = "and") =>
        parts.Count <= 1 ? string.Join("", parts) : string.Join(", ", parts.Take(parts.Count - 1)) + $" {conjunction} " + parts[^1];

    /// <summary>"hits" → "hit", "touches" → "touch", "bites" → "bite".</summary>
    private static string Verb(string message)
    {
        var word = message.Split(' ')[0];
        if (word.EndsWith("ches") || word.EndsWith("shes") || word.EndsWith("sses")) word = word[..^2];
        else if (word.EndsWith('s')) word = word[..^1];
        return word + message[message.IndexOf(' ') is var i and >= 0 ? i.. : message.Length..];
    }

    private static string EffectPhrase(string effect) => effect switch
    {
        "hurt" => "hurt",
        "none" => "",
        "poison" => "poison",
        "acid" => "shoot acid",
        "elec" => "electrify",
        "fire" => "burn",
        "cold" => "freeze",
        "blind" => "blind",
        "confuse" or "hallu" => "confuse",
        "terrify" => "terrify",
        "paralyze" => "paralyze",
        "eat_gold" => "steal gold",
        "eat_item" => "steal items",
        "eat_food" => "eat your food",
        "eat_light" => "absorb light",
        "lose_str" => "reduce strength",
        "lose_int" => "reduce intelligence",
        "lose_wis" => "reduce wisdom",
        "lose_dex" => "reduce dexterity",
        "lose_con" => "reduce constitution",
        "lose_all" => "reduce all stats",
        "exp_10" or "exp_20" or "exp_40" or "exp_80" => "drain your life force",
        "disenchant" => "disenchant",
        "drain_charges" => "drain charges",
        "black_breath" => "inflict the Black Breath",
        _ => effect.Replace('_', ' '),
    };

    private static string SpellPhrase(GameData data, MonsterSpellDef s)
    {
        var element = s.Element is { } e ? data.Element(e)?.Name ?? e : null;
        return s.Kind switch
        {
            MonsterSpellKind.Breath => element ?? s.Id.Replace("BR_", "").ToLowerInvariant(),
            MonsterSpellKind.Bolt => element is null ? s.Id switch
            {
                "ARROW" => "fire arrows", "BOULDER" => "throw boulders", "SHOT" => "fire shots", "BOLT" => "fire bolts",
                "WHIP" => "lash with a whip", "MISSILE" => "cast magic missiles", _ => "cast bolts",
            } : $"cast {element} bolts",
            MonsterSpellKind.Ball => element is null ? "cast balls of power" : $"cast {element} balls",
            MonsterSpellKind.Wound => "cause wounds",
            MonsterSpellKind.Mind => "blast your mind",
            MonsterSpellKind.Status => s.Timed switch
            {
                "blind" => "blind", "confused" => "confuse", "afraid" => "terrify", "slow" => "slow",
                "paralyzed" => "paralyze", _ => "curse",
            },
            MonsterSpellKind.Heal => "heal itself",
            MonsterSpellKind.HealKin => "heal its kin",
            MonsterSpellKind.Haste => "haste itself",
            MonsterSpellKind.Blink => "blink",
            MonsterSpellKind.Teleport => "teleport",
            MonsterSpellKind.TeleportTo => "teleport you to it",
            MonsterSpellKind.TeleportAway => "teleport you away",
            MonsterSpellKind.TeleportLevel => "teleport you to another level",
            MonsterSpellKind.TeleportSelfTo => "teleport to you",
            MonsterSpellKind.Summon => s.Kin ? "summon its kin" : s.SummonFlag is { } f ? $"summon {f.ToLowerInvariant()}s"
                : s.SummonGlyphs is not null ? "summon allies" : "summon monsters",
            MonsterSpellKind.Shriek => "shriek for help",
            MonsterSpellKind.DrainMana => "drain mana",
            MonsterSpellKind.Forget => "cause amnesia",
            MonsterSpellKind.Traps => "create traps",
            MonsterSpellKind.Darkness => "create darkness",
            MonsterSpellKind.Web => "weave webs",
            MonsterSpellKind.Storm => "create storms",
            _ => s.Id.ToLowerInvariant(),
        };
    }
}

/// <summary>
/// What monster recall knows of the player (Angband known_state): resistance levels (by element or
/// protection id, as far as the player knows them) and the saving throw.
/// </summary>
public sealed record RecallViewer(Func<string, int> Resist, int SavingThrow)
{
    public static readonly RecallViewer Unprotected = new(_ => 0, 0);
}

/// <summary>Angband's recall colours (colour names as in its data files).</summary>
public static class RecallColors
{
    public const string Hurt = "Violet";
    public const string Resist = "Light Umber";
    public const string Verb = "Light Red";
    public const string Number = "Light Green";
}

/// <summary>
/// Coloured runs inside recall text: <c>\u0001colour\u0002text\u0003</c>. <see cref="Strip"/> gives
/// the plain text, <see cref="Parse"/> the runs for display.
/// </summary>
public static class RecallMarkup
{
    private const char Open = '\u0001', Mid = '\u0002', Close = '\u0003';

    public static string Color(string text, string color) => $"{Open}{color}{Mid}{text}{Close}";

    public static string Strip(string marked)
    {
        var sb = new StringBuilder(marked.Length);
        foreach (var (text, _) in Parse(marked)) sb.Append(text);
        return sb.ToString();
    }

    /// <summary>The text as runs, each with its colour name (null for the default colour).</summary>
    public static IEnumerable<(string Text, string? Color)> Parse(string marked)
    {
        var i = 0;
        while (i < marked.Length)
        {
            var open = marked.IndexOf(Open, i);
            if (open < 0) { yield return (marked[i..], null); yield break; }
            if (open > i) yield return (marked[i..open], null);
            var mid = marked.IndexOf(Mid, open);
            var close = marked.IndexOf(Close, mid);
            yield return (marked[(mid + 1)..close], marked[(open + 1)..mid]);
            i = close + 1;
        }
    }
}
