#!/usr/bin/env python3
"""Brings AVABand's game data back into line with Angband 4.2.5's (see compare_with_angband.py).

Each entry AVABand already has is rebuilt from 4.2.5 by the same conversion the importers use, then
merged into ours: every field the importer knows how to produce takes 4.2.5's value (or is removed
when 4.2.5 has none), and fields that are AVABand's own are kept, as are ids.

Usage:
    sync_with_angband.py <angband lib/gamedata> [--data src/Angband.Data/data] [--only monsters,objects,...]
"""
import argparse, json, os, sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import angband_monster_import as mi  # noqa: E402

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


SECTIONS = {"monsters": sync_monsters, "monster_spells": sync_monster_spells, "blow_effects": sync_blow_effects}


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
