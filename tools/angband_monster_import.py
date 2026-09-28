#!/usr/bin/env python3
"""Import monsters from Angband 4.2's gamedata (monster.txt + monster_base.txt) into AVABand's monsters.json.

Monsters already in monsters.json (matched by name) are kept as they are; everything else is appended.
Mimics get the object kinds they pose as ("mimics"), looked up in objects.json; ones with nothing to
pose as (chest mimics: AVABand has no chests) are skipped. Spells and blow
effects are mapped onto the ones AVABand implements; anything else is dropped (or, for blow effects,
treated as plain damage). Angband's data is available under the GPL v2 or the Angband licence.

Usage:
    angband_monster_import.py <monster.txt> <monster_base.txt> <data/monsters.json> [--max-depth N]
"""
import argparse, json, re, unicodedata

COLORS = {
    "d": "Dark", "w": "White", "s": "Slate", "o": "Orange", "r": "Red", "g": "Green", "b": "Blue", "u": "Umber",
    "D": "LightDark", "W": "LightSlate", "P": "LightPurple", "y": "Yellow", "R": "LightRed", "G": "LightGreen",
    "B": "LightBlue", "U": "LightUmber", "p": "Purple", "v": "Violet", "t": "Teal", "m": "Mud", "Y": "LightYellow",
    "i": "Magenta", "T": "LightTeal", "V": "LightViolet", "I": "LightPink", "M": "MustardYellow", "z": "BlueSlate",
    "Z": "DeepLightBlue",
}

METHODS = {"hit", "bite", "claw", "touch", "gaze", "engulf", "crush", "sting", "spore", "crawl", "butt", "wail",
           "kick", "spit", "punch", "insult", "beg", "drool", "moan"}

EFFECTS = {
    "HURT": "hurt", "POISON": "poison", "FIRE": "fire", "ACID": "acid", "CONFUSE": "confuse", "PARALYZE": "paralyze",
    "COLD": "cold", "ELEC": "elec", "TERRIFY": "terrify", "BLIND": "blind", "DISENCHANT": "disenchant",
    "DRAIN_CHARGES": "drain_charges", "EAT_GOLD": "eat_gold", "EAT_ITEM": "eat_item", "EAT_FOOD": "eat_food",
    "EAT_LIGHT": "eat_light", "LOSE_STR": "lose_str", "LOSE_INT": "lose_int", "LOSE_WIS": "lose_wis",
    "LOSE_DEX": "lose_dex", "LOSE_CON": "lose_con", "LOSE_ALL": "lose_all", "EXP_10": "exp_10", "EXP_20": "exp_20",
    "EXP_40": "exp_40", "EXP_80": "exp_80", "HALLU": "hallu",
    # Not modelled: plain (heavy) damage.
    "SHATTER": "hurt", "BLACK_BREATH": "exp_20",
}

# Angband spell -> AVABand monster spell (None = dropped). Every 4.2 spell is modelled now.
SPELLS = {s: s for s in [
    "SHRIEK", "ARROW", "BOULDER", "SHOT", "BOLT", "WHIP", "SPIT", "MISSILE",
    "BR_ACID", "BR_ELEC", "BR_FIRE", "BR_COLD", "BR_POIS", "BR_NETH", "BR_LIGHT", "BR_DARK", "BR_SOUN", "BR_CHAO",
    "BR_DISE", "BR_NEXU", "BR_TIME", "BR_INER", "BR_GRAV", "BR_SHAR", "BR_PLAS", "BR_WALL", "BR_MANA",
    "BA_ACID", "BA_ELEC", "BA_FIRE", "BA_COLD", "BA_POIS", "BA_SHAR", "BA_NETH", "BA_WATE", "BA_MANA", "BA_HOLY",
    "BA_DARK", "BA_LIGHT",
    "BO_ACID", "BO_ELEC", "BO_FIRE", "BO_COLD", "BO_POIS", "BO_NETH", "BO_WATE", "BO_MANA", "BO_PLAS", "BO_ICE",
    "BE_ELEC", "BE_NETH",
    "DRAIN_MANA", "MIND_BLAST", "BRAIN_SMASH", "SCARE", "BLIND", "CONF", "SLOW", "HOLD", "HASTE", "HEAL", "HEAL_KIN",
    "BLINK", "TPORT", "TELE_TO", "TELE_SELF_TO", "TELE_AWAY", "TELE_LEVEL", "DARKNESS", "TRAPS", "FORGET",
    "S_KIN", "S_MONSTER", "S_MONSTERS", "S_ANIMAL", "S_SPIDER", "S_HOUND", "S_HYDRA", "S_DEMON", "S_UNDEAD",
    "S_DRAGON", "SHAPECHANGE",
    "WOUND", "WEAVE", "STORM", "S_AINU", "S_HI_DEMON", "S_HI_UNDEAD", "S_HI_DRAGON", "S_WRAITH", "S_UNIQUE",
]}


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


def values(entry, key):
    return [v for k, v in entry["lines"] if k == key]


def one(entry, key, default=None):
    v = values(entry, key)
    return v[0] if v else default


def flag_list(entry, key):
    return [f.strip() for line in values(entry, key) for f in line.split("|") if f.strip()]


# Angband monsters AVABand keeps under older names (see --skip-names).
ALIASES = {"wild dog": "jackal", "half-orc": "hill_orc"}


def friend_race(target, names):
    """Angband lookup_monster: an exact name, else the first race whose name contains it."""
    if target.lower() == "same":
        return "same"
    exact = next((n for n in names if n.lower() == target.lower()), None)
    name = exact or next((n for n in names if target.lower() in n.lower()), None)
    if name is None:
        return None
    return ALIASES.get(name.lower(), slug(name))


def friends_of(entry, bases, names):
    """Angband friends / friends-base lines: chance, number (dice), and a race or a base's symbol."""
    out = []
    for key, value in entry["lines"]:
        if key not in ("friends", "friends-base"):
            continue
        parts = value.split(":")
        chance, dice, target = int(parts[0]), parts[1], parts[2]
        role = parts[3] if len(parts) > 3 else None
        if key == "friends":
            race = friend_race(target, names)
            if race:
                friend = {"chance": chance, "number": dice, "race": race}
                if role == "bodyguard": friend["role"] = "bodyguard"
                out.append(friend)
        else:
            out.append({"chance": chance, "number": dice, "base": target, "glyph": bases.get(target, {}).get("glyph", "?")})
    return out


def convert(entry, bases, names=()):
    base = bases.get(one(entry, "base"), {})
    depth = int(one(entry, "depth", "0"))
    flags = [f for f in base.get("flags", []) + flag_list(entry, "flags") if f not in set(flag_list(entry, "flags-off"))]
    flags = list(dict.fromkeys(flags))

    blows = []
    for b in values(entry, "blow"):
        parts = b.split(":")
        method = parts[0].lower()
        effect = EFFECTS.get(parts[1], "hurt") if len(parts) > 1 and parts[1] else "none"
        blow = {"method": method if method in METHODS else "hit", "effect": effect}
        blow["damage"] = parts[2] if len(parts) > 2 and parts[2] else "0"
        blows.append(blow)

    spells = []
    for s in flag_list(entry, "spells"):
        mapped = SPELLS.get(s)
        if mapped and mapped not in spells:
            spells.append(mapped)
    # Angband 4.2 keeps two frequencies (1 in N): innate-freq for innate attacks, spell-freq for
    # the rest; a race with attacks of a kind but no frequency for it gets 1 in 4 (mon-init.c).
    innate_freq = int(one(entry, "innate-freq", "0")) or 4
    spell_freq = int(one(entry, "spell-freq", "0")) or 4

    color = one(entry, "color", "w")
    out = {
        "id": slug(entry["name"]),
        "name": entry["name"],
        "glyph": one(entry, "glyph", base.get("glyph", "?")),
        "color": COLORS.get(color[0], "White"),
        "depth": depth,
        **({"spellPower": int(one(entry, "spell-power"))} if one(entry, "spell-power", str(depth)) != str(depth) else {}),
        "rarity": int(one(entry, "rarity", "1")),
        "speed": int(one(entry, "speed", "110")) - 110,
        "hitPoints": int(one(entry, "hit-points", "1")),
        "armour": int(one(entry, "armor-class", "0")),
        "sleep": int(one(entry, "sleepiness", "0")),
        "hearing": int(one(entry, "hearing", "20")),
        "smell": int(one(entry, "smell", "20")),
        "experience": int(one(entry, "experience", "0")),
        "blows": blows,
        "flags": flags,
        "description": " ".join(v.strip() for v in values(entry, "desc")).replace("  ", " "),
    }
    shapes = values(entry, "shape")
    if shapes:
        out["shapeNames"] = [x.strip() for x in shapes]
    if one(entry, "plural"):
        out["plural"] = one(entry, "plural")
    friends = friends_of(entry, bases, names)
    if friends:
        out["friends"] = friends
    if spells:
        if any(sp in INNATE for sp in spells):
            out["innateFrequency"] = innate_freq
        if any(sp not in INNATE for sp in spells):
            out["spellFrequency"] = spell_freq
        out["spells"] = spells
    return out


# Angband's innate "spells" (list-mon-spells.h, RST_INNATE): breaths, missiles, spit, shrieks.
INNATE = {"SHRIEK", "WHIP", "SPIT", "SHOT", "ARROW", "BOLT", "BOULDER", "WEAVE"} | {
    "BR_ACID", "BR_ELEC", "BR_FIRE", "BR_COLD", "BR_POIS", "BR_NETH", "BR_LIGHT", "BR_DARK", "BR_SOUN", "BR_CHAO",
    "BR_DISE", "BR_NEXU", "BR_TIME", "BR_INER", "BR_GRAV", "BR_SHAR", "BR_PLAS", "BR_WALL", "BR_MANA"}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("monsters")
    ap.add_argument("bases")
    ap.add_argument("out")
    ap.add_argument("--max-depth", type=int, default=127)
    ap.add_argument("--objects", help="AVABand objects.json, to resolve what mimics pose as")
    ap.add_argument("--skip-names", nargs="*", default=["wild dog", "half-orc"],
                    help="Angband monsters AVABand already has under another name")
    args = ap.parse_args()

    bases = {}
    for b in parse(args.bases):
        bases[b["name"]] = {"glyph": one(b, "glyph", "?"), "flags": flag_list(b, "flags")}

    kinds = {}
    if args.objects:
        for k in json.load(open(args.objects, encoding="utf-8")):
            kinds[(k["base"], k["name"].replace("~", "").replace("& ", "").lower())] = k["id"]
    existing = json.load(open(args.out, encoding="utf-8"))
    names = {m["name"].lower() for m in existing} | {n.lower() for n in args.skip_names}
    ids = {m["id"] for m in existing}
    added = 0
    entries = parse(args.monsters)
    all_names = [e["name"] for e in entries if not e["name"].startswith("<")]
    for entry in entries:
        if entry["name"].startswith("<") or entry["name"].lower() in names:
            continue
        m = convert(entry, bases, all_names)
        mimic_lines = values(entry, "mimic")
        if mimic_lines:
            poses = [kinds.get((tval.strip(), name.strip().lower())) for tval, _, name in (l.partition(":") for l in mimic_lines)]
            poses = [p for p in poses if p]
            if not poses:
                continue  # nothing to pose as (chest mimics)
            m["mimics"] = poses
        if m["depth"] > args.max_depth or m["id"] in ids:
            continue
        existing.append(m)
        ids.add(m["id"])
        added += 1

    # Resolve shapes: a monster's name, or a monster base (any non-unique of it near the right depth).
    by_name = {m["name"].lower(): m for m in existing}
    base_of = {slug(e["name"]): one(e, "base") for e in parse(args.monsters)}
    for m in existing:
        names = m.pop("shapeNames", None)
        if not names:
            continue
        ids = []
        for n in names:
            if n.lower() in by_name:
                ids.append(by_name[n.lower()]["id"])
            else:
                ids += [x["id"] for x in existing if base_of.get(x["id"]) == n and "UNIQUE" not in x["flags"]
                        and abs(x["depth"] - m["depth"]) <= 15]
        ids = list(dict.fromkeys(i for i in ids if i != m["id"]))
        if ids:
            m["shapes"] = ids
        elif "spells" in m:
            m["spells"] = [s for s in m["spells"] if s != "SHAPECHANGE"]
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(existing, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print(f"added {added} monsters ({len(existing)} total)")


if __name__ == "__main__":
    main()
