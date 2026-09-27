#!/usr/bin/env python3
"""Convert Angband 4.2's shape.txt (player shapechanges) into AVABand's data/shapes.json.

Usage:
    angband_shape_import.py <shape.txt> <data/shapes.json>
"""
import json, os, re, sys

sys.path.insert(0, os.path.dirname(__file__))
from angband_object_import import MODIFIERS, RESISTS, FLAG_RESISTS, FLAG_ABILITIES, parse, get, one, effects


def main():
    shapes = []
    for e in parse(sys.argv[1]):
        if e["name"] == "normal":
            continue
        combat = [int(x) for x in (one(e, "combat") or "0:0:0").split(":")]
        shape = {"id": re.sub(r"[^a-z]+", "_", e["name"].lower()).strip("_"), "name": e["name"].replace("Pukel", "Púkel"),
                 "toHit": combat[0], "toDam": combat[1], "toAc": combat[2]}
        skills = {}
        for key, skill in (("skill-save", "save"), ("skill-melee", "melee"), ("skill-disarm-phys", "disarm"),
                           ("skill-stealth", "stealth"), ("skill-device", "device")):
            if one(e, key):
                skills[skill] = skills.get(skill, 0) + int(one(e, key))
        mods, resists, flags = {}, [], []
        for line in get(e, "values"):
            for part in (p.strip() for p in line.split("|")):
                m = re.fullmatch(r"([A-Z_]+)\[(-?\d+)\]", part)
                if not m:
                    continue
                key, val = m.group(1), int(m.group(2))
                if key in MODIFIERS:
                    mods[MODIFIERS[key]] = val
                elif key in RESISTS:
                    resists.append(RESISTS[key])
        for line in get(e, "obj-flags"):
            for f in (x.strip() for x in line.split("|")):
                if f in FLAG_RESISTS:
                    resists.append(FLAG_RESISTS[f])
                elif f in FLAG_ABILITIES:
                    flags.append(FLAG_ABILITIES[f])
                elif f == "PROT_STUN":
                    resists.append("stun")
        effect, _ = effects(e)
        shape.update({k: v for k, v in (("skills", skills), ("modifiers", mods), ("resists", list(dict.fromkeys(resists))),
                                         ("flags", flags), ("effect", effect), ("blows", get(e, "blow"))) if v})
        shapes.append(shape)
    with open(sys.argv[2], "w", encoding="utf-8") as f:
        json.dump(shapes, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"{len(shapes)} shapes")


if __name__ == "__main__":
    main()
