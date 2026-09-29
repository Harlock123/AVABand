using System.Globalization;
using System.Text;
using Angband.Core.Definitions;
using Angband.Core.Game;
using Angband.Core.Items;

namespace Angband.Core.Records;

/// <summary>
/// Object knowledge text (Angband obj-info.c): what an object, ego, artifact or rune does, as far
/// as the player knows. Used by Inspect and the knowledge browser.
/// </summary>
public static class ObjectInfo
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // --- Effects -------------------------------------------------------------------------------

    /// <summary>A plain-English summary of an effect string, e.g. "heals 20 hit points and cures blindness".</summary>
    public static string EffectText(GameData data, string effect)
    {
        var parts = new List<string>();
        var resists = new List<string>();
        var list = ItemEffects.Parse(effect).ToList();
        // Stat gains restore too, so "restores and raises your strength" is just "raises"; several
        // stats read as one phrase ("raises your strength and constitution").
        var gained = list.Where(e => e.Name == "gain_stat").Select(e => e.Arg(0)).ToHashSet();
        var stats = new Dictionary<string, List<string>>();
        for (var i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (e.Name == "random" && e.Int(0) > 0)
            {
                var n = Math.Min(e.Int(0), list.Count - i - 1);
                var options = list.Skip(i + 1).Take(n).Select(x => EffectText(data, x.ToEffectString())).Distinct().ToList();
                parts.Add("does one of these at random: " + Join(options, "or"));
                i += n;
                continue;
            }
            if (e.Name is "gain_stat" or "restore_stat" && e.Arg(0) != "all")
            {
                if (e.Name == "restore_stat" && gained.Contains(e.Arg(0))) continue;
                if (!stats.TryGetValue(e.Name, out var names))
                {
                    stats[e.Name] = names = [];
                    parts.Add("\0" + e.Name);
                }
                names.Add(Stat(e.Arg(0)));
                continue;
            }
            if (e.Name == "timed" && e.Arg(0).StartsWith("oppose_"))
            {
                resists.Add(ElementName(data, e.Arg(0)[7..]));
                continue;
            }
            parts.Add(Phrase(data, e));
        }
        if (resists.Count > 0) parts.Add("grants temporary resistance to " + Join(resists));
        for (var i = 0; i < parts.Count; i++)
            if (parts[i].StartsWith('\0'))
                parts[i] = (parts[i] == "\0gain_stat" ? "raises your " : "restores your ") + Join(stats[parts[i][1..]]);
        return Join(parts.Where(p => p.Length > 0).Distinct().ToList());
    }

    private static string ElementName(GameData data, string id) => id switch
    {
        "none" => "magic",
        _ => data.Element(id)?.Name ?? id,
    };

    private static string Elements(GameData data, string ids) =>
        Join(ids.Split('/').Select(x => ElementName(data, x)).ToList(), "or");

    private static string Phrase(GameData data, ItemEffect e) => e.Name switch
    {
        "heal" => e.Int(1) > 0 ? $"heals {e.Arg(0)} hit points or {e.Arg(1)}% of your hit points, whichever is more" : $"heals {e.Arg(0)} hit points",
        "cure" => e.Arg(0) switch
        {
            "afraid" => "removes fear", "poisoned" => "neutralizes poison", "blind" => "cures blindness",
            "confused" => "cures confusion", "cut" => "heals cuts", "stun" => "cures stunning", _ => $"cures {e.Arg(0)}",
        },
        "reduce" => $"reduces {TimedNoun(data, e.Arg(0))}",
        "timed" => e.Arg(0) switch
        {
            "fast" => "hastes you", "hero" => "makes you heroic", "berserk" => "puts you in a berserker rage",
            "blessed" => "blesses you", "prot_evil" => "protects you from evil", "telepathy" => "grants telepathy",
            "see_invisible" => "lets you see invisible things", "infravision" => "sharpens your infravision",
            "att_conf" => "makes your hands glow to confuse the next monster you hit",
            "terror" => "fills you with terror (fast but unable to fight)", "stoneskin" => "turns your skin to stone",
            "sprint" => "makes you sprint", "stealth" => "makes you stealthy", "scrambled" => "scrambles your stats",
            "paralyzed" => "paralyzes you", "confused" => "confuses you", "blind" => "blinds you",
            "poisoned" => "poisons you", "afraid" => "frightens you", "slow" => "slows you",
            "image" => "makes you hallucinate", "amnesia" => "makes you forget things",
            _ => $"inflicts {TimedNoun(data, e.Arg(0))}",
        } + (e.Int(1) >= 10000 ? "" : $" for {e.Arg(1)} turns"),
        "nourish" => "feeds you",
        "satisfy" => "fills your stomach",
        "set_food" => "leaves you hungry",
        "teleport" => !e.Arg(0).StartsWith('M') && e.Int(0) <= 10 ? "teleports you a short distance" : "teleports you",
        "teleport_level" => "takes you up or down a level",
        "deep_descent" => "takes you five levels down, after a short delay",
        "recall" => "recalls you to the town or to your deepest level",
        "light_area" => "lights up the area",
        "darkness" => "darkens the area and may blind you",
        "detect_monsters" => "detects monsters nearby",
        "detect_evil" => "detects evil monsters nearby",
        "detect_invisible" => "detects invisible monsters nearby",
        "detect_objects" => "senses objects nearby",
        "detection" => "detects monsters and objects nearby",
        "map_area" => "maps the area",
        "enlightenment" => "maps the whole level",
        "identify" => "reveals an unknown rune",
        "remove_curse" => "tries to remove a curse",
        "restore_mana" => "restores your mana",
        "restore_exp" => "restores your life levels",
        "gain_exp" => "gives you experience",
        "gain_stat" => $"raises your {Stat(e.Arg(0))}",
        "restore_stat" => e.Arg(0) == "all" ? "restores your stats" : $"restores your {Stat(e.Arg(0))}",
        "drain_stat" => e.Arg(0).StartsWith('!') ? $"drains a stat other than {Stat(e.Arg(0)[1..])}" : $"drains your {Stat(e.Arg(0))}",
        "enchant" => e.Arg(0) switch { "to_h" => "improves your weapon's accuracy", "to_d" => "improves your weapon's damage", _ => "improves your armour" },
        "recharge" => "recharges a wand or staff (it may explode)",
        "acquirement" => "creates an excellent object",
        "bolt" => $"fires {Article(e.Arg(0) == "none" ? "magic" : ElementName(data, e.Arg(0)))} bolt ({e.Arg(1)})",
        "beam" => $"fires a beam of {ElementName(data, e.Arg(0))} ({e.Arg(1)})",
        "ball" => $"fires a ball of {Elements(data, e.Arg(0))} ({e.Arg(1)})",
        "breath" => $"lets you breathe {Elements(data, e.Arg(0))} ({e.Arg(1)})",
        "monster_status" => e.Arg(0) switch
        {
            "slow" => "slows a monster", "confuse" => "confuses a monster", "sleep" => "puts a monster to sleep",
            "hold" => "holds a monster still", "scare" => "scares a monster", "stun" => "stuns a monster", _ => "affects a monster",
        },
        "project_los" => e.Arg(0) switch
        {
            "haste" => "hastes every monster in view", "slow" => "slows every monster in view",
            "confuse" => "confuses every monster in view", "sleep" => "puts every monster in view to sleep",
            _ => "affects every monster in view",
        },
        "dispel" => e.Arg(0) is "none" or "" ? $"damages every monster in view ({e.Arg(1)})" : $"damages every {e.Arg(0).ToLowerInvariant()} monster in view ({e.Arg(1)})",
        "teleport_other" => "teleports a monster away",
        "drain_life" => $"drains the life of a living monster ({e.Arg(0)})",
        "heal_monster" => "heals a monster",
        "haste_monster" => "hastes a monster",
        "clone_monster" => "clones a monster",
        "polymorph" => "changes a monster into another",
        "stone_to_mud" => "turns a wall to mud",
        "light_line" => "lights a beam that hurts light-sensitive monsters",
        "disarm" => "disarms traps in a line",
        "wonder" => "does something unpredictable",
        "aggravate" => "wakes up every monster nearby",
        "summon" => "summons monsters",
        "trap_creation" => "surrounds you with traps",
        "destruction" => "destroys the area around you",
        "earthquake" => "causes an earthquake",
        "mass_banishment" => "banishes the monsters around you",
        "banish" => "banishes every monster of a kind you choose",
        "probe" => "reveals the monsters in view",
        "destroy_doors" => "destroys the doors around you",
        "glyph" => "inscribes a glyph of warding",
        "curse_weapon" => "curses your weapon",
        "curse_armour" => "curses your armour",
        "shapechange" => $"changes you into {Article(e.Arg(0).Replace('_', ' '))}",
        "damage" => $"hurts you ({e.Arg(0)})",
        "fire_damage" => "burns",
        "stun" => "stuns you",
        _ => e.Name.Replace('_', ' '),
    };

    private static string TimedNoun(GameData data, string id) => id switch
    {
        "cut" => "cuts", "confused" => "confusion", "poisoned" => "poison", "stun" => "stunning", "blind" => "blindness",
        "afraid" => "fear", "slow" => "slowness", "paralyzed" => "paralysis", "image" => "hallucination",
        "free_act" => "free action", "oppose_conf" => "resistance to confusion", "bold" => "boldness", "amnesia" => "amnesia",
        "blackbreath" => "the Black Breath",
        _ => (data.Timed(id)?.Name ?? id).ToLowerInvariant(),
    };

    private static string Stat(string id) => id switch
    {
        "str" => "strength", "int" => "intelligence", "wis" => "wisdom", "dex" => "dexterity", "con" => "constitution", _ => id,
    };

    // --- Kinds, items, egos, artifacts ----------------------------------------------------------

    /// <summary>A kind's name as the player knows it ("Potion of Speed", or "Icky Green Potion" if unaware).</summary>
    public static string KindName(GameSession game, ObjectKindDef kind)
    {
        var serial = game.Objects.NextSerial;
        var sample = game.Objects.Create(kind);
        game.Objects.NextSerial = serial;
        return Capitalize(ItemNaming.Describe(sample, game.Knowledge, withArticle: false, full: false));
    }

    /// <summary>What the player knows about an object kind (the object knowledge browser).</summary>
    public static string DescribeKind(GameSession game, ObjectKindDef kind)
    {
        var serial = game.Objects.NextSerial;
        var sample = game.Objects.Create(kind);
        game.Objects.NextSerial = serial;
        return Describe(game, sample, sampleOnly: true);
    }

    /// <summary>What the player knows about a particular object (Inspect).</summary>
    public static string DescribeItem(GameSession game, Item item) => Describe(game, item, sampleOnly: false);

    private static string Describe(GameSession game, Item item, bool sampleOnly)
    {
        var k = game.Knowledge;
        var data = game.Data;
        var sb = new StringBuilder();
        sb.Append(Capitalize(sampleOnly ? ItemNaming.Describe(item, k, withArticle: false, full: false) : game.Describe(item))).Append("\n\n");

        if (!k.KnowsKind(item))
        {
            sb.Append(k.HasTried(item.Kind) ? "You have tried it, but don't know what it does.\n" : "You don't know what it does.\n");
            // What you have learned of it all the same — a curse that showed itself (Angband obj->known).
            if (!sampleOnly && KnownProperties(game, item) is { Count: > 0 } learned) sb.Append(string.Join(" ", learned)).Append('\n');
            return sb.ToString().TrimEnd();
        }

        var b = item.Base;
        var facts = new List<string>();
        if (b.IsWeapon || item.IsAmmo || b.Id == "digger") facts.Add($"It does {item.Damage} damage");
        if (item.IsThrowing)
            facts.Add($"it is made for throwing, doing {GameSession.ThrowMultiplier(item)} times its damage thrown");
        if (b.Slot == EquipSlot.Bow && item.Multiplier > 0) facts.Add($"it multiplies the damage of its missiles by {item.Multiplier}");
        if (item.Armour > 0) facts.Add($"it gives {item.Armour} armour");
        if (item.Kind.Fuel > 0) facts.Add($"it burns for up to {item.Kind.Fuel} turns");
        if (item.Kind.Charges is { } charges) facts.Add($"it is found with {charges} charges");
        if (item.Kind.Recharge is { } time && b.Id == "rod") facts.Add($"it recharges in {time} turns");
        if (facts.Count > 0) sb.Append(Capitalize(Join(facts))).Append(". ");
        sb.Append($"It weighs {(item.Weight / 10.0).ToString("0.#", Inv)} lb.\n");

        if (item.Kind.Effect is { Length: > 0 } effect)
        {
            var verb = b.Id switch
            {
                "potion" => "quaffed", "scroll" => "read", "food" or "mushroom" => "eaten", "wand" => "aimed",
                "staff" => "used", "rod" => "zapped", _ => "used",
            };
            sb.Append($"When {verb}, it {EffectText(data, effect)}.\n");
        }
        if (item.Activation is { Length: > 0 } act)
        {
            var recharge = item.Artifact?.Activation is not null ? item.Artifact.Recharge : item.Kind.Recharge;
            sb.Append($"When activated, it {(item.ActivationText ?? EffectText(data, act)).TrimEnd('.', ' ')}")
              .Append(recharge is null ? ".\n" : $" (it recharges in {recharge} turns).\n");
        }

        var runes = KnownProperties(game, item);
        if (runes.Count > 0) sb.Append(string.Join(" ", runes)).Append('\n');
        if (!sampleOnly && !k.IsFullyKnown(item)) sb.Append("You do not know all of its runes.\n");

        var description = item.Artifact?.Description is { Length: > 0 } ad && (sampleOnly || k.IsFullyKnown(item)) ? ad : item.Kind.Description;
        if (description.Length > 0) sb.Append('\n').Append(description).Append('\n');
        return sb.ToString().TrimEnd();
    }

    /// <summary>The object's known properties: modifiers, slays, brands, resistances, abilities and curses.</summary>
    private static List<string> KnownProperties(GameSession game, Item item)
    {
        var k = game.Knowledge;
        var data = game.Data;
        var lines = new List<string>();
        var mods = item.Modifiers.Where(kv => kv.Value != 0 && k.KnowsRune(RuneIds.Modifier(kv.Key)))
            .Select(kv => ModifierText(kv.Key, kv.Value)).ToList();
        if (mods.Count > 0) lines.Add($"It gives {Join(mods)}.");
        var slays = item.Slays.Where(s => k.KnowsRune(RuneIds.Slay(s.MonsterFlag))).Select(s => $"{s.Name} (x{s.Multiplier})").ToList();
        if (slays.Count > 0) lines.Add($"It slays {Join(slays)}.");
        var brands = item.Brands.Where(x => k.KnowsRune(RuneIds.Brand(x.Element))).Select(x => $"{x.Name} (x{x.Multiplier})").ToList();
        if (brands.Count > 0) lines.Add($"It is branded with {Join(brands)}.");
        var resists = item.Resists.Where(r => k.KnowsProperty(item, RuneIds.Resist(r))).Select(r => ProtectionName(data, r)).ToList();
        if (resists.Count > 0) lines.Add($"It provides {Join(resists)}.");
        var immune = item.Immunities.Where(r => k.KnowsRune(RuneIds.Resist(r))).Select(r => ElementName(data, r)).ToList();
        if (immune.Count > 0) lines.Add($"It makes you immune to {Join(immune)}.");
        var abilities = item.Flags.Where(f => ItemFlags.Abilities.Contains(f) && k.KnowsProperty(item, RuneIds.Flag(f))).Select(ItemFlags.Name).ToList();
        if (abilities.Count > 0) lines.Add($"It grants {Join(abilities)}.");
        // Angband describe_curses: what each known curse does, and whether it can be broken.
        foreach (var c in item.Curses.Where(c => k.KnowsRune(RuneIds.Curse(c))))
            lines.Add($"It {data.Curse(c)?.Description ?? c}{(item.CursePower(c) >= 100 ? "; this curse cannot be removed" : "")}.");
        return lines;
    }

    public static string DescribeEgo(GameSession game, EgoItemDef ego)
    {
        var data = game.Data;
        var sb = new StringBuilder($"{Capitalize(ego.Name)}\n\n");
        var bases = ego.AllBases(data).Select(b => data.ObjectBase(b) is { } ob ? ItemNaming.Plain(ob.Name, true).ToLowerInvariant() : b).Distinct().ToList();
        sb.Append($"Found on {Join(bases)}.\n");
        var facts = new List<string>();
        if (!ego.ToHit.Equals(Randomness.Dice.Zero)) facts.Add($"extra accuracy ({ego.ToHit})");
        if (!ego.ToDam.Equals(Randomness.Dice.Zero)) facts.Add($"extra damage ({ego.ToDam})");
        if (!ego.ToAc.Equals(Randomness.Dice.Zero)) facts.Add($"extra armour ({ego.ToAc})");
        facts.AddRange(ego.Modifiers.Select(kv => $"{kv.Value:+0;-0} {ModifierName(kv.Key)}"));
        facts.AddRange(ego.Rolls.Select(kv => kv.Key switch
        {
            "to_h" => $"extra accuracy ({kv.Value})",
            "to_d" => $"extra damage ({kv.Value})",
            "to_a" => $"extra armour ({kv.Value})",
            _ => $"{kv.Value} {ModifierName(kv.Key)}",
        }));
        if (ego.RandomPower is { } power)
            facts.Add(power switch
            {
                "sustain" => "a random sustain",
                "high_resist" => "a random high resistance",
                "base_resist" => "a random resistance",
                "resist_or_power" => "a random resistance or power",
                _ => "a random power",
            });
        facts.AddRange(ego.Slays.Select(s => $"slays {s.Name} (x{s.Multiplier})"));
        facts.AddRange(ego.Brands.Select(x => $"a {x.Name} brand (x{x.Multiplier})"));
        facts.AddRange(ego.Resists.Select(r => ProtectionName(data, r)));
        facts.AddRange(ego.Flags.Select(ItemFlags.Name));
        facts.AddRange(ego.Curses.Select(c => $"the curse of {data.Curse(c)?.Name ?? c}"));
        if (facts.Count > 0) sb.Append($"It gives {Join(facts)}.\n");
        return sb.ToString().TrimEnd();
    }

    public static string DescribeArtifact(GameSession game, ArtifactDef art)
    {
        // Describing must not use the artifact up: CreateArtifact marks it created.
        var alreadyCreated = game.Objects.CreatedArtifacts.Contains(art.Id);
        var serial = game.Objects.NextSerial;
        var item = game.Objects.CreateArtifact(art);
        game.Objects.NextSerial = serial;
        if (!alreadyCreated) game.Objects.CreatedArtifacts.Remove(art.Id);
        var sb = new StringBuilder();
        sb.Append(Capitalize(ItemNaming.Plain(item.Kind.Name, false))).Append(' ').Append(art.Name).Append("\n\n");
        var facts = new List<string>();
        if (item.Base.IsWeapon) facts.Add($"It does {item.Damage} damage ({item.ToHit:+0;-0},{item.ToDam:+0;-0})");
        if (item.Armour > 0 || item.ToAc != 0) facts.Add($"it gives [{item.Armour},{item.ToAc:+0;-0}] armour");
        if (facts.Count > 0) sb.Append(Capitalize(Join(facts))).Append(".\n");
        var mods = item.Modifiers.Where(kv => kv.Value != 0).Select(kv => ModifierText(kv.Key, kv.Value)).ToList();
        if (mods.Count > 0) sb.Append($"It gives {Join(mods)}.\n");
        if (item.Slays.Count > 0) sb.Append($"It slays {Join(item.Slays.Select(s => $"{s.Name} (x{s.Multiplier})").ToList())}.\n");
        if (item.Brands.Count > 0) sb.Append($"It is branded with {Join(item.Brands.Select(x => $"{x.Name} (x{x.Multiplier})").ToList())}.\n");
        if (item.Resists.Count > 0) sb.Append($"It provides {Join(item.Resists.Select(r => ProtectionName(game.Data, r)).ToList())}.\n");
        if (item.Immunities.Count > 0) sb.Append($"It makes you immune to {Join(item.Immunities.Select(r => ElementName(game.Data, r)).ToList())}.\n");
        var abilities = item.Flags.Where(ItemFlags.Abilities.Contains).Select(ItemFlags.Name).ToList();
        if (abilities.Count > 0) sb.Append($"It grants {Join(abilities)}.\n");
        if (art.Activation is not null) sb.Append($"When activated, it {(art.ActivationText ?? EffectText(game.Data, art.Activation)).TrimEnd('.', ' ')}").Append(art.Recharge is null ? ".\n" : $" (it recharges in {art.Recharge} turns).\n");
        if (art.Description.Length > 0) sb.Append('\n').Append(art.Description).Append('\n');
        return sb.ToString().TrimEnd();
    }

    // --- Shapes (Angband do_cmd_knowledge_shapechange, shape_lore) ----------------------------------

    /// <summary>What a shape does, as 4.2's shape knowledge says it, and which spells change you into it.</summary>
    public static string DescribeShape(GameData data, ShapeDef shape)
    {
        var sb = new StringBuilder();
        sb.Append(Capitalize(shape.Name)).Append("\n\n");
        sb.Append("Like all shapes, the equipment at the time of the shapechange sets the base attributes, including "
                  + "damage per blow, number of blows and resistances. While changed, items in your pack or on the floor "
                  + "(except for pickup or eating) are inaccessible. To switch back to your normal shape, cast a spell or "
                  + "use an item command other than eat (drop, for instance).\n\n");
        void Adds(IReadOnlyList<string> parts, string lead = "Adds")
        {
            if (parts.Count > 0) sb.Append(lead).Append(' ').Append(Join(parts)).Append(".\n");
        }
        var combat = new List<string>();
        if (shape.ToAc != 0) combat.Add($"{shape.ToAc:+0;-0} to AC");
        if (shape.ToHit != 0) combat.Add($"{shape.ToHit:+0;-0} to hit");
        if (shape.ToDam != 0) combat.Add($"{shape.ToDam:+0;-0} to damage");
        Adds(combat);
        Adds([.. shape.Skills.Where(kv => kv.Value != 0).Select(kv => $"{kv.Value:+0;-0} to {SkillName(kv.Key)}")]);
        var stats = new[] { "str", "int", "wis", "dex", "con" };
        Adds([.. shape.Modifiers.Where(kv => kv.Value != 0 && !stats.Contains(kv.Key)).Select(kv => $"{kv.Value:+0;-0} to {ModifierName(kv.Key)}")]);
        Adds([.. stats.Where(st => shape.Modifiers.GetValueOrDefault(st) != 0).Select(st => $"{shape.Modifiers[st]:+0;-0} to {ModifierName(st)}")]);
        var protections = new[] { "free_act", "see_invis", "blind", "conf", "fear", "stun", "hold_life" };
        Adds([.. shape.Resists.Where(r => !protections.Contains(r) && !r.StartsWith("sust_", StringComparison.Ordinal)).Select(r => ElementName(data, r))],
            "Makes you resistant to");
        Adds([.. shape.Resists.Where(protections.Contains).Select(r => ProtectionName(data, r))], "Gives");
        Adds([.. shape.Resists.Where(r => r.StartsWith("sust_", StringComparison.Ordinal)).Select(r => Stat(r[5..]))], "Sustains");
        Adds([.. shape.Immunities.Select(r => ElementName(data, r))], "Makes you immune to");
        Adds([.. shape.Flags.Where(f => f != "ROCK").Select(ItemFlags.Name)], "Grants");
        if (shape.Flags.Contains("ROCK")) sb.Append("Its body of stone neither bleeds nor heals its cuts.\n");
        if (shape.Effect.Length > 0) sb.Append($"Changing into the shape {EffectText(data, shape.Effect).TrimEnd('.', ' ')}.\n");
        if (shape.Blows.Count > 0) sb.Append($"Its blows: {Join([.. shape.Blows.Distinct()])}.\n");

        var triggers = data.Spells.Where(sp => sp.Effect.Split(';').Any(e => e.Trim() == $"shapechange:{shape.Id}")).ToList();
        if (triggers.Count > 0) sb.Append('\n');
        foreach (var spell in triggers)
            foreach (var cls in spell.Classes.Keys.Select(c => data.Classes.FirstOrDefault(x => x.Id == c)?.Name ?? c))
                sb.Append($"The {cls} spell, {spell.Name}, from {BookName(data, spell.Book)} triggers the shapechange.\n");
        return sb.ToString().TrimEnd();
    }

    private static string SkillName(string skill) => skill switch
    {
        "disarm_magic" => "disarming magical traps",
        "disarm" => "disarming", "device" => "magic devices", "save" => "saving throws", "melee" => "melee to hit",
        "bow" => "shooting to hit", "throw" => "throwing to hit", "dig" => "digging", "search" => "searching", _ => skill,
    };

    private static string BookName(GameData data, string book) =>
        data.Object(book) is { } kind ? "[" + ItemNaming.Plain(kind.Name, false).TrimStart('[').TrimEnd(']') + "]" : book;

    // --- Runes ------------------------------------------------------------------------------------

    /// <summary>Every rune that appears on some object, ego or artifact in the game data.</summary>
    public static IReadOnlyList<string> AllRunes(GameData data)
    {
        var runes = new SortedSet<string>(StringComparer.Ordinal) { RuneIds.ToHit, RuneIds.ToDam, RuneIds.ToAc };
        void Add(IEnumerable<string> mods, IEnumerable<SlayDef> slays, IEnumerable<BrandDef> brands, IEnumerable<string> resists,
            IEnumerable<string> flags, IEnumerable<string> curses)
        {
            foreach (var m in mods) runes.Add(RuneIds.Modifier(m));
            foreach (var s in slays) runes.Add(RuneIds.Slay(s.MonsterFlag));
            foreach (var b in brands) runes.Add(RuneIds.Brand(b.Element));
            foreach (var r in resists) runes.Add(RuneIds.Resist(r));
            foreach (var f in flags.Where(ItemFlags.Abilities.Contains)) runes.Add(RuneIds.Flag(f));
            foreach (var c in curses) runes.Add(RuneIds.Curse(c));
        }
        foreach (var k in data.Objects) Add(k.Modifiers.Keys.Concat(k.Rolls.Keys.Where(r => !r.StartsWith("to_"))), k.Slays, k.Brands, k.Resists, k.Flags, k.Curses);
        foreach (var e in data.Egos) Add(e.Modifiers.Keys.Concat(e.Rolls.Keys.Where(r => !r.StartsWith("to_"))), e.Slays, e.Brands, e.Resists, e.Flags, e.Curses);
        foreach (var a in data.Artifacts) Add(a.Modifiers.Keys, a.Slays, a.Brands, a.Resists.Concat(a.Immunities), a.Flags, a.Curses);
        foreach (var c in data.Curses) runes.Add(RuneIds.Curse(c.Id));
        return runes.ToList();
    }

    /// <summary>What a rune means.</summary>
    public static string DescribeRune(GameSession game, string rune)
    {
        var name = game.RuneName(rune);
        var (kind, arg) = rune.IndexOf(':') is var i and > 0 ? (rune[..i], rune[(i + 1)..]) : (rune, "");
        var text = kind switch
        {
            RuneIds.ToHit => "Makes attacks with (or while wearing) the object more accurate.",
            RuneIds.ToDam => "Makes attacks with (or while wearing) the object more damaging.",
            RuneIds.ToAc => "Magical protection added to the object's armour.",
            "mod" => $"Changes your {ModifierName(arg)} by the amount shown.",
            "slay" => $"Weapons with it do extra damage to {arg.ToLowerInvariant()} monsters.",
            "brand" => $"Weapons with it do extra {ElementName(game.Data, arg)} damage, unless the monster is immune.",
            "resist" => $"Wearing it gives {ProtectionName(game.Data, arg)}.",
            "flag" => $"Wearing it grants {ItemFlags.Name(arg)}.",
            "curse" => game.Data.Curse(arg)?.Description ?? "A curse.",
            _ => "",
        };
        return $"{Capitalize(name)}\n\n{text}";
    }

    /// <summary>"+2 strength"; shots are counted in tenths, as in Angband ("+1.0 shots").</summary>
    private static string ModifierText(string mod, int value) =>
        mod == "shots" ? string.Create(Inv, $"{value / 10.0:+0.0;-0.0} shots") : $"{value:+0;-0} {ModifierName(mod)}";

    /// <summary>
    /// A curse, for the Curses knowledge page (AVABand's own; 4.2.5 lists curses among the runes):
    /// what it does, what it carries (penalties, weaknesses, flags), how often it acts, what it can
    /// be found on and what it won't share an item with.
    /// </summary>
    public static string DescribeCurse(GameSession game, CurseDef curse)
    {
        var data = game.Data;
        var sb = new StringBuilder();
        sb.Append(Capitalize(curse.Name)).Append(" curse\n\n");
        sb.Append($"It {curse.Description}.\n");
        var penalties = new List<string>();
        if (curse.ToHit != 0) penalties.Add($"{curse.ToHit:+0;-0} to-hit");
        if (curse.ToDam != 0) penalties.Add($"{curse.ToDam:+0;-0} to-dam");
        if (curse.ToAc != 0) penalties.Add($"{curse.ToAc:+0;-0} armour");
        penalties.AddRange(curse.Modifiers.Where(kv => kv.Value != 0).Select(kv => $"{kv.Value:+0;-0} {ModifierName(kv.Key)}"));
        if (penalties.Count > 0) sb.Append($"It gives {Join(penalties)}.\n");
        if (curse.Vulnerabilities.Count > 0)
            sb.Append($"It makes you vulnerable to {Join([.. curse.Vulnerabilities.Select(v => ElementName(data, v))])}.\n");
        if (curse.Resists.Count > 0) sb.Append($"It gives {Join([.. curse.Resists.Select(r => ProtectionName(data, r))])}.\n");
        if (curse.Effect is { Length: > 0 } && RandomValue.Parse(curse.Time) is var time && time.Min > 0)
        {
            var most = time.Base + time.DiceCount * time.Dice;
            sb.Append(most > time.Min ? $"It acts every {time.Min} to {most} turns.\n" : $"It acts every {most} turns.\n");
        }
        var bases = curse.Bases.Select(b => data.ObjectBase(b) is { } ob ? ItemNaming.Plain(ob.Name, true) : b).Distinct().ToList();
        if (bases.Count > 0) sb.Append($"It can be found on {Join(bases)}.\n");
        var conflicts = curse.Conflicts.Select(c => data.Curse(c)?.Name ?? c).ToList();
        if (conflicts.Count > 0) sb.Append($"It never shares an item with the {Join(conflicts, "or")} curse{(conflicts.Count > 1 ? "s" : "")}.\n");
        var carried = game.Player.Inventory.Equipped.Concat(game.Player.Inventory.Pack)
            .Where(i => i.Curses.Contains(curse.Id)).Select(i => $"{game.Describe(i)} (strength {i.CursePower(curse.Id)})").ToList();
        if (carried.Count > 0) sb.Append($"\nYou carry it on {Join(carried)}.\n");
        return sb.ToString().TrimEnd();
    }

    private static string ModifierName(string mod) => mod switch
    {
        "str" => "strength", "int" => "intelligence", "wis" => "wisdom", "dex" => "dexterity", "con" => "constitution",
        "infra" => "infravision", "light" => "light", "blows" => "blows", "shots" => "shots", "tunnel" => "tunnelling",
        "search" => "searching", "might" => "might", "moves" => "moves", "dam_red" => "damage reduction", _ => mod,
    };

    private static string ProtectionName(GameData data, string id) => id switch
    {
        "free_act" => "free action", "see_invis" => "see invisible", "blind" => "protection from blindness",
        "conf" => "protection from confusion", "fear" => "protection from fear", "stun" => "protection from stunning",
        "hold_life" => "hold life", _ when id.StartsWith("sust_") => $"sustained {Stat(id[5..])}",
        _ => $"resistance to {ElementName(data, id)}",
    };

    private static string Article(string noun) => ("aeiou".Contains(char.ToLowerInvariant(noun[0])) ? "an " : "a ") + noun;

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string Join(IReadOnlyList<string> parts, string conjunction = "and") =>
        parts.Count <= 1 ? string.Join("", parts) : string.Join(", ", parts.Take(parts.Count - 1)) + $" {conjunction} " + parts[^1];
}
