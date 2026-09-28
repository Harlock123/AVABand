#!/usr/bin/env python3
"""Brings AVABand's game data back into line with Angband 4.2.5's (see compare_with_angband.py).

Each entry AVABand already has is rebuilt from 4.2.5 by the same conversion the importers use, then
merged into ours: every field the importer knows how to produce takes 4.2.5's value (or is removed
when 4.2.5 has none), and fields that are AVABand's own are kept, as are ids.

Usage:
    sync_with_angband.py <angband lib/gamedata> [--data src/Angband.Data/data] [--only monsters,objects,...]
"""
import argparse, json, os, re, sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import angband_monster_import as mi  # noqa: E402
import angband_object_import as oi  # noqa: E402
import angband_ego_artifact_import as ea  # noqa: E402
import compare_with_angband as cw  # noqa: E402

# AVABand kept two monsters under their older Angband names; they are 4.2.5's under another name.
MONSTER_ALIASES = {"jackal": "wild dog", "hill orc": "half-orc"}


def merge(ours, fresh, producible, keep=("id",)):
    """4.2.5's value for every field the importer can produce; ours for the rest."""
    out = dict(ours)
    for key in producible:
        if key in keep:
            continue
        if key in fresh:
            out[key] = fresh[key]
        else:
            out.pop(key, None)
    return out


def write_json(path, data, indent):
    """indent=None: one entry to a line (as monster_spells.json and egos.json are kept)."""
    with open(path, "w", encoding="utf-8") as f:
        if indent is None:
            f.write("[\n" + ",\n".join("  " + json.dumps(x, ensure_ascii=False) for x in data) + "\n]\n")
        else:
            json.dump(data, f, indent=indent, ensure_ascii=False)
            f.write("\n")


def sync_monsters(gd, data):
    path = os.path.join(data, "monsters.json")
    ours = json.load(open(path, encoding="utf-8"))
    bases = {b["name"]: {"glyph": mi.one(b, "glyph", "?"), "flags": mi.flag_list(b, "flags")}
             for b in mi.parse(os.path.join(gd, "monster_base.txt"))}
    entries = [e for e in mi.parse(os.path.join(gd, "monster.txt")) if not e["name"].startswith("<")]
    all_names = [e["name"] for e in entries]
    by_name = {e["name"].lower(): e for e in entries}
    kinds = {(k["base"], k["name"].replace("~", "").replace("& ", "").lower()): k["id"]
             for k in json.load(open(os.path.join(data, "objects.json"), encoding="utf-8"))}

    fresh_all = {}
    for m in ours:
        e = by_name.get(MONSTER_ALIASES.get(m["name"].lower(), m["name"].lower()))
        if e is None:
            continue
        fresh = mi.convert(e, bases, all_names)
        mimic_lines = mi.values(e, "mimic")
        if mimic_lines:
            poses = [kinds.get((t.strip(), n.strip().lower())) for t, _, n in (l.partition(":") for l in mimic_lines)]
            fresh["mimics"] = [p for p in poses if p]
        fresh_all[m["id"]] = fresh
    producible = set().union(*(f.keys() for f in fresh_all.values())) - {"shapeNames"} | {"shapes"}

    # Shapes, as the importer resolves them: a monster by name, or kin of a base near the right depth.
    by_our_name = {m["name"].lower(): m for m in ours}
    base_of = {mi.slug(e["name"]): mi.one(e, "base") for e in entries}
    changed = 0
    result = []
    for m in ours:
        fresh = fresh_all.get(m["id"])
        if fresh is None:
            result.append(m)
            continue
        names = fresh.pop("shapeNames", None)
        if names:
            ids = []
            for n in names:
                if n.lower() in by_our_name:
                    ids.append(by_our_name[n.lower()]["id"])
                else:
                    ids += [x["id"] for x in ours if base_of.get(x["id"]) == n and "UNIQUE" not in x["flags"]
                            and abs(x["depth"] - fresh["depth"]) <= 15]
            ids = list(dict.fromkeys(i for i in ids if i != m["id"]))
            if ids:
                fresh["shapes"] = ids
            elif "spells" in fresh:
                fresh["spells"] = [s for s in fresh["spells"] if s != "SHAPECHANGE"]
        merged = merge(m, fresh, producible, keep=("id", "name"))
        changed += merged != m
        result.append(merged)
    # Escorts name races by the importer's ids (slugs of 4.2.5 names): point them at ours.
    our_id = {mi.slug(e["name"]): None for e in entries}
    for m in result:
        our_id[mi.slug(m["name"])] = m["id"]
        alias = MONSTER_ALIASES.get(m["name"].lower())
        if alias:
            our_id[mi.slug(alias)] = m["id"]
    for m in result:
        for f in m.get("friends", []):
            if f.get("race") not in (None, "same") and our_id.get(f["race"]):
                f["race"] = our_id[f["race"]]
    # Monsters 4.2.5 doesn't have go.
    extra = [m["name"] for m in result if m["id"] not in fresh_all]
    result = [m for m in result if m["id"] in fresh_all]
    write_json(path, result, 1)
    print(f"monsters: {changed} of {len(ours)} brought into line; removed (not in 4.2.5): {extra}")


def records(path, start="name"):
    """Angband's key:value records, as (name, [(key, value)...])."""
    out, cur = [], None
    for raw in open(path, encoding="utf-8"):
        line = raw.rstrip("\n").rstrip("\r")
        if not line or line.startswith("#"):
            continue
        key, _, value = line.partition(":")
        if key == start:
            cur = (value, [])
            out.append(cur)
        elif cur is not None:
            cur[1].append((key, value))
    return out


def first(lines, key):
    return next((v for k, v in lines if k == key), None)


def plain_dice(text):
    """4.2.5's "11+1d4" in our order, "1d4+11"; a constant stays as it is."""
    text = text.strip()
    if "d" not in text:
        return text
    base, _, dice = text.rpartition("+") if "+" in text.split("d")[0] or text.index("+") < text.index("d") else ("", "", text)
    dice = dice if not dice.startswith("d") else "1" + dice
    return f"{dice}+{base}" if base else dice


DAMAGING = ("BOLT", "BALL", "SHORT_BEAM", "ARC", "LASH", "DAMAGE", "STAR", "SPOT")


def sync_monster_spells(gd, data):
    path = os.path.join(data, "monster_spells.json")
    ours = json.load(open(path, encoding="utf-8"))
    by_id = {s["id"]: s for s in ours}
    proj = {name: lines for name, lines in records(os.path.join(gd, "projection.txt"), start="code")}
    changed = 0
    for name, lines in records(os.path.join(gd, "monster_spell.txt")):
        s = by_id.get(name)
        if s is None:
            continue
        before = json.dumps(s, sort_keys=True)
        effects = [v for k, v in lines if k == "effect"]
        kind, _, arg = (effects[0] if effects else "").partition(":")
        arg = arg.split(":")[0]
        dice = first(lines, "dice")
        if kind == "BREATH" and arg in proj:
            divisor = int(first(proj[arg], "divisor") or 3)
            cap = int(first(proj[arg], "damage-cap") or 1600)
            s.pop("breathDivisor", None)
            s.pop("breathCap", None)
            if divisor != 3:
                s["breathDivisor"] = divisor
            if cap != 1600:
                s["breathCap"] = cap
        elif (kind in DAMAGING and dice and not s.get("powerScaled")
              and len([e for e in effects if e.split(":")[0] in DAMAGING]) == 1):
            # 4.2.5's damage exactly: its dice string and expressions, worked out from spell power.
            s["damageFormula"] = dice
            terms = {}
            for k, v in lines:
                if k == "expr":
                    var, _, expr = v.partition(":")
                    terms[var] = expr
            if terms:
                s["formulaTerms"] = terms
            else:
                s.pop("formulaTerms", None)
            for old in ("damage", "levelDivisor", "levelPercent"):
                s.pop(old, None)
        elif kind == "TIMED_INC" and s.get("timed") and dice and "$" not in dice:
            s["duration"] = plain_dice(dice)
        changed += json.dumps(s, sort_keys=True) != before
    write_json(path, ours, None)
    print(f"monster spells: {changed} of {len(ours)} brought into line")


def sync_blow_effects(gd, data):
    path = os.path.join(data, "blow_effects.json")
    ours = json.load(open(path, encoding="utf-8"))
    by_id = {b["id"]: b for b in ours}
    changed = 0
    for name, lines in records(os.path.join(gd, "blow_effects.txt")):
        b = by_id.get(name.lower())
        power = int(first(lines, "power") or 0)
        if b is None:
            print(f"blow effects: {name} is not modelled")
        elif b.get("power") != power:
            b["power"] = power
            changed += 1
    write_json(path, ours, None)
    print(f"blow effects: {changed} of {len(ours)} brought into line")


def effect_key(part):
    a = part.strip().split(":")
    return ":".join(a[:2 if a[0] in cw.SUBTYPED and len(a) > 1 else 1])


def merge_effect(ours, theirs, known, keep_unknown_missing=False):
    """Ours, part by part, brought into line with 4.2.5's translation: its parts replace ours with the
    same key, parts the importer could produce but 4.2.5 lacks go, and AVABand's own parts stay."""
    o = [p.strip() for p in (ours or "").split(";") if p.strip()]
    t = [p.strip() for p in (theirs or "").split(";") if p.strip()]
    tkeys = {}
    for p in t:
        tkeys.setdefault(effect_key(p), []).append(p)
    # The importer spells TIMED_INC:BOLD as cure:afraid; AVABand as timed:bold.
    if any(effect_key(p) == "timed:bold" for p in o) and "cure:afraid" in tkeys:
        tkeys.pop("cure:afraid")
    out, done = [], set()
    for p in o:
        k = effect_key(p)
        if k in tkeys:
            if k not in done:
                out += tkeys[k]
                done.add(k)
        elif k not in known or keep_unknown_missing:
            out.append(p)
    out += [p for k, ps in tkeys.items() if k not in done for p in ps]
    return "; ".join(out) or None


def set_or_pop(o, key, value, default):
    if value == default or value is None:
        o.pop(key, None)
    else:
        o[key] = value


def fixed_or_roll(o, field, roll_key, text):
    """A to-hit/to-dam/to-ac/modifier: a plain number in `field`, or a random value in rolls."""
    text = (text or "0").replace(" ", "")
    rolls = dict(o.get("rolls") or {})
    rolls.pop(roll_key, None)
    if re.fullmatch(r"[+-]?\d+", text):
        set_or_pop(o, field, int(text), 0)
    else:
        o.pop(field, None)
        rolls[roll_key] = oi.random_value(text)
    set_or_pop(o, "rolls", rolls or None, None)


def slay_def(tag):
    flag, _, mult = tag.partition("x")
    return {"monsterFlag": flag, "multiplier": int(mult), "verb": "smite", "name": ea.SLAY_NAMES[flag]}


def brand_def(tag):
    elem, _, mult = tag.partition("x")
    code = next(k for k, v in ea.BRANDS.items() if v[0] == elem)
    return {"element": elem, "multiplier": int(mult), "verb": ea.BRANDS[code][1], "name": ea.BRANDS[code][2]}


def sync_props(o, tp):
    """Modifiers, resists, flags, ignores, curses, slays and brands, as 4.2.5 has them."""
    op = cw.props_ours(o)
    mods = dict(o.get("modifiers") or {})
    rolls = dict(o.get("rolls") or {})
    for m in set(op["mods"]) | set(tp["mods"]):
        if op["mods"].get(m, "0") == tp["mods"].get(m, "0"):
            continue
        mods.pop(m, None)
        rolls.pop(m, None)
        v = tp["mods"].get(m)
        if v is None:
            continue
        if re.fullmatch(r"-?\d+", v):
            mods[m] = int(v)
        else:
            rolls[m] = oi.random_value(v)
    set_or_pop(o, "modifiers", mods or None, None)
    set_or_pop(o, "rolls", rolls or None, None)
    for key in ("resists", "flags", "ignore", "curses"):
        if op[key] != tp[key]:
            kept = [x for x in (o.get(key) or []) if x in tp[key]]
            set_or_pop(o, key, kept + sorted(tp[key] - set(kept)) or None, None)
    if op["slays"] != tp["slays"]:
        set_or_pop(o, "slays", [slay_def(t) for t in sorted(tp["slays"])] or None, None)
    if op["brands"] != tp["brands"]:
        set_or_pop(o, "brands", [brand_def(t) for t in sorted(tp["brands"])] or None, None)


def sync_objects(gd, data):
    path = os.path.join(data, "objects.json")
    ours = json.load(open(path, encoding="utf-8"))
    idx = {}
    for o in ours:
        idx.setdefault((cw.family(o["base"]), cw.norm_name(o["name"])), o)
    theirs = [(e, cw.one(e, "type")) for e in cw.parse_records(os.path.join(gd, "object.txt"))
              if cw.one(e, "type") != "none" and not e["name"].startswith("<")]
    for name, b in cw.class_books(gd).items():
        if b["props"]:
            cost, common, rng = b["props"].split(":")
            theirs.append(({"name": name, "lines": [("type", b["tval"]), ("level", rng.split(" to ")[0].strip()),
                                                    ("weight", "30"), ("cost", cost), ("alloc", f"{common}:{rng}")]},
                           b["tval"]))
    known = cw.importer_effect_keys(gd)
    scratch = cw.Section("objects", "", "", "")
    changed, missing = 0, []
    for e, t in theirs:
        base = cw.TYPE_TO_BASE.get(t)
        if base is None:
            continue
        o = idx.get((cw.family(base), cw.norm_name(e["name"])))
        if o is None:
            missing.append((e, t, base))
            continue
        before = json.dumps(o, sort_keys=True)
        ob = o["base"]
        o["level"] = int(cw.one(e, "level", "0"))
        o["cost"] = int(cw.one(e, "cost", "0"))
        o["weight"] = int(cw.one(e, "weight", "0"))
        alloc = cw.one(e, "alloc")
        if alloc:
            common, rng = alloc.split(":", 1)
            lo, _, hi = rng.partition(" to ")
            common, lo, hi = int(common), int(lo), int(hi or lo)
        else:
            common, lo, hi = 0, None, None
        o["commonness"] = common
        if common:
            o["minDepth"] = lo
            if not (o.get("maxDepth", 127) >= 100 and hi >= 100):
                set_or_pop(o, "maxDepth", hi, 127)
        attack = (cw.one(e, "attack") or "").split(":") + ["", "", ""]
        armor = (cw.one(e, "armor") or "").split(":") + ["", ""]
        if ob in cw.WEAPONY or "damage" in o:
            set_or_pop(o, "damage", attack[0] if cw.canon_rv(attack[0]) != "0" else None, None)
        fixed_or_roll(o, "toHit", "to_h", attack[1])
        fixed_or_roll(o, "toDam", "to_d", attack[2])
        set_or_pop(o, "armour", int(armor[0] or 0), 0)
        fixed_or_roll(o, "toAc", "to_a", armor[1])
        sync_props(o, cw.props_42(e, scratch))
        pval = cw.one(e, "pval")
        if base == "bow":
            set_or_pop(o, "multiplier", int(pval or 0), 0)
        elif base in ("flask", "light") and pval:
            o["fuel"] = int(pval)
        for key, line in (("charges", "charges"), ("recharge", "time")):
            v = cw.one(e, line)
            set_or_pop(o, key, v.replace(" ", "") if v else None, None)
        set_or_pop(o, "power", int(cw.one(e, "power", "0")), 0)
        pile = cw.one(e, "pile")
        chance, _, stack = (pile or "100:1").partition(":")
        set_or_pop(o, "stackSize", stack if stack != "1" else None, None)
        set_or_pop(o, "pileChance", int(chance), 100)
        if cw.get(e, "effect") and not cw.get(e, "expr"):
            eff, miss = oi.effects(e)
            field = "activation" if (o.get("activation") or ob not in (
                "potion", "scroll", "food", "mushroom", "wand", "staff", "rod", "flask")) else "effect"
            set_or_pop(o, field, merge_effect(o.get(field), eff, known, keep_unknown_missing=bool(miss)), None)
        changed += json.dumps(o, sort_keys=True) != before
    # Kinds 4.2.5 doesn't have go, except the special artifact kinds it makes from artifact.txt
    # (the Phial, the Star, the rings of power...): those are marked INSTA_ART, as 4.2.5 makes them.
    used = {id(idx.get((cw.family(cw.TYPE_TO_BASE[t]), cw.norm_name(e["name"]))))
            for e, t in theirs if t in cw.TYPE_TO_BASE}
    artifact_kinds = {a["kind"] for a in json.load(open(os.path.join(data, "artifacts.json"), encoding="utf-8"))}
    removed = []
    for o in list(ours):
        if id(o) in used or "INSTA_ART" in (o.get("flags") or []):
            continue
        if o["id"] in artifact_kinds:
            o["flags"] = (o.get("flags") or []) + ["INSTA_ART"]
        else:
            ours.remove(o)
            removed.append(o["id"])
    if removed:
        print(f"objects: removed (not in 4.2.5): {removed}")
    # Kinds 4.2.5 has and we don't: made by the importer's conversion, beside their fellows.
    for e, t, base in missing:
        if base == "gold":
            continue  # the treasures are placed by hand, in 4.2.5's order (MakeGold picks by it)
        if base == "bow":
            base = "sling" if "Sling" in e["name"] else "crossbow" if "Crossbow" in e["name"] else "bow"
        kind = oi.convert(e, base)
        effect, miss = oi.effects(e)
        if effect and not miss:
            kind["effect"] = effect
        elif miss:
            print(f"objects: {t}: {e['name']} left out ({', '.join(miss)} untranslatable)")
            continue
        if cw.one(e, "power"):
            kind["power"] = int(cw.one(e, "power"))
        if not kind["commonness"]:
            kind.pop("minDepth", None)
            kind.pop("maxDepth", None)
        props = cw.props_42(e, scratch)
        if props["ignore"]:
            kind["ignore"] = sorted(props["ignore"])
        name = kind["name"].replace("~", "")
        kind_id = oi.slug(name)
        if base in oi.PREFIX and not kind_id.startswith(oi.PREFIX[base].rstrip("_")):
            kind_id = oi.PREFIX[base] + kind_id
        kind = {"id": kind_id, **{k: v for k, v in kind.items() if k != "id"}}
        at = max((i for i, o in enumerate(ours) if o["base"] == base), default=len(ours) - 1) + 1
        ours.insert(at, kind)
        print(f"objects: added {kind_id}")
    for i, o in enumerate(ours):
        # Depths read best beside commonness.
        if "minDepth" in o or "maxDepth" in o:
            items = [(k, v) for k, v in o.items() if k not in ("minDepth", "maxDepth")]
            at = next((n + 1 for n, (k, _) in enumerate(items) if k == "commonness"), len(items))
            depths = [(k, o[k]) for k in ("minDepth", "maxDepth") if k in o]
            ours[i] = dict(items[:at] + depths + items[at:])
    write_json(path, ours, 2)
    print(f"objects: {changed} of {len(ours)} brought into line; not in ours: "
          f"{[t + ': ' + e['name'] for e, t, _ in missing]}")


SECTIONS = {"monsters": sync_monsters, "monster_spells": sync_monster_spells, "blow_effects": sync_blow_effects,
            "objects": sync_objects}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("gamedata")
    ap.add_argument("--data", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "Angband.Data", "data"))
    ap.add_argument("--only", default=",".join(SECTIONS))
    args = ap.parse_args()
    for name in args.only.split(","):
        SECTIONS[name](args.gamedata, args.data)


if __name__ == "__main__":
    main()
