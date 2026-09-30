#!/usr/bin/env python3
"""Tiles for AVABand's quests, in every bundled tileset — and the mappings that use them.

Draws five small pixel-art tiles (CC0, made for AVABand) — the sealed door, the dwarven forge, the
barrow altar, the hermit's door and the Key of Belegost — at each tileset's cell size (drawn at
16x16, scaled to fit), as ava_*.png in the tileset's folder; then writes every quest mapping into
its tileset.json (the rest borrow the nearest tile the set already has: the inn a shop door, the
relic an amulet, the shards a sword...). Re-run it after rebuilding any tileset.

Stdlib only (PNG writing from dcss_flavor_tiles.py).

Usage:
    avaband_quest_tiles.py src/Angband.Avalonia/tilesets
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dcss_flavor_tiles import png_write  # noqa: E402

N = 16
CLEAR = (0, 0, 0, 0)


def hexc(h, a=255):
    return (int(h[1:3], 16), int(h[3:5], 16), int(h[5:7], 16), a)


def canvas(fill=CLEAR):
    return [[fill for _ in range(N)] for _ in range(N)]


def rect(c, x0, y0, x1, y1, col):
    for y in range(max(0, y0), min(N, y1 + 1)):
        for x in range(max(0, x0), min(N, x1 + 1)):
            c[y][x] = col


def px(c, x, y, col):
    if 0 <= x < N and 0 <= y < N:
        c[y][x] = col


def stone_floor(c, base="#2c2a2e", joint="#1e1c20"):
    rect(c, 0, 0, N - 1, N - 1, hexc(base))
    for y in (4, 9, 14):
        rect(c, 0, y, N - 1, y, hexc(joint))
    for y0, xs in ((0, (5, 12)), (5, (2, 9, 14)), (10, (6, 12)), (15, (3, 10))):
        for x in xs:
            rect(c, x, y0, x, y0 + 3, hexc(joint))


def sealed_door():
    c = canvas()
    stone_floor(c, "#26232b", "#18161c")
    rect(c, 2, 1, 13, 15, hexc("#5a5066"))          # frame
    rect(c, 3, 2, 12, 15, hexc("#3a3244"))          # the door
    rect(c, 5, 1, 10, 1, hexc("#5a5066"))
    for x, y in ((5, 4), (10, 4), (4, 7), (11, 7), (5, 11), (10, 11), (6, 13), (9, 13)):
        px(c, x, y, hexc("#b477ff"))                # runes
    for x, y in ((6, 4), (9, 4), (4, 8), (11, 8)):
        px(c, x, y, hexc("#7a4fb0"))
    rect(c, 7, 7, 8, 9, hexc("#1a1620"))            # the lock
    px(c, 7, 8, hexc("#e6c35c"))
    px(c, 8, 8, hexc("#e6c35c"))
    return c


def forge():
    c = canvas()
    stone_floor(c)
    rect(c, 1, 5, 14, 15, hexc("#5b5550"))          # hearth
    rect(c, 2, 6, 13, 14, hexc("#46403c"))
    rect(c, 3, 8, 12, 13, hexc("#2a1a12"))          # firebox
    for x, y, col in ((4, 12, "#ff5a1a"), (5, 11, "#ff8a2a"), (6, 10, "#ffb84a"), (7, 9, "#fff08a"), (8, 10, "#ffb84a"),
                      (9, 11, "#ff8a2a"), (10, 12, "#ff5a1a"), (7, 11, "#ffd060"), (8, 12, "#ff8a2a"), (6, 12, "#ffb84a"),
                      (5, 13, "#c83a10"), (9, 13, "#c83a10"), (11, 13, "#c83a10"), (8, 8, "#ffe070"), (6, 9, "#ffa040")):
        px(c, x, y, hexc(col))
    rect(c, 5, 2, 10, 4, hexc("#6a645e"))           # the chimney hood
    rect(c, 6, 0, 9, 1, hexc("#6a645e"))
    return c


def altar():
    c = canvas()
    stone_floor(c)
    rect(c, 2, 5, 13, 7, hexc("#c4c0b4"))           # slab
    rect(c, 2, 8, 13, 9, hexc("#8e8a80"))
    rect(c, 4, 10, 11, 14, hexc("#77736a"))         # base
    rect(c, 3, 15, 12, 15, hexc("#5e5a52"))
    for x, y in ((6, 6), (7, 6), (9, 5), (5, 8), (10, 9)):
        px(c, x, y, hexc("#5a2a2a"))                # old stains
    px(c, 8, 12, hexc("#4a4640"))
    return c


def hermitage():
    c = canvas()
    stone_floor(c, "#34302a", "#26221e")
    rect(c, 3, 3, 12, 15, hexc("#1c1814"))          # the doorway
    rect(c, 4, 4, 11, 15, hexc("#6b4a2a"))          # planks
    for x in (6, 9):
        rect(c, x, 4, x, 15, hexc("#553a20"))
    rect(c, 5, 2, 10, 2, hexc("#1c1814"))
    rect(c, 6, 1, 9, 1, hexc("#1c1814"))
    rect(c, 7, 6, 8, 7, hexc("#ffd070"))            # a lamp in the window
    px(c, 7, 5, hexc("#ffe8a0"))
    px(c, 10, 11, hexc("#c8a060"))                  # the latch
    return c


def key():
    c = canvas()
    iron, dark, gold = hexc("#8c8c96"), hexc("#4c4c56"), hexc("#e6c35c")
    for x, y in ((2, 5), (3, 4), (4, 4), (5, 5), (5, 6), (5, 7), (4, 8), (3, 8), (2, 7), (1, 6)):
        px(c, x, y, iron)                            # the bow (a ring)
    px(c, 3, 6, dark)
    rect(c, 6, 6, 13, 7, iron)                       # the shaft
    rect(c, 6, 7, 13, 7, dark)
    rect(c, 11, 8, 11, 10, iron)                     # the bit
    rect(c, 13, 8, 13, 11, iron)
    rect(c, 12, 10, 12, 10, iron)
    px(c, 3, 3, gold)
    px(c, 8, 6, gold)
    px(c, 10, 6, gold)
    return c


def brazier(lit):
    c = canvas()
    stone_floor(c)
    iron, dark = hexc("#5a5a62"), hexc("#34343a")
    rect(c, 6, 12, 9, 14, dark)                     # the stone foot
    rect(c, 4, 15, 11, 15, dark)
    rect(c, 7, 9, 8, 11, iron)                      # the stem
    rect(c, 2, 6, 13, 8, iron)                      # the bowl
    rect(c, 3, 9, 12, 9, dark)
    if lit:
        for x, y, col in ((4, 5, "#ff5a1a"), (5, 4, "#ff8a2a"), (6, 3, "#ffb84a"), (7, 1, "#fff08a"), (8, 2, "#ffd060"), (9, 3, "#ffb84a"),
                          (10, 4, "#ff8a2a"), (11, 5, "#ff5a1a"), (7, 4, "#ffe070"), (8, 4, "#fff08a"), (6, 5, "#ffb84a"), (9, 5, "#ffb84a"),
                          (7, 2, "#fff08a"), (8, 3, "#ffe070"), (5, 5, "#ff8a2a"), (10, 5, "#ff8a2a"), (7, 5, "#ffd060"), (8, 5, "#ffd060")):
            px(c, x, y, hexc(col))
    else:
        rect(c, 3, 5, 12, 5, hexc("#2a2a2e"))       # cold ash
        for x in (4, 7, 10):
            px(c, x, 5, hexc("#6a6a70"))
    return c


def apprentice():
    c = canvas()
    stone_floor(c, "#2e2a26", "#221e1a")
    rubble = hexc("#6e6458")
    for x, y in ((1, 10), (2, 9), (3, 11), (12, 9), (13, 10), (14, 12), (1, 13), (14, 14), (2, 14)):
        rect(c, x, y, x + 1, y + 1, rubble)
    rect(c, 6, 2, 9, 5, hexc("#e0b890"))            # a face
    rect(c, 6, 1, 9, 1, hexc("#6a4020"))            # hair
    px(c, 7, 3, hexc("#202020"))
    px(c, 9, 3, hexc("#202020"))
    rect(c, 5, 6, 10, 12, hexc("#3a6a3a"))          # a green coat
    rect(c, 4, 7, 4, 10, hexc("#3a6a3a"))
    rect(c, 11, 7, 11, 10, hexc("#3a6a3a"))
    rect(c, 6, 9, 9, 11, hexc("#8a6a3a"))           # the basket of moss
    rect(c, 7, 9, 8, 9, hexc("#5aa050"))
    rect(c, 5, 13, 10, 15, rubble)                  # pinned under the rock
    return c


DRAWN = {
    "terrain:sealed_door": ("ava_sealed_door.png", sealed_door, True),
    "terrain:dwarven_forge": ("ava_dwarven_forge.png", forge, True),
    "terrain:quest_altar": ("ava_quest_altar.png", altar, True),
    "terrain:hermitage": ("ava_hermitage.png", hermitage, True),
    "object:key_of_belegost": ("ava_key.png", key, False),
    "terrain:cold_brazier": ("ava_brazier_cold.png", lambda: brazier(False), True),
    "terrain:lit_brazier": ("ava_brazier_lit.png", lambda: brazier(True), True),
    "terrain:trapped_apprentice": ("ava_apprentice.png", apprentice, True),
}
# The rest borrow the nearest tile the set has (the first that exists).
BORROWED = [
    ("terrain:shop_inn", ["terrain:shop_general", "terrain:shop_home"]),
    ("object-base:quest", ["object-base:scroll"]),
    ("object-base:relic", ["object-base:amulet"]),
    ("object:shard_of_the_hilt", ["object-base:sword"]),
    ("object:shard_of_the_blade", ["object-base:sword"]),
    ("object:shard_of_the_point", ["object-base:sword"]),
    ("object:reforged_blade", ["object:long_sword", "object-base:sword"]),
    ("object:water_of_ulmo", ["object-base:potion"]),
    ("object:black_market_strongbox", ["object-base:chest"]),
    ("object:wardens_taper", ["object:wooden_torch", "object-base:light"]),
]


def scaled(c, w, h, full):
    """The 16x16 drawing at the set's cell size: scaled down or up (nearest), squeezed into a
    narrow cell (8x16) as a square in the middle, the rest filled like the tile's edge."""
    side = min(w, h)
    out = [[CLEAR for _ in range(w)] for _ in range(h)]
    edge = c[0][0] if full else CLEAR
    for y in range(h):
        for x in range(w):
            out[y][x] = edge
    ox, oy = (w - side) // 2, (h - side) // 2
    for y in range(side):
        for x in range(side):
            out[oy + y][ox + x] = c[y * N // side][x * N // side]
    return [p for row in out for p in row]


def main():
    root = sys.argv[1]
    for name in sorted(os.listdir(root)):
        path = os.path.join(root, name, "tileset.json")
        if not os.path.exists(path):
            continue
        text = open(path, encoding="utf-8").read()
        manifest = json.loads(text)
        w, h = manifest["tileWidth"], manifest["tileHeight"]
        tiles = manifest["tiles"]
        wanted = {}
        for key, (file, draw, full) in DRAWN.items():
            png_write(os.path.join(root, name, file), w, h, scaled(draw(), w, h, full))
            wanted[key] = file
        for key, sources in BORROWED:
            source = next((s for s in sources if s in tiles), None)
            if source is not None:
                wanted[key] = tiles[source]
        # Write the mappings in the file's own style: replace a line that has the key, else add it.
        indent = re.search(r'\n([ \t]*)"terrain:floor"', text).group(1)
        for key, value in wanted.items():
            line = f'{indent}{json.dumps(key)}: {json.dumps(value, ensure_ascii=False, separators=(", ", ": "))}'
            pattern = re.compile(r'\n[ \t]*' + re.escape(json.dumps(key)) + r': [^\n]*?(,?)(?=\n)')
            if pattern.search(text):
                text = pattern.sub(lambda m: "\n" + line + m.group(1), text, count=1)
            else:
                close = text.rstrip()[:-1].rstrip()          # without the root's closing brace
                tiles_close = len(close) - 1                  # the tiles object's closing brace
                before = close[:tiles_close].rstrip()
                text = before + ",\n" + line + close[len(before):] + text.rstrip()[len(close):] + "\n"
        json.loads(text)
        open(path, "w", encoding="utf-8").write(text)
        print(f"{name}: {len(DRAWN)} drawn, {len(wanted) - len(DRAWN)} borrowed ({w}x{h})")


if __name__ == "__main__":
    main()
