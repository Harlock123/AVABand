#!/usr/bin/env python3
"""Import egos (ego_item.txt) and artifacts (artifact.txt) from Angband 4.2 into AVABand's data.

Existing egos and artifacts (matched by name) are kept. Properties AVABand models are converted —
bonuses, modifiers (random ones as rolls), slays, brands, resistances and protections, abilities
(regeneration, telepathy...) and curses; activations are not. Artifacts on special base objects
(the Star of Elendil, the rings of power...) get a kind of their own that never generates normally.
Angband's data is available under the GPL v2 or the Angband licence.

Usage:
    angband_ego_artifact_import.py <ego_item.txt> <artifact.txt> <data dir>
(activation.txt is read from beside artifact.txt.)
"""
import json, os, re, sys

sys.path.insert(0, os.path.dirname(__file__))
from angband_object_import import (TYPES, MODIFIERS, RESISTS, FLAG_RESISTS, FLAG_ABILITIES, CURSES, parse, get, one,
                                   slug, effects, describe)

SLAY_NAMES = {"EVIL": "evil creatures", "UNDEAD": "undead", "DEMON": "demons", "ORC": "orcs", "TROLL": "trolls",
              "GIANT": "giants", "DRAGON": "dragons", "ANIMAL": "animals"}
BRANDS = {"ACID": ("acid", "dissolve", "acid"), "ELEC": ("elec", "shock", "lightning"),
          "FIRE": ("fire", "burn", "fire"), "COLD": ("cold", "freeze", "cold"), "POIS": ("pois", "poison", "poison")}
# Angband ego names AVABand already has under another name.
EGO_ALIASES = {"of Flame": "of Burning", "of Frost": "of Freezing"}


def dice(text):
    """'d6' -> '1d6'; '5' stays; 'M' parts (magic bonus) can't be expressed as dice and are dropped."""
    text = re.sub(r"M\d+", "", text or "0").strip("+") or "0"
    parts = [p for p in text.replace("-", "+-").split("+") if p]
    dice_parts = ["1" + p if p.startswith("d") else p for p in parts if "d" in p]
    base = sum(int(p) for p in parts if p.lstrip("-").isdigit())
    if not dice_parts:
        return str(base) if base >= 0 else f"0d1{base}"
    return dice_parts[0] + (f"{base:+d}" if base else "")


def properties(e):
    mods, rolls, resists, flags, curses, slays, brands = {}, {}, [], [], [], [], []
    for line in get(e, "values"):
        for part in (p.strip() for p in line.split("|")):
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
                    rolls[MODIFIERS[key]] = val.replace(" ", "")
    for line in get(e, "flags"):
        for f in (x.strip() for x in line.split("|")):
            if f in FLAG_RESISTS:
                resists.append(FLAG_RESISTS[f])
            elif f in FLAG_ABILITIES and f != "THROWING":
                flags.append(FLAG_ABILITIES[f])
    for line in get(e, "curse"):
        cname = line.split(":")[0]
        if CURSES.get(cname):
            curses.append(CURSES[cname])
    for line in get(e, "slay"):
        flag, _, mult = line.partition("_")
        if flag in SLAY_NAMES:
            slays.append({"monsterFlag": flag, "multiplier": int(mult or 2), "verb": "smite", "name": SLAY_NAMES[flag]})
    for line in get(e, "brand"):
        elem, _, mult = line.partition("_")
        if elem in BRANDS:
            el, verb, name = BRANDS[elem]
            brands.append({"element": el, "multiplier": int(mult or 3), "verb": verb, "name": name})
    return mods, rolls, list(dict.fromkeys(resists)), flags, curses, slays, brands


def import_egos(path, egos_path):
    egos = json.load(open(egos_path, encoding="utf-8"))
    names = {(e["name"], b) for e in egos for b in e["bases"]}
    ids = {e["id"] for e in egos}
    added = 0
    for e in parse(path):
        name = EGO_ALIASES.get(e["name"], e["name"])
        bases = []
        for t in get(e, "type"):
            if TYPES.get(t):
                bases.append(TYPES[t])
        for it in get(e, "item"):
            t = it.split(":")[0]
            if TYPES.get(t) and TYPES[t] not in bases:
                bases.append(TYPES[t])
        bases = ["sling", "bow", "crossbow"] if bases == ["bow"] else [b for b in bases if b != "bow"] + (
            ["sling", "bow", "crossbow"] if "bow" in bases and bases != ["bow"] else [])
        bases = [b for b in bases if (name, b) not in names]
        if not bases:
            continue
        alloc = one(e, "alloc", "10:1 to 127").split(":")
        lo, _, hi = alloc[1].partition(" to ")
        mods, rolls, resists, flags, curses, slays, brands = properties(e)
        ego = {"id": "", "name": name, "bases": bases, "level": int(lo or 1), "commonness": int(alloc[0]),
               "maxDepth": int(hi or 127)}
        combat = (one(e, "combat") or "0:0:0").split(":")
        for key, text in zip(("toHit", "toDam", "toAc"), combat):
            d = dice(text)
            if d not in ("0", ""):
                ego[key] = d
        if mods:
            ego["modifiers"] = mods
        if rolls:
            ego["rolls"] = rolls
        for key, value in (("slays", slays), ("brands", brands), ("resists", resists), ("flags", flags), ("curses", curses)):
            if value:
                ego[key] = value
        if not any(k in ego for k in ("toHit", "toDam", "toAc", "modifiers", "rolls", "slays", "brands", "resists", "flags")):
            continue
        ego_id = slug(name)
        if ego_id in ids:
            ego_id = f"{ego_id}_{bases[0]}"
        n = 2
        while ego_id in ids:
            ego_id = f"{slug(name)}_{bases[0]}_{n}"
            n += 1
        ego["id"] = ego_id
        ids.add(ego_id)
        egos.append(ego)
        for b in bases:
            names.add((name, b))
        added += 1
    with open(egos_path, "w", encoding="utf-8") as f:
        f.write("[\n" + ",\n".join("  " + json.dumps(x, ensure_ascii=False) for x in egos) + "\n]\n")
    return added


def load_activations(path):
    """Angband activation.txt: name -> (AVABand effect, description), for those AVABand can express."""
    acts = {}
    for a in parse(path):
        effect, missing = effects(a)
        if effect and not missing:
            text = " ".join(v.strip() for v in get(a, "desc")).strip()
            acts[a["name"]] = (effect, text or describe(effect))
    return acts


def import_artifacts(path, artifacts_path, objects_path, activations):
    artifacts = json.load(open(artifacts_path, encoding="utf-8"))
    kinds = json.load(open(objects_path, encoding="utf-8"))
    names = {a["name"] for a in artifacts}
    ids = {a["id"] for a in artifacts}
    by_name = {}
    for k in kinds:
        base = {"sling": "bow", "crossbow": "bow"}.get(k["base"], k["base"])
        by_name[(base, k["name"].replace("~", "").replace("& ", "").lower())] = k["id"]
    kind_ids = {k["id"] for k in kinds}
    added, new_kinds = 0, 0
    for e in parse(path):
        if e["name"] in names:
            continue
        btype, _, bname = one(e, "base-object", ":").partition(":")
        base = TYPES.get(btype) or ("light" if btype == "light" else None)
        if base is None:
            continue
        key = (base, bname.replace("& ", "").replace("~", "").lower())
        kind_id = by_name.get(key)
        if kind_id is None:
            # A special object only this artifact is made from (Angband's "special" kinds).
            kind_id = slug(f"{bname}")
            if kind_id in kind_ids:
                kind_id = f"{base}_{kind_id}"
            real_base = base if base != "bow" else "bow"
            kinds.append({"id": kind_id, "name": bname, "base": real_base, "level": int(one(e, "level", "1")),
                          "commonness": 0, "cost": int(one(e, "cost", "0")), "weight": int(one(e, "weight", "0")),
                          "flags": ["INSTA_ART"]})
            kind_ids.add(kind_id)
            by_name[key] = kind_id
            new_kinds += 1
        alloc = one(e, "alloc", "10:1 to 127").split(":")
        chance = max(1, int(alloc[0]))
        lo = alloc[1].partition(" to ")[0]
        mods, rolls, resists, flags, curses, slays, brands = properties(e)
        for k, v in rolls.items():  # artifacts have fixed values; take a random one's base
            mods[k] = int(re.sub(r"\D.*", "", v) or 1)
        art = {"id": "", "name": e["name"], "kind": kind_id, "level": int(lo or one(e, "level", "1")),
               "rarity": max(1, round(100 / chance))}
        attack = (one(e, "attack") or "0d0:0:0").split(":")
        armor = (one(e, "armor") or "0:0").split(":")
        if attack[0] not in ("0d0", "1d1") and base in ("sword", "hafted", "polearm", "arrow", "bolt", "shot"):
            art["damage"] = attack[0]
        if len(attack) > 1 and re.fullmatch(r"-?\d+", attack[1]) and attack[1] != "0":
            art["toHit"] = int(attack[1])
        if len(attack) > 2 and re.fullmatch(r"-?\d+", attack[2]) and attack[2] != "0":
            art["toDam"] = int(attack[2])
        if armor[0] not in ("0", ""):
            art["armour"] = int(armor[0])
        if len(armor) > 1 and re.fullmatch(r"-?\d+", armor[1]) and armor[1] != "0":
            art["toAc"] = int(armor[1])
        if mods:
            art["modifiers"] = mods
        for k2, value in (("slays", slays), ("brands", brands), ("resists", resists), ("flags", flags), ("curses", curses)):
            if value:
                art[k2] = value
        act = one(e, "act")
        if act and act in activations:
            art["activation"], art["activationText"] = activations[act]
            if one(e, "time"):
                art["recharge"] = one(e, "time").replace(" ", "")
        desc = " ".join(v.strip() for v in get(e, "desc")).replace("  ", " ")
        if desc:
            art["description"] = desc
        art_id = slug(e["name"])
        if art_id in ids:
            art_id = f"{art_id}_{kind_id}"
        art["id"] = art_id
        ids.add(art_id)
        artifacts.append(art)
        added += 1
    with open(artifacts_path, "w", encoding="utf-8") as f:
        f.write("[\n" + ",\n".join("  " + json.dumps(x, ensure_ascii=False) for x in artifacts) + "\n]\n")
    with open(objects_path, "w", encoding="utf-8") as f:
        json.dump(kinds, f, indent=2, ensure_ascii=False)
        f.write("\n")
    return added, new_kinds


def main():
    ego_txt, artifact_txt, data = sys.argv[1:4]
    activation_txt = os.path.join(os.path.dirname(artifact_txt), "activation.txt")
    activations = load_activations(activation_txt) if os.path.exists(activation_txt) else {}
    egos = import_egos(ego_txt, os.path.join(data, "egos.json"))
    arts, kinds = import_artifacts(artifact_txt, os.path.join(data, "artifacts.json"), os.path.join(data, "objects.json"),
                                   activations)
    print(f"added {egos} egos, {arts} artifacts ({kinds} special kinds)")


if __name__ == "__main__":
    main()
