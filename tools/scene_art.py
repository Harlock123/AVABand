#!/usr/bin/env python3
"""More of AVABand's scene pictures (CC0, made for AVABand): rendered, not drawn by hand, in the
same way as the Word of Recall's (recall_portal_art.py, whose renderer this reuses).

  deep-descent.jpg    looking straight down a shaft as the floor gives way, rubble falling ahead of
                      you toward a red glow far below
  trapdoor.jpg        a trap door fallen open under you: planks all round, a dark chute below
  quest-complete.jpg  the quest's letter of thanks on the Prancing Pony's table, sealed, by candlelight
  boss-slain.jpg      a fallen crown and a broken blade on the flagstones, in a shaft of light

The game animates them (Controls/SceneView.cs): the falls rush in with debris flying past, the
quest's gold motes rise, the dust turns in the shaft of light. Needs numpy, scipy and Pillow.
Usage: scene_art.py <art folder>
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import recall_portal_art as R  # noqa: E402

W, H = R.W, R.H
RNG = np.random.default_rng(2718)


def grid():
    ys, xs = np.mgrid[0:H, 0:W].astype(float)
    return xs, ys


def rgb_from(img):
    return np.asarray(img.convert("RGB"), float) / 255


def shaft(glow_colour, glow_power, stone, ambient):
    """Looking straight down a square shaft: its four walls converging on a glow far below."""
    xs, ys = grid()
    cx, cy = W / 2, H / 2
    dx, dy = xs - cx, ys - cy
    f = 640.0
    side = np.abs(dx) >= np.abs(dy) * (W / H) * 0.62
    with np.errstate(divide="ignore", invalid="ignore"):
        z = np.where(side, f / np.maximum(np.abs(dx), 1e-3), f * 0.62 * (W / H) / np.maximum(np.abs(dy), 1e-3))
        along = np.where(side, dy * z / f, dx * z / f)
    zfar = 14.0
    wall = R.stone_blocks(along * 2.2, z, 0.34, 0.15, 5, stone, 0.55)
    wall *= (R.value_noise((H, W), 40, 4, seed=8) * 0.5 + 0.75)[..., None]
    light = np.array(ambient) + np.exp(-(zfar - np.minimum(z, zfar)) / 3.2)[..., None] * np.array(glow_colour) * glow_power
    out = wall * light
    # The glow itself, where the shaft goes beyond sight.
    deep = np.clip((z - zfar) / 6, 0, 1)[..., None]
    swirl = R.value_noise((H, W), 10, 4, seed=21)[..., None]
    abyss = np.array(glow_colour) * glow_power * (0.9 + 0.8 * swirl)
    out = out * (1 - deep) + abyss * deep
    return out


def rubble(img, count, seed, edge_colour, sizes=(30, 150), blur=1.5):
    """Chunks of broken floor, dark, lit along the side that faces the glow."""
    rng = np.random.default_rng(seed)
    cx, cy = W / 2, H / 2
    for _ in range(count):
        r = rng.uniform(*sizes)
        a = rng.uniform(0, 6.283)
        dist = rng.uniform(0.18, 0.55)
        x, y = cx + math.cos(a) * W * dist, cy + math.sin(a) * H * dist
        pts = [(x + math.cos(t) * r * rng.uniform(0.55, 1.0), y + math.sin(t) * r * rng.uniform(0.55, 1.0) * 0.8)
               for t in np.sort(rng.uniform(0, 6.283, 7))]
        layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        d.polygon(pts, fill=(30, 25, 22, 255))
        # Lit from below: a soft glow along the side that faces the middle, and a thin bright edge.
        inner = [p for p in pts if (p[0] - x) * (cx - x) + (p[1] - y) * (cy - y) > 0]
        glow = Image.new("RGBA", img.size, (0, 0, 0, 0))
        if len(inner) > 1:
            ImageDraw.Draw(glow).line(inner, fill=edge_colour + (150,), width=max(4, int(r / 8)))
            d.line(inner, fill=tuple(min(255, c + 40) for c in edge_colour) + (200,), width=max(1, int(r / 45)))
        img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(max(3, r / 10))))
        img.alpha_composite(layer.filter(ImageFilter.GaussianBlur(blur)))


def broken_edge(img, colour):
    """The ragged lip of the floor you fell through, round the edge of the view."""
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    rng = np.random.default_rng(5)
    pts = []
    for t in np.linspace(0, 6.283, 90, endpoint=False):
        rx = W * (0.52 + rng.uniform(0, 0.06))
        ry = H * (0.55 + rng.uniform(0, 0.07))
        pts.append((W / 2 + math.cos(t) * rx, H / 2 + math.sin(t) * ry))
    d.rectangle([0, 0, W, H], fill=colour + (255,))
    d.polygon(pts, fill=(0, 0, 0, 0))
    img.alpha_composite(layer.filter(ImageFilter.GaussianBlur(2)))


def deep_descent():
    out = shaft((1.0, 0.42, 0.14), 3.2, (0.4, 0.37, 0.34), (0.006, 0.005, 0.005))
    img = R.to_image(R.vignette(out, 0.5)).convert("RGBA")
    rubble(img, 16, 1, (255, 140, 70))
    rubble(img, 6, 2, (255, 160, 90), sizes=(120, 260), blur=4)   # nearer: bigger and softer
    broken_edge(img, (18, 14, 12))
    return img.convert("RGB")


def planks(xs, ys, seed, base):
    """A floor of boards, seen from above: long planks, grain, dark gaps."""
    board = 46.0
    row = np.floor(ys / board)
    offset = R.hash2(row, row, seed) * 400
    col = np.floor((xs + offset) / 260)
    tone = 0.75 + 0.4 * (R.hash2(row, col, seed + 1) - 0.5)
    grain = R.value_noise((H // 4, W // 4), 20, 3, seed=seed)
    grain = ndimage.zoom(grain, 4, order=1)[:H, :W]
    streak = 0.85 + 0.3 * np.sin((ys % board) / board * 18 + grain * 12)
    gap = np.clip(np.minimum(ys % board, board - ys % board) / 3, 0, 1) * np.clip(np.abs(((xs + offset) % 260) - 130) / 128 * 60, 0, 1)
    return np.array(base) * (tone * streak * (0.2 + 0.8 * gap))[..., None]


def trapdoor():
    xs, ys = grid()
    wood = planks(xs, ys, 11, (0.46, 0.3, 0.17))
    # A lantern's light from the lower right, falling off.
    lamp = np.exp(-np.hypot(xs - W * 0.86, ys - H * 1.05) / 520)[..., None] * np.array([1.4, 1.05, 0.65])
    out = wood * (0.03 + lamp * 0.9)
    # The hole, and the chute below it.
    x0, x1, y0, y1 = W * 0.33, W * 0.67, H * 0.28, H * 0.86
    hole = (xs > x0) & (xs < x1) & (ys > y0) & (ys < y1)
    below = shaft((0.35, 0.4, 0.6), 0.9, (0.3, 0.3, 0.32), (0.01, 0.01, 0.015))
    out[hole] = below[hole] * 0.8
    img = R.to_image(R.vignette(out, 0.45)).convert("RGBA")
    d = ImageDraw.Draw(img)
    # The trap door itself, swung down from its hinges on the far edge: foreshortened, hanging into the dark.
    top, depth = y0, (y1 - y0) * 0.55
    # (Darker the further it hangs into the chute, away from the lamp.)
    for k in range(40):
        t0, t1 = k / 40, (k + 1) / 40
        shade = 1 - 0.85 * t0
        col = (int(78 * shade), int(50 * shade), int(28 * shade), 255)
        d.polygon([(x0 + 6 + 34 * t0, top + depth * t0), (x1 - 6 - 34 * t0, top + depth * t0),
                   (x1 - 6 - 34 * t1, top + depth * t1), (x0 + 6 + 34 * t1, top + depth * t1)], fill=col)
    for k in range(1, 5):
        t = k / 5
        xa, xb = x0 + 6 + 34 * t, x1 - 6 - 34 * t
        d.line([(xa, top + depth * t), (xb, top + depth * t)], fill=(int(30 * (1 - 0.8 * t)), int(18 * (1 - 0.8 * t)), 10, 255), width=3)
    # The hole's sides fall away into shadow (on a layer of its own, so it blends).
    shadow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    for i in range(24):
        a = int(110 * (1 - i / 24))
        sd.rectangle([x0 + i, y0, x0 + i + 1, y1], fill=(0, 0, 0, a))
        sd.rectangle([x1 - i - 1, y0, x1 - i, y1], fill=(0, 0, 0, a))
        sd.rectangle([x0, y1 - i - 1, x1, y1 - i], fill=(0, 0, 0, a))
    img.alpha_composite(shadow)
    d = ImageDraw.Draw(img)
    for hx in (x0 + 60, x1 - 60):
        d.rectangle([hx - 14, top - 8, hx + 14, top + 10], fill=(60, 60, 64, 255))
    # The hole's broken edge: splintered board ends.
    rng = np.random.default_rng(3)
    for side in ((x0, y0, x0, y1), (x1, y0, x1, y1), (x0, y1, x1, y1)):
        for _ in range(9):
            t = rng.uniform(0, 1)
            px, py = side[0] + (side[2] - side[0]) * t, side[1] + (side[3] - side[1]) * t
            s = rng.uniform(6, 16)
            d.polygon([(px - s, py - s * 0.4), (px + s, py), (px - s * 0.3, py + s * 0.6)], fill=(90, 60, 34, 255))
    return img.convert("RGB")


def quest_complete():
    xs, ys = grid()
    # The table, leaning away: boards that crowd together toward the top.
    v = (ys / H) ** 1.6 * H
    wood = planks(xs * (0.7 + 0.3 * ys / H), v, 17, (0.42, 0.26, 0.14))
    candle = np.exp(-np.hypot(xs - W * 0.2, ys - H * 0.25) / 420)[..., None] * np.array([1.6, 1.1, 0.55])
    out = wood * (0.1 + candle * 1.2)
    img = R.to_image(R.vignette(out, 0.55)).convert("RGBA")

    # The letter: parchment, a little turned, with its lines of thanks and a red seal.
    paper = Image.new("RGBA", (760, 520), (0, 0, 0, 0))
    pd = ImageDraw.Draw(paper)
    pd.rounded_rectangle([10, 10, 750, 510], radius=14, fill=(226, 206, 160, 255))
    pa = np.asarray(paper, float)
    stain = R.value_noise((520, 760), 8, 4, seed=31)
    shade = 0.82 + 0.22 * stain
    xx = np.arange(760)[None, :] / 760
    curl = 1 - 0.25 * (np.exp(-xx / 0.05) + np.exp(-(1 - xx) / 0.05))       # the rolled ends, darker
    pa[..., :3] *= (shade * curl)[..., None]
    paper = Image.fromarray(np.clip(pa, 0, 255).astype(np.uint8), "RGBA")
    pd = ImageDraw.Draw(paper)
    rng = np.random.default_rng(7)
    for line in range(9):
        y = 70 + line * 40
        x = 70
        end = 690 - (rng.uniform(0, 260) if line == 8 else rng.uniform(0, 60))
        while x < end:
            w = rng.uniform(20, 70)
            pts = [(x + t * w, y + math.sin(t * 9 + rng.uniform(0, 6)) * 4) for t in np.linspace(0, 1, 8)]
            pd.line(pts, fill=(70, 42, 24, 230), width=3)
            x += w + rng.uniform(10, 18)
    # The seal.
    sx, sy, sr = 590, 420, 58
    pd.ellipse([sx - sr - 8, sy - sr + 4, sx + sr + 6, sy + sr + 10], fill=(90, 10, 10, 160))  # its drips
    pd.ellipse([sx - sr, sy - sr, sx + sr, sy + sr], fill=(160, 22, 20, 255))
    pd.ellipse([sx - sr + 12, sy - sr + 12, sx + sr - 12, sy + sr - 12], outline=(110, 12, 12, 255), width=5)
    star = [(sx + math.cos(a) * (30 if k % 2 == 0 else 12), sy + math.sin(a) * (30 if k % 2 == 0 else 12))
            for k, a in enumerate(np.linspace(-math.pi / 2, 1.5 * math.pi, 11)[:-1])]
    pd.polygon(star, fill=(200, 60, 50, 255))
    pd.ellipse([sx - sr + 10, sy - sr + 8, sx - 6, sy - 10], fill=(230, 120, 110, 90))        # a highlight
    paper = paper.rotate(-7, resample=Image.BICUBIC, expand=True)
    shadow = Image.new("RGBA", paper.size, (0, 0, 0, 0))
    shadow.paste((0, 0, 0, 150), mask=paper.split()[3])
    shadow = shadow.filter(ImageFilter.GaussianBlur(18))
    ox, oy = (W - paper.width) // 2 + 40, (H - paper.height) // 2 + 60
    img.alpha_composite(shadow, (ox + 24, oy + 30))
    img.alpha_composite(paper, (ox, oy))
    # The candle, out of shot but for its glow; and a gold wash of rays behind the seal.
    rays = Image.new("RGBA", img.size, (0, 0, 0, 0))
    rd = ImageDraw.Draw(rays)
    cx, cy = ox + 590 * paper.width / 760, oy + 420 * paper.height / 560
    for k in range(18):
        a = k / 18 * 6.283
        rd.polygon([(cx, cy), (cx + math.cos(a - 0.05) * 1400, cy + math.sin(a - 0.05) * 1400),
                    (cx + math.cos(a + 0.05) * 1400, cy + math.sin(a + 0.05) * 1400)], fill=(255, 210, 120, 22))
    img.alpha_composite(rays.filter(ImageFilter.GaussianBlur(12)))
    return img.convert("RGB")


def boss_slain():
    ceil_y, floor_y, half = -0.9, 0.667, 1.4
    kind, X, Y, Z, N = R.surfaces(ceil_y, floor_y, half, 6.0)
    alb = np.zeros((H, W, 3))
    grit = R.value_noise((H, W), 50, 4, seed=13)[..., None] * 0.5 + 0.75
    wall = (0.36, 0.34, 0.33)
    for k, u in ((0, X), (3, Z), (4, Z)):
        m = kind == k
        alb[m] = R.stone_blocks(u, Y, 0.34, 0.14, 40 + k, wall, 0.5)[m]
    m = kind == 1
    alb[m] = R.flagstones(X, Z, 0.45, 41, (0.34, 0.32, 0.3))[m]
    m = kind == 2
    alb[m] = R.stone_blocks(X, Z, 0.5, 0.5, 42, (0.15, 0.14, 0.14), 0.3)[m]
    alb *= grit
    # The light: from a gap high above, falling on the floor where the crown lies.
    lights = [((0.0, ceil_y + 0.05, 3.6), (1.0, 0.95, 0.85), 3.0, 0.9)]
    out = R.shade(alb, kind, X, Y, Z, N, lights, (0.01, 0.01, 0.012), 8.0)
    xs, ys = grid()
    # The shaft of light itself, and its pool on the floor.
    top_x, bottom_x = R.VP[0], R.VP[0]
    bottom_y = R.VP[1] + R.F * floor_y / 3.6
    t = np.clip((ys - 0) / max(bottom_y, 1), 0, 1)
    width = 60 + 160 * t
    beam = np.clip(1 - np.abs(xs - (top_x + (bottom_x - top_x) * t)) / width, 0, 1) ** 1.5 * (ys < bottom_y + 40)
    haze = R.value_noise((H, W), 12, 3, seed=44)
    out += (beam * (0.25 + 0.2 * haze))[..., None] * np.array([1.0, 0.95, 0.85])
    pool = np.exp(-(((xs - R.VP[0]) / 260) ** 2 + ((ys - bottom_y) / 60) ** 2))
    out += pool[..., None] * np.array([0.5, 0.47, 0.42])
    img = R.to_image(R.vignette(out, 0.6)).convert("RGBA")

    d = ImageDraw.Draw(img)
    # A broken blade, lying across the pool of light.
    bx, by = R.VP[0] - 250, bottom_y + 40
    d.polygon([(bx, by), (bx + 330, by - 60), (bx + 336, by - 50), (bx + 8, by + 12)], fill=(170, 175, 185, 255))
    d.line([(bx + 6, by + 2), (bx + 330, by - 57)], fill=(230, 235, 245, 255), width=2)
    d.rectangle([bx - 30, by - 4, bx + 6, by + 16], fill=(90, 70, 40, 255))            # the hilt
    d.polygon([(bx - 4, by - 26), (bx + 12, by - 24), (bx + 4, by + 38), (bx - 12, by + 36)], fill=(140, 110, 50, 255))
    # The fallen crown, tipped on its side: a band, its points, its stones.
    cx, cy = R.VP[0] + 60, bottom_y + 10
    shadow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).ellipse([cx - 130, cy + 10, cx + 150, cy + 60], fill=(0, 0, 0, 160))
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(12)))
    crown = Image.new("RGBA", (320, 200), (0, 0, 0, 0))
    cd = ImageDraw.Draw(crown)
    cd.ellipse([20, 90, 300, 190], fill=(120, 90, 30, 255))                              # the band's far side
    cd.rectangle([20, 70, 300, 140], fill=(212, 170, 60, 255))
    cd.ellipse([20, 110, 300, 190], fill=(232, 190, 80, 255))
    cd.ellipse([40, 115, 280, 175], fill=(150, 110, 40, 255))
    for k in range(5):
        px = 40 + k * 60
        cd.polygon([(px - 18, 72), (px, 10 + (k % 2) * 16), (px + 18, 72)], fill=(222, 180, 70, 255))
        cd.ellipse([px - 7, 34 + (k % 2) * 14, px + 7, 48 + (k % 2) * 14], fill=(200, 30, 40, 255) if k % 2 else (40, 90, 210, 255))
    cd.line([(24, 100), (296, 100)], fill=(255, 230, 150, 255), width=3)
    crown = crown.rotate(24, resample=Image.BICUBIC, expand=True)
    img.alpha_composite(crown, (int(cx - crown.width / 2), int(cy - crown.height + 85)))
    return img.convert("RGB")


def main():
    out = sys.argv[1]
    for name, make in (("deep-descent", deep_descent), ("trapdoor", trapdoor), ("quest-complete", quest_complete), ("boss-slain", boss_slain)):
        path = os.path.join(out, name + ".jpg")
        make().save(path, quality=88)
        print(path, os.path.getsize(path))


if __name__ == "__main__":
    main()
