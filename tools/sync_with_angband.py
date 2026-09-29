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
    set_or_pop(o, "cursePowers", tp.get("cursePowers") or None, None)
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


def sync_egos(gd, data):
    path = os.path.join(data, "egos.json")
    ours = json.load(open(path, encoding="utf-8"))
    kinds = cw.kind_ids(data)
    base_of = {k["id"]: k["base"] for k in json.load(open(os.path.join(data, "objects.json"), encoding="utf-8"))}
    scratch = cw.Section("egos", "", "", "")

    def all_bases(o):
        return set(o.get("bases", [])) | {base_of[k] for k in o.get("kinds", []) if k in base_of}
    used, changed, added = set(), 0, []
    ids = {o["id"] for o in ours}
    for e in cw.parse_records(os.path.join(gd, "ego_item.txt")):
        tb = cw.ego_bases(e)
        cands = [o for o in ours if o["name"].lower() == e["name"].lower() and o["id"] not in used]
        o = max(cands, key=lambda c: len(all_bases(c) & tb), default=None)
        if o is None or not (all_bases(o) & tb or (not tb and not all_bases(o))):
            # One of ours covering several of 4.2.5's (Slay Animal on weapons and on ammunition) is
            # split; one we lack is made.
            bases = cw.ego_type_bases(e) or sorted({base_of[k] for k in cw.ego_kinds(e, kinds)})
            sibling = next((x["id"] for x in ours if x["name"].lower() == e["name"].lower()), None)
            ego_id = f"{sibling}_{bases[0]}" if sibling and bases else oi.slug(e["name"])
            if ego_id in ids:
                ego_id = f"{ego_id}_{bases[0] if bases else 'none'}"
            n = 2
            while ego_id in ids:
                ego_id = f"{oi.slug(e['name'])}_{bases[0]}_{n}"
                n += 1
            o = {"id": ego_id, "name": e["name"]}
            ids.add(ego_id)
            at = max((i for i, x in enumerate(ours) if x["name"].lower() == e["name"].lower()), default=len(ours) - 1) + 1
            ours.insert(at, o)
            added.append(ego_id)
        used.add(o["id"])
        before = json.dumps(o, sort_keys=True)
        bases = cw.ego_type_bases(e)
        if set(bases) != set(o.get("bases", [])):
            o["bases"] = bases
        set_or_pop(o, "kinds", cw.ego_kinds(e, kinds) or None, None)
        common, rng = cw.one(e, "alloc", "0:1 to 127").split(":", 1)
        lo, _, hi = rng.partition(" to ")
        o["level"] = int(lo)
        o["commonness"] = int(common)
        set_or_pop(o, "maxDepth", int(hi or 127), 127)
        for field in ("toHit", "toDam", "toAc"):
            o.pop(field, None)
        rolls = dict(o.get("rolls") or {})
        for key, text in zip(("to_h", "to_d", "to_a"), (cw.one(e, "combat") or "0:0:0").split(":")):
            rolls.pop(key, None)
            if cw.canon_rv(text) != "0":
                rolls[key] = oi.random_value(text.strip())
        set_or_pop(o, "rolls", rolls or None, None)
        tp = cw.props_42(e, scratch, abilities_skip=("THROWING",))
        sync_props(o, tp)
        set_or_pop(o, "randomPower", tp.get("random"), None)
        set_or_pop(o, "minimums", cw.ego_minimums(e) or None, None)
        set_or_pop(o, "flagsOff", cw.ego_flags_off(e) or None, None)
        changed += json.dumps(o, sort_keys=True) != before
    # Keys in a steady order: identity, where it's made, then what it gives.
    order = ["id", "name", "bases", "kinds", "level", "commonness", "maxDepth"]
    ours = [dict([(k, o[k]) for k in order if k in o] + [(k, v) for k, v in o.items() if k not in order]) for o in ours]
    write_json(path, ours, None)
    print(f"egos: {changed} of {len(ours)} brought into line; added {added}")


def sync_artifacts(gd, data):
    path = os.path.join(data, "artifacts.json")
    ours = json.load(open(path, encoding="utf-8"))
    kinds = {k["id"]: k for k in json.load(open(os.path.join(data, "objects.json"), encoding="utf-8"))}
    kind_by_name = cw.kind_ids(data)
    acts = cw.activation_table(gd)
    known = cw.importer_effect_keys(gd)
    scratch = cw.Section("artifacts", "", "", "")
    by_name = {cw.norm_name(a["name"]): a for a in ours}
    changed = 0
    for e in cw.parse_records(os.path.join(gd, "artifact.txt")):
        a = by_name.get(cw.norm_name(e["name"]))
        if a is None:
            print(f"artifacts: {e['name']} is not ours")
            continue
        before = json.dumps(a, sort_keys=True)
        btype, _, bname = cw.one(e, "base-object", ":").partition(":")
        base = cw.TYPE_TO_BASE.get(btype)
        kid = kind_by_name.get((cw.family(base or "?"), cw.norm_name(bname)))
        if kid and kid != a["kind"]:
            a["kind"] = kid
        kind = kinds[a["kind"]]
        chance, rng = cw.one(e, "alloc", "0:0 to 127").split(":", 1)
        lo, _, hi = rng.partition(" to ")
        a.pop("rarity", None)
        a["level"] = int(lo)
        a["allocChance"] = int(chance)
        set_or_pop(a, "maxDepth", int(hi or 127), 127)
        weight = int(cw.one(e, "weight", "0"))
        set_or_pop(a, "weight", weight, kind.get("weight", 0))
        attack = (cw.one(e, "attack") or "0d0:0:0").split(":") + ["0", "0"]
        armor = (cw.one(e, "armor") or "0:0").split(":") + ["0"]
        if base in cw.WEAPONY or "damage" in a:
            dmg = attack[0] if cw.canon_rv(attack[0]) != "0" else None
            set_or_pop(a, "damage", dmg, kind.get("damage"))
        for field, text in (("toHit", attack[1]), ("toDam", attack[2]), ("toAc", armor[1])):
            set_or_pop(a, field, int(text or 0), 0)
        set_or_pop(a, "armour", int(armor[0] or 0), kind.get("armour", 0))
        tp = cw.props_42(e, scratch, abilities_skip=("THROWING",))
        tp["ignore"] = set()
        sync_props(a, tp)
        set_or_pop(a, "immunities", sorted(tp["immunities"]) or None, None)
        act = cw.one(e, "act")
        if act and act in acts:
            info = acts[act]
            old = a.get("activation")
            new = merge_effect(old, info["effect"], known, keep_unknown_missing=bool(info["missing"]))
            if not info["expr"] and new != old:
                a["activation"] = new
                a["activationText"] = oi.describe(new)
            set_or_pop(a, "activationPower", info["power"], 0)
            set_or_pop(a, "recharge", (cw.one(e, "time") or "").replace(" ", "") or None, None)
        changed += json.dumps(a, sort_keys=True) != before
    # Keys in a steady order: identity, then where it's made, then what it is.
    order = ["id", "name", "kind", "level", "allocChance", "maxDepth", "weight"]
    ours = [dict([(k, a[k]) for k in order if k in a] + [(k, v) for k, v in a.items() if k not in order]) for a in ours]
    write_json(path, ours, 2)
    print(f"artifacts: {changed} of {len(ours)} brought into line")


def sync_player_skills(o, e, per10):
    """Stats, skills (and their growth per 10 levels, for a class) and hit die from a 4.2.5 record."""
    stats = {s: v for s, v in zip(cw.STAT_ORDER, (int(x) for x in cw.one(e, "stats", "0:0:0:0:0").split(":"))) if v}
    set_or_pop(o, "stats", stats or None, None)
    skills, growth = dict(o.get("skills") or {}), dict(o.get("skillsPer10Levels") or {})
    for ours_key, key in cw.SKILL_MAP:
        base, ten = (cw.one(e, key, "0:0").split(":") + ["0"])[:2]
        skills[ours_key] = int(base)
        if per10:
            growth[ours_key] = int(ten)
    # A race's skills are adjustments: none is nothing.
    o["skills"] = skills if per10 else {k: v for k, v in skills.items() if v}
    if per10:
        o["skillsPer10Levels"] = growth
    set_or_pop(o, "hitDie", int(cw.one(e, "hitdie", "0")), 0)


def sync_classes(gd, data):
    path = os.path.join(data, "classes.json")
    ours = json.load(open(path, encoding="utf-8"))
    by_id = {c["id"]: c for c in ours}
    changed = 0
    for e in cw.parse_records(os.path.join(gd, "class.txt")):
        c = by_id.get(cw.slug(e["name"]))
        if c is None:
            continue
        before = json.dumps(c, sort_keys=True)
        sync_player_skills(c, e, per10=True)
        c.pop("expFactor", None)  # 4.2.5 classes have none: a character's is its race's
        # 4.2.5's equip lines: all a class starts with (wearables are worn, as wield_all does).
        base_of = {k["id"]: k["base"] for k in json.load(open(os.path.join(data, "objects.json"), encoding="utf-8"))}
        wearable = {"sword", "hafted", "polearm", "digger", "sling", "bow", "crossbow", "light", "soft_armour", "hard_armour",
                    "dragon_armour", "shield", "cloak", "helm", "crown", "gloves", "boots", "ring", "amulet"}
        kit = []
        for kid, lo, hi, opt in cw.class_kit_42(e, cw.kind_ids(data)):
            entry = {"kind": kid}
            if lo != 1:
                entry["count"] = lo
            if hi != lo:
                entry["countMax"] = hi
            if base_of.get(kid) in wearable:
                entry["equip"] = True
            if opt:
                entry["unlessOption"] = opt
            kit.append(entry)
        c["startingKit"] = kit
        changed += json.dumps(c, sort_keys=True) != before
    write_json(path, ours, 2)
    print(f"classes: {changed} of {len(ours)} brought into line")


def sync_races(gd, data):
    path = os.path.join(data, "races.json")
    ours = json.load(open(path, encoding="utf-8"))
    by_id = {r["id"]: r for r in ours}
    changed = 0
    for e in cw.parse_records(os.path.join(gd, "p_race.txt")):
        r = by_id.get(cw.slug(e["name"]))
        if r is None:
            continue
        before = json.dumps(r, sort_keys=True)
        sync_player_skills(r, e, per10=False)
        r["expFactor"] = int(cw.one(e, "exp", "100"))
        set_or_pop(r, "infravision", int(cw.one(e, "infravision", "0")), 0)
        changed += json.dumps(r, sort_keys=True) != before
    write_races(path, ours)
    print(f"races: {changed} of {len(ours)} brought into line")


def write_races(path, races):
    """races.json's own layout: who, then stats and skills, protections, flags, and the description."""
    def val(v):
        if isinstance(v, dict):
            return "{ " + ", ".join(f"{json.dumps(k)}: {json.dumps(x, ensure_ascii=False)}" for k, x in v.items()) + " }" if v else "{}"
        return json.dumps(v, ensure_ascii=False)

    def fields(r, keys):
        return ", ".join(f'"{k}": {val(r[k])}' for k in keys if k in r)
    lines = [["id", "name", "hitDie", "expFactor", "infravision"], ["stats"], ["skills"], ["resists"], ["flags"]]
    known = {k for group in lines for k in group} | {"description"}
    out = []
    for r in races:
        parts = [fields(r, group) for group in lines if any(k in r for k in group)]
        parts += [f'"{k}": {val(v)}' for k, v in r.items() if k not in known]
        if "description" in r:
            parts.append(f'"description": {val(r["description"])}')
        out.append("  { " + ",\n    ".join(parts) + " }")
    with open(path, "w", encoding="utf-8") as f:
        f.write("[\n" + ",\n".join(out) + "\n]\n")


def sync_shapes(gd, data):
    path = os.path.join(data, "shapes.json")
    ours = json.load(open(path, encoding="utf-8"))
    by_id = {s["id"]: s for s in ours}
    known = cw.importer_effect_keys(gd)
    scratch = cw.Section("shapes", "", "", "")
    changed = 0
    for e in cw.parse_records(os.path.join(gd, "shape.txt")):
        s = by_id.get(re.sub(r"[^a-z]+", "_", e["name"].lower()).strip("_"))
        if s is None:
            continue
        before = json.dumps(s, sort_keys=True)
        for key, v in zip(("toHit", "toDam", "toAc"), (int(x) for x in (cw.one(e, "combat") or "0:0:0").split(":"))):
            s[key] = v
        skills = {skill: int(cw.one(e, key, "0")) for key, skill in cw.SHAPE_SKILLS if int(cw.one(e, key, "0"))}
        set_or_pop(s, "skills", skills or None, None)
        tp = cw.shape_props(e, scratch)
        sync_props(s, tp)
        set_or_pop(s, "immunities", sorted(tp["immunities"]) or None, None)
        s["blows"] = cw.get(e, "blow")
        eff, miss = oi.effects(e, level_exprs=True)
        set_or_pop(s, "effect", merge_effect(s.get("effect"), eff, known, keep_unknown_missing=bool(miss)), None)
        changed += json.dumps(s, sort_keys=True) != before
    write_json(path, ours, 2)
    print(f"shapes: {changed} of {len(ours)} brought into line")


# 4.2.5's trap effects in AVABand's trap grammar (GameSession.ApplyTrapEffects): (effect, extra effect).
# {D...} is the dungeon level (4.2.5's DUNGEON_LEVEL expressions).
TRAP_EFFECTS = {
    "trap door": ("damage:2d8", None),
    "pit": ("damage:2d6", None),
    "spiked pit": ("damage:2d6", "damage:2d6; timed:cut:4d6"),
    "poison pit": ("damage:2d6", "timed:cut:4d6; timed:poisoned:8d6"),
    "rune of summon foe": ("summon:1:none:5", None),
    "rune of summoning": ("summon:2+1d3", None),
    "rune of necromancy": ("summon:1d3M2:UNDEAD", None),
    "rune of dragonsong": ("summon:1d3:DRAGON", None),
    "hellhole": ("summon:1+1d3:DEMON", None),
    "teleport rune": ("teleport:M80", None),
    "fire trap": ("element:fire:4d{D/2}", None),
    "acid trap": ("element:acid:4d{D/2}", None),
    "slow dart": ("damage:1d4; timed_nores:slow:20+1d20", None),
    "strength loss dart": ("damage:1d4; drain:str", None),
    "dexterity loss dart": ("damage:1d4; drain:dex", None),
    "constitution loss dart": ("damage:1d4; drain:con", None),
    "blinding gas trap": ("timed:blind:25+1d50", None),
    "confusion gas trap": ("timed:confused:10+1d20", None),
    "poison gas trap": ("timed:poisoned:10+1d20", None),
    "sleep gas trap": ("timed:paralyzed:5+1d10", None),
    "aggravation trap": ("wake; project_los:haste:25", None),
    "siren": ("wake", None),
    "mine trap": ("spot:shards:2:{D*2}", None),
    "blast trap": ("spot:light:1:2d6; spot:sound:2:{D/2}; spot:fire:2:{D}; spot:force:2:{D}", None),
    "mind blasting trap": ("damage:8d{D/10}; timed:confused:3+1d4", None),
    "brain smashing trap": ("damage:10d{D/5}; timed:slow:3+1d4; timed:confused:3+1d4; timed:paralyzed:3+1d4; "
                            "timed:blind:7+1d8", None),
    "rock fall trap": ("damage:{D/10+1}d5; timed_nores:stun:2d20; rubble", None),
    "earthquake trap": ("damage:{D/10+1}d5; earthquake:5", None),
    "block fall trap": ("granite", None),
    "area blast trap": ("spot:kill_wall:2:20; spot:force:2:{D}; rubble", None),
    "blinding flash trap": ("spot:light:4:{D/10+2}d8", None),
    "blinding trap": ("spot:dark:4:{D/10+2}d8; drain_light:100+1d100", None),
    "mana drain trap": ("drain_mana:1d{D/2+1}", None),
    "knife trap": ("timed:cut:150; damage:{D/2}", None),
    "petrifying trap": ("timed:stoneskin:20+1d20; timed:stun:20+1d20", None),
}
# Kept as AVABand models them elsewhere: the decoy (Level.Decoy) and door locks (a door's lock power).
TRAPS_ELSEWHERE = {"decoy", "door lock", "no trap"}


def trap_colour(code):
    """An Angband colour: a letter ("s") or a name ("light yellow")."""
    code = (code or "w").strip()
    return mi.COLORS.get(code) if len(code) == 1 else "".join(w.capitalize() for w in code.split())


def sync_traps(gd, data):
    path = os.path.join(data, "traps.json")
    ours = {t["id"]: t for t in json.load(open(path, encoding="utf-8"))}
    ids = {v: k for k, v in cw.TRAP_ALIASES.items()}
    out = []
    for e in cw.parse_records(os.path.join(gd, "trap.txt")):
        disp, _, tname = e["name"].partition(":")
        if tname in TRAPS_ELSEWHERE:
            continue
        tid = ids.get(tname, cw.slug(tname))
        old = ours.get(tid, {})
        glyph, col = cw.graphics(e)
        flags = cw.flag_list(e, "flags")
        rarity, min_depth, max_num = ((cw.one(e, "appear") or "0:0:0").split(":") + ["0", "0"])[:3]
        t = {"id": tid, "name": disp}
        if glyph != "^":
            t["glyph"] = glyph
        t["color"] = trap_colour(col)
        if "TRAP" in flags:
            t["minDepth"] = int(min_depth)
            t["rarity"] = int(rarity)
        else:
            t["minDepth"] = 999  # placed by other means (glyphs by the player, webs by spiders)
        t["flags"] = flags
        saves = cw.flag_list(e, "save")
        if saves:
            t["save"] = saves
        vis = cw.one(e, "visibility")
        if vis:
            t["visibility"] = vis.strip().replace(" ", "")
        effect, extra = TRAP_EFFECTS.get(tname, ("", None))
        t["effect"] = effect
        if extra:
            t["extra"] = extra
        for key, line in (("message", "msg"), ("messageGood", "msg-good"), ("messageBad", "msg-bad"),
                          ("messageExtra", "msg-xtra")):
            if cw.one(e, line):
                t[key] = cw.one(e, line)
        desc = " ".join(v.strip() for v in cw.get(e, "desc"))
        t["description"] = desc or old.get("description", "")
        out.append(t)
    with open(path, "w", encoding="utf-8") as f:
        f.write("[\n" + ",\n".join("  " + json.dumps(t, ensure_ascii=False) for t in out) + "\n]\n")
    print(f"traps: {len(out)} written ({len([t for t in out if t['id'] not in ours])} new)")


def sync_terrain(gd, data):
    path = os.path.join(data, "terrain.json")
    raw = open(path, encoding="utf-8").read()
    ours = json.loads(raw)
    by_name = {t["name"].lower(): t for t in ours}
    records = cw.parse_records(os.path.join(gd, "terrain.txt"), start="code")
    vocab = {f.lower().replace("_", "") for r in records for f in cw.flag_list(r, "flags")}
    hdr = os.path.normpath(os.path.join(gd, "..", "..", "src", "list-terrain-flags.h"))
    if os.path.exists(hdr):
        vocab |= {m.lower().replace("_", "") for m in re.findall(r"^TF\((\w+)", open(hdr, encoding="utf-8").read(), re.M)}
    enum = open(os.path.join(data, "..", "..", "Angband.Core", "Definitions", "TerrainFlags.cs"), encoding="utf-8").read()
    ours_names = {n.lower(): n for n in re.findall(r"^\s+(\w+) = ", enum, re.M)}
    name_to_id = {}
    for r in records:
        t = by_name.get(cw.TERRAIN_ALIASES.get(cw.one(r, "name").lower(), cw.one(r, "name").lower()))
        if t:
            name_to_id[r["name"]] = t["id"]
    changed = 0
    for r in records:
        t = by_name.get(cw.TERRAIN_ALIASES.get(cw.one(r, "name").lower(), cw.one(r, "name").lower()))
        if t is None:
            continue
        before = json.dumps(t, sort_keys=True)
        glyph, col = cw.graphics(r)
        t["color"] = trap_colour(col)
        set_or_pop(t, "priority", int(cw.one(r, "priority", "0")), 0)
        theirs = []
        for f in cw.flag_list(r, "flags"):
            n = ours_names.get(f.lower().replace("_", ""))
            if n is None:
                print(f"terrain: {t['id']}: 4.2.5 flag {f} has no AVABand name")
            else:
                theirs.append(n)
        kept = [f for f in t.get("flags", []) if f.lower().replace("_", "") not in vocab]
        t["flags"] = [f for f in t.get("flags", []) if f in theirs] + [f for f in theirs if f not in t.get("flags", [])] + kept
        set_or_pop(t, "digDifficulty", int(cw.one(r, "digging", "0")), 0)
        m = cw.one(r, "mimic")
        set_or_pop(t, "mimic", name_to_id.get(m, m) if m else None, None)
        changed += json.dumps(t, sort_keys=True) != before
    # terrain.json keeps its own layout: the entry, then its flags on a line of their own.
    out = []
    for t in ours:
        head = {k: v for k, v in t.items() if k != "flags"}
        line = "  { " + json.dumps(head, ensure_ascii=False)[1:-1]
        line += ",\n    \"flags\": " + json.dumps(t.get("flags", []), ensure_ascii=False) + " }" if "flags" in t else " }"
        out.append(line)
    with open(path, "w", encoding="utf-8") as f:
        f.write("[\n" + ",\n".join(out) + "\n]\n")
    print(f"terrain: {changed} of {len(ours)} brought into line")


# 4.2.5 curse names AVABand spells otherwise; the rest are slugs of the name.
CURSE_IDS = {"chilled to the bone": "chilled", "burning up": "burning_up"}
# 4.2.5 curse effects in AVABand's trap grammar (GameSession.ApplyTrapEffects), "damage:weapon" being a
# blow of the wielded weapon (4.2.5's WEAPON_DAMAGE).
CURSE_EFFECTS = {"TELEPORT": "teleport:{dice}", "WAKE": "wake", "DAMAGE": "damage:weapon"}


def curse_id(name):
    return CURSE_IDS.get(name, cw.slug(name))


def curse_effect(e):
    effect = cw.one(e, "effect")
    if not effect:
        return None
    parts = effect.split(":")
    dice = (cw.one(e, "dice") or "").replace(" ", "")
    if parts[0] == "TIMED_INC":
        return f"timed:{oi.TIMED[parts[1]]}:{dice}"
    if parts[0] == "SUMMON":
        return f"summon:{dice}:{parts[1]}"
    return CURSE_EFFECTS[parts[0]].format(dice=dice)


def curse_bases(e):
    bases = []
    for t in cw.get(e, "type"):
        b = cw.TYPE_TO_BASE.get(t)
        for x in (["sling", "bow", "crossbow"] if b == "bow" else [b] if b else []):
            if x not in bases:
                bases.append(x)
    return bases


def sync_curses(gd, data):
    path = os.path.join(data, "curses.json")
    ours = {c["id"]: c for c in json.load(open(path, encoding="utf-8"))}
    records = cw.parse_records(os.path.join(gd, "curse.txt"))
    scratch = cw.Section("curses", "", "", "")
    out = []
    for e in records:
        cid = curse_id(e["name"])
        c = {"id": cid, "name": e["name"]}
        desc = " ".join(v.strip() for v in cw.get(e, "desc"))
        c["description"] = desc or ours.get(cid, {}).get("description", "")
        c["bases"] = curse_bases(e)
        combat = [int(x) for x in (cw.one(e, "combat") or "0:0:0").split(":")]
        for key, v in zip(("toHit", "toDam", "toAc"), combat):
            if v:
                c[key] = v
        mods, resists, vulns = {}, [], []
        for line in cw.get(e, "values"):
            for part in (x.strip() for x in line.split("|")):
                m = re.fullmatch(r"([A-Z_]+)\[(-?\d+)\]", part)
                if not m:
                    continue
                key, val = m.group(1), int(m.group(2))
                if key in oi.RESISTS:
                    (vulns if val < 0 else resists).append(oi.RESISTS[key])
                elif key in oi.MODIFIERS:
                    mods[oi.MODIFIERS[key]] = val
        if mods:
            c["modifiers"] = mods
        if resists:
            c["resists"] = resists
        if vulns:
            c["vulnerabilities"] = vulns
        flags = cw.flag_list(e, "flags")
        if flags:
            c["flags"] = flags
        effect = curse_effect(e)
        if effect:
            c["effect"] = effect
            c["time"] = (cw.one(e, "time") or "0").replace(" ", "")
            if cw.one(e, "msg"):
                c["effectMessage"] = cw.one(e, "msg")
        conflicts = [curse_id(x) for x in (cw.one(e, "conflict") or "").split("|") if x.strip()]
        if conflicts:
            c["conflicts"] = conflicts
        cflags = cw.flag_list(e, "conflict-flags")
        if cflags:
            c["conflictFlags"] = cflags
        out.append(c)
    with open(path, "w", encoding="utf-8") as f:
        f.write("[\n" + ",\n".join("  " + json.dumps(c, ensure_ascii=False) for c in out) + "\n]\n")
    print(f"curses: {len(out)} written ({len([c for c in out if c['id'] not in ours])} new)")


def sync_constants(gd, data):
    """The constants AVABand keeps in constants.json take 4.2.5's values (the rest live in code)."""
    path = os.path.join(data, "constants.json")
    ours = json.load(open(path, encoding="utf-8"))
    values = {}
    for line in open(os.path.join(gd, "constants.txt"), encoding="utf-8"):
        parts = line.strip().split(":")
        if len(parts) == 3 and not line.startswith("#"):
            values[f"{parts[0]}:{parts[1]}"] = int(parts[2])
    changed = []
    for key, home in cw.CONSTANT_HOMES.items():
        if home[0] != "json" or key not in values:
            continue
        v = values[key] - (home[2] if len(home) > 2 else 0)
        if ours.get(home[1]) != v:
            ours[home[1]] = v
            changed.append(home[1])
    with open(path, "w", encoding="utf-8") as f:
        json.dump(ours, f, indent=2)
        f.write("\n")
    print(f"constants: {changed or 'none'} brought into line")


SECTIONS = {"monsters": sync_monsters, "monster_spells": sync_monster_spells, "blow_effects": sync_blow_effects,
            "objects": sync_objects, "egos": sync_egos, "artifacts": sync_artifacts, "classes": sync_classes,
            "races": sync_races, "shapes": sync_shapes, "traps": sync_traps, "terrain": sync_terrain,
            "curses": sync_curses, "constants": sync_constants}


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
