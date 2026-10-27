#!/usr/bin/env python3
"""Tiles for AVABand's quests, in every bundled tileset — and the mappings that use them.

Draws small pixel-art tiles (CC0, made for AVABand) — the quests' places and things (the sealed
door, the dwarven forge, the barrow altar, the hermit's door, the braziers, the trapped apprentice,
the Key of Belegost, the dungeon's shrines), every shop's door (its number and an emblem of its trade, so no two look alike) and AVABand's own items (the bags of holding, socketed bracers, gems, weapon oils
and monster trophies, each gem, oil and dragon scale drawn in its kind's colour) — at each tileset's cell size (drawn at
16x16, scaled to fit), as ava_*.png in the tileset's folder; then writes every quest mapping into
its tileset.json (the rest borrow the nearest tile the set already has: the inn a shop door, the
relic an amulet, the shards a sword, Durgash an orc chief, Hathol a barrow-wight...). Re-run it after rebuilding any tileset.

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


def fallen_note():
    """A message scratched into the floor by one who fell (AVABand's messages from the fallen): pale strokes in the stone."""
    c = canvas()
    stone_floor(c)
    pale, deep = hexc("#d8cfa8"), hexc("#8a8068")
    # Three rows of scratched letters, each stroke with a darker groove beneath it.
    for y, strokes in ((4, ((2, 4), (6, 9), (11, 13))), (8, ((3, 6), (8, 8), (10, 13))), (12, ((2, 5), (7, 10)))):
        for x0, x1 in strokes:
            for x in range(x0, x1 + 1):
                px(c, x, y - (x % 2), pale)
                px(c, x, y + 1, deep)
    px(c, 12, 12, pale)                              # a last, hurried mark
    px(c, 13, 11, pale)
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


def bag():
    """A drawstring bag of holding (AVABand's bags)."""
    c = canvas()
    leather, dark, cord, rune = hexc("#8a5a2e"), hexc("#5e3c1e"), hexc("#d8c08a"), hexc("#c8d8ff")
    rect(c, 4, 7, 11, 14, leather)                   # the body
    rect(c, 3, 9, 12, 13, leather)
    rect(c, 5, 15, 10, 15, dark)
    rect(c, 3, 13, 3, 13, dark)
    rect(c, 12, 13, 12, 13, dark)
    rect(c, 6, 4, 9, 6, leather)                     # the gathered neck
    rect(c, 5, 6, 10, 6, cord)                       # the drawstring
    px(c, 4, 7, cord)
    px(c, 11, 7, cord)
    rect(c, 7, 2, 8, 3, dark)                        # the tie
    for x, y in ((6, 10), (9, 10), (7, 12), (8, 12)):
        px(c, x, y, rune)                            # silver runes
    return c


def bracers():
    """A pair of socketed bracers (AVABand's arms slot)."""
    c = canvas()
    band, dark, gem = hexc("#a0826a"), hexc("#6a5444"), hexc("#d04040")
    for x0 in (1, 9):
        rect(c, x0, 3, x0 + 5, 13, band)
        rect(c, x0, 5, x0 + 5, 5, dark)
        rect(c, x0, 11, x0 + 5, 11, dark)
        rect(c, x0 + 2, 7, x0 + 3, 9, dark)          # the socket
        px(c, x0 + 2, 8, gem)
        px(c, x0 + 3, 8, gem)
    return c


def gem(color):
    """A cut stone, in the gem's colour, with a highlight."""
    c = canvas()
    r, g, b = int(color[1:3], 16), int(color[3:5], 16), int(color[5:7], 16)
    body = (r, g, b, 255)
    shade = (r * 6 // 10, g * 6 // 10, b * 6 // 10, 255)
    light = (min(255, r + 90), min(255, g + 90), min(255, b + 90), 255)
    for y, (x0, x1) in enumerate([(6, 9), (4, 11), (3, 12), (3, 12), (4, 11), (5, 10), (6, 9), (7, 8)], start=4):
        rect(c, x0, y, x1, y, body)
    rect(c, 3, 6, 12, 6, shade)                      # the girdle
    for y in range(7, 12):
        px(c, 3 + (y - 6), y, shade)
    px(c, 6, 5, light)
    px(c, 7, 5, light)
    px(c, 5, 7, light)
    return c


# A 3x5 pixel font for the shopfront numerals.
DIGITS = {
    "0": ["###", "#.#", "#.#", "#.#", "###"],
    "1": [".#.", "##.", ".#.", ".#.", "###"],
    "2": ["###", "..#", "###", "#..", "###"],
    "3": ["###", "..#", ".##", "..#", "###"],
    "4": ["#.#", "#.#", "###", "..#", "..#"],
    "5": ["###", "#..", "###", "..#", "###"],
    "6": ["###", "#..", "###", "#.#", "###"],
    "7": ["###", "..#", ".#.", ".#.", ".#."],
    "8": ["###", "#.#", "###", "#.#", "###"],
    "9": ["###", "#.#", "###", "..#", "###"],
}

# Each shop's sign: its number, the sign's colour, and an 8x5 emblem of its trade.
EMBLEMS = {
    "barrel": [".######.", "#......#", "########", "#......#", ".######."],
    "shield": ["#######.", "#.....#.", "#.....#.", ".#...#..", "..###..."],
    "sword": ["......##", ".....##.", "#..##...", ".##.....", "#.#....."],
    "book": ["########", "#...#..#", "#...#..#", "#...#..#", "########"],
    "potion": ["...##...", "...##...", "..####..", ".######.", ".######."],
    "star": ["...#....", ".#.#.#..", "..###...", ".#.#.#..", "...#...."],
    "skull": ["..####..", ".#.##.#.", ".######.", "..#..#..", "..####.."],
    "house": ["...##...", "..####..", ".######.", ".#.##.#.", ".#.##.#."],
    "mug": [".####...", ".#..###.", ".#..#.#.", ".#..###.", ".####..."],
    "gem": ["...##...", "..####..", ".######.", "..####..", "...##..."],
}
SHOPS = {  # terrain id: (number, sign colour, emblem)
    "shop_general": ("1", "#6a4a2a", "barrel"),
    "shop_armoury": ("2", "#4a5a6a", "shield"),
    "shop_weaponsmith": ("3", "#7a2a2a", "sword"),
    "shop_bookseller": ("4", "#2a3a7a", "book"),
    "shop_alchemist": ("5", "#2a6a3a", "potion"),
    "shop_magic": ("6", "#1a6a7a", "star"),
    "shop_black_market": ("7", "#1a1a1e", "skull"),
    "shop_home": ("8", "#8a5a1a", "house"),
    "shop_inn": ("9", "#3a5a2a", "mug"),
    "shop_artificer": ("0", "#5a2e7a", "gem"),
}


def shopfront(digit, sign, emblem):
    """A town building's entrance: a sign over the door with the shop's number and an emblem of its
    trade, in the shop's own colour — so no two shops look alike, in any tileset."""
    c = canvas()
    stone_floor(c, "#4a4450", "#36323c")
    rect(c, 0, 1, 15, 7, hexc(sign))                      # the sign board
    rect(c, 0, 1, 15, 1, hexc("#2a2630"))
    rect(c, 0, 7, 15, 7, hexc("#2a2630"))
    for y, row in enumerate(DIGITS[digit]):              # the number, in gold
        for x, ch in enumerate(row):
            if ch == "#":
                px(c, 1 + x, 2 + y, hexc("#ffe080"))
    for y, row in enumerate(EMBLEMS[emblem]):            # the emblem, pale
        for x, ch in enumerate(row):
            if ch == "#":
                px(c, 6 + x, 2 + y, hexc("#f0ece0"))
    rect(c, 5, 9, 10, 15, hexc("#6a4424"))                # the door
    rect(c, 5, 9, 10, 9, hexc("#3a2414"))
    rect(c, 7, 9, 8, 15, hexc("#4e3018"))
    px(c, 9, 12, hexc("#e0c060"))                         # its handle
    return c


def wagon():
    """A merchant caravan's wagon (AVABand's caravans): a striped canvas cover over a wooden bed on two
    wheels, standing on the town's cobbles."""
    c = canvas()
    stone_floor(c, "#4a4450", "#36323c")
    for i, x0 in enumerate(range(2, 14, 3)):                  # the cover, striped amber and canvas
        rect(c, x0, 3, x0 + 2, 8, hexc("#d8a030" if i % 2 == 0 else "#efe4c8"))
    rect(c, 2, 2, 13, 2, hexc("#efe4c8"))                     # its rounded top
    rect(c, 1, 9, 14, 11, hexc("#6a4422"))                    # the bed
    rect(c, 1, 9, 14, 9, hexc("#8a5c30"))
    for cx in (4, 11):                                        # the wheels
        rect(c, cx - 1, 11, cx + 1, 14, hexc("#2a1c10"))
        rect(c, cx - 2, 12, cx + 2, 13, hexc("#2a1c10"))
        px(c, cx, 12, hexc("#8a5c30"))
    return c


def cobbles(c):
    """The town's cobbles, under each of the things the town puts up as it grows (as under the wagon)."""
    stone_floor(c, "#4a4450", "#36323c")


def lamp_post():
    """A lamp post (the town grown with your renown): an iron post on a foot, a glass lamp lit at the top."""
    c = canvas()
    cobbles(c)
    iron, dark, shine = hexc("#9a9aa8"), hexc("#141418"), hexc("#d0d0dc")
    rect(c, 4, 13, 11, 15, dark)                     # the foot
    rect(c, 6, 4, 9, 13, dark)                       # the post, outlined
    rect(c, 7, 5, 8, 13, iron)
    rect(c, 7, 5, 7, 12, shine)
    rect(c, 4, 4, 11, 4, dark)                       # the lamp's cap
    rect(c, 5, 0, 10, 0, dark)
    rect(c, 5, 14, 10, 14, iron)
    rect(c, 5, 1, 10, 3, hexc("#ffd060"))            # the lamp, lit
    rect(c, 6, 1, 9, 3, hexc("#fff0a0"))
    px(c, 7, 2, hexc("#ffffff"))
    px(c, 4, 2, hexc("#ffb84a", 140))                # its glow
    px(c, 11, 2, hexc("#ffb84a", 140))
    return c


def well():
    """A stone well with a little roof and a bucket."""
    c = canvas()
    cobbles(c)
    roof, post, stone, water = hexc("#8a3a24"), hexc("#6a4422"), hexc("#9a96a0"), hexc("#2050c0")
    for y, (x0, x1) in enumerate([(6, 9), (4, 11), (2, 13)]):
        rect(c, x0, y, x1, y, roof)                  # the roof
    rect(c, 3, 3, 3, 9, post)                        # its posts
    rect(c, 12, 3, 12, 9, post)
    rect(c, 7, 3, 7, 6, hexc("#c8b080"))             # the rope and bucket
    rect(c, 6, 7, 8, 8, post)
    rect(c, 2, 10, 13, 15, stone)                    # the stone ring
    rect(c, 4, 10, 11, 11, water)
    for x in (2, 6, 10):
        rect(c, x, 13, x, 15, hexc("#6a6670"))
    return c


def stall():
    """A market stall: a striped awning on two posts over a trestle of goods."""
    c = canvas()
    cobbles(c)
    for i, x0 in enumerate(range(1, 15, 2)):         # the awning, striped red and cream
        rect(c, x0, 1, x0 + 1, 4, hexc("#c83a2a" if i % 2 == 0 else "#efe4c8"))
    for x in range(1, 15, 2):
        px(c, x, 5, hexc("#c83a2a"))                 # its scalloped edge
    rect(c, 2, 5, 2, 14, hexc("#6a4422"))            # the posts
    rect(c, 13, 5, 13, 14, hexc("#6a4422"))
    rect(c, 1, 10, 14, 11, hexc("#8a5c30"))          # the trestle
    for x, col in ((3, "#e04030"), (5, "#40b040"), (7, "#f0d050"), (9, "#e04030"), (11, "#f0a030")):
        rect(c, x, 8, x + 1, 9, hexc(col))           # its goods
    return c


def fountain():
    """A round fountain: a stone basin of water, a jet in the middle."""
    c = canvas()
    cobbles(c)
    stone, water, light = hexc("#b0acb8"), hexc("#3070e0"), hexc("#a0d8ff")
    for y, (x0, x1) in enumerate([(5, 10), (3, 12), (2, 13), (1, 14), (1, 14), (1, 14), (2, 13), (3, 12), (5, 10)], start=5):
        rect(c, x0, y, x1, y, stone)                 # the basin
    for y, (x0, x1) in enumerate([(5, 10), (3, 12), (3, 12), (3, 12), (3, 12), (5, 10)], start=6):
        rect(c, x0, y, x1, y, water)
    rect(c, 7, 2, 8, 9, light)                       # the jet
    for x, y in ((5, 2), (10, 2), (4, 4), (11, 4), (6, 1), (9, 1)):
        px(c, x, y, light)                           # its spray
    px(c, 5, 8, light)
    px(c, 10, 10, light)
    return c


def statue():
    """A statue on a plinth: a figure in pale stone, sword raised."""
    c = canvas()
    cobbles(c)
    stone, shade, plinth = hexc("#e0dcd0"), hexc("#a8a498"), hexc("#7a7684")
    rect(c, 3, 12, 12, 15, plinth)                   # the plinth
    rect(c, 4, 12, 11, 12, hexc("#9a96a4"))
    rect(c, 7, 1, 8, 2, stone)                       # the head
    rect(c, 6, 3, 9, 7, stone)                       # the body
    rect(c, 9, 3, 9, 7, shade)
    rect(c, 6, 8, 6, 11, stone)                      # the legs
    rect(c, 9, 8, 9, 11, shade)
    rect(c, 4, 3, 5, 4, stone)                       # the raised arm
    rect(c, 3, 0, 3, 3, hexc("#c8c8d0"))             # and the sword
    rect(c, 2, 3, 4, 3, shade)
    return c


def shrine_altar(c, slab, side, foot):
    """A shrine's altar (AVABand's shrines): a slab on a stepped foot, its colours the shrine's own."""
    rect(c, 2, 9, 13, 10, slab)                      # the slab
    rect(c, 2, 11, 13, 11, side)
    rect(c, 4, 12, 11, 14, foot)                     # the foot
    rect(c, 3, 15, 12, 15, side)
    px(c, 3, 9, hexc("#ffffff", 120))


def shrine(power):
    """A shrine to one of the Valar, or Morgoth's dark altar, or one gone dark: the altar, and the power's sign above it."""
    c = canvas()
    stone_floor(c)
    if power == "morgoth":
        iron, rust, red = hexc("#3a3438"), hexc("#241e22"), hexc("#e02020")
        shrine_altar(c, iron, rust, hexc("#2c2628"))
        for x in (2, 5, 10, 13):                      # the spikes of its crown
            rect(c, x, 6, x, 8, iron)
            px(c, x, 5, red)
        rect(c, 6, 2, 9, 7, hexc("#5a0a0a"))          # a red fire on it
        rect(c, 7, 1, 8, 6, red)
        px(c, 7, 3, hexc("#ffb040"))
        px(c, 8, 4, hexc("#ffb040"))
        for x, y in ((5, 9), (10, 10), (8, 9)):
            px(c, x, y, hexc("#701010"))              # old stains
        return c
    if power == "spent":
        shrine_altar(c, hexc("#6a6870"), hexc("#4a4850"), hexc("#3e3c44"))
        for x, y in ((6, 9), (9, 10), (11, 9)):
            px(c, x, y, hexc("#34323a"))              # cracks
        return c
    shrine_altar(c, hexc("#d8d4c8"), hexc("#9e9a90"), hexc("#86827a"))
    if power == "elbereth":                           # a star, white and blue
        light, glow = hexc("#ffffff"), hexc("#80e8ff")
        rect(c, 7, 1, 8, 7, glow)
        rect(c, 4, 4, 11, 4, glow)
        for d in range(1, 3):
            for x, y in ((7 - d, 4 - d), (8 + d, 4 - d), (7 - d, 4 + d), (8 + d, 4 + d)):
                px(c, x, y, glow)
        rect(c, 7, 3, 8, 5, light)
        for x, y in ((2, 1), (13, 2), (3, 7), (12, 7)):
            px(c, x, y, light)                        # the other stars
    elif power == "aule":                             # a smith's hammer, the forge-glow behind it
        rect(c, 3, 1, 12, 7, hexc("#6a3010", 160))
        rect(c, 5, 1, 10, 3, hexc("#9aa0a8"))          # the head
        rect(c, 5, 1, 10, 1, hexc("#d0d4dc"))
        rect(c, 7, 4, 8, 8, hexc("#8a5a2a"))           # the haft
        px(c, 4, 6, hexc("#ffa040"))
        px(c, 11, 5, hexc("#ffa040"))
    elif power == "yavanna":                          # a green sapling, flowering
        rect(c, 7, 3, 8, 8, hexc("#6a4a22"))           # the stem
        for x, y in ((5, 3), (6, 2), (9, 2), (10, 3), (4, 5), (5, 4), (10, 4), (11, 5), (6, 6), (9, 6), (7, 1), (8, 1)):
            px(c, x, y, hexc("#40c040"))
        for x, y in ((6, 4), (9, 5), (8, 2)):
            px(c, x, y, hexc("#a0ff80"))
        for x, y in ((5, 2), (10, 2), (7, 0)):
            px(c, x, y, hexc("#ffe0f0"))              # its flowers
        for x in (3, 5, 10, 12):
            px(c, x, 8, hexc("#3a8a3a"))              # moss on the altar's edge
    elif power == "mandos":                           # a grey hooded figure
        cloak, shade = hexc("#7890a8"), hexc("#3a4858")
        rect(c, 6, 1, 9, 2, cloak)                     # the hood
        rect(c, 5, 3, 10, 8, cloak)
        rect(c, 7, 2, 8, 3, shade)                     # the face, hidden
        rect(c, 10, 3, 10, 8, shade)
        px(c, 7, 2, hexc("#c0d8f0"))
        px(c, 8, 2, hexc("#c0d8f0"))
    return c


def oil(color):
    """A stoppered flask of weapon oil (AVABand's oils), the oil in its colour."""
    c = canvas()
    glass, cork = hexc("#c8d8e0", 200), hexc("#8a5a2e")
    r, g, b = int(color[1:3], 16), int(color[3:5], 16), int(color[5:7], 16)
    liquid, shine = (r, g, b, 255), (min(255, r + 80), min(255, g + 80), min(255, b + 80), 255)
    rect(c, 7, 1, 8, 3, cork)                        # the stopper
    rect(c, 6, 4, 9, 6, glass)                       # the neck
    for y, (x0, x1) in enumerate([(4, 11), (3, 12), (3, 12), (3, 12), (3, 12), (4, 11), (5, 10)], start=7):
        rect(c, x0, y, x1, y, glass)                 # the bowl
    for y, (x0, x1) in enumerate([(4, 11), (4, 11), (4, 11), (5, 10)], start=10):
        rect(c, x0, y, x1, y, liquid)                # the oil, filling it two-thirds
    px(c, 5, 10, shine)
    px(c, 5, 11, shine)
    return c


def scale(color):
    """A dragon's scale (AVABand's trophies), in the dragon's colour, ridged and pointed."""
    c = canvas()
    r, g, b = int(color[1:3], 16), int(color[3:5], 16), int(color[5:7], 16)
    body = (r, g, b, 255)
    ridge = (r * 6 // 10, g * 6 // 10, b * 6 // 10, 255)
    shine = (min(255, r + 70), min(255, g + 70), min(255, b + 70), 255)
    for y, (x0, x1) in enumerate([(5, 10), (4, 11), (3, 12), (3, 12), (3, 12), (4, 11), (4, 11), (5, 10), (6, 9), (7, 8)], start=3):
        rect(c, x0, y, x1, y, body)
    for y in range(4, 12):
        px(c, 7, y, ridge)                           # the midrib
    for x0, y in ((5, 6), (9, 6), (5, 9), (9, 9)):
        px(c, x0, y, ridge)
    px(c, 5, 4, shine)
    px(c, 6, 4, shine)
    return c


def hide():
    """A strip of troll hide, grey and knobbled."""
    c = canvas()
    skin, dark = hexc("#7a7468"), hexc("#4e4a42")
    rect(c, 2, 4, 13, 11, skin)
    for x, y in ((3, 4), (12, 11), (2, 11), (13, 4)):
        px(c, x, y, CLEAR)
    for x, y in ((4, 6), (8, 5), (11, 7), (6, 9), (10, 10)):
        px(c, x, y, dark)
    return c


def silk():
    """A skein of grey spider silk."""
    c = canvas()
    thread, shade = hexc("#c8c8d0"), hexc("#8a8a96")
    for y in range(4, 13):
        for x in range(3, 13):
            if (x + y) % 3 == 0:
                px(c, x, y, thread)
            elif (x - y) % 4 == 0:
                px(c, x, y, shade)
    rect(c, 3, 8, 12, 8, thread)                     # the band round the skein
    return c


SCALE_COLOURS = {
    "red_dragon_scale": "#d03020", "white_dragon_scale": "#e8f0f8", "blue_dragon_scale": "#3060e0",
    "black_dragon_scale": "#404048", "green_dragon_scale": "#30a040",
}

# AVABand's weapon oils (ava_objects.json): each kind drawn in its colour.
OIL_COLOURS = {
    "oil_of_venom": "#30b040", "oil_of_burning": "#e04020", "oil_of_frost": "#d8f0ff", "oil_of_storms": "#4070ff",
    "oil_of_corrosion": "#90a0a0", "holy_water": "#a0d8ff",
}

# AVABand's gems (ava_objects.json): each kind drawn in its colour (every quality alike).
GEM_COLOURS = {
    "ruby": "#e0283a", "sapphire": "#2a5ae0", "topaz": "#f0c030", "emerald": "#20b060", "diamond": "#e8f0ff",
    "opal": "#a8e0d0", "amethyst": "#9a50d8", "garnet": "#a02838", "bloodstone": "#2e5a38", "black_onyx": "#303038",
    "star_sapphire": "#5a8aff",
}

DRAWN = {
    "terrain:sealed_door": ("ava_sealed_door.png", sealed_door, True),
    "terrain:dwarven_forge": ("ava_dwarven_forge.png", forge, True),
    "terrain:quest_altar": ("ava_quest_altar.png", altar, True),
    "terrain:hermitage": ("ava_hermitage.png", hermitage, True),
    "terrain:fallen_note": ("ava_fallen_note.png", fallen_note, True),
    "object:key_of_belegost": ("ava_key.png", key, False),
    "terrain:cold_brazier": ("ava_brazier_cold.png", lambda: brazier(False), True),
    "terrain:lit_brazier": ("ava_brazier_lit.png", lambda: brazier(True), True),
    "terrain:trapped_apprentice": ("ava_apprentice.png", apprentice, True),
    "object-base:bag": ("ava_bag.png", bag, False),
    "object-base:bracers": ("ava_bracers.png", bracers, False),
    "object-base:gem": ("ava_gem.png", lambda: gem("#e8f0ff"), False),
}
for _gem, _colour in GEM_COLOURS.items():
    _tile = ("ava_gem_" + _gem + ".png", (lambda col: lambda: gem(col))(_colour), False)
    for _prefix in ("", "chipped_", "flawed_"):
        if _prefix and _gem in ("bloodstone", "black_onyx", "star_sapphire"):
            continue
        DRAWN["object:" + _prefix + _gem] = _tile
for _shop, (_digit, _sign, _emblem) in SHOPS.items():  # every shop's door, its own
    DRAWN["terrain:" + _shop] = ("ava_" + _shop + ".png", (lambda d, sg, e: lambda: shopfront(d, sg, e))(_digit, _sign, _emblem), True)
DRAWN["terrain:shop_caravan"] = ("ava_shop_caravan.png", wagon, True)
# The town as it grows with your renown.
DRAWN["terrain:town_lamp"] = ("ava_town_lamp.png", lamp_post, True)
DRAWN["terrain:town_well"] = ("ava_town_well.png", well, True)
DRAWN["terrain:market_stall"] = ("ava_market_stall.png", stall, True)
DRAWN["terrain:town_fountain"] = ("ava_town_fountain.png", fountain, True)
DRAWN["terrain:town_statue"] = ("ava_town_statue.png", statue, True)
# Shrines in the dungeon: one to each of the Valar, Morgoth's dark altar, and an altar gone dark.
for _power in ("elbereth", "aule", "yavanna", "mandos", "morgoth", "spent"):
    DRAWN["terrain:shrine_" + _power] = ("ava_shrine_" + _power + ".png", (lambda pw: lambda: shrine(pw))(_power), True)
DRAWN["object-base:oil"] = ("ava_oil.png", lambda: oil("#30b040"), False)
DRAWN["object-base:trophy"] = ("ava_trophy.png", lambda: scale("#d03020"), False)
for _trophy, _colour in SCALE_COLOURS.items():
    DRAWN["object:" + _trophy] = ("ava_" + _trophy + ".png", (lambda col: lambda: scale(col))(_colour), False)
DRAWN["object:troll_hide"] = ("ava_troll_hide.png", hide, False)
DRAWN["object:spider_silk"] = ("ava_spider_silk.png", silk, False)
for _oil, _colour in OIL_COLOURS.items():
    for _grade in ("", "lesser_", "greater_"):  # (every grade in the one flask)
        DRAWN["object:" + _grade + _oil] = ("ava_" + _oil + ".png", (lambda col: lambda: oil(col))(_colour), False)
# The rest borrow the nearest tile the set has (the first that exists).
BORROWED = [
    ("object-base:quest", ["object-base:scroll"]),
    ("object-base:relic", ["object-base:amulet"]),
    ("object:shard_of_the_hilt", ["object-base:sword"]),
    ("object:shard_of_the_blade", ["object-base:sword"]),
    ("object:shard_of_the_point", ["object-base:sword"]),
    ("object:reforged_blade", ["object:long_sword", "object-base:sword"]),
    ("object:water_of_ulmo", ["object-base:potion"]),
    ("object:black_market_strongbox", ["object-base:chest"]),
    ("object:wardens_taper", ["object:wooden_torch", "object-base:light"]),
    # The quests' own monsters: the nearest Angband monster's picture.
    ("monster:durgash_the_keybearer", ["monster:golfimbul_the_hill_orc_chief", "monster:orc_captain"]),
    ("monster:hathol_lord_of_the_barrow", ["monster:barrow_wight"]),
    ("monster:fengel_the_fence", ["monster:master_thief"]),
    ("monster:nar_the_red_handed", ["monster:nar_the_dwarf"]),
    ("monster:skarn_quickfingers", ["monster:grishnakh_the_hill_orc"]),
    ("monster:ilse_shadowcloak", ["monster:illusionist", "monster:master_thief"]),
    ("monster:the_shade_of_the_stair", ["monster:ghost", "monster:moaning_spirit"]),
    ("monster:skorvath_the_cold_drake", ["monster:ancient_white_dragon", "monster:mature_white_dragon"]),
    ("monster:grishnag_the_warchief", ["monster:shagrat_the_orc_captain", "monster:orc_captain"]),
    ("monster:the_keeper_of_the_stone", ["monster:nether_wraith", "monster:black_wraith", "monster:white_wraith"]),
    ("object:heart_of_the_mountain", ["object:arkenstone", "object-base:amulet"]),
    ("object:palantir", ["object:elfstone", "object:star", "object-base:light"]),
    # The Arcane Artificer's door, and his star-forged chisel.
    ("object:star_forged_chisel", ["object:shovel", "object-base:digger"]),
    # The Frozen Cellar and the Burning Library: what's fetched, and the potion belt and scroll case they earn.
    ("object:alchemists_crate", ["object:large_wooden_chest", "object:small_wooden_chest", "object-base:chest"]),
    ("object:scorched_folio", ["object-base:scroll"]),
    ("object:alchemists_bandolier", ["object:sling", "object-base:sling"]),
    ("object:scribes_case", ["object-base:magic_book"]),
    # AVABand's lamps, helm and gloves: their nearest Angband kind's picture.
    ("object:dwarven_lamp", ["object:lantern", "object-base:light"]),
    ("object:elven_lantern", ["object:lantern", "object-base:light"]),
    ("object:miners_helm", ["object:metal_cap", "object-base:helm"]),
    ("object:delvers_gloves", ["object:gauntlets", "object:leather_gloves", "object-base:gloves"]),
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
        decoder = json.JSONDecoder()
        for key, value in wanted.items():
            line = f'{indent}{json.dumps(key)}: {json.dumps(value, ensure_ascii=False, separators=(", ", ": "))}'
            found = re.search(r'\n[ \t]*' + re.escape(json.dumps(key)) + r': ', text)
            if found:
                # The existing value, however many lines it takes (a set's lit and unlit variants), replaced whole.
                _, end = decoder.raw_decode(text, found.end())
                text = text[:found.start()] + "\n" + line + text[end:]
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
