#!/usr/bin/env python3
"""The Word of Recall cutscene's artwork (CC0, made for AVABand): rendered, not drawn by hand.

  recall-portal-room.jpg         a torchlit dungeon room, the rent in its far wall lighting it violet
  recall-portal-square-day.jpg   the town square by day, the rent hanging in the air over the cobbles
  recall-portal-square-night.jpg the same by night: stars, lit windows, the rent's glow on the stones
  recall-portal-rim.png          the torn edge of the rent, with cracks of light running out into the world
  recall-hand-open.png           a vast incorporeal hand, reaching
  recall-hand-grasp.png          the same hand, closing

The game (Controls/SceneView.cs) lays them together and animates them: the rent tears open and
shimmers, the hand reaches out of it toward you, grows, closes, and the light takes you. The rent
is in the same place in every backdrop (PORTAL below, as fractions of the picture), so one rim
fits all three.

The rooms are rendered as 3-D surfaces in one-point perspective and lit by point lights (two
torches, the rent itself); the square the same way, with house fronts for walls and the sky for a
ceiling. Needs numpy, scipy and Pillow. Usage: recall_portal_art.py <art folder>
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage

W, H = 1600, 900
VP = (800.0, 430.0)          # the vanishing point
F = 1320.0                   # focal length: the back wall at Z=4 spans x 470..1130
ZB = 4.0
# The rent: centre and radii as fractions of the picture (SceneView.cs has the same numbers).
PORTAL = (0.5, 0.445, 0.094, 0.24)
RNG = np.random.default_rng(1729)


# --- noise ---------------------------------------------------------------------------------------

def value_noise(shape, cells, octaves=4, seed=0):
    rng = np.random.default_rng(seed)
    out = np.zeros(shape)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        n = int(cells * 2 ** o) + 2
        grid = rng.random((n, n))
        z = ndimage.zoom(grid, (shape[0] / n * 1.02, shape[1] / n * 1.02), order=3)[: shape[0], : shape[1]]
        if z.shape != shape:
            z = np.pad(z, ((0, shape[0] - z.shape[0]), (0, shape[1] - z.shape[1])), mode="edge")
        out += amp * z
        total += amp
        amp *= 0.5
    return out / total


def hash2(a, b, seed=0):
    """A repeatable 0..1 value per integer cell."""
    h = (a.astype(np.int64) * 73856093) ^ (b.astype(np.int64) * 19349663) ^ (seed * 83492791)
    h = (h ^ (h >> 13)) * 1274126177
    return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0


# --- surfaces ------------------------------------------------------------------------------------

def surfaces(ceiling_y, floor_y, half_width, back_z):
    """For every pixel: which surface it is on and where that is in the world (X right, Y down, Z in)."""
    ys, xs = np.mgrid[0:H, 0:W].astype(float)
    dx, dy = xs - VP[0], ys - VP[1]
    kind = np.full((H, W), -1)
    X = np.zeros((H, W)); Y = np.zeros((H, W)); Z = np.full((H, W), back_z)
    # The back wall.
    bx0, bx1 = VP[0] - F * half_width / back_z, VP[0] + F * half_width / back_z
    by0, by1 = VP[1] + F * ceiling_y / back_z, VP[1] + F * floor_y / back_z
    back = (xs >= bx0) & (xs <= bx1) & (ys >= by0) & (ys <= by1)
    kind[back] = 0
    X[back] = dx[back] * back_z / F; Y[back] = dy[back] * back_z / F
    with np.errstate(divide="ignore", invalid="ignore"):
        # Floor (below the horizon) and ceiling: where the ray meets Y = floor_y / ceiling_y.
        zf = np.nan_to_num(F * floor_y / dy, nan=-1, posinf=-1, neginf=-1)
        zc = np.nan_to_num(F * ceiling_y / dy, nan=-1, posinf=-1, neginf=-1)
        # Side walls: X = -+half_width.
        zl = np.nan_to_num(F * -half_width / dx, nan=-1, posinf=-1, neginf=-1)
        zr = np.nan_to_num(F * half_width / dx, nan=-1, posinf=-1, neginf=-1)
    for k, z, cond in ((1, zf, dy > 0), (2, zc, dy < 0), (3, zl, dx < 0), (4, zr, dx > 0)):
        m = cond & (kind == -1) & (z > 0) & (z < back_z)
        if k in (1, 2):
            m &= np.abs(dx * z / F) <= half_width
        else:
            yy = dy * z / F
            m &= (yy >= ceiling_y) & (yy <= floor_y)
        kind[m] = k
        Z[m] = z[m]; X[m] = dx[m] * z[m] / F; Y[m] = dy[m] * z[m] / F
    normals = {0: (0, 0, -1), 1: (0, -1, 0), 2: (0, 1, 0), 3: (1, 0, 0), 4: (-1, 0, 0)}
    N = np.zeros((H, W, 3))
    for k, n in normals.items():
        N[kind == k] = n
    return kind, X, Y, Z, N


def shade(albedo, kind, X, Y, Z, N, lights, ambient, fog):
    """Lambert lighting from point lights, with distance falloff and a little fog into the dark."""
    P = np.stack([X, Y, Z], axis=-1)
    light = np.zeros((H, W, 3)) + np.array(ambient)
    for pos, colour, power, reach in lights:
        L = np.array(pos) - P
        d = np.linalg.norm(L, axis=-1) + 1e-6
        lam = np.clip((L * N).sum(-1) / d, 0, 1)
        att = power / (1 + (d / reach) ** 2)
        light += (lam * att)[..., None] * np.array(colour)
    out = albedo * light
    out *= np.exp(-Z / fog)[..., None] * 0.5 + 0.5
    return out


def stone_blocks(u, v, bw, bh, seed, base, var):
    """Coursed stone: blocks bw x bh in world units, offset every other course, mortar between."""
    row = np.floor(v / bh)
    uu = u / bw + 0.5 * (row % 2)
    col = np.floor(uu)
    fu, fv = uu - col, v / bh - row
    mortar = np.minimum(np.minimum(fu, 1 - fu) * bw, np.minimum(fv, 1 - fv) * bh)
    tone = 0.75 + var * (hash2(col, row, seed) - 0.5)
    joint = np.clip(mortar / 0.018, 0, 1) ** 0.6
    return np.array(base) * (tone * (0.35 + 0.65 * joint))[..., None]


def flagstones(u, v, size, seed, base):
    col, row = np.floor(u / size), np.floor(v / size)
    fu, fv = u / size - col, v / size - row
    edge = np.minimum(np.minimum(fu, 1 - fu), np.minimum(fv, 1 - fv)) * size
    tone = 0.7 + 0.45 * (hash2(col, row, seed) - 0.5)
    return np.array(base) * (tone * (0.3 + 0.7 * np.clip(edge / 0.03, 0, 1)))[..., None]


def cobbles(u, v, size, seed, base):
    """Rounded cobbles on a jittered grid: bright in the middle, dark in the joints."""
    ci, cj = np.floor(u / size), np.floor(v / size)
    best = np.full(u.shape, 9.0); tone = np.zeros(u.shape)
    for di in (-1, 0, 1):
        for dj in (-1, 0, 1):
            a, b = ci + di, cj + dj
            px = (a + 0.2 + 0.6 * hash2(a, b, seed)) * size
            py = (b + 0.2 + 0.6 * hash2(a, b, seed + 1)) * size
            d = np.hypot(u - px, v - py) / size
            closer = d < best
            best = np.where(closer, d, best)
            tone = np.where(closer, hash2(a, b, seed + 2), tone)
    bump = np.clip(1 - best * 1.6, 0, 1) ** 0.5
    return np.array(base) * ((0.6 + 0.5 * tone) * (0.25 + 0.75 * bump))[..., None]


def to_image(rgb):
    rgb = rgb / (1 + rgb * 0.35)            # soft highlights
    rgb = np.clip(rgb, 0, 1) ** (1 / 2.2)
    return Image.fromarray((rgb * 255).astype(np.uint8))


def vignette(rgb, strength=0.55):
    ys, xs = np.mgrid[0:H, 0:W]
    r = np.hypot((xs - W / 2) / (W / 2), (ys - H / 2) / (H / 2))
    return rgb * (1 - strength * np.clip(r - 0.35, 0, 1) ** 1.5)[..., None]


def portal_glow(rgb, colour, strength):
    """The rent's own light spilling over everything near it on screen."""
    cx, cy = PORTAL[0] * W, PORTAL[1] * H
    ys, xs = np.mgrid[0:H, 0:W]
    d = np.hypot((xs - cx) / (PORTAL[2] * W), (ys - cy) / (PORTAL[3] * H))
    glow = np.exp(-np.clip(d - 0.9, 0, None) * 1.3) * strength
    return rgb + glow[..., None] * np.array(colour)


def torch_sprite(img, x, y, scale):
    """A sconce and its flame, drawn over the rendered wall."""
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    s = scale
    d.rectangle([x - 5 * s, y, x + 5 * s, y + 38 * s], fill=(40, 30, 24, 255))
    d.rectangle([x - 13 * s, y + 30 * s, x + 13 * s, y + 36 * s], fill=(52, 40, 30, 255))
    glow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse([x - 70 * s, y - 90 * s, x + 70 * s, y + 40 * s], fill=(255, 150, 60, 120))
    glow = glow.filter(ImageFilter.GaussianBlur(40 * s))
    d.ellipse([x - 13 * s, y - 40 * s, x + 13 * s, y + 4 * s], fill=(255, 120, 40, 255))
    d.ellipse([x - 8 * s, y - 30 * s, x + 8 * s, y], fill=(255, 220, 140, 255))
    img.alpha_composite(glow)
    img.alpha_composite(layer)


# --- the dungeon room ----------------------------------------------------------------------------

def room():
    ceil_y, floor_y, half = -0.727, 0.667, 1.0
    kind, X, Y, Z, N = surfaces(ceil_y, floor_y, half, ZB)
    alb = np.zeros((H, W, 3))
    grit = value_noise((H, W), 60, 4, seed=3)[..., None] * 0.6 + 0.7
    grit *= value_noise((H, W), 5, 3, seed=4)[..., None] * 0.6 + 0.7     # damp and soot, in patches
    wall = (0.4, 0.38, 0.36)
    m = kind == 0
    alb[m] = stone_blocks(X, Y, 0.34, 0.14, 1, wall, 0.5)[m]
    for k, u in ((3, Z), (4, Z)):
        m = kind == k
        alb[m] = stone_blocks(u, Y, 0.34, 0.14, 2 + k, wall, 0.5)[m]
    m = kind == 1
    alb[m] = flagstones(X, Z, 0.42, 7, (0.36, 0.33, 0.3))[m]
    m = kind == 2
    alb[m] = stone_blocks(X, Z, 0.5, 0.5, 9, (0.2, 0.18, 0.17), 0.3)[m]
    alb *= grit
    portal = (0.0, -0.08, ZB - 0.05)
    lights = [
        ((-0.93, -0.12, 2.1), (1.0, 0.52, 0.2), 2.4, 0.42),
        ((0.93, -0.12, 2.1), (1.0, 0.52, 0.2), 2.4, 0.42),
        (portal, (0.5, 0.32, 1.0), 2.6, 0.6),
    ]
    rgb = shade(alb, kind, X, Y, Z, N, lights, (0.008, 0.007, 0.01), 5.0)
    rgb = portal_glow(rgb, (0.3, 0.18, 0.7), 0.3)
    rgb = vignette(rgb, 0.6)
    img = to_image(rgb).convert("RGBA")
    for side in (-1, 1):   # the torches at Z 2.1, a little above the middle of the wall
        x = VP[0] + F * side * 0.93 / 2.1
        y = VP[1] + F * -0.12 / 2.1
        torch_sprite(img, x, y - 10, 1.25)
    return img.convert("RGB")


# --- the town square -----------------------------------------------------------------------------

def square(day):
    ceil_y, floor_y, half, back_z = -1.6, 0.667, 1.6, 7.0
    kind, X, Y, Z, N = surfaces(ceil_y, floor_y, half, back_z)
    ys, xs = np.mgrid[0:H, 0:W].astype(float)
    alb = np.zeros((H, W, 3))

    # House fronts: plaster between timbers, a window or two a storey, each house its own width and
    # height (its roofline cuts into the sky; what is above it is sky, not wall).
    def facade(u, v, seed):
        widths = 0.9
        hi = np.floor(u / widths)
        fu = u / widths - hi
        top = ceil_y + 0.25 + 0.55 * hash2(hi, hi * 0 + 3, seed)        # this house's eaves
        peak = top - 0.35 * (1 - np.abs(fu - 0.5) * 2)                    # its gable
        plaster = np.array([0.72, 0.66, 0.55]) * (0.8 + 0.3 * hash2(hi, hi * 0 + 5, seed))[..., None]
        timber = np.array([0.18, 0.12, 0.08])
        storey = 0.55
        sv = (v - top) / storey
        beam = (np.abs(fu - 0.02) < 0.025) | (np.abs(fu - 0.98) < 0.025) | (np.abs(sv - np.round(sv)) * storey < 0.03)
        wx = (np.abs(fu - 0.3) < 0.09) | (np.abs(fu - 0.7) < 0.09)
        wy = (sv - np.floor(sv) > 0.3) & (sv - np.floor(sv) < 0.72)
        window = wx & wy & (v > top + 0.05) & (v < floor_y - 0.15)
        colour = np.where(beam[..., None], timber, plaster)
        lit = hash2(hi * 7 + np.floor(fu * 3), np.floor(sv), seed + 11) > 0.35
        if day:
            win = np.array([0.1, 0.12, 0.15])
        else:
            win = np.where(lit[..., None], np.array([3.2, 2.0, 0.8]), np.array([0.05, 0.05, 0.06]))
        colour = np.where(window[..., None], win, colour)
        roof = (v < top) & (v >= peak)
        colour = np.where(roof[..., None], np.array([0.3, 0.14, 0.1]) * (0.8 + 0.2 * hash2(hi, hi, seed + 2))[..., None], colour)
        sky = v < peak
        return colour, sky, window

    sky_mask = np.zeros((H, W), bool)
    window_mask = np.zeros((H, W), bool)
    for k, u, v in ((0, X + 5.0, Y), (3, Z, Y), (4, Z + 0.45, Y)):
        m = kind == k
        colour, sky, window = facade(u, v, 20 + k)
        alb[m] = colour[m]
        sky_mask |= m & sky
        window_mask |= m & window
    m = kind == 1
    alb[m] = cobbles(X, Z, 0.16, 30, (0.5, 0.47, 0.43))[m]
    sky_mask |= kind == 2
    sky_mask |= kind == -1
    alb *= value_noise((H, W), 50, 3, seed=5)[..., None] * 0.4 + 0.8

    portal = (0.0, -0.1, 3.4)
    if day:
        sun = [((-3.0, -6.0, -2.0), (1.0, 0.95, 0.85), 1.6, 9.0)]
        lights = sun + [(portal, (0.5, 0.32, 1.0), 1.2, 0.55)]
        rgb = shade(alb, kind, X, Y, Z, N, lights, (0.35, 0.36, 0.4), 30.0)
    else:
        lights = [(portal, (0.5, 0.32, 1.0), 2.8, 0.7), ((0.0, -3.0, 2.0), (0.35, 0.45, 0.8), 0.3, 5.0)]
        rgb = shade(alb, kind, X, Y, Z, N, lights, (0.012, 0.016, 0.035), 12.0)
        rgb[window_mask] = alb[window_mask] * 0.6                     # lit windows glow on their own

    # The sky over it all.
    t = np.clip(ys / VP[1], 0, 1)[..., None]
    if day:
        sky = np.array([0.25, 0.45, 0.85]) * (1 - t) + np.array([0.8, 0.85, 0.9]) * t
        clouds = np.clip(value_noise((H, W), 6, 5, seed=41) - 0.52, 0, 1)[..., None] * 2.2
        sky = sky * (1 - clouds) + clouds * np.array([1.0, 1.0, 1.0])
    else:
        sky = np.array([0.005, 0.008, 0.03]) * (1 - t) + np.array([0.04, 0.05, 0.1]) * t
        stars = (RNG.random((H, W)) > 0.9985) * RNG.random((H, W)) * 1.5
        sky = sky + ndimage.gaussian_filter(stars, 0.7)[..., None] * 3
        moon = np.hypot(xs - 1260, ys - 110) < 34
        sky[moon] = np.array([1.4, 1.35, 1.2])
    rgb[sky_mask] = sky[sky_mask]
    rgb = portal_glow(rgb, (0.3, 0.18, 0.7), 0.18 if day else 0.35)
    rgb = vignette(rgb, 0.35 if day else 0.55)
    return to_image(rgb)


# --- the rent's torn edge ------------------------------------------------------------------------

def rim():
    """The ragged edge of the rent at twice the size it is shown, with cracks running out from it."""
    pw, ph = int(PORTAL[2] * W * 2 * 1.9), int(PORTAL[3] * H * 2 * 1.5)
    s = 2
    w, h = pw * s, ph * s
    cx, cy = w / 2, h / 2
    rx, ry = PORTAL[2] * W * s, PORTAL[3] * H * s
    ys, xs = np.mgrid[0:h, 0:w].astype(float)
    ang = np.arctan2((ys - cy) / ry, (xs - cx) / rx)
    d = np.hypot((xs - cx) / rx, (ys - cy) / ry)
    # A torn edge: the radius wanders with the angle.
    k = np.arange(1, 24)
    phases = RNG.random(len(k)) * 6.283
    amps = 0.045 / k ** 0.7
    rag = (amps[:, None, None] * np.sin(k[:, None, None] * ang[None] + phases[:, None, None])).sum(0)
    edge = d - (1 + rag)
    line = np.exp(-(edge / 0.03) ** 2)                               # the bright torn line
    inner = np.clip(-edge / 0.25, 0, 1) * (edge < 0)                  # light falling inside
    outer = np.exp(-np.clip(edge, 0, None) / 0.12) * (edge >= 0)       # the glow on the world round it
    shadow = np.exp(-np.clip(edge, 0, None) / 0.05) * (edge >= 0)
    colour = np.zeros((h, w, 3)); alpha = np.zeros((h, w))
    white = np.array([0.95, 0.9, 1.0]); violet = np.array([0.7, 0.45, 1.0])
    colour += line[..., None] * white
    alpha += line
    colour += outer[..., None] * violet * 0.6
    alpha += outer * 0.45
    alpha += shadow * 0.25                                             # a dark lip just outside
    colour += (inner * 0.0)[..., None]
    # Cracks: jagged lines of light running out into the wall or the air.
    img = Image.new("L", (w, h), 0)
    dr = ImageDraw.Draw(img)
    for _ in range(14):
        a = RNG.random() * 6.283
        r0 = 1.0 + 0.03 * RNG.standard_normal()
        x, y = cx + math.cos(a) * rx * r0, cy + math.sin(a) * ry * r0
        width, bright = 4 * s, 255.0
        for _ in range(int(3 + RNG.random() * 5)):
            a += RNG.normal(0, 0.45)
            step = (18 + RNG.random() * 24) * s
            nx, ny = x + math.cos(a) * step, y + math.sin(a) * step
            dr.line([x, y, nx, ny], fill=int(bright), width=max(1, int(width)))
            x, y, width, bright = nx, ny, width * 0.78, bright * 0.8
    cracks = np.asarray(img, float) / 255
    crack_glow = ndimage.gaussian_filter(cracks, 6 * s)
    colour += cracks[..., None] * white + crack_glow[..., None] * violet * 1.5
    alpha += cracks + crack_glow * 1.2
    alpha = np.clip(alpha, 0, 1)
    colour = np.clip(colour / np.maximum(alpha, 1e-3)[..., None], 0, 1)
    # Nothing reaches the picture's edge: it fades out before it.
    border = np.minimum(np.minimum(xs, w - 1 - xs), np.minimum(ys, h - 1 - ys))
    alpha *= np.clip(border / (40 * s), 0, 1)
    rgba = np.dstack([colour, alpha])
    out = Image.fromarray((rgba * 255).astype(np.uint8), "RGBA")
    return out.resize((pw, ph), Image.LANCZOS)


# --- the hand ------------------------------------------------------------------------------------

def capsule(draw, x0, y0, angle, length, width0, width1, fill=255):
    """A tapering finger segment from (x0, y0) along angle (degrees from straight up)."""
    a = math.radians(angle)
    ux, uy = math.sin(a), -math.cos(a)
    px, py = -uy, ux
    x1, y1 = x0 + ux * length, y0 + uy * length
    draw.polygon([(x0 + px * width0 / 2, y0 + py * width0 / 2), (x1 + px * width1 / 2, y1 + py * width1 / 2),
                  (x1 - px * width1 / 2, y1 - py * width1 / 2), (x0 - px * width0 / 2, y0 - py * width0 / 2)], fill=fill)
    draw.ellipse([x0 - width0 / 2, y0 - width0 / 2, x0 + width0 / 2, y0 + width0 / 2], fill=fill)
    draw.ellipse([x1 - width1 / 2, y1 - width1 / 2, x1 + width1 / 2, y1 + width1 / 2], fill=fill)
    return x1, y1


def finger(draw, x, y, angle, length, width, hook, inward):
    """Three joints. Straight (hook 0), or hooked like a claw closing: the last two joints fold over
    toward the palm's middle (inward: +1 to turn clockwise, -1 anticlockwise), foreshortened."""
    parts = (0.45, 0.3, 0.25)
    for i, share in enumerate(parts):
        seg = length * share * (1 - 0.35 * hook * (i > 0))
        w0, w1 = width * (1 - 0.07 * i), width * (0.93 - 0.07 * i)
        angle += inward * hook * (70 if i == 1 else 65 if i == 2 else 0)
        x, y = capsule(draw, x, y, angle, seg, w0, w1)


def hand(grasp):
    s = 3                       # drawn at three times the size, for smooth edges
    w, h = 560 * s, 760 * s
    mask = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(mask)
    cx, palm_top = 280 * s, 390 * s
    # The forearm, rising from below and narrowing to the wrist.
    d.polygon([(cx - 95 * s, h), (cx + 105 * s, h), (cx + 88 * s, 560 * s), (cx - 82 * s, 560 * s)], fill=255)
    # The palm.
    d.rounded_rectangle([cx - 118 * s, palm_top, cx + 122 * s, 600 * s], radius=70 * s, fill=255)
    d.ellipse([cx - 125 * s, 450 * s, cx + 130 * s, 640 * s], fill=255)
    # Fingers: index to little, spread; closing, they hook over toward the palm like a claw.
    hook = 1.0 if grasp else 0.0
    spread = 0.6 if grasp else 1.0
    fingers = [(-78, -13, 250, 58), (-24, -4, 285, 60), (30, 5, 265, 56), (82, 15, 205, 50)]
    for dx, ang, length, width in fingers:
        finger(d, cx + dx * s, palm_top + 30 * s, ang * spread, length * s, width * s, hook, 1 if dx < 0 else -1)
    # The thumb, out to the side (curling in across the palm when closing).
    if grasp:
        x, y = capsule(d, cx - 105 * s, 545 * s, -40, 120 * s, 74 * s, 64 * s)
        capsule(d, x, y, 40, 95 * s, 64 * s, 54 * s)
    else:
        x, y = capsule(d, cx - 105 * s, 545 * s, -52, 130 * s, 74 * s, 64 * s)
        capsule(d, x, y, -30, 105 * s, 64 * s, 54 * s)
    m = np.asarray(mask.filter(ImageFilter.GaussianBlur(1.2 * s)), float) / 255

    # Incorporeal: bright at its edges and faint within (as light through smoke), veined with light,
    # glowing round, and trailing away to nothing down the arm.
    inner = ndimage.gaussian_filter(m, 18 * s)
    rim_light = np.clip(m - inner * 0.92, 0, 1) * 2.4
    halo = ndimage.gaussian_filter(m, 26 * s)
    ys, xs = np.mgrid[0:h, 0:w].astype(float)
    smoke = value_noise((h // 4, w // 4), 8, 5, seed=13 + grasp)
    smoke = ndimage.zoom(smoke, 4, order=1)[:h, :w]
    veins = np.abs(value_noise((h // 4, w // 4), 14, 3, seed=5) - 0.5)
    veins = np.clip(1 - ndimage.zoom(veins, 4, order=1)[:h, :w] * 34, 0, 1) * m
    fade = np.clip((h - ys) / (230 * s) + (smoke - 0.5) * 0.9, 0, 1) ** 1.3   # the arm trails away
    body = m * (0.28 + 0.35 * smoke) + rim_light * m + veins * 0.18
    alpha = np.clip(body + halo * 0.55, 0, 1) * fade
    tint = np.array([0.75, 0.9, 1.0])
    colour = tint * (0.7 + 0.3 * np.clip(rim_light + veins, 0, 1))[..., None]
    colour = colour + (halo * (1 - m))[..., None] * np.array([0.35, 0.25, 0.9]) * 0.8
    colour = np.clip(colour, 0, 1)
    rgba = np.dstack([colour, alpha])
    img = Image.fromarray((rgba * 255).astype(np.uint8), "RGBA")
    return img.resize((w // s, h // s), Image.LANCZOS)


def main():
    out = sys.argv[1]
    room().save(os.path.join(out, "recall-portal-room.jpg"), quality=88)
    square(True).save(os.path.join(out, "recall-portal-square-day.jpg"), quality=88)
    square(False).save(os.path.join(out, "recall-portal-square-night.jpg"), quality=88)
    rim().save(os.path.join(out, "recall-portal-rim.png"), optimize=True)
    hand(False).save(os.path.join(out, "recall-hand-open.png"), optimize=True)
    hand(True).save(os.path.join(out, "recall-hand-grasp.png"), optimize=True)
    for name in sorted(os.listdir(out)):
        if name.startswith("recall-portal") or name.startswith("recall-hand"):
            print(name, os.path.getsize(os.path.join(out, name)))


if __name__ == "__main__":
    main()
