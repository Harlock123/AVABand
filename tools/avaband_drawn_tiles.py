#!/usr/bin/env python3
"""Draws the few item pictures the Dungeon Crawl Stone Soup pack lacks (a shovel, a pick, a mattock
and crossbow bolts), in its style: 32x32, a dark outline, lit from the top left. Released CC0 like
the pack. Writes avaband_*.png into tilesets/dcss and maps them in its tileset.json.

Usage:
    avaband_drawn_tiles.py <tilesets/dcss>
"""
import json, math, os, sys

sys.path.insert(0, os.path.dirname(__file__))
from dcss_flavor_tiles import png_write  # noqa: E402

OUTLINE = (24, 18, 14, 255)
WOOD = [(92, 58, 30, 255), (132, 88, 46, 255), (170, 120, 70, 255)]    # dark, mid, light
STEEL = [(88, 92, 100, 255), (150, 156, 166, 255), (214, 220, 228, 255)]
GOLD = [(120, 90, 20, 255), (200, 160, 40, 255), (250, 220, 110, 255)]
MITHRIL = [(70, 110, 140, 255), (140, 190, 220, 255), (220, 240, 255, 255)]
FLETCH = [(90, 90, 90, 255), (150, 150, 150, 255), (200, 200, 200, 255)]


class Canvas:
    def __init__(self):
        self.px = [(0, 0, 0, 0)] * (32 * 32)

    def put(self, x, y, c):
        if 0 <= x < 32 and 0 <= y < 32:
            self.px[y * 32 + x] = c

    def get(self, x, y):
        return self.px[y * 32 + x] if 0 <= x < 32 and 0 <= y < 32 else (0, 0, 0, 0)

    def line(self, x0, y0, x1, y1, width, shades):
        """A shaft from (x0, y0) to (x1, y1): lighter on its upper-left side."""
        length = max(1, math.hypot(x1 - x0, y1 - y0))
        nx, ny = -(y1 - y0) / length, (x1 - x0) / length  # normal
        steps = int(length * 3) + 1
        for i in range(steps + 1):
            t = i / steps
            cx, cy = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
            for k in range(-width * 2, width * 2 + 1):
                off = k / 4
                if abs(off) > width / 2:
                    continue
                side = off * (1 if nx + ny < 0 else -1)
                shade = shades[2] if side > width / 6 else shades[0] if side < -width / 6 else shades[1]
                self.put(round(cx + nx * off), round(cy + ny * off), shade)

    def blob(self, points, shades):
        """A filled polygon, lit from the top left."""
        xs = [p[0] for p in points]
        ys = [p[1] for p in points]
        for y in range(int(min(ys)), int(max(ys)) + 1):
            for x in range(int(min(xs)), int(max(xs)) + 1):
                if inside(x + 0.5, y + 0.5, points):
                    cx, cy = sum(xs) / len(xs), sum(ys) / len(ys)
                    d = (x - cx) + (y - cy)
                    self.put(x, y, shades[2] if d < -2 else shades[0] if d > 2 else shades[1])

    def outline(self):
        """A dark edge round everything drawn, as the pack's pictures have."""
        drawn = [(x, y) for y in range(32) for x in range(32) if self.get(x, y)[3]]
        for x, y in drawn:
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                if not self.get(x + dx, y + dy)[3]:
                    self.put(x + dx, y + dy, OUTLINE)


def inside(x, y, poly):
    result = False
    j = len(poly) - 1
    for i in range(len(poly)):
        (xi, yi), (xj, yj) = poly[i], poly[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            result = not result
        j = i
    return result


def shovel():
    c = Canvas()
    c.line(24, 4, 12, 18, 2, WOOD)                       # handle, top right to the blade
    c.line(26, 2, 22, 6, 3, WOOD)                        # grip
    c.blob([(12, 15), (16, 19), (11, 27), (6, 28), (4, 24), (6, 19)], STEEL)  # spade blade
    c.outline()
    return c


def pick():
    c = Canvas()
    c.line(7, 27, 20, 11, 2, WOOD)                       # handle
    c.blob([(8, 11), (16, 7), (22, 7), (28, 12), (22, 10), (16, 10)], STEEL)  # curved head, pointed both ends
    c.outline()
    return c


def mattock():
    c = Canvas()
    c.line(7, 27, 19, 12, 2, WOOD)                       # handle
    c.blob([(19, 8), (27, 13), (21, 11)], STEEL)         # the pick end
    c.blob([(10, 7), (20, 8), (20, 12), (10, 14)], STEEL)  # the broad adze blade
    c.outline()
    return c


def bolt(head):
    c = Canvas()
    u = (1 / math.sqrt(2), -1 / math.sqrt(2))   # along the bolt, tail to tip
    n = (1 / math.sqrt(2), 1 / math.sqrt(2))    # across it

    def at(base, a, b):
        return (base[0] + u[0] * a + n[0] * b, base[1] + u[1] * a + n[1] * b)

    tail, neck = (8, 25), (20, 13)
    for side in (1, -1):                          # two vanes at the tail
        c.blob([at(tail, 5, 0), at(tail, -1, 3.5 * side), at(tail, -2, 3.5 * side), at(tail, 1, 0)], FLETCH)
    c.line(tail[0], tail[1], neck[0], neck[1], 2, WOOD)  # a short, thick shaft
    c.blob([at(neck, 7, 0), at(neck, 1, 2.8), at(neck, -1, 0), at(neck, 1, -2.8)], head)  # quarrel head
    c.outline()
    return c


PICTURES = {
    "object:shovel": ("avaband_shovel.png", shovel),
    "object:pick": ("avaband_pick.png", pick),
    "object:mattock": ("avaband_mattock.png", mattock),
    "object:bolt": ("avaband_bolt.png", lambda: bolt(STEEL)),
    "object:seeker_bolt": ("avaband_seeker_bolt.png", lambda: bolt(GOLD)),
    "object:mithril_bolt": ("avaband_mithril_bolt.png", lambda: bolt(MITHRIL)),
    "object-base:bolt": ("avaband_bolt.png", lambda: bolt(STEEL)),
}


def main():
    tileset_dir = sys.argv[1]
    manifest_path = os.path.join(tileset_dir, "tileset.json")
    manifest = json.load(open(manifest_path, encoding="utf-8"))
    for key, (name, draw) in PICTURES.items():
        png_write(os.path.join(tileset_dir, name), 32, 32, draw().px)
        manifest["tiles"][key] = name
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"{len(PICTURES)} tiles drawn")


if __name__ == "__main__":
    main()
