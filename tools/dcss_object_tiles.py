#!/usr/bin/env python3
"""Give AVABand's object kinds and bases Dungeon Crawl Stone Soup (CC0) item tiles.

Kinds without art get an exact-name match from DCSS's item folder where one exists (a Katana, a
Blue Dragon Scale Mail...); new bases get a representative tile. Only used PNGs are copied.

Usage:
    dcss_object_tiles.py <DCSS "Full" folder> <data/objects.json> <tilesets/dcss>
"""
import json, os, re, shutil, sys

BASES = {
    "crown": "item/armor/headgear/helmet_art_1.png",
    "dragon_armour": "item/armor/torso/gold_dragon_armor_new.png",
    "mushroom": "monster/fungi_plants/wandering_mushroom_new.png",
    "wand": "item/wand/gem_bronze_new.png",
    "staff": "item/staff/staff_0.png",
    "rod": "item/rod/rod_0_new.png",
    "digger": "player/hand_right/pick_axe.png",
    "chest": "dungeon/chest_2_closed.png",
}
ALIASES = {  # AVABand kind id -> DCSS item name
    "black_dragon_scale_mail": "shadow_dragon_scale_mail", "white_dragon_scale_mail": "ice_dragon_scale_mail",
    "red_dragon_scale_mail": "fire_dragon_scale_mail", "multi_hued_dragon_scale_mail": "pearl_dragon_scale_mail",
    "gold_dragon_scale_mail": "gold_dragon_armor", "executioners_sword": "executioners_axe",
    "two_handed_great_flail": "great_mace", "great_axe": "war_axe", "heavy_crossbow": "arbalest",
    "robe": "robe_1", "quarterstaff": "quarterstaff", "lance": "lance",
}


def main():
    src, objects_path, tileset_dir = sys.argv[1:4]
    files = {}
    for root, _, fs in os.walk(os.path.join(src, "item")):
        for f in sorted(fs):
            if f.endswith(".png"):
                base = re.sub(r"(_new|_old)?\.png$", "", f)
                base = re.sub(r"_\d+$", "", base)
                files.setdefault(base, os.path.relpath(os.path.join(root, f), src))
    manifest_path = os.path.join(tileset_dir, "tileset.json")
    manifest = json.load(open(manifest_path, encoding="utf-8"))
    tiles = manifest["tiles"]
    used = set()

    def ref(rel):
        if not os.path.exists(os.path.join(src, rel)):
            raise SystemExit(f"missing DCSS tile: {rel}")
        used.add(rel)
        return rel

    for base, rel in BASES.items():
        tiles.setdefault(f"object-base:{base}", ref(rel))
    matched = 0
    for kind in json.load(open(objects_path, encoding="utf-8")):
        key = f"object:{kind['id']}"
        if key in tiles:
            continue
        name = ALIASES.get(kind["id"], kind["id"])
        if name in files:
            tiles[key] = ref(files[name])
            matched += 1
    for rel in sorted(used):
        dest = os.path.join(tileset_dir, rel)
        if not os.path.exists(dest):
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            shutil.copyfile(os.path.join(src, rel), dest)
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"{matched} kinds matched; {len(used)} images")


if __name__ == "__main__":
    main()
