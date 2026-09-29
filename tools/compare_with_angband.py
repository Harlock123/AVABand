#!/usr/bin/env python3
"""Compare AVABand's game data (src/Angband.Data/data/*.json) with Angband 4.2.5's lib/gamedata/*.txt.

Reports, entry by entry, the fields whose values differ after normalising representation (flag
spelling and case, list order, dice formats, "1d1" placeholders, ...), plus entries present on only one
side. The mappings from 4.2's fields to AVABand's JSON are those of the importer scripts in this
directory (angband_*_import.py), which are imported and reused where possible.

Standard library only. Read-only: it never writes to the game data.

Usage:
    compare_with_angband.py <angband lib/gamedata dir> [--data src/Angband.Data/data] [--out report.md]
"""
import argparse, collections, json, os, re, sys, unicodedata

HERE = os.path.dirname(os.path.abspath(__file__))
sys.dont_write_bytecode = True   # don't leave .pyc files for the importers in tools/__pycache__
sys.path.insert(0, HERE)
import angband_object_import as OI            # noqa: E402
import angband_monster_import as MI           # noqa: E402
import angband_ego_artifact_import as EA      # noqa: E402

NONE = "—"


# ---------------------------------------------------------------------------------------------------
# Parsing helpers

def parse_records(path, start="name"):
    """Angband's key:value records; a record starts at each `start` line."""
    entries, cur = [], None
    for raw in open(path, encoding="utf-8"):
        line = raw.rstrip("\n").rstrip("\r")
        if not line or line.startswith("#"):
            continue
        key, _, value = line.partition(":")
        if key == start:
            cur = {"name": value, "lines": []}
            entries.append(cur)
        elif cur is not None:
            cur["lines"].append((key, value))
    return entries


get, one = OI.get, OI.one


def flag_list(e, key):
    return [f.strip() for line in get(e, key) for f in line.split("|") if f.strip()]


def graphics(e, key="graphics"):
    """'graphics:G:c' -> (glyph, colour letter). The glyph can itself be ':'."""
    v = one(e, key)
    if not v:
        return None, None
    return v[0], (v[2:] if len(v) > 2 else None)


def ascii_fold(s):
    return unicodedata.normalize("NFKD", s).encode("ascii", "ignore").decode()


def norm_name(s):
    """Object/monster name for matching: no articles/plural markers/accents/case."""
    s = s or ""
    m = re.search(r"\|([^|]*)\|[^|]*\|", s)          # |singular|plural|
    if m:
        s = s[:m.start()] + m.group(1) + s[m.end():]
    s = ascii_fold(s).replace("& ", "").replace("~", "").lower()
    return re.sub(r"\s+", " ", s).strip()


def slug(s):
    return MI.slug(s)


# ---------------------------------------------------------------------------------------------------
# Value normalisation

TERM = re.compile(r"([+-]?)(\d*d\d+|[Mm]\d+|\d+)")


def canon_rv(text):
    """Canonical form of a dice / Angband random value ('20+2d10', '1d20+20', 'd4', '4+M6', '5+d5M10').

    Terms are reordered (constant, dice, then M bonus); '0d0', '0' and empty mean 0. Anything that isn't a
    plain random value (e.g. an expression with $) is returned unchanged, stripped of spaces."""
    if text is None:
        return "0"
    t = str(text).replace(" ", "")
    if t in ("", "0", "0d0", "+0", "-0"):
        return "0"
    pos, const, dice, mb = 0, 0, [], 0
    for m in TERM.finditer(t):
        if m.start() != pos:
            return t
        pos = m.end()
        sign = -1 if m.group(1) == "-" else 1
        term = m.group(2)
        if "d" in term:
            n, s = term.split("d")
            n = int(n or 1)
            if n and int(s):
                dice.append((sign, n, int(s)))
        elif term[0] in "Mm":
            mb += sign * int(term[1:])
        else:
            const += sign * int(term)
    if pos != len(t):
        return t
    parts = []
    if const:
        parts.append(str(const))
    for sign, n, s in sorted(dice, key=lambda d: (d[2], d[1], d[0])):
        parts.append(("-" if sign < 0 else "+") + f"{n}d{s}")
    if mb:
        parts.append(("-" if mb < 0 else "+") + f"M{abs(mb)}")
    out = "".join(parts).lstrip("+")
    return out or "0"


def rv_avg(text):
    """Average of a canonical random value, or None."""
    c = canon_rv(text)
    total, pos = 0.0, 0
    for m in TERM.finditer(c):
        if m.start() != pos:
            return None
        pos = m.end()
        sign = -1 if m.group(1) == "-" else 1
        term = m.group(2)
        if "d" in term:
            n, s = term.split("d")
            total += sign * int(n or 1) * (int(s) + 1) / 2
        elif term[0] in "Mm":
            total += sign * int(term[1:]) / 2
        else:
            total += sign * int(term)
    return total if pos == len(c) else None


def fmt(v):
    if v is None or v == "" or v == [] or v == {}:
        return NONE
    if isinstance(v, (set, frozenset)):
        return "{" + ", ".join(sorted(map(str, v))) + "}"
    if isinstance(v, (list, tuple)):
        return "[" + ", ".join(map(str, v)) + "]"
    if isinstance(v, dict):
        return "{" + ", ".join(f"{k}: {v[k]}" for k in sorted(v)) + "}"
    return str(v)


def norm_effect(s):
    """An AVABand effect string as an order-insensitive tuple of its parts."""
    if not s:
        return ()
    parts = [re.sub(r"\s+", "", p) for p in re.split(r"[;]", s)]
    return tuple(sorted(p for p in parts if p))


SUBTYPED = {"cure", "timed", "reduce", "restore_stat", "gain_stat", "drain_stat", "bolt", "beam", "ball", "breath",
            "enchant", "dispel", "project_los", "monster_status", "shapechange", "summon"}


def effect_parts(s):
    """AVABand effect string -> {key: [params]}; key is the verb, plus its subject for verbs that have one
    (cure:blind, timed:fast, bolt:fire...)."""
    out = collections.defaultdict(list)
    for p in norm_effect(s):
        a = p.split(":")
        n = 2 if a[0] in SUBTYPED and len(a) > 1 else 1
        key = ":".join(a[:n])
        out[key].append(":".join(canon_rv(x) if re.fullmatch(r"[\dd+\-Mm]+", x) else x for x in a[n:]))
    return {k: sorted(v) for k, v in out.items()}


_IMPORTER_KEYS = None


def importer_effect_keys(gd):
    """Every effect key the importer's translation can produce from 4.2.5's data: an AVABand effect part
    whose key isn't among these is an AVABand extension, not drift."""
    global _IMPORTER_KEYS
    if _IMPORTER_KEYS is None:
        keys = set()
        for fn in ("object.txt", "activation.txt", "shape.txt"):
            for e in parse_records(os.path.join(gd, fn)):
                keys |= set(effect_parts(OI.effects(e)[0]))
        _IMPORTER_KEYS = keys
    return _IMPORTER_KEYS


def cmp_effects(sec, gd, label, field, ours, theirs, missing=(), has_expr=False):
    """Compare an AVABand effect string with the importer's translation of the 4.2.5 effect lines, part by
    part. Parts only in ours that the importer can never produce are AVABand extensions (tallied, not
    reported); untranslatable 4.2.5 effects are tallied too."""
    o, t = effect_parts(ours), effect_parts(theirs)
    # The importer turns TIMED_INC:BOLD into cure:afraid; AVABand spells it timed:bold.
    if "timed:bold" in o and "cure:afraid" in t and "cure:afraid" not in o:
        o.pop("timed:bold")
        t.pop("cure:afraid")
    for m in missing:
        sec.unmodelled[f"effect the importer can't translate: {m.split(':')[0]}"] += 1
    known = importer_effect_keys(gd)
    for k in sorted(set(o) | set(t)):
        ov, tv = o.get(k), t.get(k)
        if ov == tv:
            continue
        if tv is None and k not in known:
            sec.unmodelled[f"AVABand-only effect part `{k}` (no 4.2.5 counterpart via the importer)"] += 1
            continue
        fo = NONE if ov is None else ("; ".join(f"{k}:{x}" if x else k for x in ov))
        ft = NONE if tv is None else ("; ".join(f"{k}:{x}" if x else k for x in tv))
        if has_expr and tv is not None:
            ft += " (4.2.5 uses a `$` expression here or nearby, evaluated at the importer's stand-in 20)"
        sec.diff(label, f"{field} `{k}`", fo, ft)


# ---------------------------------------------------------------------------------------------------
# Report model

class Section:
    def __init__(self, key, title, ours_file, theirs_file):
        self.key, self.title, self.ours_file, self.theirs_file = key, title, ours_file, theirs_file
        self.diffs = collections.OrderedDict()   # entry -> [(field, ours, theirs, ours_absent)]
        self.only_theirs, self.only_ours = [], []
        self.compared = 0
        self.unmodelled = collections.Counter()   # 4.2.5 property ours can't express -> entries
        self.notes = []
        self.ran = True

    def diff(self, entry, field, ours, theirs, ours_absent=False):
        self.diffs.setdefault(entry, []).append((field, fmt(ours), fmt(theirs), ours_absent))

    def cmp(self, entry, field, ours, theirs, ours_absent=False):
        if ours != theirs:
            self.diff(entry, field, ours, theirs, ours_absent)

    def cmp_set(self, entry, field, ours, theirs):
        ours, theirs = set(ours), set(theirs)
        if ours != theirs:
            only_o, only_t = ours - theirs, theirs - ours
            self.diffs.setdefault(entry, []).append(
                (field, fmt(only_o) if only_o else "{}", fmt(only_t) if only_t else "{}", False))

    def n_field_diffs(self):
        return sum(len(v) for v in self.diffs.values())


SECTIONS = []


def section(*a):
    s = Section(*a)
    SECTIONS.append(s)
    return s


def load(data, name):
    return json.load(open(os.path.join(data, name), encoding="utf-8"))


# ---------------------------------------------------------------------------------------------------
# Shared property mapping (objects, egos, artifacts, shapes)

FLAG_TO_RESIST = dict(OI.FLAG_RESISTS, PROT_STUN="stun")
FLAG_TO_ABILITY = dict(OI.FLAG_ABILITIES)
EXTRA_KIND_FLAGS = {"BURNS_OUT": "BURNS_OUT", "TAKES_FUEL": "REFUELABLE", "INSTA_ART": "INSTA_ART", "EXPLODE": "EXPLODE",
                    "BLESSED": "BLESSED", "NO_FUEL": "NO_FUEL", "STICKY": "STICKY",
                    "GOOD": "GOOD"}
# Ego kind flags asking for a random extra power, in AVABand's spelling (EgoItemDef.RandomPower).
RANDOM_POWERS = {"RAND_SUSTAIN": "sustain", "RAND_POWER": "power", "RAND_HI_RES": "high_resist",
                 "RAND_BASE_RES": "base_resist", "RAND_RES_POWER": "resist_or_power"}
SLAY_FLAGS = set(EA.SLAY_NAMES)
BRAND_ELEMS = {k: v[0] for k, v in EA.BRANDS.items()}


def props_42(e, sec, *, abilities_skip=(), values_key="values", flags_key="flags"):
    """4.2.5 values/flags/slay/brand/curse lines -> AVABand's vocabulary. Properties AVABand has no
    spelling for are counted in sec.unmodelled."""
    p = {"mods": {}, "resists": set(), "flags": set(), "ignore": set(), "curses": set(), "slays": set(),
         "brands": set(), "immunities": set()}
    for line in get(e, values_key):
        for part in (x.strip() for x in line.split("|")):
            m = re.fullmatch(r"([A-Z_]+)\[(.+)\]", part)
            if not m:
                continue
            key, val = m.groups()
            if key in OI.RESISTS:
                lvl = int(val) if re.fullmatch(r"-?\d+", val) else 1
                if lvl >= 3:
                    p["immunities"].add(OI.RESISTS[key])
                elif lvl < 0:
                    sec.unmodelled[f"{key}[{lvl}] vulnerability"] += 1
                else:
                    p["resists"].add(OI.RESISTS[key])
            elif key in OI.MODIFIERS:
                p["mods"][OI.MODIFIERS[key]] = canon_rv(val)
            else:
                sec.unmodelled[f"values {key}"] += 1
    for f in flag_list(e, flags_key):
        if f in FLAG_TO_RESIST:
            p["resists"].add(FLAG_TO_RESIST[f])
        elif f in FLAG_TO_ABILITY:
            if f not in abilities_skip:
                p["flags"].add(FLAG_TO_ABILITY[f])
        elif f.startswith("IGNORE_"):
            p["ignore"].add(f[7:].lower())
        elif re.fullmatch(r"LIGHT_\d", f):
            p["mods"]["light"] = canon_rv(f[6:])
        elif re.fullmatch(r"DIG_\d", f):
            p["mods"]["tunnel"] = canon_rv(f[4:])
        elif f in EXTRA_KIND_FLAGS:
            p["flags"].add(EXTRA_KIND_FLAGS[f])
        elif f in RANDOM_POWERS:
            p["random"] = RANDOM_POWERS[f]
        else:
            sec.unmodelled[f"flag {f}"] += 1
    p["cursePowers"] = {}
    for line in get(e, "curse"):
        cname, _, power = line.partition(":")
        if OI.CURSES.get(cname):
            p["curses"].add(OI.CURSES[cname])
            p["cursePowers"][OI.CURSES[cname]] = int(power or 0)
        else:
            sec.unmodelled[f"curse {cname}"] += 1
    for line in get(e, "slay"):
        flag, _, mult = line.partition("_")
        if flag in SLAY_FLAGS:
            p["slays"].add(f"{flag}x{int(mult or 2)}")
        else:
            sec.unmodelled[f"slay {line}"] += 1
    for line in get(e, "brand"):
        elem, _, mult = line.partition("_")
        if elem in BRAND_ELEMS:
            p["brands"].add(f"{BRAND_ELEMS[elem]}x{int(mult or 3)}")
        else:
            sec.unmodelled[f"brand {line}"] += 1
    return p


def props_ours(o):
    mods = {k: canon_rv(v) for k, v in (o.get("modifiers") or {}).items()}
    for k, v in (o.get("rolls") or {}).items():
        if k not in ("to_h", "to_d", "to_a"):
            mods[k] = canon_rv(v)
    return {"mods": mods, "resists": set(o.get("resists") or []), "flags": set(o.get("flags") or []),
            "ignore": set(o.get("ignore") or []), "curses": set(o.get("curses") or []),
            "slays": {f"{s['monsterFlag']}x{s.get('multiplier', 2)}" for s in o.get("slays") or []},
            "brands": {f"{b['element']}x{b.get('multiplier', 3)}" for b in o.get("brands") or []},
            "immunities": set(o.get("immunities") or []), "cursePowers": dict(o.get("cursePowers") or {})}


def cmp_props(sec, entry, ours, theirs, keys=("mods", "resists", "flags", "ignore", "curses", "slays", "brands",
                                              "immunities")):
    if "curses" in keys and theirs.get("cursePowers") is not None:
        sec.cmp(entry, "curse powers", ours.get("cursePowers") or {}, theirs["cursePowers"])
    for k in keys:
        if k == "mods":
            o, t = ours["mods"], theirs["mods"]
            for m in sorted(set(o) | set(t)):
                sec.cmp(entry, f"modifier {m}", o.get(m, "0"), t.get(m, "0"))
        else:
            sec.cmp_set(entry, {"resists": "resists/protections", "flags": "ability flags"}.get(k, k), ours[k], theirs[k])


# ---------------------------------------------------------------------------------------------------
# Monsters

def innate_spells(gd):
    """RST_INNATE spells, from src/list-mon-spells.h beside the gamedata if present, else the importer's list."""
    path = os.path.normpath(os.path.join(gd, "..", "..", "src", "list-mon-spells.h"))
    if os.path.exists(path):
        out = set()
        for line in open(path, encoding="utf-8"):
            m = re.match(r"\s*RSF\((\w+),\s*(.*)\)", line)
            if m and "RST_INNATE" in m.group(2):
                out.add(m.group(1))
        return out, path
    return set(MI.INNATE), None


def friends_42(e, all_names, by_name, aliases):
    """4.2.5 friends/friends-base lines, with races resolved to AVABand ids through AVABand's names
    (Angband's lookup_monster: exact name, else the first race whose name contains it)."""
    out = []
    for key, value in e["lines"]:
        if key not in ("friends", "friends-base"):
            continue
        parts = value.split(":")
        chance, dice, target = int(parts[0]), parts[1], parts[2]
        role = parts[3] if len(parts) > 3 else None
        if key == "friends-base":
            out.append({"chance": chance, "number": dice, "base": target})
            continue
        if target.lower() == "same":
            race = "same"
        else:
            exact = next((n for n in all_names if n.lower() == target.lower()), None)
            name = exact or next((n for n in all_names if target.lower() in n.lower()), None)
            if name is None:
                continue
            m = by_name.get(norm_name(name))
            race = m["id"] if m else aliases.get(norm_name(name), slug(name))
        out.append({"chance": chance, "number": dice, "race": race, "role": role})
    return out


def compare_monsters(gd, data):
    sec = section("monsters", "Monsters", "monsters.json", "monster.txt (+ monster_base.txt)")
    bases = {b["name"]: {"glyph": one(b, "glyph", "?"), "flags": flag_list(b, "flags")}
             for b in parse_records(os.path.join(gd, "monster_base.txt"))}
    entries = [e for e in parse_records(os.path.join(gd, "monster.txt")) if not e["name"].startswith("<")]
    all_names = [e["name"] for e in entries]
    ours = load(data, "monsters.json")
    by_name = {norm_name(m["name"]): m for m in ours}
    by_id = {m["id"]: m for m in ours}
    blow_effects = {b["id"] for b in load(data, "blow_effects.json")}
    blow_methods = {b["id"] for b in load(data, "blow_methods.json")}
    our_spells = {s["id"] for s in load(data, "monster_spells.json")}
    innate, innate_src = innate_spells(gd)
    aliases = {norm_name(k): v for k, v in MI.ALIASES.items()}
    used = set()
    for e in entries:
        n = norm_name(e["name"])
        m = by_name.get(n) or by_id.get(slug(e["name"])) or by_id.get(aliases.get(n, ""))
        if m is None:
            sec.only_theirs.append(f"{e['name']} (depth {one(e, 'depth', '?')})")
            continue
        used.add(m["id"])
        sec.compared += 1
        label = m["name"] if norm_name(m["name"]) == n else f"{m['name']} (4.2.5: {e['name']})"
        base = bases.get(one(e, "base"), {})
        glyph = one(e, "glyph") or base.get("glyph")
        col = one(e, "color", "w")
        depth = int(one(e, "depth", "0"))
        sec.cmp(label, "base glyph/glyph", m.get("glyph"), glyph)
        sec.cmp(label, "base", m.get("base", ""), slug(one(e, "base", "")))
        sec.cmp(label, "colour", m.get("color", "White"), MI.COLORS.get(col[0], f"?{col}"))
        sec.cmp(label, "depth", m.get("depth", 0), depth)
        sec.cmp(label, "rarity", m.get("rarity", 1), int(one(e, "rarity", "1")))
        sec.cmp(label, "speed (+110)", m.get("speed", 0), int(one(e, "speed", "110")) - 110)
        sec.cmp(label, "hit points", m.get("hitPoints", 1), int(one(e, "hit-points", "1")))
        sec.cmp(label, "experience", m.get("experience", 0), int(one(e, "experience", "0")))
        sec.cmp(label, "armour class", m.get("armour", 0), int(one(e, "armor-class", "0")))
        sec.cmp(label, "sleepiness", m.get("sleep", 0), int(one(e, "sleepiness", "0")))
        # 4.2.5 zero-allocates the race: a missing hearing/smell line means 0. AVABand's loader defaults
        # hearing to 20 and smell to 0 when the JSON omits them.
        sec.cmp(label, "hearing", m.get("hearing", 20), int(one(e, "hearing", "0")), "hearing" not in m)
        sec.cmp(label, "smell", m.get("smell", 0), int(one(e, "smell", "0")), "smell" not in m)
        sec.cmp(label, "spell power", m.get("spellPower") or m.get("depth", 0),
                int(one(e, "spell-power", str(depth))))

        # Blows
        theirs_blows = []
        for b in get(e, "blow"):
            parts = b.split(":")
            meth = parts[0].lower()
            if meth not in blow_methods:
                sec.unmodelled[f"blow method {parts[0]}"] += 1
                meth = "hit" if meth not in MI.METHODS else meth
            eff = parts[1] if len(parts) > 1 else ""
            if not eff:
                effect = "none"
            elif eff.lower() in blow_effects:
                effect = eff.lower()
            else:
                sec.unmodelled[f"blow effect {eff}"] += 1
                effect = MI.EFFECTS.get(eff, "hurt")
            theirs_blows.append(f"{meth}:{effect}:{canon_rv(parts[2] if len(parts) > 2 else '')}")
        ours_blows = [f"{b.get('method')}:{b.get('effect')}:{canon_rv(b.get('damage'))}" for b in m.get("blows") or []]
        if ours_blows != theirs_blows:
            if len(ours_blows) == len(theirs_blows):
                for i, (a, b) in enumerate(zip(ours_blows, theirs_blows)):
                    if a != b:
                        sec.diff(label, f"blow {i + 1}", a, b)
            else:
                sec.diff(label, "blows", ours_blows, theirs_blows)

        # Flags: base flags + own flags - flags-off, as Angband merges them.
        off = set(flag_list(e, "flags-off"))
        tflags = {f for f in base.get("flags", []) + flag_list(e, "flags") if f not in off}
        sec.cmp_set(label, "flags", m.get("flags") or [], tflags)

        # Spells
        tspells = set()
        for s in flag_list(e, "spells"):
            if s in our_spells:
                tspells.add(s)
            else:
                sec.unmodelled[f"spell {s}"] += 1
        ospells = set(m.get("spells") or [])
        sec.cmp_set(label, "spells", ospells, tspells)
        # Frequencies only matter when the race has spells of that kind (4.2 defaults them to 4).
        t_inn = any(s in innate for s in tspells)
        t_spl = any(s not in innate for s in tspells)
        o_inn = any(s in innate for s in ospells)
        o_spl = any(s not in innate for s in ospells)
        if t_inn and o_inn:
            sec.cmp(label, "innate frequency (1 in N)", m.get("innateFrequency", 0),
                    int(one(e, "innate-freq", "0")) or 4)
        if t_spl and o_spl:
            sec.cmp(label, "spell frequency (1 in N)", m.get("spellFrequency", 0),
                    int(one(e, "spell-freq", "0")) or 4)

        # Plural and friends
        if one(e, "plural") or m.get("plural"):
            sec.cmp(label, "plural", m.get("plural"), one(e, "plural"))
        tf = []
        for f in friends_42(e, all_names, by_name, aliases):
            who = f.get("race") or "base:" + f.get("base", "?")
            tf.append(f"{f['chance']}%:{canon_rv(f['number'])}:{who}" + (":bodyguard" if f.get("role") == "bodyguard" else ""))
        # friends lines naming a race no one has (lookup fails) are dropped by the importer too.
        of = [f"{f.get('chance')}%:{canon_rv(f.get('number'))}:{f.get('race') or 'base:' + str(f.get('base'))}"
              + (":bodyguard" if f.get("role") == "bodyguard" else "") for f in m.get("friends") or []]
        if sorted(of) != sorted(tf):
            sec.diff(label, "friends", sorted(set(of) - set(tf)) or "[]", sorted(set(tf) - set(of)) or "[]")
    for m in ours:
        if m["id"] not in used:
            sec.only_ours.append(f"{m['name']} ({m['id']})")
    sec.notes += [
        "Not compared: `light` (4.2.5 monster light radius; AVABand has no such field), `desc`, `mimic`, "
        "`shape`, `drop`/`drop-base`, `friends` chance for races the lookup can't resolve, `innate-freq`/"
        "`spell-freq` when either side has no spells of that kind, spell messages.",
        "Speed is compared as 4.2.5 `speed` − 110. Flags are compared after merging the monster base's flags "
        "and removing `flags-off`, as 4.2.5 does.",
        "Blow effects/methods and spells that AVABand's blow_effects.json/blow_methods.json/monster_spells.json "
        "don't define are mapped the importer's way (e.g. SHATTER → hurt) and counted under unmodelled.",
        "Innate spells: " + (f"read from {innate_src}" if innate_src else "importer's INNATE list (list-mon-spells.h not found)") + ".",
    ]
    return sec


# ---------------------------------------------------------------------------------------------------
# Objects (object.txt kinds + class.txt books)

TYPE_TO_BASE = dict(OI.TYPES, light="light", flask="flask", gold="gold", chest="chest")
TYPE_TO_BASE.update({"magic book": "magic_book", "prayer book": "prayer_book", "nature book": "nature_book",
                     "shadow book": "shadow_book"})
BOW_FAMILY = {"sling": "bow", "crossbow": "bow"}
WEAPONY = {"sword", "hafted", "polearm", "digger", "arrow", "bolt", "shot", "flask"}
AMMO = {"arrow", "bolt", "shot"}


def family(base):
    return BOW_FAMILY.get(base, base)


def class_books(gd):
    """Book kinds defined in class.txt: name -> dict(tval, dungeon, properties)."""
    books = collections.OrderedDict()
    cur = None
    for c in parse_records(os.path.join(gd, "class.txt")):
        for k, v in c["lines"]:
            if k == "book":
                tval, where, name, _n, realm = v.split(":")
                cur = books.setdefault(name, {"tval": tval, "dungeon": where == "dungeon", "realm": realm,
                                              "props": None, "graphics": None, "classes": []})
                cur["classes"].append(c["name"])
            elif k == "book-properties" and cur is not None:
                cur["props"] = v           # last one parsed wins, as in 4.2.5 (it overwrites the kind)
            elif k == "book-graphics" and cur is not None:
                cur["graphics"] = v
    return books


def compare_objects(gd, data):
    sec = section("objects", "Object kinds", "objects.json", "object.txt (+ book lines of class.txt)")
    ours = load(data, "objects.json")
    idx = {}
    for o in ours:
        idx.setdefault((family(o["base"]), norm_name(o["name"])), o)
    used = set()
    theirs = []
    for e in parse_records(os.path.join(gd, "object.txt")):
        t = one(e, "type")
        if t == "none" or e["name"].startswith("<"):
            continue
        theirs.append((e, t))
    for name, b in class_books(gd).items():
        if not b["props"]:
            continue
        cost, common, rng = b["props"].split(":")
        e = {"name": name, "lines": [("type", b["tval"]), ("level", rng.split(" to ")[0].strip()),
                                     ("weight", "30"), ("cost", cost), ("alloc", f"{common}:{rng}")]}
        theirs.append((e, b["tval"]))
    for e, t in theirs:
        base = TYPE_TO_BASE.get(t)
        if base is None:
            sec.unmodelled[f"type {t}"] += 1
            continue
        o = idx.get((family(base), norm_name(e["name"])))
        if o is None:
            sec.only_theirs.append(f"{t}: {e['name']}")
            continue
        used.add(o["id"])
        sec.compared += 1
        label = f"{o['id']} ({t}: {norm_name(e['name'])})"
        ob = o["base"]
        sec.cmp(label, "level", o.get("level", 0), int(one(e, "level", "0")))
        sec.cmp(label, "cost", o.get("cost", 0), int(one(e, "cost", "0")), "cost" not in o)
        sec.cmp(label, "weight (1/10 lb)", o.get("weight", 0), int(one(e, "weight", "0")), "weight" not in o)
        alloc = one(e, "alloc")
        if alloc:
            common, rng = alloc.split(":", 1)
            lo, _, hi = rng.partition(" to ")
            common, lo, hi = int(common), int(lo), int(hi or lo)
        else:
            common, lo, hi = 0, None, None
        sec.cmp(label, "commonness", o.get("commonness", 10), common, "commonness" not in o)
        if common and o.get("commonness", 10):
            # AVABand generates a kind across minDepth..maxDepth (as 4.2.5 its alloc range), or from
            # its level when it has no minDepth.
            if "minDepth" in o:
                sec.cmp(label, "alloc min depth (minDepth)", o["minDepth"], lo)
            else:
                sec.cmp(label, "alloc min depth (ours has no minDepth: level gates it)", o.get("level", 0), lo)
            omax = o.get("maxDepth", 127)
            if not (omax >= 100 and hi >= 100):
                sec.cmp(label, "alloc max depth", omax, hi, "maxDepth" not in o)

        # Combat
        attack = (one(e, "attack") or "").split(":") + ["", "", ""]
        armor = (one(e, "armor") or "").split(":") + ["", ""]
        if ob in WEAPONY or "damage" in o:
            sec.cmp(label, "damage dice", canon_rv(o.get("damage")), canon_rv(attack[0]))
        rolls = o.get("rolls") or {}
        sec.cmp(label, "to-hit", canon_rv(rolls.get("to_h", o.get("toHit", 0))), canon_rv(attack[1]))
        sec.cmp(label, "to-dam", canon_rv(rolls.get("to_d", o.get("toDam", 0))), canon_rv(attack[2]))
        sec.cmp(label, "armour", o.get("armour", 0), int(armor[0] or 0))
        sec.cmp(label, "to-ac", canon_rv(rolls.get("to_a", o.get("toAc", 0))), canon_rv(armor[1]))

        # Properties
        tp = props_42(e, sec)
        op = props_ours(o)
        pval = one(e, "pval")
        if base == "bow":
            sec.cmp(label, "multiplier (pval)", o.get("multiplier", 0), int(pval or 0))
        elif base in ("flask", "light") and pval:
            sec.cmp(label, "fuel (pval)", o.get("fuel", 0), int(pval))
        elif pval:
            sec.unmodelled[f"pval on {t}"] += 1
        cmp_props(sec, label, op, tp)
        sec.cmp(label, "charges", canon_rv(o.get("charges")), canon_rv(one(e, "charges")))
        sec.cmp(label, "recharge time", canon_rv(o.get("recharge")), canon_rv(one(e, "time")))
        sec.cmp(label, "power", o.get("power", 0), int(one(e, "power", "0")), "power" not in o)
        pile = one(e, "pile")
        chance, _, stack = (pile or "100:1").partition(":")
        sec.cmp(label, "pile number (stackSize)", canon_rv(o.get("stackSize", "1")), canon_rv(stack))
        sec.cmp(label, "pile chance (pileChance)", o.get("pileChance", 100), int(chance))

        # Effects / activation, via the importer's translation of the effect lines.
        if get(e, "effect"):
            eff, missing = OI.effects(e)
            field = "activation" if (o.get("activation") or ob not in (
                "potion", "scroll", "food", "mushroom", "wand", "staff", "rod", "flask")) else "effect"
            ours_eff = o.get("activation") if field == "activation" else o.get("effect")
            cmp_effects(sec, gd, label, field, ours_eff, eff, missing, bool(get(e, "expr")))
        elif o.get("effect") or o.get("activation"):
            sec.diff(label, "effect/activation", o.get("effect") or o.get("activation"), None)
    for o in ours:
        if o["id"] not in used:
            tag = " [INSTA_ART special kind: 4.2.5 makes these from artifact.txt]" if "INSTA_ART" in (o.get("flags") or []) else ""
            sec.only_ours.append(f"{o['id']} ({o['base']}: {o['name']}){tag}")
    sec.notes += [
        "Matched by (base, name) ignoring '&', '~', case and accents; sling/bow/crossbow all match 4.2.5 `bow`.",
        "Weight is in tenths of a pound on both sides, compared directly.",
        "Allocation: AVABand generates a kind across minDepth..maxDepth, as 4.2.5 does its alloc range (level "
        "plays no part); without a minDepth, from its level. A missing maxDepth is 127 (the C# default). "
        "A max depth of 100 or more on both sides is treated as equal. Depths aren't compared when either side's "
        "commonness is 0.",
        "Damage dice are compared for weapons, ammo, diggers, flasks and anything ours gives damage to; the 4.2.5 "
        "`attack:1d1`/`1d2` placeholders on wands/staffs/rods are ignored.",
        "Effects/activations are compared part by part (keyed by verb and subject, e.g. `heal`, `cure:blind`, "
        "`timed:fast`, `bolt:fire`) against the importer's translation of the 4.2.5 effect/dice lines (`effects()` "
        "in angband_object_import.py). Parts only in ours that the importer can never produce (e.g. `cure:amnesia`, "
        "`timed:bold`) are AVABand extensions and are tallied, not reported. `$` expressions are evaluated at the "
        "importer's stand-in values (player level/spell power 20), and teleport `M60` becomes 60, so dice with "
        "expressions may differ spuriously.",
        "Book kinds come from class.txt `book`/`book-properties` (level = alloc minimum, weight 30, as 4.2.5's "
        "init.c sets them).",
        "Not compared: `graphics` (AVABand colours objects per base/flavour), `desc`, `msg`, `effect-yx`, "
        "`pval` other than launcher multiplier and fuel.",
    ]
    return sec


# ---------------------------------------------------------------------------------------------------
# Egos

def ego_bases(e):
    bases = []
    for t in get(e, "type"):
        if t in TYPE_TO_BASE:
            bases.append(TYPE_TO_BASE[t])
    for it in get(e, "item"):
        t = it.split(":")[0]
        if t in TYPE_TO_BASE and TYPE_TO_BASE[t] not in bases:
            bases.append(TYPE_TO_BASE[t])
    out = set()
    for b in bases:
        out |= {"sling", "bow", "crossbow"} if b == "bow" else {b}
    return out


def kind_ids(data):
    """(base family, normalised name) -> our kind id, for ego `item:` lines."""
    return {(family(k["base"]), norm_name(k["name"])): k["id"] for k in load(data, "objects.json")}


def ego_kinds(e, kinds):
    """An ego's `item:` lines as our kind ids."""
    out = []
    for it in get(e, "item"):
        t, _, name = it.partition(":")
        base = TYPE_TO_BASE.get(t)
        if base and (family(base), norm_name(name)) in kinds:
            out.append(kinds[(family(base), norm_name(name))])
    return out


def ego_type_bases(e):
    """The bases on an ego's `type:` lines (a launcher ego covers slings, bows and crossbows)."""
    out = []
    for t in get(e, "type"):
        b = TYPE_TO_BASE.get(t)
        for x in (["sling", "bow", "crossbow"] if b == "bow" else [b] if b else []):
            if x not in out:
                out.append(x)
    return out


def ego_minimums(e):
    """min-combat (255 = none) and min-values, as AVABand's EgoItemDef.Minimums."""
    out = {}
    combat = one(e, "min-combat")
    if combat:
        for key, v in zip(("to_h", "to_d", "to_a"), combat.split(":")):
            if v.strip() and int(v) != 255:
                out[key] = int(v)
    for line in get(e, "min-values"):
        for part in (x.strip() for x in line.split("|")):
            m = re.fullmatch(r"([A-Z_]+)\[(-?\d+)\]", part)
            if m and m.group(1) in OI.MODIFIERS:
                out[OI.MODIFIERS[m.group(1)]] = int(m.group(2))
    return out


def ego_flags_off(e):
    return sorted(EXTRA_KIND_FLAGS.get(f, FLAG_TO_ABILITY.get(f, f)) for f in flag_list(e, "flags-off"))


def compare_egos(gd, data):
    sec = section("egos", "Ego items", "egos.json", "ego_item.txt")
    ours = load(data, "egos.json")
    theirs = parse_records(os.path.join(gd, "ego_item.txt"))
    kinds = kind_ids(data)
    base_of = {k["id"]: k["base"] for k in load(data, "objects.json")}
    by_name = collections.defaultdict(list)
    for o in ours:
        by_name[o["name"].lower()].append(o)

    def all_bases(o):
        return set(o.get("bases", [])) | {base_of[k] for k in o.get("kinds", []) if k in base_of}
    used = set()
    for e in theirs:
        tb = ego_bases(e)
        cands = [o for o in by_name.get(e["name"].lower(), []) if o["id"] not in used]
        best = max(cands, key=lambda o: len(all_bases(o) & tb), default=None)
        if best is None or not (all_bases(best) & tb or (not tb and not all_bases(best))):
            sec.only_theirs.append(f"{e['name']} ({', '.join(sorted(tb))})")
            continue
        o = best
        used.add(o["id"])
        sec.compared += 1
        label = f"{o['id']} ({e['name']} on {', '.join(sorted(tb))})"
        sec.cmp_set(label, "bases", o.get("bases", []), ego_type_bases(e))
        sec.cmp_set(label, "kinds (item: lines)", o.get("kinds", []), ego_kinds(e, kinds))
        sec.cmp(label, "minimums (min-combat, min-values)", o.get("minimums") or {}, ego_minimums(e))
        sec.cmp_set(label, "flags off", o.get("flagsOff", []), ego_flags_off(e))
        alloc = one(e, "alloc", "0:1 to 127")
        common, rng = alloc.split(":", 1)
        lo, _, hi = rng.partition(" to ")
        sec.cmp(label, "level (alloc min)", o.get("level", 0), int(lo))
        sec.cmp(label, "commonness", o.get("commonness", 10), int(common))
        sec.cmp(label, "alloc max depth", o.get("maxDepth", 127), int(hi or 127), "maxDepth" not in o)
        combat = (one(e, "combat") or "0:0:0").split(":")
        for key, name, text in zip(("toHit", "toDam", "toAc"), ("to-hit", "to-dam", "to-ac"), combat):
            r = (o.get("rolls") or {}).get({"toHit": "to_h", "toDam": "to_d", "toAc": "to_a"}[key])
            sec.cmp(label, f"{name} (random)", canon_rv(r if r is not None else o.get(key)), canon_rv(text))
        tp = props_42(e, sec, abilities_skip=("THROWING",))
        sec.cmp(label, "random power (RAND_*)", o.get("randomPower"), tp.get("random"))
        cmp_props(sec, label, props_ours(o), tp)
    for o in ours:
        if o["id"] not in used:
            sec.only_ours.append(f"{o['id']} ({o['name']} on {', '.join(o.get('bases', []))})")
    sec.notes += [
        "4.2.5 egos sharing a name (e.g. `of Speed` for boots and for dragon armour) are each paired with the "
        "AVABand ego of that name whose bases overlap most; an AVABand ego covering several of them is compared "
        "against each, and only bases missing from ours are reported.",
        "Level = alloc minimum and maxDepth = alloc maximum (the importer's convention); commonness = alloc "
        "commonness.",
        "Not compared (no AVABand field): `info` cost and rating, `desc`.",
    ]
    return sec


# ---------------------------------------------------------------------------------------------------
# Artifacts

def activation_table(gd):
    table = {}
    for a in parse_records(os.path.join(gd, "activation.txt")):
        eff, missing = OI.effects(a)
        table[a["name"]] = {"effect": eff, "missing": missing, "power": int(one(a, "power", "0")),
                            "expr": bool(get(a, "expr"))}
    return table


def compare_artifacts(gd, data):
    sec = section("artifacts", "Artifacts", "artifacts.json", "artifact.txt (+ activation.txt)")
    ours = load(data, "artifacts.json")
    kinds = {k["id"]: k for k in load(data, "objects.json")}
    acts = activation_table(gd)
    by_name = {norm_name(a["name"]): a for a in ours}
    used = set()
    for e in parse_records(os.path.join(gd, "artifact.txt")):
        a = by_name.get(norm_name(e["name"]))
        btype, _, bname = one(e, "base-object", ":").partition(":")
        if a is None:
            sec.only_theirs.append(f"{e['name']} ({btype}: {bname})")
            continue
        used.add(a["id"])
        sec.compared += 1
        label = f"{a['id']} ({e['name']})"
        kind = kinds.get(a.get("kind"))
        base = TYPE_TO_BASE.get(btype)
        if kind is None:
            sec.diff(label, "kind", f"{a.get('kind')} (not in objects.json)", f"{btype}: {bname}")
        elif (family(kind["base"]), norm_name(kind["name"])) != (family(base or "?"), norm_name(bname)):
            sec.diff(label, "kind", f"{kind['base']}: {norm_name(kind['name'])}", f"{btype}: {norm_name(bname)}")
        alloc = one(e, "alloc", "0:0 to 127")
        chance, rng = alloc.split(":", 1)
        lo = int(rng.partition(" to ")[0])
        lvl = int(one(e, "level", "0"))
        if a.get("level", 0) != lo:
            extra = " (= 4.2.5 `level:`)" if a.get("level", 0) == lvl else ""
            sec.diff(label, "level (alloc min)", f"{a.get('level', 0)}{extra}", f"{lo} (level: {lvl})")
        hi = int(rng.partition(" to ")[2] or 127)
        sec.cmp(label, "alloc chance (allocChance)", a.get("allocChance", 10), int(chance))
        sec.cmp(label, "alloc max depth (maxDepth)", a.get("maxDepth", 127), hi)
        if kind is not None:
            sec.cmp(label, "weight (its own, or its kind's)", a.get("weight", kind.get("weight", 0)), int(one(e, "weight", "0")))
        attack = (one(e, "attack") or "0d0:0:0").split(":") + ["0", "0"]
        armor = (one(e, "armor") or "0:0").split(":") + ["0"]
        if base in WEAPONY or "damage" in a:
            ours_dmg = a.get("damage") or (kind or {}).get("damage")
            sec.cmp(label, "damage dice (ours: artifact or kind)", canon_rv(ours_dmg), canon_rv(attack[0]))
        sec.cmp(label, "to-hit", canon_rv(a.get("toHit", 0)), canon_rv(attack[1]))
        sec.cmp(label, "to-dam", canon_rv(a.get("toDam", 0)), canon_rv(attack[2]))
        ours_ac = a.get("armour") or (kind or {}).get("armour", 0)
        sec.cmp(label, "armour (ours: artifact or kind)", ours_ac, int(armor[0] or 0))
        sec.cmp(label, "to-ac", canon_rv(a.get("toAc", 0)), canon_rv(armor[1]))
        tp = props_42(e, sec, abilities_skip=("THROWING",))
        op = props_ours(a)
        # Artifact ignore flags: ours has no `ignore` on artifacts (they ignore everything in 4.2.5 anyway).
        tp["ignore"] = set()
        cmp_props(sec, label, op, tp)
        act = one(e, "act")
        if act:
            info = acts.get(act)
            if info is None:
                sec.diff(label, "activation", a.get("activation"), f"{act} (not in activation.txt)")
            else:
                if info["missing"] and not a.get("activation"):
                    sec.diff(label, "activation", None, f"{act} [untranslatable: {', '.join(info['missing'])}]")
                cmp_effects(sec, gd, label, f"activation ({act})", a.get("activation"), info["effect"], info["missing"],
                            info["expr"])
                sec.cmp(label, "activation power", a.get("activationPower", 0), info["power"])
            sec.cmp(label, "recharge time", canon_rv(a.get("recharge")), canon_rv(one(e, "time")))
        elif a.get("activation"):
            sec.diff(label, "activation", a.get("activation"), None)
    for a in ours:
        if a["id"] not in used:
            sec.only_ours.append(f"{a['id']} ({a['name']})")
    sec.notes += [
        "Matched by name. Level is compared with the alloc minimum (the importer's convention, and what "
        "AVABand's artifact roll uses); when ours instead equals 4.2.5's `level:` line it says so.",
        "Allocation: level, allocChance and maxDepth are 4.2.5's alloc minimum, chance and maximum, as "
        "ObjectFactory.TryMakeArtifact and MakeSpecialArtifact roll them.",
        "Damage and armour: an artifact without its own falls back to its kind's, as ObjectFactory.ApplyArtifact "
        "does, so the effective values are compared.",
        "Activations are compared against the importer's translation of activation.txt (see Objects).",
        "Not compared: `cost` (AVABand artifacts have none), `graphics`, `msg`, `desc`, IGNORE_* flags "
        "(ours has no per-artifact ignore list).",
    ]
    return sec


# ---------------------------------------------------------------------------------------------------
# Classes, class spells, races

STAT_ORDER = ["str", "int", "wis", "dex", "con"]
SKILL_MAP = [("disarm", "skill-disarm-phys"), ("disarm_magic", "skill-disarm-magic"), ("search", "skill-search"),
             ("device", "skill-device"), ("save", "skill-save"),
             ("stealth", "skill-stealth"), ("melee", "skill-melee"), ("bow", "skill-shoot"),
             ("throw", "skill-throw"), ("dig", "skill-dig")]
REV_RESIST = {v: k for k, v in OI.RESISTS.items()}
REV_RESIST.update({v: k for k, v in FLAG_TO_RESIST.items()})


# 4.2.5 birth options that leave a kit line out (eopts), by AVABand's option id.
KIT_OPTIONS = {"birth_no_recall": "birth_no_recall"}


def class_kit_42(e, kinds):
    """4.2.5's equip lines as AVABand kit entries (kind, least, most, option), or a note of what didn't resolve."""
    out = []
    for v in get(e, "equip"):
        tval, name, lo, hi, eopt = (v.split(":") + ["none"])[:5]
        base = TYPE_TO_BASE.get(tval)
        kid = kinds.get((family(base or "?"), norm_name(name)))
        out.append((kid or f"{tval}:{name} (no such kind)", int(lo), int(hi), None if eopt == "none" else KIT_OPTIONS.get(eopt, eopt)))
    return out


def player_tokens_42(e):
    toks = set()
    for f in flag_list(e, "obj-flags") + flag_list(e, "player-flags"):
        toks.add(f)
    for line in get(e, "values"):
        for part in (x.strip() for x in line.split("|")):
            m = re.fullmatch(r"([A-Z_]+)\[(.+)\]", part)
            if m:
                toks.add(m.group(1) if m.group(2) == "1" else f"{m.group(1)}[{m.group(2)}]")
    return toks


def player_tokens_ours(o):
    toks = set()
    for r in o.get("resists") or []:
        toks.add(REV_RESIST.get(r, r.upper()))
    for f in o.get("flags") or []:
        toks.add({"REGENERATE": "REGEN"}.get(f, f))
    return toks


def compare_classes_and_spells(gd, data):
    csec = section("classes", "Classes", "classes.json", "class.txt")
    ssec = section("spells", "Class spells", "spells.json", "class.txt (spell lines)")
    rsec = section("races", "Races", "races.json", "p_race.txt")
    classes = load(data, "classes.json")
    spells = load(data, "spells.json")
    by_id = {c["id"]: c for c in classes}
    used = set()
    seen_spells = set()
    for e in parse_records(os.path.join(gd, "class.txt")):
        c = by_id.get(slug(e["name"]))
        if c is None:
            csec.only_theirs.append(e["name"])
            continue
        used.add(c["id"])
        csec.compared += 1
        label = c["name"]
        stats = [int(x) for x in one(e, "stats", "0:0:0:0:0").split(":")]
        for s, v in zip(STAT_ORDER, stats):
            csec.cmp(label, f"stat {s}", (c.get("stats") or {}).get(s, 0), v)
        for ours_key, key in SKILL_MAP:
            base, per10 = (one(e, key, "0:0").split(":") + ["0"])[:2]
            csec.cmp(label, f"skill {ours_key} ({key})", (c.get("skills") or {}).get(ours_key, 0), int(base))
            csec.cmp(label, f"skill {ours_key} per 10 levels", (c.get("skillsPer10Levels") or {}).get(ours_key, 0), int(per10))
        csec.cmp(label, "hit die", c.get("hitDie", 0), int(one(e, "hitdie", "0")))
        ours_kit = sorted((k["kind"], k.get("count", 1), k.get("countMax", k.get("count", 1)), k.get("unlessOption"))
                          for k in c.get("startingKit") or [])
        csec.cmp(label, "starting kit (kind, least, most, unless option)", ours_kit,
                 sorted(class_kit_42(e, kind_ids(data)), key=lambda t: (t[0], t[1], t[2], t[3] or "")))
        # 4.2.5 has no class experience factor (only races have `exp`).
        csec.cmp(label, "experience factor (4.2.5 classes have none)", c.get("expFactor", 0), 0)
        tflags = player_tokens_42(e)
        if "NO_MANA" in tflags and not c.get("realm"):
            tflags.discard("NO_MANA")
            csec.unmodelled["NO_MANA (implicit in ours: no realm)"] += 1
        csec.cmp_set(label, "flags", player_tokens_ours(c), tflags)
        magic = one(e, "magic")
        realms = {v.split(":")[4] for v in get(e, "book")}
        if magic or c.get("realm"):
            csec.cmp(label, "first spell level (magic:)", c.get("firstSpellLevel"), int(magic.split(":")[0]) if magic else None)
            csec.cmp(label, "realm", c.get("realm"), ", ".join(sorted(realms)) or None)

        # Spells of this class
        ours_sp = {norm_name(s["name"]): s for s in spells if c["id"] in (s.get("classes") or {})}
        book = None
        for k, v in e["lines"]:
            if k == "book":
                book = v.split(":")[2]
                continue
            if k != "spell":
                continue
            name, lvl, mana, fail, exp = v.rsplit(":", 4)
            s = ours_sp.get(norm_name(name))
            slabel = f"{c['name']}: {name}"
            if s is None:
                ssec.only_theirs.append(f"{slabel} (level {lvl}, in {book})")
                continue
            seen_spells.add((c["id"], s["id"]))
            ssec.compared += 1
            cs = s["classes"][c["id"]]
            ssec.cmp(slabel, "level", cs.get("level"), int(lvl))
            ssec.cmp(slabel, "mana", cs.get("mana"), int(mana))
            ssec.cmp(slabel, "fail", cs.get("fail"), int(fail))
            ssec.cmp(slabel, "exp", cs.get("exp"), int(exp))
            ssec.cmp(slabel, "book", s.get("book"), slug(book.strip("[]")))
        for n, s in ours_sp.items():
            if (c["id"], s["id"]) not in seen_spells:
                ssec.only_ours.append(f"{c['name']}: {s['name']} ({s['id']}, book {s.get('book')})")
    for c in classes:
        if c["id"] not in used:
            csec.only_ours.append(c["id"])
    csec.notes += [
        "Skills: ours `disarm` is compared with 4.2.5 `skill-disarm-phys` and `disarm_magic` with "
        "`skill-disarm-magic`; `bow` = `skill-shoot`; `search` = `skill-search`; a skill missing from ours "
        "counts as 0.",
        "Flags: `player-flags` + `obj-flags` against ours `flags` (+ `resists`); NO_MANA is taken as implied by "
        "a class with no realm.",
        "Not compared: `max-attacks`, `min-weight`, `strength-multiplier` (AVABand's `blows` field is a "
        "different model), `magic` spell weight, titles, starting equipment (`equip` vs `startingKit`), "
        "`book-graphics`.",
    ]
    ssec.notes += [
        "Spells are matched per class by name. Book is compared as the book name slugged "
        "(`[Magic for Beginners]` → `magic_for_beginners`) against spells.json `book`.",
        "Not compared: spell effects, dice and expressions, descriptions (spells.json uses AVABand effect "
        "strings).",
    ]

    races = load(data, "races.json")
    rby = {r["id"]: r for r in races}
    rused = set()
    for e in parse_records(os.path.join(gd, "p_race.txt")):
        r = rby.get(slug(e["name"]))
        if r is None:
            rsec.only_theirs.append(e["name"])
            continue
        rused.add(r["id"])
        rsec.compared += 1
        label = r["name"]
        stats = [int(x) for x in one(e, "stats", "0:0:0:0:0").split(":")]
        for s, v in zip(STAT_ORDER, stats):
            rsec.cmp(label, f"stat {s}", (r.get("stats") or {}).get(s, 0), v)
        for ours_key, key in SKILL_MAP:
            rsec.cmp(label, f"skill {ours_key} ({key})", (r.get("skills") or {}).get(ours_key, 0), int(one(e, key, "0")))
        rsec.cmp(label, "hit die", r.get("hitDie", 0), int(one(e, "hitdie", "0")))
        rsec.cmp(label, "experience factor", r.get("expFactor", 0), int(one(e, "exp", "0")))
        rsec.cmp(label, "infravision", r.get("infravision", 0), int(one(e, "infravision", "0")))
        rsec.cmp_set(label, "flags/resists", player_tokens_ours(r), player_tokens_42(e))
    for r in races:
        if r["id"] not in rused:
            rsec.only_ours.append(r["id"])
    rsec.notes += [
        "Skills as for classes (disarm = `skill-disarm-phys`, disarm_magic = `skill-disarm-magic`, bow = "
        "`skill-shoot`, search = `skill-search`).",
        "Flags/resists: 4.2.5 `obj-flags`, `player-flags` and `values` (RES_x[1] → RES_x) against ours "
        "`resists` (mapped back to 4.2.5 names: acid → RES_ACID, sust_dex → SUST_DEX, hold_life → HOLD_LIFE, ...) "
        "and `flags` (REGENERATE → REGEN).",
        "Not compared: `history`, `age`, `height`, `weight`, `equip`, descriptions.",
    ]


# ---------------------------------------------------------------------------------------------------
# Shapes

SHAPE_SKILLS = (("skill-save", "save"), ("skill-melee", "melee"), ("skill-disarm-phys", "disarm"),
                ("skill-disarm-magic", "disarm_magic"), ("skill-stealth", "stealth"), ("skill-device", "device"))


def shape_props(e, sec):
    """A shape's properties: its obj-flags and values as for objects, and its player-flags (ROCK) as flags."""
    p = props_42(e, sec, flags_key="obj-flags")
    p["flags"] |= set(flag_list(e, "player-flags"))
    return p


def compare_shapes(gd, data):
    sec = section("shapes", "Shapes", "shapes.json", "shape.txt")
    ours = load(data, "shapes.json")
    by_id = {s["id"]: s for s in ours}
    used = set()
    for e in parse_records(os.path.join(gd, "shape.txt")):
        if e["name"] == "normal":
            continue
        sid = re.sub(r"[^a-z]+", "_", e["name"].lower()).strip("_")
        s = by_id.get(sid)
        if s is None:
            sec.only_theirs.append(e["name"])
            continue
        used.add(sid)
        sec.compared += 1
        label = s["name"]
        combat = [int(x) for x in (one(e, "combat") or "0:0:0").split(":")]
        for key, v in zip(("toHit", "toDam", "toAc"), combat):
            sec.cmp(label, key, s.get(key, 0), v)
        for key, skill in SHAPE_SKILLS:
            sec.cmp(label, f"skill {skill}", (s.get("skills") or {}).get(skill, 0), int(one(e, key, "0")))
        tp = shape_props(e, sec)
        op = props_ours(s)
        cmp_props(sec, label, op, tp, keys=("mods", "resists", "flags", "immunities"))
        sec.cmp(label, "blows (in order)", s.get("blows") or [], get(e, "blow"))
        eff, missing = OI.effects(e, level_exprs=True)
        cmp_effects(sec, gd, label, "effect", s.get("effect"), eff, missing, bool(get(e, "expr")))
    for s in ours:
        if s["id"] not in used:
            sec.only_ours.append(s["id"])
    sec.notes += ["Matched by the importer's id (Pukel-man → pukel_man). Modifiers/resists/flags as for objects "
                  "(obj-flags and values; player-flags such as ROCK are flags); PROT_STUN → resist `stun`. "
                  "Effects keep their dependence on the player's level or hit points ({L}, lose_hp_fraction), "
                  "as shapes apply them at the player's level.",
                  "Not compared: `change-msg`, `effect-msg`, `desc`."]
    return sec


# ---------------------------------------------------------------------------------------------------
# Traps

TRAP_ALIASES = {  # AVABand id -> 4.2.5 trap (second name field)
    "trap_door": "trap door", "pit": "pit", "spiked_pit": "spiked pit", "slow_dart": "slow dart",
    "weakness_dart": "strength loss dart", "fire_rune": "fire trap", "acid_rune": "acid trap",
    "poison_gas": "poison gas trap", "confusion_gas": "confusion gas trap", "sleep_gas": "sleep gas trap",
    "teleport_rune": "teleport rune", "summon_rune": "rune of summoning", "alarm": "siren",
    "glyph_warding": "glyph of warding", "web": "web",
}
DICE_TOKEN = re.compile(r"(?<![\w$])(?:\d+\+)?\d*d\d+(?:[+-]\d+)?(?![\w$])")


TRAPS_ELSEWHERE = {"decoy": "Level.Decoy (the Decoy spell)", "door lock": "a door's lock power (Square.LockPower)"}


def trap_colour(code):
    code = (code or "w").strip()
    return MI.COLORS.get(code) if len(code) == 1 else "".join(w.capitalize() for w in code.split())


def trap_dice_42(e):
    """4.2.5's dice/dice-xtra values, each with its own DUNGEON_LEVEL variables (an expr line follows the
    dice it belongs to) written as AVABand's {D...}."""
    lines = e["lines"]
    out = []
    for i, (k, d) in enumerate(lines):
        if k not in ("dice", "dice-xtra"):
            continue
        d = d.strip()
        for k2, v2 in lines[i + 1:]:
            if k2 in ("effect", "effect-xtra", "dice", "dice-xtra"):
                break
            if k2 == "expr":
                var, _, rest = v2.partition(":")
                base, _, ops = rest.partition(":")
                if base == "DUNGEON_LEVEL":
                    ops = ops.replace(" ", "")
                    d = d.replace("$" + var, "{D" + ("" if ops in ("+0", "") else ops) + "}")
        out.append(d)
    return out


def trap_radii_42(e):
    """The radii of 4.2.5's SPOT/BALL trap effects (and EARTHQUAKE's), in order; a radius-0 spot is ours
    `element` (it falls on you alone)."""
    out = []
    for k, v in e["lines"]:
        if k in ("effect", "effect-xtra"):
            parts = v.split(":")
            if parts[0] in ("SPOT", "BALL") and len(parts) > 2 and int(parts[2]) > 0:
                out.append(int(parts[2]))
            elif parts[0] == "EARTHQUAKE" and len(parts) > 2:
                out.append(int(parts[2]))
    return out


# Which argument of each AVABand trap verb is its amount (the dice), and which its radius.
TRAP_AMOUNT_ARG = {"damage": 0, "timed": 1, "timed_nores": 1, "element": 1, "spot": 2, "teleport": 0, "summon": 0,
                   "project_los": 1, "drain_light": 0, "drain_mana": 0}
TRAP_RADIUS_ARG = {"spot": 1, "earthquake": 0}


def trap_parts(effect):
    return [p.strip().split(":") for p in (effect or "").split(";") if p.strip()]


def trap_values(effect):
    """The amounts (dice) in an AVABand trap effect string."""
    return [a[TRAP_AMOUNT_ARG[a[0]] + 1] for a in trap_parts(effect)
            if a[0] in TRAP_AMOUNT_ARG and len(a) > TRAP_AMOUNT_ARG[a[0]] + 1]


def trap_radii(effect):
    return [int(a[TRAP_RADIUS_ARG[a[0]] + 1]) for a in trap_parts(effect)
            if a[0] in TRAP_RADIUS_ARG and len(a) > TRAP_RADIUS_ARG[a[0]] + 1]


def compare_traps(gd, data):
    sec = section("traps", "Traps", "traps.json", "trap.txt")
    ours = {t["id"]: t for t in load(data, "traps.json")}
    ids = {v: k for k, v in TRAP_ALIASES.items()}
    used = set()
    for e in parse_records(os.path.join(gd, "trap.txt")):
        disp, _, tname = e["name"].partition(":")
        if tname == "no trap":
            continue
        if tname in TRAPS_ELSEWHERE:
            sec.unmodelled[f"{tname} (modelled as {TRAPS_ELSEWHERE[tname]})"] += 1
            continue
        tid = ids.get(tname, slug(tname))
        t = ours.get(tid)
        if t is None:
            sec.only_theirs.append(f"{tname} ({disp})")
            continue
        used.add(tid)
        sec.compared += 1
        label = f"{tid} (4.2.5: {tname})"
        glyph, col = graphics(e)
        sec.cmp(label, "display name", t["name"], disp)
        sec.cmp(label, "glyph", t.get("glyph", "^"), glyph)
        sec.cmp(label, "colour", t.get("color", "White"), trap_colour(col))
        flags = flag_list(e, "flags")
        sec.cmp_set(label, "flags", t.get("flags", []), flags)
        rarity, min_depth = ((one(e, "appear") or "0:0").split(":") + ["0"])[:2]
        if "TRAP" in flags:
            sec.cmp(label, "min depth", t.get("minDepth", 0), int(min_depth))
            sec.cmp(label, "rarity", t.get("rarity", 0), int(rarity))
        sec.cmp_set(label, "save", t.get("save", []), flag_list(e, "save"))
        sec.cmp(label, "visibility", canon_rv(t.get("visibility", "0")), canon_rv((one(e, "visibility") or "0").replace(" ", "")))
        for key, line in (("message", "msg"), ("messageGood", "msg-good"), ("messageBad", "msg-bad"),
                          ("messageExtra", "msg-xtra")):
            sec.cmp(label, f"message ({line})", t.get(key), one(e, line))
        ours_vals = sorted(canon_rv(v) for v in trap_values(t.get("effect")) + trap_values(t.get("extra")))
        theirs_vals = sorted(canon_rv(v) for v in trap_dice_42(e))
        sec.cmp(label, "effect amounts (all dice)", ours_vals, theirs_vals)
        sec.cmp(label, "blast and earthquake radii", sorted(trap_radii(t.get("effect")) + trap_radii(t.get("extra"))),
                sorted(trap_radii_42(e)))
        teffs = [v for k, v in e["lines"] if k in ("effect", "effect-xtra")]
        if teffs and not t.get("effect"):
            sec.diff(label, "effect", None, teffs)
    for tid, t in ours.items():
        if tid not in used:
            sec.only_ours.append(f"{tid} ({t['name']})")
    sec.notes += [
        "Traps are paired by id: a hand-written alias table for AVABand's older ids ("
        + ", ".join(f"{k} → {v}" for k, v in TRAP_ALIASES.items()) + "), otherwise the 4.2.5 trap name as an id.",
        "Effects: AVABand's trap effect strings have their own grammar (GameSession.ApplyTrapEffects), so the "
        "values in them (dice and amounts) are compared as a multiset with 4.2.5's `dice`/`dice-xtra` lines, "
        "4.2.5's DUNGEON_LEVEL variables written as AVABand's `{D...}`.",
        "Not compared: the `appear` max number (unused by 4.2.5's pick_trap), `desc`.",
    ]
    return sec


# ---------------------------------------------------------------------------------------------------
# Terrain

TERRAIN_ALIASES = {"lava": "lava stream"}


def compare_curses(gd, data):
    sec = section("curses", "Curses", "curses.json", "curse.txt")
    ours = {c["id"]: c for c in load(data, "curses.json")}
    used = set()
    for e in parse_records(os.path.join(gd, "curse.txt")):
        cid = OI.CURSES.get(e["name"], slug(e["name"]))
        c = ours.get(cid)
        if c is None:
            sec.only_theirs.append(e["name"])
            continue
        used.add(cid)
        sec.compared += 1
        label = f"{cid} ({e['name']})"
        bases = set()
        for t in get(e, "type"):
            b = TYPE_TO_BASE.get(t)
            bases |= {"sling", "bow", "crossbow"} if b == "bow" else {b} if b else set()
        sec.cmp_set(label, "bases", c.get("bases", []), bases)
        combat = [int(x) for x in (one(e, "combat") or "0:0:0").split(":")]
        for key, v in zip(("toHit", "toDam", "toAc"), combat):
            sec.cmp(label, key, c.get(key, 0), v)
        mods, res, vul = {}, set(), set()
        for line in get(e, "values"):
            for part in (x.strip() for x in line.split("|")):
                m = re.fullmatch(r"([A-Z_]+)\[(-?\d+)\]", part)
                if m and m.group(1) in OI.RESISTS:
                    (vul if int(m.group(2)) < 0 else res).add(OI.RESISTS[m.group(1)])
                elif m and m.group(1) in OI.MODIFIERS:
                    mods[OI.MODIFIERS[m.group(1)]] = int(m.group(2))
        sec.cmp(label, "modifiers", c.get("modifiers") or {}, mods)
        sec.cmp_set(label, "resists", c.get("resists", []), res)
        sec.cmp_set(label, "vulnerabilities", c.get("vulnerabilities", []), vul)
        sec.cmp_set(label, "flags", c.get("flags", []), flag_list(e, "flags"))
        sec.cmp_set(label, "conflicts", c.get("conflicts", []),
                    [OI.CURSES.get(x.strip(), slug(x.strip())) for x in (one(e, "conflict") or "").split("|") if x.strip()])
        sec.cmp_set(label, "conflict flags", c.get("conflictFlags", []), flag_list(e, "conflict-flags"))
        effect = one(e, "effect")
        sec.cmp(label, "has an effect", bool(c.get("effect")), bool(effect))
        if effect:
            dice = (one(e, "dice") or "").replace(" ", "")
            ours_eff = c.get("effect") or ""
            if "$" not in dice and dice:
                sec.cmp(label, "effect amount", canon_rv(ours_eff.split(":")[-1] if not ours_eff.startswith("summon")
                                                         else ours_eff.split(":")[1]), canon_rv(dice))
            sec.cmp(label, "effect kind", ours_eff.split(":")[0],
                    {"TIMED_INC": "timed", "TELEPORT": "teleport", "SUMMON": "summon", "WAKE": "wake",
                     "DAMAGE": "damage"}.get(effect.split(":")[0], effect))
            sec.cmp(label, "time", canon_rv(c.get("time", "0")), canon_rv((one(e, "time") or "0").replace(" ", "")))
            sec.cmp(label, "message", c.get("effectMessage") or None, one(e, "msg"))
    for cid, c in ours.items():
        if cid not in used:
            sec.only_ours.append(cid)
    sec.notes += ["Matched by the importer's curse ids (the slug of the name; `burning up` → burning_up, `chilled to the "
                  "bone` → chilled). Effects are AVABand trap-effect strings: compared by kind, amount, time and message; "
                  "the treacherous weapon's WEAPON_DAMAGE is `damage:weapon`.",
                  "Not compared: `desc`."]
    return sec


# 4.2.5 constants.txt -> where AVABand keeps each: ("json", constants.json key[, offset]) or
# ("cs", source file under src/Angband.Core, constant or property name), or ("kind", object id, field).
CS = "src/Angband.Core/"
CONSTANT_HOMES = {
    "world:max-depth": ("json", "maxDepth", 1),      # ours is the deepest level, 4.2.5's the count of levels
    "world:day-length": ("json", "dayLength"),
    "player:max-sight": ("json", "maxSight"),
    "carry-cap:pack-size": ("json", "packSize"),
    "carry-cap:quiver-size": ("json", "quiverSize"),
    "carry-cap:quiver-slot-size": ("json", "quiverSlotSize"),
    "mon-gen:repro-max": ("json", "maxBreeders"),
    "mon-play:mult-rate": ("json", "breedRate"),
    "player:start-gold": ("json", "startGold"),
    "store:turns": ("json", "storeTurns"),
    "store:inven-max": ("json", "storeInvenMax"),
    "store:shuffle": ("json", "storeShuffle"),
    "store:magic-level": ("json", "storeMagicLevel"),
    "mon-gen:chance": ("json", "allocMonsterChance"),
    "mon-gen:town-day": ("json", "townMonstersDay"),
    "mon-gen:town-night": ("json", "townMonstersNight"),
    "obj-make:default-lamp": ("json", "defaultLampFuel"),
    "obj-make:fuel-torch": ("kind", "wooden_torch", "fuel"),
    "obj-make:fuel-lamp": ("kind", "lantern", "fuel"),
    "mon-gen:level-min": ("cs", "Definitions/DungeonProfileDef.cs", "MonsterMin"),
    "mon-gen:ood-chance": ("cs", "Monsters/MonsterSpawner.cs", "OutOfDepthChance"),
    "mon-gen:ood-amount": ("cs", "Monsters/MonsterSpawner.cs", "OutOfDepthAmount"),
    "mon-gen:group-max": ("cs", "Monsters/MonsterSpawner.cs", "GroupMax"),
    "mon-gen:group-dist": ("cs", "Monsters/MonsterSpawner.cs", "GroupDistance"),
    "mon-play:break-glyph": ("cs", "Game/GameSession.Special.cs", "GlyphHardness"),
    "mon-play:life-drain": ("cs", "Game/GameSession.MonsterSpells.cs", "LifeDrainPercent"),
    "mon-play:flee-range": ("cs", "Game/GameSession.MonsterRange.cs", "FleeRangeBeyondSight"),
    "mon-play:turn-range": ("cs", "Game/GameSession.MonsterRange.cs", "TurnRange"),
    "world:feeling-total": ("cs", "Game/LevelFeelings.cs", "FeelingTotal"),
    "world:feeling-need": ("cs", "Game/LevelFeelings.cs", "FeelingNeed"),
    "world:move-energy": ("cs", "Time/EnergyTable.cs", "MoveEnergy"),
    "world:dungeon-hgt": ("cs", "Definitions/DungeonProfileDef.cs", "Height"),
    "world:dungeon-wid": ("cs", "Definitions/DungeonProfileDef.cs", "Width"),
    "carry-cap:thrown-quiver-mult": ("cs", "Items/Inventory.cs", "ThrownQuiverMultiplier"),
    "obj-make:max-depth": ("cs", "Items/ObjectFactory.cs", "MaxObjectDepth"),
    "obj-make:great-obj": ("cs", "Items/ObjectFactory.cs", "GreatObjectChance"),
    "obj-make:great-ego": ("cs", "Items/ObjectFactory.cs", "GreatEgoChance"),
    "player:max-range": ("cs", "Game/GameSession.Combat.cs", "MaxRange"),
}
# Constants AVABand has no counterpart for, and why (listed, not compared).
CONSTANTS_ELSEWHERE = {
    "level-max:monsters": "AVABand's monster roster grows as needed",
    "player:food-value": "AVABand's hunger is Angband 4.1's model (constants.json food*)",
    "dun-gen:cent-max": "an array size in 4.2.5's generator", "dun-gen:door-max": "an array size in 4.2.5's generator",
    "dun-gen:wall-max": "an array size in 4.2.5's generator", "dun-gen:tunn-max": "an array size in 4.2.5's generator",
    "carry-cap:floor-size": "AVABand floor piles have no limit",
    "world:stair-skip": "the birth option's default (Angband birth_levels_skip); AVABand has none",
    "world:town-hgt": "AVABand's town is its own layout (town.json)",
    "world:town-wid": "AVABand's town is its own layout (town.json)",
    "dun-gen:amt-room": "dungeon_profile.json allocation (compared there)",
    "dun-gen:amt-item": "dungeon_profile.json allocation (compared there)",
    "dun-gen:amt-gold": "dungeon_profile.json allocation (compared there)",
    "dun-gen:pit-max": "dungeon_profile.json (compared there)",
}
CRITICAL_PREFIXES = {"melee-critical": "Melee", "ranged-critical": "Ranged", "o-melee-critical": "OMelee",
                     "o-ranged-critical": "ORanged"}


def cs_constant(root, rel, name):
    src = open(os.path.join(root, CS, rel), encoding="utf-8").read()
    m = re.search(r"\b" + name + r"\b\s*(?:\{[^}]*\})?\s*=\s*(-?\d[\d_]*)", src)
    return int(m.group(1).replace("_", "")) if m else None


def pascal(words):
    return "".join(w[:1].upper() + w[1:] for w in re.split(r"[-_]", words) if w)


def compare_constants(gd, data):
    sec = section("constants", "Constants", "constants.json and C# constants", "constants.txt")
    root = os.path.normpath(os.path.join(data, "..", "..", ".."))
    ours = load(data, "constants.json")
    kinds = {k["id"]: k for k in load(data, "objects.json")}
    crit = open(os.path.join(root, CS, "Combat/CriticalTables.cs"), encoding="utf-8").read()
    levels = collections.defaultdict(list)
    for line in open(os.path.join(gd, "constants.txt"), encoding="utf-8"):
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        parts = line.split(":")
        group = parts[0]
        if group.endswith("-critical-level"):
            levels[group[:-len("-level")]].append([int(x) if re.fullmatch(r"-?\d+", x) else x for x in parts[1:]])
            continue
        key, value = f"{group}:{parts[1]}", int(parts[2])
        sec.compared += 1
        if group in CRITICAL_PREFIXES:
            name = CRITICAL_PREFIXES[group] + pascal(parts[1]).replace("Toh", "ToHit")
            m = re.search(r"\b" + name + r"\s*=\s*(-?\d+)", crit)
            sec.cmp(key, f"CriticalTables.{name}", int(m.group(1)) if m else None, value)
            continue
        home = CONSTANT_HOMES.get(key)
        if home is None:
            sec.compared -= 1
            why = CONSTANTS_ELSEWHERE.get(key)
            sec.unmodelled[f"{key} ({why})" if why else f"{key} (no AVABand counterpart)"] += 1
            continue
        if home[0] == "json":
            sec.cmp(key, f"constants.json {home[1]}", ours.get(home[1], None) if home[1] in ours else None,
                    value - (home[2] if len(home) > 2 else 0))
        elif home[0] == "kind":
            sec.cmp(key, f"{home[1]}.{home[2]}", kinds.get(home[1], {}).get(home[2]), value)
        else:
            sec.cmp(key, f"{home[1]} {home[2]}", cs_constant(root, home[1], home[2]), value)
    grades = {"HIT_GOOD": "Good", "HIT_GREAT": "Great", "HIT_SUPERB": "Superb", "HIT_HI_GREAT": "HighGreat",
              "HIT_HI_SUPERB": "HighSuperb"}
    for group, rows in levels.items():
        name = CRITICAL_PREFIXES[group] + "Levels"
        m = re.search(name + r"\s*=\s*\[(.*?)\];", crit, re.S)
        tuples = re.findall(r"\(([^()]*)\)", m.group(1)) if m else []
        ours_rows = [[int(x) if re.fullmatch(r"-?\d+", x.strip()) else x.strip().replace("CriticalGrade.", "")
                      for x in t.split(",")] for t in tuples]
        theirs_rows = [[grades.get(x, x) if isinstance(x, str) else x for x in r] for r in rows]
        sec.compared += 1
        sec.cmp(f"{group}-level", f"CriticalTables.{name}", ours_rows, theirs_rows)
    sec.notes += ["Each 4.2.5 constant is compared with where AVABand keeps it: constants.json, an object kind, or a "
                  "named C# constant read from the source (critical hits: Combat/CriticalTables.cs). "
                  "`world:max-depth` counts levels; AVABand's `maxDepth` is the deepest level (one less)."]
    return sec


def compare_terrain(gd, data):
    sec = section("terrain", "Terrain", "terrain.json", "terrain.txt")
    ours = load(data, "terrain.json")
    by_name = {t["name"].lower(): t for t in ours}
    records = parse_records(os.path.join(gd, "terrain.txt"), start="code")
    vocab = {f.lower().replace("_", "") for r in records for f in flag_list(r, "flags")}
    hdr = os.path.normpath(os.path.join(gd, "..", "..", "src", "list-terrain-flags.h"))
    if os.path.exists(hdr):
        vocab |= {m.lower().replace("_", "") for m in re.findall(r"^TF\((\w+)", open(hdr, encoding="utf-8").read(), re.M)}
    ours_only = collections.Counter()
    name_to_id = {}
    for r in records:
        t = by_name.get(TERRAIN_ALIASES.get(one(r, "name").lower(), one(r, "name").lower()))
        if t:
            name_to_id[r["name"]] = t["id"]
    used = set()
    for r in records:
        name = one(r, "name")
        t = by_name.get(TERRAIN_ALIASES.get(name.lower(), name.lower()))
        if t is None:
            sec.only_theirs.append(f"{name} ({r['name']})")
            continue
        used.add(t["id"])
        sec.compared += 1
        label = f"{t['id']} ({name})"
        glyph, col = graphics(r)
        sec.cmp(label, "glyph", t.get("glyph"), glyph)
        sec.cmp(label, "colour", t.get("color", "White"), MI.COLORS.get((col or "w")[0]))
        sec.cmp(label, "priority", t.get("priority", 0), int(one(r, "priority", "0")))
        oflags = {f.lower().replace("_", "") for f in t.get("flags") or []}
        for f in sorted(oflags - vocab):
            ours_only[f] += 1
        sec.cmp_set(label, "flags", oflags & vocab, {f.lower().replace("_", "") for f in flag_list(r, "flags")})
        sec.cmp(label, "digging", t.get("digDifficulty", 0), int(one(r, "digging", "0")))
        m = one(r, "mimic")
        sec.cmp(label, "mimic", t.get("mimic"), name_to_id.get(m, m) if m else None)
    for t in ours:
        if t["id"] not in used:
            sec.only_ours.append(f"{t['id']} ({t['name']})")
    if ours_only:
        sec.notes.append("AVABand-only terrain flags with no 4.2.5 counterpart (not compared): "
                         + ", ".join(f"{k} ({n})" for k, n in sorted(ours_only.items())) + ".")
    sec.notes += ["Matched by name, case-insensitively (plus `lava` → `lava stream`). Flags are compared ignoring "
                  "case and underscores (DOOR_ANY = DoorAny), over 4.2.5's terrain flag vocabulary.",
                  "Not compared: messages (`walk-msg`, `run-msg`, `hurt-msg`, `die-msg`, `confused-msg`), "
                  "`look-prefix`, `look-in-preposition`, `resist-flag`, `desc`."]
    return sec


# ---------------------------------------------------------------------------------------------------
# Stores

STORE_CODES = {"STORE_GENERAL": "general", "STORE_ARMOR": "armoury", "STORE_WEAPON": "weaponsmith",
               "STORE_BOOK": "bookseller", "STORE_ALCHEMY": "alchemist", "STORE_MAGIC": "magic",
               "STORE_BLACK": "black_market", "HOME": "home"}


def compare_stores(gd, data):
    sec = section("stores", "Stores", "stores.json", "store.txt")
    ours = {s["id"]: s for s in load(data, "stores.json")}
    objs = load(data, "objects.json")
    idx = {(family(o["base"]), norm_name(o["name"])): o["id"] for o in objs}
    books = class_books(gd)

    def resolve(item):
        tval, _, name = item.partition(":")
        base = TYPE_TO_BASE.get(tval)
        if not name:   # a whole tval: 4.2.5 stocks every (town, for books) kind of it
            out = []
            for bname, b in books.items():
                if b["tval"] == tval and not b["dungeon"]:
                    out.append(idx.get((family(base), norm_name(bname)), f"{tval}:{bname}?"))
            return out or [f"{tval}:*"]
        return [idx.get((family(base or "?"), norm_name(name)), f"{item} (no such kind in ours)")]

    used = set()
    for e in parse_records(os.path.join(gd, "store.txt"), start="store"):
        sid = STORE_CODES.get(e["name"])
        s = ours.get(sid)
        if s is None:
            sec.only_theirs.append(e["name"])
            continue
        used.add(sid)
        sec.compared += 1
        label = f"{sid} ({e['name']})"
        owners = [tuple(v.split(":", 1)) for v in get(e, "owner")]
        if sid != "home":
            sec.cmp(label, "owners (purse, name)", [f"{o.get('purse')} {o.get('name')}" for o in s.get("owners") or []],
                    [f"{p} {n}" for p, n in owners])
        slots = one(e, "slots")
        if slots:
            lo, hi = slots.split(":")
            sec.cmp(label, "slots min", s.get("minItems", 0), int(lo))
            sec.cmp(label, "slots max", s.get("maxItems", 0), int(hi))
        if one(e, "turnover"):
            sec.cmp(label, "turnover", s.get("turnover", 0), int(one(e, "turnover")))
        for key in ("always", "normal"):
            theirs = [x for v in get(e, key) for x in resolve(v)]
            sec.cmp_set(label, key, s.get(key) or [], theirs)
        buys = set()
        for v in get(e, "buy"):
            b = TYPE_TO_BASE.get(v, v)
            buys |= {"sling", "bow", "crossbow"} if b == "bow" else {b}
        sec.cmp_set(label, "buys", s.get("buys") or [], buys)
        for v in get(e, "buy-flag"):
            sec.unmodelled[f"buy-flag {v}"] += 1
    for sid in ours:
        if sid not in used:
            sec.only_ours.append(sid)
    sec.notes += ["Store items are resolved to AVABand object ids by (base, name); unresolvable ones are shown as "
                  "`tval:name (no such kind in ours)`. A whole-tval `always:` line stands for every town book of "
                  "that tval.", "Not compared: `slots` for the home (ours `capacity`), `buy-flag`."]
    return sec


# ---------------------------------------------------------------------------------------------------
# Blow effects, monster bases, monster spells

def compare_blow_effects(gd, data):
    sec = section("blow_effects", "Blow effects", "blow_effects.json", "blow_effects.txt")
    ours = {b["id"]: b for b in load(data, "blow_effects.json")}
    used = set()
    for e in parse_records(os.path.join(gd, "blow_effects.txt")):
        bid = e["name"].lower()
        b = ours.get(bid)
        if b is None:
            sec.only_theirs.append(e["name"])
            continue
        used.add(bid)
        sec.compared += 1
        sec.cmp(bid, "power (monster to-hit)", b.get("power", 0), int(one(e, "power", "0")))
        # Elemental blows: what resists them. 4.2.5 names it on `resist:` for effect-type element, and
        # implies it for the four base elements; flag-type protections (sustains, PROT_*, HOLD_LIFE) are
        # applied in code on both sides and aren't compared.
        res = one(e, "resist")
        if one(e, "effect-type") == "element" or e["name"] in ("ACID", "ELEC", "FIRE", "COLD"):
            theirs = (res or e["name"]).lower()
            sec.cmp(bid, "resisted by (ours: element or preventedBy)", b.get("element") or b.get("preventedBy"), theirs)
    for bid in ours:
        if bid not in used:
            sec.only_ours.append(bid)
    sec.notes += ["`power` is the monster to-hit power (4.2.5 mon-attack.c `effect->power`).",
                  "Not compared: `eval`, `desc`, lore colours, `lash-type`; resistance for flag-type effects "
                  "(sustains, PROT_*, HOLD_LIFE: handled in code); ours' timed-effect fields (the 4.2.5 "
                  "equivalents live in C code, not in blow_effects.txt)."]
    return sec


def compare_monster_bases(gd, data):
    sec = section("monster_bases", "Monster bases", "monster_bases.json", "monster_base.txt")
    ours = {b["id"]: b for b in load(data, "monster_bases.json")}
    used = set()
    ours = {slug(k): v for k, v in ours.items()}
    for e in parse_records(os.path.join(gd, "monster_base.txt")):
        bid = slug(e["name"])
        b = ours.get(bid)
        if b is None:
            sec.only_theirs.append(e["name"])
            continue
        used.add(bid)
        sec.compared += 1
        sec.cmp(bid, "glyph", b.get("glyph"), one(e, "glyph"))
    for bid in ours:
        if bid not in used:
            sec.only_ours.append(bid)
    sec.notes += ["Only the glyph is compared; the base flags are compared through each monster's merged flags. "
                  "Not compared: `pain`, `desc`."]
    return sec


def eval_expr(base, expr):
    """4.2.5 expression 'base / 8 + 1' evaluated left to right with C integer division."""
    v = base
    toks = expr.split()
    for op, n in zip(toks[::2], toks[1::2]):
        n = int(n)
        if op == "+":
            v += n
        elif op == "-":
            v -= n
        elif op == "*":
            v *= n
        elif op == "/":
            v = int(v / n)
    return v


def spell_avg_42(e, power):
    dice = one(e, "dice")
    if dice is None:
        return None
    for line in get(e, "expr"):
        name, base, expr = line.split(":", 2)
        bv = {"SPELL_POWER": power, "MAX_SIGHT": 20}.get(base)
        if bv is None:
            return None
        dice = dice.replace("$" + name, str(eval_expr(bv, expr)))
    if "$" in dice:
        return None
    return rv_avg(dice)


def compare_summons(gd, data):
    sec = section("summons", "Summons", "summons.json", "summon.txt")
    ours = {x["id"]: x for x in load(data, "summons.json")}
    used = set()
    for e in parse_records(os.path.join(gd, "summon.txt")):
        x = ours.get(e["name"])
        if x is None:
            sec.only_theirs.append(e["name"])
            continue
        used.add(e["name"])
        sec.compared += 1
        label = e["name"]
        sec.cmp(label, "sound (msgt)", x.get("sound", "SUM_MONSTER"), one(e, "msgt"))
        sec.cmp(label, "uniques allowed", bool(x.get("uniques")), one(e, "uniques") == "1")
        sec.cmp_set(label, "bases", x.get("bases", []), [slug(b) for b in get(e, "base")])
        sec.cmp(label, "race flag", x.get("raceFlag"), one(e, "race-flag"))
        sec.cmp(label, "fallback", x.get("fallback"), one(e, "fallback"))
        sec.cmp(label, "description", x.get("description", ""), one(e, "desc", ""))
    for sid in ours:
        if sid not in used:
            sec.only_ours.append(sid)
    sec.notes += ["Every field of summon.txt is compared. Monster spells are checked to summon the kind their "
                  "`effect:SUMMON:<kind>` names, with 4.2.5's dice as the most attempts."]
    return sec


def compare_monster_spells(gd, data):
    sec = section("monster_spells", "Monster spells", "monster_spells.json", "monster_spell.txt (+ projection.txt, list-mon-spells.h)")
    ours = {s["id"]: s for s in load(data, "monster_spells.json")}
    innate, _src = innate_spells(gd)
    proj = {p["name"]: p for p in parse_records(os.path.join(gd, "projection.txt"), start="code")}
    used = set()
    powers = (10, 30, 50, 70, 90)
    for e in parse_records(os.path.join(gd, "monster_spell.txt")):
        s = ours.get(e["name"])
        if s is None:
            sec.only_theirs.append(e["name"])
            continue
        used.add(e["name"])
        sec.compared += 1
        label = e["name"]
        sec.cmp(label, "innate", bool(s.get("innate")), e["name"] in innate)
        effs = get(e, "effect")
        first = effs[0].split(":") if effs else [""]
        kind, arg = first[0], (first[1] if len(first) > 1 else "")
        # LASH takes its element from the caster's blows, so it has none of its own to compare.
        if kind in ("BOLT", "BALL", "BREATH", "SHORT_BEAM", "ARC", "STAR", "SPOT") and len(effs) == 1:
            t_el = arg.lower()
            o_el = s.get("element") or "none"
            mapped = OI.ELEMENTS.get(arg)
            if t_el in ("missile", "arrow"):
                t_el = "none"
            if o_el != t_el:
                if mapped == o_el:
                    sec.unmodelled[f"element {arg} (ours: {o_el})"] += 1
                else:
                    sec.diff(label, "element", o_el, t_el)
        if kind == "BREATH":
            p = proj.get(arg)
            if p:
                # C# defaults when the JSON omits them: BreathDivisor 3, BreathCap 1600.
                sec.cmp(label, "breath divisor (projection.txt)", s.get("breathDivisor", 3), int(one(p, "divisor", "0")))
                sec.cmp(label, "breath damage cap (projection.txt)", s.get("breathCap", 1600), int(one(p, "damage-cap", "0")))
            continue
        if len([x for x in effs if x.split(":")[0] in ("BOLT", "BALL", "SHORT_BEAM", "ARC", "LASH", "DAMAGE")]) > 1:
            sec.unmodelled["several damaging effects (damage not compared)"] += 1
        elif kind in ("BOLT", "BALL", "SHORT_BEAM", "ARC", "LASH", "DAMAGE", "STAR", "SPOT") and not s.get("powerScaled"):
            ours_avg = []
            theirs_avg = []
            for pw in powers:
                t = spell_avg_42(e, pw)
                if s.get("damageFormula"):
                    # Ours in 4.2.5's own terms: evaluated the same way.
                    mine = {"name": label, "lines": [("dice", s["damageFormula"])]
                            + [("expr", f"{k}:{v}") for k, v in (s.get("formulaTerms") or {}).items()]}
                    oa = spell_avg_42(mine, pw)
                else:
                    oa = rv_avg(s.get("damage") or "0")
                    if oa is not None:
                        div = s.get("levelDivisor") or 0
                        oa += (pw // div if div else 0) + pw * (s.get("levelPercent") or 0) // 100
                if t is None or oa is None:
                    break
                ours_avg.append(round(oa, 1))
                theirs_avg.append(round(t, 1))
            if ours_avg and any(abs(a - b) > max(1.0, 0.03 * b) for a, b in zip(ours_avg, theirs_avg)):
                sec.diff(label, f"average damage at power {'/'.join(map(str, powers))}",
                         "/".join(f"{x:g}" for x in ours_avg), "/".join(f"{x:g}" for x in theirs_avg)
                         + f"  (4.2.5 dice {one(e, 'dice')}; " + "; ".join(get(e, "expr")) + ")")
        elif kind == "TIMED_INC" and s.get("timed"):
            # Player status spells (blind, scare, confuse, slow, hold...). MON_TIMED_INC (the caster hastes
            # itself, shapechanges) is hard-coded in AVABand and not compared.
            d = one(e, "dice")
            if d and "$" not in d:
                sec.cmp(label, "duration", canon_rv(s.get("duration")), canon_rv(d))
            tt = OI.TIMED.get(arg, arg.lower())
            sec.cmp(label, "timed effect", s.get("timed"), tt)
        elif kind == "SUMMON":
            sec.cmp(label, "summon kind", s.get("summon"), arg)
            sec.cmp(label, "most summons (dice)", canon_rv(s.get("count", "1")), canon_rv(one(e, "dice") or "1"))
        elif kind in ("MON_HEAL_HP", "MON_HEAL_KIN"):
            t = spell_avg_42(e, 10)
            if t is not None and s.get("healPerLevel"):
                sec.cmp(label, "heal per spell-power level", s.get("healPerLevel"), t / 10)
    for sid in ours:
        if sid not in used:
            sec.only_ours.append(sid)
    sec.notes += [
        "AVABand scales spell damage on the monster's *depth* (damage + depth/levelDivisor + depth×levelPercent/100); "
        "4.2.5 on its *spell power* (usually = depth). Average damage is compared at powers "
        "10/30/50/70/90 with both formulas and reported when they differ by more than max(1, 3%).",
        "Breaths are compared through projection.txt's divisor and damage cap (ours defaults: 3 and 1600). "
        "Player-status durations are compared only when 4.2.5's dice have no `$` expression.",
        "Not compared: `hit` chance, `power-cutoff` message tiers, messages, lore, WOUND (`powerScaled`), "
        "damage of spells with several damaging effects (STORM...), LASH element (it comes from the caster's "
        "blows), MON_TIMED_INC (HASTE, SHAPECHANGE), teleport distances.",
    ]
    return sec


# ---------------------------------------------------------------------------------------------------
# Rendering

COLLAPSE_AT = 20


def render(gd, data, out):
    L = []
    w = L.append
    w("# AVABand data vs Angband 4.2.5")
    w("")
    w(f"- AVABand data: `{os.path.abspath(data)}`")
    w(f"- Angband gamedata: `{os.path.abspath(gd)}`")
    w("- Generated by `tools/compare_with_angband.py`.")
    w("")
    w("Each difference is shown as **field: ours → 4.2.5**. For set-valued fields (flags, resists, spells, "
      "store stock...) only the members that differ are shown: `{only in ours} → {only in 4.2.5}`. `—` means "
      "absent/none. Differences shared by many entries are collapsed into a *systematic* list at the top of "
      "each section (still counted in the totals).")
    w("")
    w("## Contents")
    for s in SECTIONS:
        w(f"- [{s.title}](#{s.key.replace('_', '-')}) — `{s.ours_file}` vs `{s.theirs_file}`")
    w("- [Summary](#summary)")
    w("")
    for s in SECTIONS:
        w(f"<a id=\"{s.key.replace('_', '-')}\"></a>")
        w(f"## {s.title}: `{s.ours_file}` vs `{s.theirs_file}`")
        w("")
        w(f"{s.compared} entries matched; {len(s.diffs)} with differences ({s.n_field_diffs()} field differences); "
          f"{len(s.only_theirs)} only in 4.2.5; {len(s.only_ours)} only in ours.")
        w("")
        # Systematic collapse
        pattern = collections.Counter()
        absent = collections.Counter()
        for entry, ds in s.diffs.items():
            for f, o, t, a in ds:
                pattern[(f, o, t)] += 1
                if a:
                    absent[f] += 1
        collapsed_patterns = {p for p, n in pattern.items() if n >= COLLAPSE_AT}
        collapsed_absent = {f for f, n in absent.items() if n >= COLLAPSE_AT}
        if collapsed_patterns or collapsed_absent:
            w("### Systematic differences")
            w("")
            for (f, o, t) in sorted(collapsed_patterns, key=lambda p: -pattern[p]):
                ents = [e for e, ds in s.diffs.items() if any((d[0], d[1], d[2]) == (f, o, t) for d in ds)]
                w(f"- **{f}: {o} → {t}** in {len(ents)} entries: " + ", ".join(ents))
            for f in sorted(collapsed_absent):
                ents = [f"{e} ({d[2]})" for e, ds in s.diffs.items() for d in ds
                        if d[0] == f and d[3] and (d[0], d[1], d[2]) not in collapsed_patterns]
                if ents:
                    w(f"- **{f}** is absent from ours (so the loader default applies) in "
                      f"{absent[f]} entries; 4.2.5 values: " + ", ".join(ents))
            w("")
        if s.only_theirs:
            w(f"### Only in 4.2.5 ({len(s.only_theirs)})")
            w("")
            w(", ".join(s.only_theirs))
            w("")
        if s.only_ours:
            w(f"### Only in ours ({len(s.only_ours)})")
            w("")
            w(", ".join(s.only_ours))
            w("")
        rest = collections.OrderedDict()
        for entry, ds in s.diffs.items():
            keep = [d for d in ds if (d[0], d[1], d[2]) not in collapsed_patterns
                    and not (d[3] and d[0] in collapsed_absent)]
            if keep:
                rest[entry] = keep
        if rest:
            w(f"### Differences by entry ({len(rest)} entries)")
            w("")
            for entry, ds in rest.items():
                w(f"#### {entry}")
                for f, o, t, _a in ds:
                    w(f"- {f}: {o} → {t}")
                w("")
        if s.unmodelled:
            w("### 4.2.5 properties AVABand's format can't express (not reported as differences)")
            w("")
            for k, n in sorted(s.unmodelled.items(), key=lambda kv: (-kv[1], kv[0])):
                w(f"- {k}: {n}")
            w("")
        if s.notes:
            w("### Notes: normalisation and fields not compared")
            w("")
            for n in s.notes:
                w(f"- {n}")
            w("")
    w("## Not compared at all")
    w("")
    w("- 4.2.5 files with no comparison here: vault.txt and room_template.txt (imported by "
      "angband_vault_import.py but not compared), pit.txt, dungeon_profile.txt, object_base.txt, "
      "object_property.txt, player_property.txt, player_timed.txt, projection.txt (except breath "
      "divisors/caps), realm.txt, flavor.txt, names.txt, history.txt, hints.txt, body.txt, brand.txt, "
      "slay.txt, pain.txt, chest_trap.txt, quest.txt, constants.txt, visuals.txt, world.txt, "
      "ui_*.txt, blow_methods.txt (methods are only checked for existence).")
    w("- Descriptions and messages everywhere.")
    w("")
    w("<a id=\"summary\"></a>")
    w("## Summary")
    w("")
    w("| Category | Matched | With differences | Field differences | Only in 4.2.5 | Only in ours | Unexpressible 4.2.5 properties |")
    w("|---|---:|---:|---:|---:|---:|---:|")
    tot = [0] * 6
    for s in SECTIONS:
        row = [s.compared, len(s.diffs), s.n_field_diffs(), len(s.only_theirs), len(s.only_ours), sum(s.unmodelled.values())]
        tot = [a + b for a, b in zip(tot, row)]
        w(f"| {s.title} | " + " | ".join(map(str, row)) + " |")
    w("| **Total** | " + " | ".join(f"**{x}**" for x in tot) + " |")
    w("")
    text = "\n".join(L) + "\n"
    if out:
        with open(out, "w", encoding="utf-8") as f:
            f.write(text)
    else:
        sys.stdout.write(text)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("gamedata", help="Angband 4.2.5 lib/gamedata directory")
    ap.add_argument("--data", default=os.path.join(HERE, "..", "src", "Angband.Data", "data"),
                    help="AVABand data directory (default: src/Angband.Data/data)")
    ap.add_argument("--out", help="write the Markdown report here (default: stdout)")
    ap.add_argument("--fail-on-diff", action="store_true",
                    help="exit 1 if anything differs (fields, entries only in 4.2.5, or only in ours other than the "
                         "special artifact kinds 4.2.5 makes from artifact.txt) — for CI")
    args = ap.parse_args()
    gd, data = args.gamedata, args.data
    for fn in (compare_monsters, compare_monster_bases, compare_monster_spells, compare_blow_effects,
               compare_objects, compare_egos, compare_artifacts, compare_classes_and_spells, compare_shapes,
               compare_traps, compare_terrain, compare_stores, compare_curses, compare_constants, compare_summons):
        fn(gd, data)
    render(gd, data, args.out)
    if args.out:
        for s in SECTIONS:
            print(f"{s.title:16} matched {s.compared:4}  with diffs {len(s.diffs):4}  field diffs {s.n_field_diffs():5}  "
                  f"only 4.2.5 {len(s.only_theirs):4}  only ours {len(s.only_ours):4}")
    if args.fail_on_diff:
        drift = [f"{s.title}: {s.n_field_diffs()} field differences, {len(s.only_theirs)} only in 4.2.5, "
                 f"{len([o for o in s.only_ours if 'INSTA_ART' not in o])} only in ours"
                 for s in SECTIONS
                 if s.n_field_diffs() or s.only_theirs or any("INSTA_ART" not in o for o in s.only_ours)]
        if drift:
            print("Drift from Angband 4.2.5's data:\n  " + "\n  ".join(drift), file=sys.stderr)
            sys.exit(1)
        print("No drift from Angband 4.2.5's data.")


if __name__ == "__main__":
    main()
