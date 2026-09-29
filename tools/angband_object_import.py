#!/usr/bin/env python3
"""Import object kinds from Angband 4.2's object.txt into AVABand's objects.json.

Kinds already present (same base and name) are kept. Weapons, armour and jewellery are converted
numerically; potions, scrolls, food, mushrooms and magic devices have their effect lines translated
into AVABand effect strings, and kinds whose effects can't be expressed are skipped (and listed).
Angband's data is available under the GPL v2 or the Angband licence.

Usage:
    angband_object_import.py <object.txt> <data/objects.json> [--report]
"""
import argparse, json, re, sys, unicodedata

TYPES = {
    "sword": "sword", "hafted": "hafted", "polearm": "polearm", "arrow": "arrow", "bolt": "bolt", "shot": "shot",
    "boots": "boots", "helm": "helm", "crown": "crown", "soft armor": "soft_armour", "hard armor": "hard_armour",
    "dragon armor": "dragon_armour", "cloak": "cloak", "gloves": "gloves", "shield": "shield", "ring": "ring",
    # artifact.txt spells them the British way.
    "soft armour": "soft_armour", "hard armour": "hard_armour", "dragon armour": "dragon_armour",
    "amulet": "amulet", "potion": "potion", "scroll": "scroll", "food": "food", "mushroom": "mushroom",
    "wand": "wand", "staff": "staff", "rod": "rod", "bow": "bow", "digger": "digger", "chest": "chest",
}
PREFIX = {"ring": "ring_of_", "amulet": "amulet_of_", "wand": "wand_of_", "staff": "staff_of_", "rod": "rod_of_",
          "mushroom": "mushroom_of_", "potion": "potion_of_", "scroll": "scroll_of_"}
MODIFIERS = {"STR": "str", "INT": "int", "WIS": "wis", "DEX": "dex", "CON": "con", "SPEED": "speed",
             "STEALTH": "stealth", "INFRA": "infra", "LIGHT": "light", "SEARCH": "search", "BLOWS": "blows",
             "SHOTS": "shots", "TUNNEL": "tunnel", "MIGHT": "might", "DAM_RED": "dam_red", "MOVES": "moves"}
RESISTS = {"RES_ACID": "acid", "RES_ELEC": "elec", "RES_FIRE": "fire", "RES_COLD": "cold", "RES_POIS": "pois",
           "RES_LIGHT": "light", "RES_DARK": "dark", "RES_SOUND": "sound", "RES_SHARD": "shards",
           "RES_NEXUS": "nexus", "RES_NETHER": "nether", "RES_CHAOS": "chaos", "RES_DISEN": "disen"}
FLAG_RESISTS = {"FREE_ACT": "free_act", "SEE_INVIS": "see_invis", "PROT_FEAR": "fear", "PROT_BLIND": "blind",
                "PROT_CONF": "conf", "SUST_STR": "sust_str", "SUST_INT": "sust_int", "SUST_WIS": "sust_wis",
                "SUST_DEX": "sust_dex", "SUST_CON": "sust_con"}
FLAG_ABILITIES = {"REGEN": "REGEN", "SLOW_DIGEST": "SLOW_DIGEST", "HOLD_LIFE": "HOLD_LIFE", "TELEPATHY": "TELEPATHY",
                  "FEATHER": "FEATHER", "AFRAID": "AFRAID", "IMPAIR_HP": "IMPAIR_HP", "AGGRAVATE": "AGGRAVATE",
                  "DRAIN_EXP": "DRAIN_EXP", "IMPACT": "IMPACT", "TRAP_IMMUNE": "TRAP_IMMUNE",
                  "THROWING": "THROWING"}
# Every 4.2.5 curse (curse.txt) by AVABand's id: the slug of its name, but for two older ids.
_CURSE_NAMES = ["vulnerability", "teleportation", "dullness", "sickliness", "enveloping", "irritation", "weakness",
                "clumsiness", "slowness", "annoyance", "poison", "siren", "hallucination", "paralysis", "dragon summon",
                "demon summon", "undead summon", "impair mana recovery", "impair hitpoint recovery", "cowardice",
                "stone", "anti-teleportation", "treacherous weapon", "burning up", "chilled to the bone", "steelskin",
                "air swing"]
CURSES = {n: {"burning up": "burning_up", "chilled to the bone": "chilled"}.get(n, re.sub(r"[^a-z0-9]+", "_", n).strip("_"))
          for n in _CURSE_NAMES}
TIMED = {"FAST": "fast", "BLESSED": "blessed", "HERO": "hero", "SHERO": "berserk", "OPP_FIRE": "oppose_fire",
         "OPP_COLD": "oppose_cold", "OPP_POIS": "oppose_pois", "OPP_ACID": "oppose_acid", "OPP_ELEC": "oppose_elec",
         "PROTEVIL": "prot_evil", "SINVIS": "see_invisible", "SINFRA": "infravision", "TELEPATHY": "telepathy",
         "CONFUSED": "confused", "PARALYZED": "paralyzed", "BLIND": "blind", "POISONED": "poisoned", "SLOW": "slow",
         "STUN": "stun", "CUT": "cut", "AFRAID": "afraid", "TERROR": "terror", "STONESKIN": "stoneskin",
         "SPRINT": "sprint", "STEALTH": "stealth", "ATT_CONF": "att_conf", "SCRAMBLE": "scrambled",
         "AMNESIA": "amnesia", "IMAGE": "image", "OPP_CONF": "oppose_conf", "ATT_VAMP": "att_vamp"}
ELEMENTS = {"ACID": "acid", "ELEC": "elec", "FIRE": "fire", "COLD": "cold", "POIS": "pois", "LIGHT": "light",
            "DARK": "dark", "NETHER": "nether", "MISSILE": "none", "MANA": "none", "HOLY_ORB": "none",
            "WATER": "none", "PLASMA": "fire", "ICE": "cold", "SHARD": "shards", "SOUND": "sound", "CHAOS": "chaos",
            "DISEN": "disen", "NEXUS": "nexus"}
FOOD_UNIT = 200  # Angband 4.2 food units -> AVABand's (a ration: 30 -> 6000)
EXPR_VALUE = 20   # stand-in for player level / spell power in $-expressions


def slug(name):
    plain = unicodedata.normalize("NFKD", name).encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9]+", "_", plain.lower().replace("'", "")).strip("_")


def parse(path):
    entries, cur = [], None
    for raw in open(path, encoding="utf-8"):
        line = raw.rstrip("\n")
        if not line or line.startswith("#"):
            continue
        key, _, value = line.partition(":")
        if key == "name":
            cur = {"name": value, "lines": []}
            entries.append(cur)
        elif cur is not None:
            cur["lines"].append((key, value))
    return entries


def get(e, key):
    return [v for k, v in e["lines"] if k == key]


def one(e, key, default=None):
    v = get(e, key)
    return v[0] if v else default


def dice_text(text):
    """Angband dice ('20+2d10', '$B+10d10', '12d8', '300+m35') -> (AVABand dice 'XdY+Z', percent)."""
    text = text.replace("$B", str(EXPR_VALUE)).replace("$S", str(EXPR_VALUE)).replace("$D", "3")
    percent = 0
    # Upper-case M: a magic-bonus amount used as a plain value here (e.g. teleport range "M60").
    text = re.sub(r"M(\d+)", r"\1", text)
    m = re.search(r"m(\d+)", text)
    if m:
        percent = int(m.group(1))
        text = text[:m.start()].rstrip("+") + text[m.end():]
    parts = [p for p in text.split("+") if p]
    dice = [p for p in parts if "d" in p]
    base = sum(int(p) for p in parts if p.isdigit())
    if dice:
        d = dice[0] if not dice[0].startswith("d") else "1" + dice[0]
        return (d + (f"+{base}" if base else ""), percent)
    return (str(base), percent)


def random_value(text):
    """'1+M5' / '5+d5M10' / 'd4' -> AVABand random value (same syntax, 'M' kept)."""
    return text.replace(" ", "")


def level_dice(dice, lines):
    """A dice string with its PLAYER_LEVEL variables as {L...} expressions ("1d$S" with S = level -> "1d{L}"),
    for effects worked out at the player's level (shapes); None if another variable is left."""
    for k, v in lines:
        if k != "expr":
            continue
        var, _, expr = v.partition(":")
        base, _, ops = expr.partition(":")
        if base != "PLAYER_LEVEL":
            continue
        ops = ops.replace(" ", "")
        dice = dice.replace("$" + var, "{L" + ("" if ops in ("+0", "") else ops) + "}")
    return None if "$" in dice else dice


def effects(e, level_exprs=False):
    """Translate effect/dice lines. Returns (effect string, untranslated names). With level_exprs, dice that
    depend on the player's level or hit points keep that dependence (for shapes, applied at the player's level)."""
    out, missing, dropped = [], [], 0
    lines = e["lines"]
    shared = None  # Angband SET_VALUE: one roll shared by the following effects
    select = 0     # Angband SELECT n: the next n effects are alternatives (random breaths)
    choices = []
    group = 0      # Angband RANDOM n: the next n effects are alternatives, all of which must translate
    for i, (k, v) in enumerate(lines):
        if k != "effect":
            continue
        if v.startswith("SELECT"):
            nxt = next((v2 for k2, v2 in lines[i + 1:] if k2 == "dice"), "0")
            select, choices = int(nxt) if nxt.isdigit() else 0, []
            continue
        if select:
            choices.append(v)
            select -= 1
            if select:
                continue
            # All the alternatives must be breaths (or arcs, which AVABand breathes) of one strength:
            # "breath:acid/fire/...:dice".
            elems = [c.split(":")[1] for c in choices if c.startswith(("BREATH:", "ARC:"))]
            if len(elems) != len(choices) or any(el not in ELEMENTS for el in elems):
                missing.append("SELECT")
                continue
            d = dice_text(next((v2 for k2, v2 in lines[i + 1:] if k2 == "dice"), "0"))[0]
            out.append("breath:" + "/".join(dict.fromkeys(ELEMENTS[el] for el in elems)) + f":{d}")
            continue
        parts = v.split(":")
        name = parts[0]
        arg = parts[1] if len(parts) > 1 else ""
        extra = parts[2] if len(parts) > 2 else ""
        dice = None
        for k2, v2 in lines[i + 1:]:
            if k2 == "effect":
                break
            if k2 == "dice":
                dice = v2
        if name == "SET_VALUE":
            shared = dice
            continue
        if name == "CLEAR_VALUE":
            shared = None
            continue
        dice = dice or shared
        if level_exprs and dice and "$" in dice:
            hp_share = next((v2.split(":")[2].replace(" ", "") for k2, v2 in lines[i + 1:]
                             if k2 == "expr" and v2.split(":")[1] == "PLAYER_HP"), None)
            if name == "DAMAGE" and hp_share and hp_share.startswith("/"):
                out.append(f"lose_hp_fraction:{hp_share[1:]}")  # a share of your hit points (the vampire's change)
                continue
            leveled = level_dice(dice.strip(), lines)
            if leveled is not None:
                if name == "DAMAGE":
                    out.append(f"damage:{leveled}")
                    continue
                if name in ("PROJECT_LOS", "PROJECT_LOS_AWARE") and arg == "TURN_ALL":
                    out.append(f"project_los:scare:{leveled}")
                    continue
        d, pct = dice_text(dice) if dice else ("0", 0)
        t = None
        if name == "RANDOM":
            group = int(d) if d.isdigit() else 0
            out.append(f"random:{group}")
            continue
        if name == "DAMAGE":
            t = f"damage:{d}"
        elif name == "HEAL_HP":
            t = f"heal:{d}:{pct}"
        elif name == "CURE" and arg in TIMED:
            t = f"cure:{TIMED[arg]}"
        elif name == "TIMED_DEC" and arg in TIMED:
            t = f"reduce:{TIMED[arg]}"
        elif name in ("TIMED_INC", "TIMED_SET", "TIMED_INC_NO_RES") and arg in TIMED:
            t = f"timed:{TIMED[arg]}:{d}"
        elif name == "TIMED_INC" and arg == "BOLD":
            t = "cure:afraid"
        elif name == "NOURISH":
            n = int(d.split("d")[0]) if "d" not in d else 1
            n = int(d) if d.isdigit() else n
            t = {"INC_BY": f"nourish:{n * FOOD_UNIT}", "INC_TO": "satisfy", "SET_TO": f"set_food:{n * FOOD_UNIT}"}.get(arg)
        elif name == "RESTORE_STAT":
            t = f"restore_stat:{arg.lower()}"
        elif name == "GAIN_STAT":
            t = f"gain_stat:{arg.lower()}"
        elif name == "DRAIN_STAT":
            t = f"drain_stat:{arg.lower()}"
        elif name == "LOSE_RANDOM_STAT":
            t = f"drain_stat:!{arg.lower()}"  # a random stat other than this one
        elif name == "RESTORE_MANA":
            t = "restore_mana"
        elif name == "RESTORE_EXP":
            t = "restore_exp"
        elif name == "GAIN_EXP":
            t = "gain_exp:100000"
        elif name in ("DETECT_GOLD", "SENSE_OBJECTS", "DETECT_OBJECTS"):
            t = "detect_objects:30"
        elif name == "DETECT_INVISIBLE_MONSTERS":
            t = "detect_invisible:30"
        elif name == "DETECT_VISIBLE_MONSTERS":
            t = "detect_monsters:30"
        elif name == "DETECT_EVIL":
            t = "detect_evil:30"
        elif name == "MAP_AREA":
            t = "map_area:30"
        elif name in ("LIGHT_AREA",) or (name in ("SPOT", "STAR") and arg.startswith("LIGHT")):
            t = "light_area"
        elif name == "LIGHT_LEVEL":
            t = "enlightenment"
        elif name == "DARKEN_AREA" or (name == "SPOT" and arg.startswith("DARK")):
            t = "darkness"
        elif name == "TELEPORT":
            # "M60": a share of the level's size (60% of its larger side), as 4.2.5 reads it.
            t = f"teleport:{dice.strip()}" if dice and re.fullmatch(r"M\d+", dice.strip()) else f"teleport:{d if d.isdigit() else 10}"
        elif name == "TELEPORT_LEVEL":
            t = "teleport_level"
        elif name == "DEEP_DESCENT":
            t = "deep_descent"
        elif name == "RECALL":
            t = "recall"
        elif name == "REMOVE_CURSE":
            t = f"remove_curse:{dice.replace(' ', '')}" if dice and "$" not in dice else "remove_curse"
        elif name == "IDENTIFY":
            t = "identify"
        elif name == "ENCHANT":
            n = d if d.isdigit() else "1"
            t = {"TOHIT": f"enchant:to_h:{n}", "TODAM": f"enchant:to_d:{n}", "TOAC": f"enchant:to_a:{n}",
                 "TOBOTH": f"enchant:to_h:{n}; enchant:to_d:{n}"}.get(arg)
        elif name == "RECHARGE":
            t = f"recharge:{int(d) * 10 if d.isdigit() else 60}"  # Angband 4.2 strength -> 3.x-style power
        elif name == "ACQUIRE":
            t = f"acquirement:{d if d.isdigit() and d != '0' else 1}"
        elif name == "MASS_BANISH":
            t = "mass_banishment:20"
        elif name == "DESTRUCTION":
            t = "destruction:15"
        elif name == "EARTHQUAKE":
            t = "earthquake:10"
        elif name == "SUMMON":
            t = f"summon:{d if d != '0' else 1}:{arg}"
        elif name == "WAKE":
            t = "aggravate"
        elif name == "TOUCH" and arg == "MAKE_TRAP":
            t = "trap_creation"
        elif name in ("BOLT", "BOLT_OR_BEAM", "BOLT_AWARE") and arg == "MON_DRAIN":
            t = f"drain_life:{d}"
        elif name == "BOLT" and arg == "ARROW":
            t = f"bolt:none:{d}"
        elif name == "STAR_BALL" and arg in ELEMENTS:
            t = f"ball:{ELEMENTS[arg]}:{d}:{extra or 3}"
        elif name == "BIZARRE":
            t = "wonder"
        elif name == "TOUCH_AWARE" and arg == "SLEEP_ALL":
            t = "project_los:sleep:40"
        elif name in ("BOLT", "BOLT_OR_BEAM", "BOLT_AWARE") and arg in ELEMENTS:
            t = f"bolt:{ELEMENTS[arg]}:{d}"
        elif name == "BEAM" and arg in ELEMENTS:
            t = f"beam:{ELEMENTS[arg]}:{d}"
        elif name == "BALL" and arg in ELEMENTS:
            t = f"ball:{ELEMENTS[arg]}:{d}:{extra or 2}"
        elif name in ("ARC", "BREATH") and arg in ELEMENTS:
            t = f"breath:{ELEMENTS[arg]}:{d}"
        elif name == "LINE" and arg == "LIGHT_WEAK":
            t = "light_line:6d8"
        elif name == "LINE" and arg == "KILL_WALL":
            t = "stone_to_mud"
        elif name in ("BOLT_AWARE", "BOLT_STATUS") and arg in ("MON_SLOW", "MON_HOLD", "MON_CONF", "MON_STUN", "TURN_ALL"):
            status = {"MON_SLOW": "slow", "MON_HOLD": "hold", "MON_CONF": "confuse", "MON_STUN": "stun", "TURN_ALL": "scare"}[arg]
            t = f"monster_status:{status}:{int(one(e, 'level', '10')) + 10}"
        elif arg == "MON_POLY":
            t = "polymorph"
        elif name == "BOLT_STATUS" and arg == "AWAY_ALL":
            t = "teleport_other"
        elif name == "BOLT_STATUS" and arg == "MON_CLONE":
            t = "clone_monster"
        elif name == "BOLT_STATUS" and arg == "MON_HEAL":
            t = f"heal_monster:{d}"
        elif name == "BOLT_STATUS" and arg == "MON_SPEED":
            t = "haste_monster"
        elif name == "BOLT_STATUS_DAM" and arg == "MON_DRAIN":
            t = f"drain_life:{d}"
        elif name == "ALTER" and arg == "KILL_TRAP":
            t = "disarm"
        elif name == "WONDER":
            t = "wonder"
        elif name == "BANISH":
            t = "banish"
        elif name == "PROBE":
            t = "probe"
        elif name == "TOUCH" and arg == "KILL_DOOR":
            t = "destroy_doors"
        elif name == "GLYPH" and arg == "WARDING":
            t = "glyph"
        elif name == "CURSE_WEAPON":
            t = "curse_weapon"
        elif name == "CURSE_ARMOR":
            t = "curse_armour"
        elif name == "SHAPECHANGE":
            t = f"shapechange:{re.sub(r'[^a-z]+', '_', arg.lower()).strip('_')}"
        elif name == "LINE" and arg == "DARK_WEAK":
            t = "darkness"
        elif name in ("PROJECT_LOS", "PROJECT_LOS_AWARE"):
            t = {"MON_SPEED": "project_los:haste", "MON_SLOW": "project_los:slow:30", "MON_CONF": "project_los:confuse:30",
                 "SLEEP_ALL": "project_los:sleep:30", "DISP_EVIL": f"dispel:EVIL:{d}",
                 "DISP_UNDEAD": f"dispel:UNDEAD:{d}", "DISP_ALL": f"dispel:none:{d}",
                 "TURN_ALL": f"project_los:scare:{d}"}.get(arg)
        if group:
            # Inside a RANDOM group every alternative must translate, duplicates included.
            group -= 1
            if t is None:
                missing.append(f"RANDOM alternative {v}")
            else:
                out.append(t)
            continue
        if t is None:
            # Cures and statuses AVABand doesn't model (amnesia, hallucination...) are simply left out.
            if not name.startswith(("CURE", "TIMED_")):
                missing.append(v)
            dropped += 1
        elif t not in out:
            out.append(t)
    # Something whose only remaining effect is its food value isn't the item it claims to be.
    if dropped and out and all(x.startswith(("nourish", "set_food")) for x in out):
        missing.append("only food value left")
    return "; ".join(out), missing


ELEMENT_NAMES = {"acid": "acid", "elec": "lightning", "fire": "fire", "cold": "frost", "pois": "poison",
                 "light": "light", "dark": "darkness", "nether": "nether", "none": "magic", "shards": "shards",
                 "sound": "sound", "chaos": "chaos", "disen": "disenchantment", "nexus": "nexus"}
TIMED_PHRASES = {"fast": "hastes you", "hero": "makes you heroic", "berserk": "puts you in a berserker rage",
                 "blessed": "blesses you", "prot_evil": "protects you from evil", "telepathy": "expands your mind",
                 "see_invisible": "lets you see invisible things", "infravision": "sharpens your infravision"}


def describe(effect):
    """A plain-English summary of an AVABand effect string (for activation descriptions)."""
    parts, resists = [], []
    for raw in (x.strip() for x in effect.split(";")):
        a = raw.split(":")
        n = a[0]
        el = ELEMENT_NAMES.get(a[1], a[1]) if len(a) > 1 else ""
        if "/" in el or (len(a) > 1 and "/" in a[1]):
            names = [ELEMENT_NAMES.get(x, x) for x in a[1].split("/")]
            el = ", ".join(names[:-1]) + " or " + names[-1]
        if n == "bolt":
            parts.append(f"fires a {el} bolt ({a[2]})" if el != "magic" else f"fires a magic missile ({a[2]})")
        elif n == "beam":
            parts.append(f"fires a beam of {el} ({a[2]})")
        elif n == "ball":
            parts.append(f"fires a ball of {el} ({a[2]})")
        elif n == "breath":
            parts.append(f"lets you breathe {el} ({a[2]})")
        elif n == "heal":
            parts.append(f"heals {a[1]} hit points")
        elif n == "cure":
            parts.append({"afraid": "removes fear", "poisoned": "neutralizes poison", "blind": "cures blindness",
                          "confused": "cures confusion", "cut": "heals cuts", "stun": "cures stunning"}.get(a[1], f"cures {a[1]}"))
        elif n == "timed" and a[1].startswith("oppose_"):
            resists.append(ELEMENT_NAMES.get(a[1][7:], a[1][7:]))
        elif n == "timed":
            parts.append(TIMED_PHRASES.get(a[1], f"grants {a[1]}"))
        elif n == "teleport":
            parts.append("teleports you a short distance" if a[1].isdigit() and int(a[1]) <= 10 else "teleports you")
        else:
            parts.append({"detect_objects": "detects objects", "detect_monsters": "detects monsters",
                          "detect_invisible": "detects invisible monsters", "detect_evil": "detects evil",
                          "map_area": "maps the area", "enlightenment": "maps the whole level",
                          "light_area": "lights up the area", "recall": "recalls you to town or the dungeon",
                          "restore_exp": "restores your life levels", "recharge": "recharges a wand or staff",
                          "teleport_other": "teleports a monster away", "stone_to_mud": "turns a wall to mud",
                          "drain_life": "drains the life of a monster", "mass_banishment": "banishes nearby monsters",
                          "wonder": "does something bizarre", "dispel": "dispels monsters in view",
                          "monster_status": "affects a monster", "project_los": "affects every monster in view",
                          "restore_mana": "restores your mana", "light_line": "lights a beam",
                          "teleport_level": "takes you to another level"}.get(n, n.replace("_", " ")))
    if resists:
        parts.append("grants temporary resistance to " + (", ".join(resists[:-1]) + " and " + resists[-1] if len(resists) > 1 else resists[0]))
    parts = list(dict.fromkeys(parts))
    return ", ".join(parts[:-1]) + " and " + parts[-1] if len(parts) > 1 else (parts[0] if parts else "")


def convert(e, base):
    name = e["name"].replace("& ", "")
    level = int(one(e, "level", "0"))
    alloc = one(e, "alloc", "0:0 to 0").split(":")
    commonness = int(alloc[0])
    lo, _, hi = alloc[1].partition(" to ")
    out = {"id": "", "name": name, "base": base, "level": level, "commonness": commonness,
           "minDepth": int(lo or 0), "maxDepth": int(hi or 127),
           "cost": int(one(e, "cost", "0")), "weight": int(one(e, "weight", "0"))}
    rolls, mods, resists, flags, curses = {}, {}, [], [], []

    attack = one(e, "attack")
    if attack and base in ("sword", "hafted", "polearm", "digger", "arrow", "bolt", "shot", "bow", "dragon_armour", "gloves", "ring", "amulet"):
        dmg, th, td = (attack.split(":") + ["0", "0"])[:3]
        if dmg and dmg != "0d0" and base not in ("ring", "amulet", "gloves", "dragon_armour"):
            out["damage"] = dmg
        for key, text in (("to_h", th), ("to_d", td)):
            if text and text != "0":
                if re.fullmatch(r"-?\d+", text):
                    out["toHit" if key == "to_h" else "toDam"] = int(text)
                else:
                    rolls[key] = random_value(text)
    armor = one(e, "armor")
    if armor:
        ac, ta = (armor.split(":") + ["0"])[:2]
        if ac and ac != "0":
            out["armour"] = int(ac)
        if ta and ta != "0":
            if re.fullmatch(r"-?\d+", ta):
                out["toAc"] = int(ta)
            else:
                rolls["to_a"] = random_value(ta)
    for line in get(e, "values"):
        for part in line.split("|"):
            part = part.strip()
            m = re.fullmatch(r"([A-Z_]+)\[(.+)\]", part)
            if not m:
                continue
            key, val = m.groups()
            if key in RESISTS:
                resists.append(RESISTS[key])
            elif key in MODIFIERS:
                if re.fullmatch(r"-?\d+", val):
                    mods[MODIFIERS[key]] = int(val)
                else:
                    rolls[MODIFIERS[key]] = random_value(val)
    for line in get(e, "flags"):
        for f in (x.strip() for x in line.split("|")):
            if f in FLAG_RESISTS:
                resists.append(FLAG_RESISTS[f])
            elif f in FLAG_ABILITIES:
                flags.append(FLAG_ABILITIES[f])
            elif re.fullmatch(r"DIG_\d", f):
                mods["tunnel"] = int(f[4:])  # Angband 4.2 digging power: +20 skill per level
    for line in get(e, "curse"):
        cname = line.split(":")[0]
        if CURSES.get(cname):
            curses.append(CURSES[cname])
    if base == "bow":
        out["multiplier"] = 4 if "Heavy" in name else 3 if ("Long" in name or "Light" in name) else 2
    pile = one(e, "pile")
    if pile and base in ("arrow", "bolt", "shot"):
        out["stackSize"] = pile.split(":")[1]
    if mods:
        out["modifiers"] = mods
    if rolls:
        out["rolls"] = rolls
    if resists:
        out["resists"] = list(dict.fromkeys(resists))
    if flags:
        out["flags"] = flags
    if curses:
        out["curses"] = curses
    if one(e, "charges"):
        out["charges"] = one(e, "charges").replace(" ", "")
    if one(e, "time"):
        out["recharge"] = one(e, "time").replace(" ", "")
    desc = " ".join(v.strip() for v in get(e, "desc")).replace("  ", " ")
    if desc:
        out["description"] = desc
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("objects_txt")
    ap.add_argument("out")
    ap.add_argument("--report", action="store_true")
    args = ap.parse_args()

    existing = json.load(open(args.out, encoding="utf-8"))
    have = {(o["base"], o["name"].replace("~", "").lower()) for o in existing}
    have_names = {(o["base"] in ("sling", "bow", "crossbow") and "bow" or o["base"], o["name"].replace("~", "").lower())
                  for o in existing}
    ids = {o["id"] for o in existing}
    added, skipped = 0, []
    for e in parse(args.objects_txt):
        kind_type = one(e, "type")
        base = TYPES.get(kind_type)
        if base is None:
            continue
        name = e["name"].replace("& ", "")
        key = (base, name.replace("~", "").lower())
        if key in have or key in have_names:
            continue
        if base == "bow":
            base = "sling" if "Sling" in name else "crossbow" if "Crossbow" in name else "bow"
        kind = convert(e, base)
        if base in ("potion", "scroll", "food", "mushroom", "wand", "staff", "rod"):
            effect, missing = effects(e)
            if missing or not effect:
                skipped.append(f"{kind_type}: {name} ({', '.join(missing) or 'no effect'})")
                continue
            kind["effect"] = effect
        elif get(e, "effect"):
            # Wearables with an effect (dragon armour, rings of Flames...) activate it.
            effect, missing = effects(e)
            if effect and not missing:
                kind["activation"] = effect
                kind["activationText"] = describe(effect)
            else:
                kind.pop("recharge", None)
                skipped.append(f"activation of {kind_type}: {name} ({', '.join(missing) or 'no effect'})")
        kind_id = slug(name.replace("~", ""))
        if base in PREFIX and not kind_id.startswith(PREFIX[base].rstrip("_")):
            kind_id = PREFIX[base] + kind_id
        if kind_id in ids:
            kind_id = f"{base}_{kind_id}"
        kind["id"] = kind_id
        ids.add(kind_id)
        existing.append(kind)
        added += 1

    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(existing, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"added {added} object kinds ({len(existing)} total); skipped {len(skipped)}")
    if args.report:
        for s in skipped:
            print("  skipped", s)


if __name__ == "__main__":
    main()
