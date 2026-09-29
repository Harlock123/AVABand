#!/usr/bin/env python3
"""Build the Tangaria (32x32) tileset for AVABand from Tangaria's PWMAngband tile files.

Needs Pillow (to read the 4096x4096 source sheet and write the packed one).

The source is Tangaria's lib/tiles/tangaria/ (https://github.com/igroglaz/Tangaria_release):
32x32.png, graf-tan.prf, xtra-tan.prf, flvr-tan.prf, plus lib/gamedata/flavor.txt. The .prf lines are
read with the parsers in angband_prf_to_tileset.py; this script adds what differs in PWMAngband/Tangaria
(features go by name, not code; its lighting variants; tval spellings; monsters Angband 4.2.5 has but
Tangaria names differently or lacks). Only the cells used are copied, packed into one sheet (the
source is 22 MB).

Usage:
    tangaria_tileset.py <folder with the Tangaria files> <data dir> <tilesets/tangaria>
Then run tileset_glyph_fallbacks.py on the result.
"""
import json, os, sys, unicodedata

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from angband_prf_to_tileset import TRAPS, norm, parse, parse_flavors, parse_objects  # noqa: E402

# AVABand terrain id -> Tangaria feature name (graf-tan.prf goes by name).
TERRAIN = {
    "floor": "open floor", "closed_door": "closed door", "open_door": "open door", "broken_door": "broken door",
    "up_staircase": "up staircase", "down_staircase": "down staircase",
    "shop_general": "general store", "shop_armoury": "armoury", "shop_weaponsmith": "weapon smiths",
    "shop_bookseller": "bookseller", "shop_alchemist": "alchemy shop", "shop_magic": "magic shop",
    "shop_black_market": "black market", "shop_home": "home",
    "secret_door": "granite wall",  # looks like the wall until found, as in Angband
    "rubble": "pile of rubble", "passable_rubble": "pile of passable rubble",
    "magma_vein": "magma vein", "quartz_vein": "quartz vein",
    "magma_with_treasure": "magma vein with treasure", "quartz_with_treasure": "quartz vein with treasure",
    "granite_wall": "granite wall", "permanent_wall": "permanent wall", "lava": "lava",
}

# AVABand object base -> Tangaria tval.
TVALS = {
    "sword": "sword", "hafted": "hafted", "polearm": "polearm", "digger": "digger", "sling": "bow", "bow": "bow",
    "crossbow": "bow", "shot": "shot", "arrow": "arrow", "bolt": "bolt", "soft_armour": "soft armour",
    "hard_armour": "hard armour", "shield": "shield", "helm": "helm", "gloves": "gloves", "boots": "boots",
    "cloak": "cloak", "light": "light", "magic_book": "magic book", "prayer_book": "prayer book",
    "nature_book": "nature book", "shadow_book": "shadow book", "scroll": "scroll", "food": "food",
    "flask": "flask", "crown": "crown", "dragon_armour": "dragon armour", "chest": "chest", "gold": "gold",
}

# AVABand monster id -> the Tangaria monster drawn for it (Angband 4.2.5 monsters Tangaria lacks or renames).
MONSTERS = {
    "red_hatted_elf": "malicious leprechaun", "father_christmas": "moroz", "apprentice": "novice mage",
    "giant_white_louse": "giant head louse", "giant_black_louse": "mosquito marshes giant black louse",
    "large_grey_snake": "large white snake",
    "eastern_dwarf": "nar, the dwarf", "blacklock_mage": "ibun, son of mim", "stonefoot_warrior": "khim, son of mim",
    "ironfist_priest": "fundin bluecloak", "dark_dwarven_lord": "mim, betrayer of turin",
    "stiffbeard_sorcerer": "ibun, son of mim",
    "grishnakh_the_hill_orc": "grishnukh, the hill orc", "azog_enemy_of_the_dwarves": "azog, the great orc",
    "angamaite_of_umbar": "angamait of umbar", "pseudo_dragon": "baby gold dragon",
    "druadan_mage": "mage", "druadan_druid": "druid", "dark_hound": "shadow hound",
    "homunculus": "imp", "nruling": "manes",
    "shadow_drake": "mature shadow drake", "chaos_drake": "mature chaos drake", "law_drake": "mature law drake",
    "balance_drake": "mature balance drake", "ethereal_drake": "mature ethereal drake",
    "crystal_drake": "mature crystal drake", "death_drake": "dracolich", "ethereal_dragon": "great ethereal wyrm",
    "great_wyrm_of_annihilation": "great wyrm of chaos", "sky_dragon": "sky drake",
    "skeleton_etten": "skeleton ettin", "etten": "ettin",
    "black_hearted_huorn": "old forest huorn", "hasty_ent": "young ent",
    "maia_of_nienna": "maia", "maia_of_mandos": "maia", "maia_of_orome": "maia", "maia_of_yavanna": "maia",
    "maia_of_aule": "maia", "maia_of_ulmo": "triton", "maia_of_manwe": "maia", "maia_of_varda": "maia",
    "osse_herald_of_ulmo": "triton", "makar_the_warrior": "maia", "measse_the_bloody": "valkyrie",
    "lord_of_carn_dum": "black knight", "gilim_the_giant_of_eruman": "fasolt the giant",
    "nan_the_giant": "cloud giant", "werewolf_of_sauron": "werewolf", "spider_of_gorgoroth": "ancient spider",
    "feanorian_raider": "green elf archer", "tevildo_prince_of_cats": "hellcat",
}

# AVABand object id -> (Tangaria tval, name) where the names differ.
OBJECTS = {"piece_of_elvish_waybread": ("food", "lembas"),
           "shining_dragon_scale_mail": ("dragon armour", "silver dragon scale mail")}
BOOKS = ("magic_book", "prayer_book", "nature_book", "shadow_book")

NAME = "Tangaria (32x32)"
AUTHOR = ("Tangar Igroglaz (tileset designer), David Gervais (most of the artwork), Shtukensia, Terter, Repne, "
          "Ayenes, Ivan Voirol, Refuzzle, Nikkita31, Kenney and others; see LICENSE.txt")
LICENSE = "Creative Commons Attribution 3.0 (https://creativecommons.org/licenses/by/3.0/)"
SOURCE = "https://tangaria.com/tileset/"
SHEET = "tangaria.png"
COLUMNS = 32


def plain(name):
    """Lower case without accents: Tangaria spells Sméagol 'Smeagol'."""
    return "".join(c for c in unicodedata.normalize("NFKD", name) if not unicodedata.combining(c)).lower()


def cell(r):
    col, row = r.split(":")[1].split(",")
    return int(col), int(row)


def variants(by_light):
    """PWMAngband's torch/los/lit/dark -> AVABand's lit/torch/dark. 'los' is the normal lit look and
    Tangaria uses 'lit' as the remembered look ('dark' is nearly black)."""
    lit = by_light.get("los") or by_light.get("*") or by_light.get("lit")
    torch = by_light.get("torch") or lit
    dark = by_light.get("lit") or by_light.get("dark") or lit
    return lit if lit == torch == dark else {"lit": lit, "torch": torch, "dark": dark}


def main():
    src, data, out = sys.argv[1:4]
    load = lambda name: json.load(open(os.path.join(data, name), encoding="utf-8"))
    feats, traps, monsters, player = parse(os.path.join(src, "graf-tan.prf"))
    monsters = {plain(k): v for k, v in monsters.items()}
    tiles, missing = {}, []

    for our_id, name in TERRAIN.items():
        tiles[f"terrain:{our_id}"] = variants(feats[name])
    for t in load("traps.json"):
        name = TRAPS.get(t["id"], t["id"].replace("_", " "))
        if t["id"] == "web":
            tiles["trap:web"] = variants(feats["web"])
        elif name in traps:
            tiles[f"trap:{t['id']}"] = variants(traps[name])
        else:
            missing.append(f"trap:{t['id']}")

    own = 0
    for race in load("monsters.json"):
        r = monsters.get(MONSTERS.get(race["id"], plain(race["name"])))
        if r:
            tiles[f"monster:{race['id']}"] = r
            own += 1
        else:
            missing.append(f"monster:{race['id']}")
    tiles["player"] = player  # graf-tan.prf's default: the human warrior

    objects, pile = parse_objects(os.path.join(src, "graf-tan.prf"))
    flavors = parse_flavors(os.path.join(src, "flvr-tan.prf"), os.path.join(src, "flavor.txt"))
    kinds = load("objects.json")
    for kind in kinds:
        tval = TVALS.get(kind["base"])
        r = objects.get((tval, norm(kind["name"]))) if tval else None
        if kind["id"] in OBJECTS:
            r = objects.get(OBJECTS[kind["id"]])
        if r:
            tiles[f"object:{kind['id']}"] = r
    # Books: Tangaria's differ in name, so the n-th book of a realm gets the n-th Tangaria book's tile.
    for base in BOOKS:
        theirs = [r for (tval, _), r in objects.items() if tval == TVALS[base]]
        ours = [k for k in kinds if k["base"] == base]
        for k, r in zip(ours, theirs):
            tiles.setdefault(f"object:{k['id']}", r)
    tiles["object:pile"] = pile
    nflav = 0
    for group in load("flavors.json"):
        for fl in group.get("flavors", []):
            r = flavors.get((group["id"], fl["name"].lower()))
            if r:
                tiles[f"flavor:{group['id']}:{fl['name'].lower()}"] = r
                nflav += 1
    for base in load("object_bases.json"):
        mapped = [tiles[f"object:{k['id']}"] for k in kinds
                  if k["base"] == base["id"] and f"object:{k['id']}" in tiles]
        fallback = mapped[0] if mapped else flavors.get((base.get("flavor") or base["id"], "*"))
        if fallback:
            tiles[f"object-base:{base['id']}"] = fallback
        else:
            missing.append(f"object-base:{base['id']}")

    # Pack the cells used into one sheet and point the tiles at it.
    used = sorted({cell(r) for v in tiles.values() for r in (v.values() if isinstance(v, dict) else [v])},
                  key=lambda c: (c[1], c[0]))
    where = {c: (i % COLUMNS, i // COLUMNS) for i, c in enumerate(used)}
    source = Image.open(os.path.join(src, "32x32.png")).convert("RGBA")
    sheet = Image.new("RGBA", (COLUMNS * 32, (len(used) + COLUMNS - 1) // COLUMNS * 32))
    for (c, r), (x, y) in where.items():
        sheet.paste(source.crop((c * 32, r * 32, c * 32 + 32, r * 32 + 32)), (x * 32, y * 32))
    os.makedirs(out, exist_ok=True)
    sheet.save(os.path.join(out, SHEET), optimize=True)

    def moved(r):
        x, y = where[cell(r)]
        return f"main:{x},{y}"
    tiles = {k: ({lk: moved(r) for lk, r in v.items()} if isinstance(v, dict) else moved(v)) for k, v in tiles.items()}
    manifest = {
        "name": NAME, "author": AUTHOR, "license": LICENSE, "source": SOURCE,
        "tileWidth": 32, "tileHeight": 32, "sheets": {"main": SHEET},
        "opaque": False,  # creatures, objects and traps are drawn over the terrain
        "tiles": tiles,
    }
    with open(os.path.join(out, "tileset.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=1, ensure_ascii=False)
        f.write("\n")
    objs = sum(k.startswith("object:") for k in tiles) - 1
    print(f"{len(tiles)} tiles in {len(used)} cells; monsters with art {own}, objects {objs}, flavours {nflav}; "
          f"unmapped: {', '.join(missing) or 'none'}")


if __name__ == "__main__":
    main()
