#!/usr/bin/env python3
"""Build the "hexany" tileset from Hexany's Roguelike Tiles (16x16, 1-bit, CC0).

Hexany's Roguelike Tiles by Hexany Ives: https://hexany-ives.itch.io/hexanys-roguelike-tiles
(version 0.3.0; the "Transparent" tile sheets are used).

The art is one light colour on transparency, so each tile is tinted here with the colour AVABand's
data gives the thing it stands for — a monster's colour from monsters.json, an object's from
objects.json, a flavour's from flavors.json, terrain's and traps' from their files — the way the
pack's own example maps colour it. That tells creatures that share a picture apart (a red and a white
baby dragon) and gives every unknown potion, ring or wand flavour its own colour. Very dark colours
are lifted so nothing vanishes on the black map. Every (picture, colour) pair used is packed into one
sheet, hexany.png; nothing else is copied.

Monsters are matched by name with the RULES below, then by glyph (GLYPHS); objects by name, then by
base; flavours use their base's picture in the flavour's colour. Run tileset_glyph_fallbacks.py
afterwards (it adds "monster-glyph:" keys for monsters added to the data later).

Stdlib only (PNG reading/writing comes from dcss_flavor_tiles.py next to this script).

Usage:
    hexany_tileset.py <pack's Tilesheets/Transparent folder> <data folder> <tilesets/hexany>
"""
import json, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dcss_flavor_tiles import png_read, png_write  # noqa: E402

T = 16
COLUMNS = 32
SHEETS = {"c": "creatures_transparent.png", "g": "general_transparent.png",
          "i": "items_transparent.png", "a": "autotile_transparent.png"}

# ---- Terrain and traps: key -> cell ("sheet:col,row") or a list of (cell, colour) layers composited. ----
WALL = "a:3,3"
TERRAIN = {
    "floor": "g:1,0", "closed_door": "g:4,0", "open_door": "g:5,0", "broken_door": "g:8,0",
    "up_staircase": "g:3,0", "down_staircase": "g:2,0",
    "shop_general": "g:23,0", "shop_armoury": "i:2,2", "shop_weaponsmith": "i:1,0", "shop_bookseller": "g:17,1",
    "shop_alchemist": "i:1,4", "shop_magic": "i:2,4", "shop_black_market": "i:17,3", "shop_home": "g:7,0",
    "rubble": "g:3,2", "passable_rubble": "g:1,2",
    "magma_vein": WALL, "quartz_vein": WALL, "granite_wall": WALL, "permanent_wall": "a:3,0",
    "magma_with_treasure": [(WALL, "LightDark"), ("i:4,3", "Orange")],
    "quartz_with_treasure": [(WALL, "Slate"), ("i:4,3", "Yellow")],
    "lava": "a:13,1",
}
TRAPS = {
    "glyph_warding": "g:7,1", "web": "g:21,1", "trap_door": "g:10,0", "pit": "g:25,1", "spiked_pit": "g:26,1",
    "poison_pit": "g:25,1", "hellhole": "g:25,1", "knife_trap": "g:26,1",
    "slow_dart": "i:11,0", "weakness_dart": "i:11,0", "dexterity_loss_dart": "i:11,0",
    "constitution_loss_dart": "i:11,0",
    "blinding_gas_trap": "i:4,3", "confusion_gas": "i:4,3", "poison_gas": "i:4,3", "sleep_gas": "i:4,3",
    "aggravation_trap": "g:14,0", "alarm": "g:14,0",
    "mine_trap": "i:10,1", "blast_trap": "i:10,1", "area_blast_trap": "i:10,1",
    "rock_fall_trap": "g:3,2", "earthquake_trap": "g:3,2", "block_fall_trap": "g:4,2",
    "blinding_flash_trap": "i:3,3", "blinding_trap": "i:3,3",
}
RUNE = "g:6,1"  # any other trap: the magic circle
PLAYER = "c:6,0"
PILE = "g:27,0"

# ---- Objects: (regex on the lower-case kind name, cell); first match wins, then BASES. Magic devices,
# jewellery, potions, scrolls, books and dragon armour always use their base's picture (in their colour). ----
BASE_ONLY = {"wand", "staff", "rod", "ring", "amulet", "potion", "scroll", "mushroom", "dragon_armour",
             "magic_book", "prayer_book", "nature_book", "shadow_book"}
OBJECT_RULES = [
    (r"dagger|main gauche", "i:0,1"), (r"rapier", "i:1,1"),
    (r"short sword|cutlass|tulwar|scimitar|katana", "i:0,0"),
    (r"zweihander|executioner|blade of chaos", "i:2,0"),
    (r"whip", "i:8,0"), (r"ball-and-chain", "i:20,3"), (r"flail", "i:8,1"), (r"morning star", "i:4,0"),
    (r"quarterstaff", "i:2,1"), (r"lucerne hammer", "i:4,1"), (r"hammer|maul", "i:6,1"), (r"mace", "i:3,0"),
    (r"trident", "i:7,1"), (r"halberd|glaive|lochaber|beaked axe", "i:5,1"), (r"throwing axe", "i:6,0"),
    (r"axe", "i:5,0"), (r"scythe", "i:7,0"), (r"spear|pike|\blance", "i:12,0"),
    (r"shovel|pick|mattock", "i:5,4"),
    (r"crossbow", "i:9,1"), (r"bow", "i:9,0"), (r"sling", "i:11,1"),
    (r"shot|pebble", "i:22,3"), (r"arrow", "i:10,0"), (r"bolt", "i:11,0"),
    (r"robe|leather armour|leather scale", "i:0,2"), (r"chain mail", "i:1,2"),
    (r"leather shield|wicker|small metal", "i:7,2"), (r"large metal", "i:9,2"),
    (r"\bcap\b", "i:5,2"), (r"helm", "i:6,2"),
    (r"gauntlets|caestus", "i:11,2"), (r"gloves", "i:10,2"),
    (r"torch", "i:9,3"), (r"lantern", "i:8,3"), (r"phial|star|arkenstone", "i:3,3"),
    (r"apple|fruit", "i:13,3"), (r"flesh|meat", "i:7,3"), (r"whisky|wine|miruvor|liquor|draught", "i:16,3"),
    (r"^(copper|silver|gold|mithril|adamantite)$", "i:5,3"),
]
BASES = {
    "sword": "i:1,0", "hafted": "i:4,0", "polearm": "i:12,0", "digger": "i:5,4", "sling": "i:11,1",
    "bow": "i:9,0", "crossbow": "i:9,1", "shot": "i:22,3", "arrow": "i:10,0", "bolt": "i:11,0",
    "soft_armour": "i:0,2", "hard_armour": "i:2,2", "shield": "i:8,2", "helm": "i:5,2", "gloves": "i:10,2",
    "boots": "i:3,2", "cloak": "i:0,2", "light": "i:9,3", "amulet": "i:1,3", "ring": "i:13,1",
    "magic_book": "g:22,0", "prayer_book": "g:22,0", "nature_book": "g:22,0", "shadow_book": "g:22,0",
    "potion": "i:1,4", "scroll": "i:0,4", "food": "i:12,3", "flask": "i:1,4", "crown": "i:6,2",
    "dragon_armour": "i:2,2", "mushroom": "c:10,14", "wand": "i:2,3", "staff": "i:2,4", "rod": "i:14,4",
    "chest": "g:26,0", "gold": "i:4,4",
}

# ---- Monsters: (regex on the lower-case name, cell); first match wins, then GLYPHS. ----
RULES = [
    (r"skeleton (troll|etten)|cantoras", "c:2,4"), (r"kobold", "c:12,3"), (r"feagwath|vecna", "c:5,7"),
    (r"shadow drake", "c:0,16"), (r"creeping .*coins", "i:5,3"), (r"potion mimic", "i:1,4"), (r"scroll mimic", "i:0,4"),
    (r"ring mimic", "i:13,1"), (r"chest mimic", "c:5,14"), (r"lurker|trapper", "c:13,6"),
    (r"shrieker", "c:9,14"), (r"shambling mound", "c:13,14"), (r"memory moss", "c:12,14"),
    (r"saruman", "c:15,0"), (r"sauron, the sorcerer|mouth of sauron", "c:0,7"),
    (r"ossë", "c:5,12"), (r"makar|meássë", "c:6,0"),
    (r"crow|craban", "c:3,10"), (r"roc\b", "c:2,10"), (r"winged horror", "c:15,6"),
    (r"harpy|hippogriff|griffon", "c:2,10"), (r"chimaera|gorgimaera|manticore", "c:15,15"),
    (r"minotaur", "c:8,4"), (r"jabberwock", "c:15,14"),
    (r"louse|flea", "c:13,3"),
    (r"great .*wyrm|wyrm of", "c:15,14"), (r"wyvern", "c:4,10"), (r"drolem", "c:0,15"),
    (r"kavlax|hydra", "c:4,15"),
    (r"air (spirit|elemental)|ariel", "c:2,6"), (r"will o' the wisp", "c:3,6"),
    (r"fire (spirit|elemental)|vargo|magma elemental", "c:2,16"),
    (r"water (spirit|elemental)|waldern", "c:7,14"), (r"ooze elemental", "c:8,14"),
    (r"invisible stalker|smoke elemental", "c:13,6"),
    (r"poltergeist", "c:2,6"), (r"banshee|moaning spirit|lost soul|spectre|phantom|spirit troll", "c:3,7"),
    (r"dread|shadow|nightwing|nightwalker", "c:13,6"),
    (r"black reaver|uvatha|adunaphel|akhorahil|ren the unclean|ji indur|dwar,|hoarmurath|khamûl|witch-king",
     "c:0,7"),
    (r"wraith", "c:3,7"), (r"wight", "c:1,7"),
    (r"serpent|nightcrawler", "c:4,15"),
    (r"cave ogre", "c:5,4"), (r"ogre (mage|shaman)", "c:12,4"), (r"black ogre|ogre chieftain|lokkak", "c:10,4"),
    (r"cyclops", "c:4,4"), (r"morgoth", "c:11,4"),
    (r"frog", "c:5,9"), (r"salamander|basilisk", "c:8,9"), (r"tarrasque", "c:15,14"),
    (r"scorpion", "c:3,15"), (r"tick\b", "c:13,3"),
    (r"stone troll|cave troll|bert|bill|tom the", "c:6,4"), (r"\bolog\b|etten", "c:9,4"),
    (r"balrog|gothmog|lungorthin", "c:7,7"), (r"vrock|fury|pazuzu", "c:15,6"), (r"horned reaper", "c:0,7"),
    (r"thuringwethil", "c:8,7"),
    (r"spectator|gauth", "c:9,6"), (r"beholder|omarax", "c:10,6"),
    (r"bone golem", "c:2,4"), (r"pukelman|colossus|silent watcher", "c:9,15"),
    (r"bullroarer|sméagol|hobbit", "c:10,1"), (r"^farmer maggot$", "c:2,1"),
    (r"father christmas|red-hatted elf", "c:9,3"), (r"blacklock mage|stiffbeard sorcerer", "c:8,3"),
    (r"dwarven lord|nár|ibun|khîm|mîm|fundin", "c:7,3"), (r"dwarf|stonefoot|ironfist", "c:6,3"),
    (r"mind flayer", "c:11,6"), (r"elf|eöl|maeglin|fëanorian", "c:4,3"),
    (r"gelatinous cube", "c:6,14"), (r"ooze", "c:8,14"),
    (r"orc archer|snaga|cave orc|grishn|lagduf|orc tracker", "c:3,3"),
    (r"doombat|bat of gorgoroth", "c:7,10"),
    (r"carrion crawler", "c:2,15"),
    (r"mumak|mûmak", "c:12,9"), (r"night mare", "c:3,9"), (r"catoblepas", "c:2,9"),
    (r"purple worm|wereworm", "c:14,15"),
    (r"druj", "c:6,7"),
    (r"mummified", "c:11,6"),
    (r"bodak", "c:14,6"),
    (r"troll", "c:1,4"),
    # People ('p' and 't').
    (r"idiot|urchin", "c:10,1"), (r"beggar|leper|wretch", "c:0,0"), (r"drunk", "c:1,0"),
    (r"merchant", "c:10,0"), (r"veteran|^soldier$|^warrior$", "c:9,0"),
    (r"wormtongue|apprentice|illusionist|mage|sorcerer|necromancer|demonologist", "c:14,0"),
    (r"witch|enchantress", "c:3,0"), (r"acolyte|priest|patriarch", "c:8,0"),
    (r"mystic", "c:12,0"), (r"druid|drúadan", "c:6,1"), (r"tamer", "c:4,1"),
    (r"ranger|archer|scout", "c:1,1"), (r"assassin|harowen|master thief", "c:0,1"),
    (r"cutpurse|rogue|brigand", "c:7,0"),
    (r"paladin|knight|blackguard|gallant|dúnadan|champion", "c:6,0"),
    (r"ar-pharazôn|castamir|lord of carn dûm|lorgan", "c:8,1"),
    (r"berserker|beorn, the shape|mercenary|ruffian|ulfang|ulfast|ulwarth|uldor|brodda|gorlim", "c:4,0"),
    (r"umbar", "c:5,0"),
]
GLYPHS = {
    "A": "c:3,6", "B": "c:0,10", "C": "c:1,9", "D": "c:0,15", "E": "c:10,15", "F": "c:1,16", "G": "c:2,7",
    "H": "c:2,10", "I": "c:1,16", "J": "c:6,9", "K": "c:8,15", "L": "c:5,7", "M": "c:2,16", "O": "c:0,4",
    "P": "c:3,4", "Q": "c:4,6", "R": "c:8,9", "S": "c:7,9", "T": "c:1,4", "U": "c:14,6", "V": "c:9,7",
    "W": "c:1,7", "X": "c:7,4", "Y": "c:13,9", "Z": "c:1,9", "a": "c:4,16", "b": "c:6,10", "c": "c:2,15",
    "d": "c:0,16", "e": "c:8,6", "f": "c:0,9", "g": "c:10,15", "h": "c:6,3", "i": "c:12,15", "j": "c:7,14",
    "k": "c:12,3", "l": "c:1,14", "m": "c:11,14", "n": "c:0,3", "o": "c:2,3", "p": "c:0,0", "q": "c:13,9",
    "r": "c:10,9", "s": "c:0,6", "t": "c:0,0", "u": "c:12,6", "v": "c:1,15", "w": "c:6,15", "x": "c:13,6",
    "y": "c:9,9", "z": "c:1,6", ",": "c:10,14", "$": "i:5,3", "!": "i:1,4", "?": "i:0,4", "=": "i:13,1",
    "~": "c:5,14", "@": "c:0,0",
}

MIN_LUMA = 90  # colours darker than this are lifted toward white


def hex_rgb(s):
    s = s.lstrip("#")
    return tuple(int(s[k:k + 2], 16) for k in (0, 2, 4))


def lift(rgb):
    luma = 0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2]
    if luma >= MIN_LUMA:
        return rgb
    t = (MIN_LUMA - luma) / (255 - luma)
    return tuple(round(v + (255 - v) * t) for v in rgb)


def main():
    src, data_dir, out_dir = sys.argv[1:4]
    load = lambda name: json.load(open(os.path.join(data_dir, name), encoding="utf-8"))
    colours = {k: lift(hex_rgb(v)) for k, v in load("colors.json").items()}
    sheets = {}

    def cell_pixels(ref):
        sheet, xy = ref.split(":")
        x, y = map(int, xy.split(","))
        if sheet not in sheets:
            sheets[sheet] = png_read(os.path.join(src, SHEETS[sheet]))
        w, h, px = sheets[sheet]
        if (x + 1) * T > w or (y + 1) * T > h:
            raise SystemExit(f"cell {ref} is outside {SHEETS[sheet]}")
        return [px[(y * T + r) * w + x * T + c] for r in range(T) for c in range(T)]

    packed, order = {}, []  # (layers) -> index

    def ref(layers):
        """layers: tuple of (cell, colour name). Returns "main:col,row" of the tinted, composited tile."""
        if layers not in packed:
            packed[layers] = len(order)
            order.append(layers)
        i = packed[layers]
        return f"main:{i % COLUMNS},{i // COLUMNS}"

    def tile(cell, colour):
        return ref(tuple(cell) if isinstance(cell, list) else ((cell, colour),))

    tiles = {}
    for t in load("terrain.json"):
        if t["id"] == "none" or t.get("mimic"):
            continue
        spec = TERRAIN[t["id"]]
        colour = "Slate" if t["id"] == "floor" else t["color"]
        tiles["terrain:" + t["id"]] = tile(spec, colour)
    for trap in load("traps.json"):
        tiles["trap:" + trap["id"]] = tile(TRAPS.get(trap["id"], RUNE), trap["color"])
    tiles["player"] = tile(PLAYER, "White")
    tiles["object:pile"] = tile(PILE, "White")

    bases = {b["id"]: b for b in load("object_bases.json")}
    for b in bases.values():
        tiles["object-base:" + b["id"]] = tile(BASES[b["id"]], b["color"])
    counts = {"object rule": 0, "object base": 0, "flavours": 0, "monster rule": 0, "monster glyph": 0}
    for o in load("objects.json"):
        name = o["name"].replace("~", "").replace("&", "").strip().lower()
        cell = None if o["base"] in BASE_ONLY else next((c for p, c in OBJECT_RULES if re.search(p, name)), None)
        counts["object rule" if cell else "object base"] += 1
        tiles["object:" + o["id"]] = tile(cell or BASES[o["base"]], o.get("color") or bases[o["base"]]["color"])
    for group in load("flavors.json"):
        cell = BASES.get(group["id"])
        for f in group.get("flavors", []) + group.get("fixed", []):
            tiles[f"flavor:{group['id']}:{f['name'].lower()}"] = tile(cell, f["color"])
            counts["flavours"] += 1

    for m in load("monsters.json"):
        name = m["name"].lower()
        cell = next((c for p, c in RULES if re.search(p, name)), None)
        counts["monster rule" if cell else "monster glyph"] += 1
        cell = cell or GLYPHS.get(m["glyph"])
        if cell is None:
            print("no art for", m["id"], m["glyph"])
            continue
        tiles["monster:" + m["id"]] = tile(cell, m["color"])

    # Pack the sheet.
    rows = (len(order) + COLUMNS - 1) // COLUMNS
    W, H = COLUMNS * T, rows * T
    out = [(0, 0, 0, 0)] * (W * H)
    for i, layers in enumerate(order):
        ox, oy = (i % COLUMNS) * T, (i // COLUMNS) * T
        for cell, colour in layers:
            rgb = colours[colour]
            for k, (r, g, b, a) in enumerate(cell_pixels(cell)):
                if a:
                    lum = max(r, g, b) / 255
                    out[(oy + k // T) * W + ox + k % T] = (round(rgb[0] * lum), round(rgb[1] * lum),
                                                          round(rgb[2] * lum), a)
    png_write(os.path.join(out_dir, "hexany.png"), W, H, out)

    manifest = {
        "name": "Hexany's Roguelike Tiles (16x16)",
        "author": "Hexany Ives",
        "license": "CC0 1.0 Public Domain Dedication (https://creativecommons.org/publicdomain/zero/1.0/)",
        "source": "https://hexany-ives.itch.io/hexanys-roguelike-tiles",
        "tileWidth": T, "tileHeight": T,
        "sheets": {"main": "hexany.png"},
        "opaque": False,
        "tiles": tiles,
    }
    with open(os.path.join(out_dir, "tileset.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(counts, f"{len(order)} tiles packed")


if __name__ == "__main__":
    main()
