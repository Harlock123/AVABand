#!/usr/bin/env python3
"""Convert an Angband 4.2 tileset (graf-*.prf + xtra-*.prf + sheet PNG) into an AVABand tileset.json.

Angband's pref lines look like:
    feat:FLOOR:lit:0x80:0x81        (feature code, lighting, attr=0x80+row, char=0x80+col)
    trap:trap door:torch:0x81:0x8E
    monster:Fang, Farmer Maggot's dog:0x9D:0x9D
AVABand keys are namespaced ids: terrain:<id>, trap:<id>, monster:<id>, monster-name:<name>, player.

Usage:
    angband_prf_to_tileset.py <graf.prf> <xtra.prf> <out/tileset.json> --name ... --sheet 16x16.png
        --size 16 16 --author ... --license ... --source ... --monsters data/monsters.json --traps data/traps.json
"""
import argparse, json, re

# AVABand terrain id -> Angband feature code
TERRAIN = {
    "none": "NONE", "floor": "FLOOR", "closed_door": "CLOSED", "open_door": "OPEN", "broken_door": "BROKEN",
    "up_staircase": "LESS", "down_staircase": "MORE",
    "shop_general": "STORE_GENERAL", "shop_armoury": "STORE_ARMOR", "shop_weaponsmith": "STORE_WEAPON",
    "shop_bookseller": "STORE_BOOK", "shop_alchemist": "STORE_ALCHEMY", "shop_magic": "STORE_MAGIC",
    "shop_black_market": "STORE_BLACK", "shop_home": "HOME",
    "rubble": "RUBBLE", "passable_rubble": "PASS_RUBBLE", "magma_vein": "MAGMA", "quartz_vein": "QUARTZ",
    "magma_with_treasure": "MAGMA_K", "quartz_with_treasure": "QUARTZ_K",
    "granite_wall": "GRANITE", "permanent_wall": "PERM", "lava": "LAVA",
}

# AVABand trap id -> Angband trap name
TRAPS = {
    "trap_door": "trap door", "pit": "pit", "spiked_pit": "spiked pit", "slow_dart": "slow dart",
    "weakness_dart": "strength loss dart", "fire_rune": "fire trap", "acid_rune": "acid trap",
    "poison_gas": "poison gas trap", "confusion_gas": "confusion gas trap", "sleep_gas": "sleep gas trap",
    "teleport_rune": "teleport rune", "summon_rune": "rune of summoning", "alarm": "siren",
    "glyph_warding": "glyph of warding",
}

# AVABand monster names Angband 4.2 spells differently.
MONSTER_ALIASES = {"jackal": "wild dog", "hill_orc": "half-orc", "dark_elven_priest": "priest",
                   "beorn_the_mountain_bear": "beorn, the shape-changer"}

# A tile a set may lack -> the tile to borrow instead.
BORROW = {"terrain:rubble": "terrain:passable_rubble", "terrain:passable_rubble": "terrain:rubble",
          "trap:block_fall_trap": "trap:rock_fall_trap"}

# AVABand object base -> Angband tval
TVALS = {
    "sword": "sword", "hafted": "hafted", "polearm": "polearm", "sling": "bow", "bow": "bow", "crossbow": "bow",
    "shot": "shot", "arrow": "arrow", "bolt": "bolt", "soft_armour": "soft armor", "hard_armour": "hard armor",
    "shield": "shield", "helm": "helm", "gloves": "gloves", "boots": "boots", "cloak": "cloak", "light": "light",
    "amulet": "amulet", "ring": "ring", "potion": "potion", "scroll": "scroll", "food": "food", "flask": "flask",
    "magic_book": "magic book", "prayer_book": "prayer book", "nature_book": "nature book",
    "shadow_book": "shadow book",
    "gold": "gold", "crown": "crown", "dragon_armour": "dragon armor", "mushroom": "mushroom",
    "wand": "wand", "staff": "staff", "rod": "rod", "digger": "digger", "chest": "chest",
}

OBJECT = re.compile(r"^object:([^:]+):(.+):((?:0x)?[0-9A-Fa-f]+):((?:0x)?[0-9A-Fa-f]+)\s*$")
FLAVOR = re.compile(r"^flavor:(\d+):((?:0x)?[0-9A-Fa-f]+):((?:0x)?[0-9A-Fa-f]+)\s*$")


def num(text):
    return int(text, 16) if text.lower().startswith("0x") else int(text)


def norm(name):
    """Compare object names loosely: case, plural markers, 'Set of', armour/armor."""
    n = name.lower().replace("~", "").replace("&", "").strip()
    for prefix in ("set of ", "pair of ", "a ", "an "):
        if n.startswith(prefix):
            n = n[len(prefix):]
    return n.replace("armour", "armor")


def parse_objects(path):
    objects, pile = {}, None
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            m = OBJECT.match(line.strip())
            if not m:
                continue
            tval, name, attr, char = m.groups()
            r = f"main:{num(char) - 0x80},{num(attr) - 0x80}"
            if name == "<pile>":
                pile = r
            else:
                objects.setdefault((tval.lower(), norm(name)), r)
    return objects, pile


def parse_flavors(flvr_path, flavor_txt):
    """flavour kind + name -> tile, joining the tileset's flvr prf with Angband's flavor.txt."""
    tiles = {}
    with open(flvr_path, encoding="utf-8", errors="replace") as f:
        for line in f:
            m = FLAVOR.match(line.strip())
            if m:
                idx, attr, char = m.groups()
                tiles[int(idx)] = f"main:{num(char) - 0x80},{num(attr) - 0x80}"
    result, kind = {}, None
    with open(flavor_txt, encoding="utf-8", errors="replace") as f:
        for line in f:
            parts = line.strip().split(":")
            if parts[0] == "kind":
                kind = parts[1]
            elif parts[0] in ("flavor", "fixed") and kind and int(parts[1]) in tiles:
                name = parts[-1].strip().lower()
                if name:
                    result.setdefault((kind, name), tiles[int(parts[1])])
                result.setdefault((kind, "*"), tiles[int(parts[1])])
    return result

LINE = re.compile(r"^(feat|trap):([^:]+):([^:]+):(0x[0-9A-Fa-f]+):(0x[0-9A-Fa-f]+)\s*$")
MONSTER = re.compile(r"^monster:(.+):(0x[0-9A-Fa-f]+):(0x[0-9A-Fa-f]+)\s*$")


def ref(attr, char):
    return f"main:{int(char, 16) - 0x80},{int(attr, 16) - 0x80}"


def parse(path):
    feats, traps, monsters, player = {}, {}, {}, None
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.strip()
            m = LINE.match(line)
            if m:
                kind, name, light, attr, char = m.groups()
                (feats if kind == "feat" else traps).setdefault(name.lower(), {})[light] = ref(attr, char)
                continue
            m = MONSTER.match(line)
            if m:
                name, attr, char = m.groups()
                if int(attr, 16) < 0x80 or int(char, 16) < 0x80:
                    continue  # a plain character, not a tile (Nomad's set draws a few monsters as letters)
                if name == "<player>":
                    player = player or ref(attr, char)  # first entry: Human Warrior
                else:
                    monsters.setdefault(name.lower(), ref(attr, char))
    return feats, traps, monsters, player


def lit_variants(by_light):
    """Collapse Angband lighting variants into AVABand's lit/torch/dark."""
    star = by_light.get("*")
    lit = by_light.get("lit") or by_light.get("los") or star
    torch = by_light.get("torch") or by_light.get("los") or star or lit
    dark = by_light.get("dark") or star or lit
    if lit == torch == dark:
        return lit
    return {"lit": lit, "torch": torch, "dark": dark}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("graf"); ap.add_argument("xtra"); ap.add_argument("out")
    ap.add_argument("--name", required=True); ap.add_argument("--author", required=True)
    ap.add_argument("--license", required=True); ap.add_argument("--source", required=True)
    ap.add_argument("--sheet", required=True); ap.add_argument("--size", nargs=2, type=int, required=True)
    ap.add_argument("--monsters", required=True); ap.add_argument("--traps", required=True)
    ap.add_argument("--objects", required=True); ap.add_argument("--bases", required=True)
    ap.add_argument("--flvr", required=True); ap.add_argument("--flavor-txt", required=True)
    ap.add_argument("--flavors", required=True, help="AVABand flavors.json")
    ap.add_argument("--extra", action="append", default=[], metavar="KEY=REF",
                    help="a tile the set lacks, e.g. trap:web=web.png (repeatable)")
    a = ap.parse_args()

    feats, traps, monsters, _ = parse(a.graf)
    _, _, extra_monsters, player = parse(a.xtra)
    for k, v in extra_monsters.items():
        monsters.setdefault(k, v)

    tiles, missing = {}, []
    for our_id, code in TERRAIN.items():
        if code.lower() in feats:
            tiles[f"terrain:{our_id}"] = lit_variants(feats[code.lower()])
        else:
            missing.append(f"terrain:{our_id}")
    # 4.2.5's traps go by trap.txt's first name, which is our id with spaces; a few older ids differ.
    trap_names = {t["id"]: TRAPS.get(t["id"], t["id"].replace("_", " ")) for t in json.load(open(a.traps))}
    for our_id, name in trap_names.items():
        if name in traps:
            tiles[f"trap:{our_id}"] = lit_variants(traps[name])
        else:
            missing.append(f"trap:{our_id}")
    for race in json.load(open(a.monsters)):
        name = MONSTER_ALIASES.get(race["id"], race["name"]).lower()
        if name in monsters:
            tiles[f"monster:{race['id']}"] = monsters[name]
        else:
            missing.append(f"monster:{race['id']}")
    if player:
        tiles["player"] = player

    # Objects: by name within the matching tval; flavoured kinds use per-flavour tiles.
    objects, pile = parse_objects(a.graf)
    flavors = parse_flavors(a.flvr, a.flavor_txt)
    bases = {b["id"]: b for b in json.load(open(a.bases))}
    for kind in json.load(open(a.objects)):
        tval = TVALS.get(kind["base"])
        r = objects.get((tval, norm(kind["name"]))) if tval else None
        if r:
            tiles[f"object:{kind['id']}"] = r
    if pile:
        tiles["object:pile"] = pile
    for group in json.load(open(a.flavors)):
        for fl in group.get("flavors", []):
            r = flavors.get((group["id"], fl["name"].lower()))
            if r:
                tiles[f"flavor:{group['id']}:{fl['name'].lower()}"] = r
    # A fallback per base: its first mapped kind, or any flavour tile of its group.
    for base_id, base in bases.items():
        mapped = [tiles[f"object:{k['id']}"] for k in json.load(open(a.objects))
                  if k["base"] == base_id and f"object:{k['id']}" in tiles]
        fallback = mapped[0] if mapped else flavors.get((base.get("flavor") or "", "*"))
        if fallback:
            tiles[f"object-base:{base_id}"] = fallback
        else:
            missing.append(f"object-base:{base_id}")
    # What the set lacks: tiles given on the command line, then a close neighbour's tile.
    for extra in a.extra:
        key, _, r = extra.partition("=")
        tiles[key] = r
    for key, neighbour in BORROW.items():
        if key not in tiles and neighbour in tiles:
            tiles[key] = tiles[neighbour]
    missing = [k for k in missing if k not in tiles]
    # Every Angband monster by name, so monsters added to the data later find their tile automatically.
    for name, r in sorted(monsters.items()):
        tiles[f"monster-name:{name}"] = r

    manifest = {
        "name": a.name, "author": a.author, "license": a.license, "source": a.source,
        "tileWidth": a.size[0], "tileHeight": a.size[1],
        "sheets": {"main": a.sheet},
        # Creature and trap tiles have transparent backgrounds; draw the terrain underneath.
        "opaque": False,
        "tiles": tiles,
    }
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print(f"{a.out}: {len(tiles)} tiles; unmapped: {', '.join(missing) or 'none'}")


if __name__ == "__main__":
    main()
