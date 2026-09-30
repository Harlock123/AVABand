#!/usr/bin/env python3
"""The older scenes' pictures, rendered to match the newer ones (CC0, made for AVABand), replacing
the public-domain paintings they used to show. Same renderer as recall_portal_art.py and
scene_art.py: surfaces in one-point perspective, lit by point lights.

  stairs-down.jpg      a stair going down into the dark, torches on the walls, fewer as it goes
  stairs-up.jpg        a stair going up toward a grey light
  stairs-up-town.jpg   a stair going up toward the evening sky over the town
  level-cavern.jpg     a cave, water at its floor, lit by your torch
  level-labyrinth.jpg  a high, narrow corridor with ways off it, mist at its end
  level-fortress.jpg   a fortress wall in a great cavern: crenellations, a red-lit gate
  level-moria.jpg      the old dwarven halls: rows of great pillars going into the dark
  level-lair.jpg       a cave strewn with bones, eyes shining in the dark beyond
  level-gauntlet.jpg   a narrow trapped corridor: plates in the floor, fire from the walls
  danger.jpg           a hall lit red from its far end, and many eyes
  death.jpg            a grave at dusk: a headstone, a dead tree, candles in the grass

Needs numpy, scipy and Pillow. Usage: scene_art_levels.py <art folder> [name ...]
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
F = R.F
VP = R.VP


def grid():
    ys, xs = np.mgrid[0:H, 0:W].astype(float)
    return xs, ys


def glow_sprite(img, x, y, radius, colour, alpha):
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).ellipse([x - radius, y - radius, x + radius, y + radius], fill=colour + (alpha,))
    img.alpha_composite(layer.filter(ImageFilter.GaussianBlur(radius / 2)))


def torch_at(img, x, y, scale):
    """A sconce with its flame, sized for its distance."""
    R.torch_sprite(img, x, y, scale)


# --- stairs ------------------------------------------------------------------------------------------

def stairs(down, top_light, top_colour, name_steps=26, sky=False):
    """A straight stair in a stone passage, going down (or up) away from you; the eye stands on a
    landing, looking a little down the stair (or up it): the horizon is raised (or lowered) to tilt."""
    xs, ys = grid()
    half, head, sh, sd, z0 = 1.0, 1.7, 0.17, 0.36, (0.55 if down else 1.4)
    eye = 1.1 if down else 0.75                                    # (standing at the top, you see down past the edge)
    # Aim so the stair's far end sits a little above the middle of the picture.
    vp = (VP[0], 420.0 - F * sh / sd if down else 610.0)
    dx, dy = xs - vp[0], ys - vp[1]
    sign = 1 if down else -1
    n = name_steps
    s = [0.0] + [z0 + k * sd for k in range(n + 1)]               # where tread k starts (s[k]) and ends (s[k+1])
    y_floor = [eye + sign * k * sh for k in range(n + 1)]          # y of tread k (down is +)
    y_ceil = [y - head - 0.4 for y in y_floor]
    best = np.full((H, W), np.inf)
    kind = np.full((H, W), -1)
    kk = np.zeros((H, W), int)
    with np.errstate(divide="ignore", invalid="ignore"):
        for k in range(n + 1):
            for plane_y, code in ((y_floor[k], 1), (y_ceil[k], 2)):
                z = plane_y * F / dy
                ok = (z > 0) & (z >= s[k]) & (z < s[k + 1]) & (np.abs(dx * z / F) <= half) & (z < best)
                best = np.where(ok, z, best); kind = np.where(ok, code, kind); kk = np.where(ok, k, kk)
            if k == 0:
                continue
            # The vertical faces between treads k-1 and k: floor risers face you going up, ceiling ones going down.
            for (ya, yb), code, facing in (((y_floor[k - 1], y_floor[k]), 3, not down), ((y_ceil[k - 1], y_ceil[k]), 4, down)):
                if not facing:
                    continue
                z = s[k]
                yh = dy * z / F
                ok = (yh >= min(ya, yb)) & (yh <= max(ya, yb)) & (np.abs(dx * z / F) <= half) & (z < best)
                best = np.where(ok, z, best); kind = np.where(ok, code, kind); kk = np.where(ok, k, kk)
        # The walls, between the ceiling and the floor at that depth.
        zw = half * F / np.abs(dx)
        step = np.clip(np.searchsorted(np.array(s[1:]), zw), 0, n)
        yh = dy * zw / F
        yf = np.array(y_floor)[step]
        yc = np.array(y_ceil)[step]
        ok = (zw > 0) & (yh <= np.maximum(yf, yc)) & (yh >= np.minimum(yf, yc)) & (zw < best) & np.isfinite(zw)
        best = np.where(ok, zw, best); kind = np.where(ok, 5, kind)
    far = s[-1]
    Z = np.where(np.isfinite(best), best, far + 3)
    X = dx * Z / F
    Y = dy * Z / F
    N = np.zeros((H, W, 3))
    N[kind == 1] = (0, -1, 0); N[kind == 2] = (0, 1, 0); N[kind == 3] = (0, 0, -1); N[kind == 4] = (0, 0, -1)
    N[(kind == 5) & (dx < 0)] = (1, 0, 0); N[(kind == 5) & (dx >= 0)] = (-1, 0, 0)
    stone = (0.4, 0.37, 0.34)
    alb = np.zeros((H, W, 3))
    m = kind == 1; alb[m] = R.flagstones(X, Z * 2.9, 0.5, 3, (0.4, 0.37, 0.33))[m]
    m = kind == 2; alb[m] = R.stone_blocks(X, Z, 0.5, 0.5, 4, (0.18, 0.17, 0.16), 0.3)[m]
    m = kind == 3; alb[m] = R.stone_blocks(X, Y, 0.5, 0.2, 5, stone, 0.4)[m]
    m = kind == 4; alb[m] = R.stone_blocks(X, Y, 0.5, 0.2, 6, (0.2, 0.19, 0.18), 0.3)[m]
    m = kind == 5; alb[m] = R.stone_blocks(Z, Y, 0.34, 0.15, 7, stone, 0.5)[m]
    alb *= (R.value_noise((H, W), 50, 4, seed=9) * 0.5 + 0.75)[..., None]
    # Torches every few steps, fewer and further as it goes; and the light at the far end.
    lights, sprites = [], []
    for k in range(2, n, 5):
        zt = s[k]
        yt = y_floor[k] - 1.05
        side = -0.95 if (k // 5) % 2 == 0 else 0.95
        lights.append(((side, yt, zt), (1.0, 0.55, 0.22), 1.4, 0.38))
        sprites.append((vp[0] + F * side / zt, vp[1] + F * yt / zt, 1.2 / zt * 1.6))
    lights.append(((0.0, y_floor[-1] - 1.0, far + 1.0), top_colour, top_light, 3.0))
    lights.append(((0.35, -0.2, 0.3), (1.0, 0.7, 0.4), 1.1, 0.8))                  # your own torch
    rgb = R.shade(alb, kind, X, Y, Z, N, lights, (0.002, 0.002, 0.003), 6.0)
    # The far end: the light where the stair leads (or the dark).
    end = kind == -1
    rgb[end] = np.array(top_colour) * top_light * (0.35 if not down else 0.05)
    if sky:  # the evening sky over the town, through the opening at the top
        t = np.clip(ys / 120, 0, 1)[..., None]
        evening = np.array([0.35, 0.3, 0.6]) * (1 - t) + np.array([1.6, 0.9, 0.5]) * t
        rgb[end] = evening[end]
    rgb = R.vignette(rgb, 0.5)
    img = R.to_image(rgb).convert("RGBA")
    for x, y, sc in sprites:
        torch_at(img, x, y, max(0.25, sc))
    return img.convert("RGB")


def stairs_down():
    return stairs(True, 0.15, (0.4, 0.2, 0.1))


def stairs_up():
    return stairs(False, 2.2, (0.75, 0.78, 0.85))


def stairs_up_town():
    return stairs(False, 3.2, (1.0, 0.72, 0.5), sky=True)


# --- caves: a tunnel of rock, marched ray by ray ------------------------------------------------------

def cave(seed, rock, tint_light, far_glow, water=True):
    """A cave chamber: a room's surfaces with their edges warped ragged, rough rock for walls, the
    light raking across bumps in it; pools on the floor. Returns the picture and its centre."""
    xs, ys = grid()
    # Warp where each pixel looks, so the chamber's edges and floor line come out ragged, like rock.
    warp = 64
    wx = (R.value_noise((H, W), 6, 4, seed=seed) - 0.5) * warp
    wy = (R.value_noise((H, W), 6, 4, seed=seed + 1) - 0.5) * warp
    kind, X, Y, Z, N = R.surfaces(-1.3, 0.8, 1.9, 10.0)
    ix = np.clip(xs + wx, 0, W - 1).astype(int)
    iy = np.clip(ys + wy, 0, H - 1).astype(int)
    kind, X, Y, Z, N = kind[iy, ix], X[iy, ix], Y[iy, ix], Z[iy, ix], N[iy, ix]
    # Rough rock: a bumpy height, its slope tipping the normals so the light picks out every lump.
    bump = R.value_noise((H, W), 45, 5, seed=seed + 2)
    gy, gx = np.gradient(bump)
    N = N.copy()
    N[..., 0] += gx * 9
    N[..., 1] += gy * 9
    N[..., 2] += (gx + gy) * 4
    N /= np.linalg.norm(N, axis=-1, keepdims=True) + 1e-9
    strata = 0.8 + 0.25 * np.sin(Y * 11 + R.value_noise((H, W), 10, 3, seed=seed + 3) * 7)
    alb = np.array(rock) * (strata * (0.65 + 0.55 * bump))[..., None]
    pools = np.zeros((H, W), bool)
    if water:
        pools = (kind == 1) & (R.value_noise((H, W), 5, 3, seed=seed + 4) > 0.56)
    lights = [((0.4, -0.3, 0.5), tint_light, 1.5, 1.1), ((0.0, -0.2, 9.0), far_glow, 1.4, 2.5)]
    rgb = R.shade(alb, kind, X, Y, Z, N, lights, (0.003, 0.003, 0.004), 8.0)
    rgb *= np.exp(-np.clip(Z - 2, 0, None) / 6)[..., None]          # the dark swallows what's far
    if water:  # the pools: still and dark, a streak of the far glow and the torch on them
        streak = np.exp(-((xs - VP[0]) / 60) ** 2) * np.clip((ys - VP[1]) / 300, 0, 1)
        rgb[pools] = (np.array([0.01, 0.016, 0.022]) + streak[..., None] * np.array(far_glow) * 0.35)[pools]
    rgb = R.vignette(rgb, 0.55)
    img = R.to_image(rgb).convert("RGBA")
    stalactites(img, seed, VP)
    return img.convert("RGB"), VP


def stalactites(img, seed, centre):
    """Stalactites hanging from the roof at their depth: cones, nearer ones bigger, lit on one side."""
    rng = np.random.default_rng(seed + 50)
    cx, cy = centre
    spikes = sorted(((rng.uniform(1.2, 9.0), rng.uniform(-1.8, 1.8)) for _ in range(34)), reverse=True)  # far first
    for z, xw in spikes:
        x = cx + F * xw / z
        top = cy + F * -1.3 / z - 30
        length = F * rng.uniform(0.15, 0.45) / z
        width = F * rng.uniform(0.04, 0.09) / z
        near = np.clip(1 - z / 9, 0, 1)
        layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        tone = int(14 + 40 * near)
        d.polygon([(x - width / 2, top), (x + width / 2, top), (x + rng.uniform(-2, 2), top + length)], fill=(tone, tone - 3, tone - 6, 255))
        d.line([(x - width / 2, top), (x, top + length)], fill=(tone + 45, tone + 32, tone + 20, 180), width=max(1, int(width / 7)))
        img.alpha_composite(layer.filter(ImageFilter.GaussianBlur(float(0.8 + 1.2 * (1 - near)))))


def level_cavern():
    img, _ = cave(71, (0.42, 0.38, 0.33), (1.0, 0.7, 0.4), (0.3, 0.55, 0.6))
    return img


def level_lair():
    img, (cx, cy) = cave(73, (0.38, 0.3, 0.27), (1.0, 0.55, 0.3), (0.5, 0.08, 0.05), water=False)
    img = img.convert("RGBA")
    d = ImageDraw.Draw(img)
    rng = np.random.default_rng(11)
    # Bones on the floor: long bones and a few skulls, smaller further off.
    for _ in range(38):
        y = rng.uniform(cy + 90, H - 20)
        depth = (y - cy) / (H - cy)
        x = cx + rng.uniform(-1, 1) * W * 0.55 * depth
        s = 6 + 40 * depth ** 1.4
        a = rng.uniform(0, math.pi)
        tone = int(150 + 70 * depth)
        if rng.random() < 0.25:
            d.ellipse([x - s * 0.5, y - s * 0.42, x + s * 0.5, y + s * 0.4], fill=(tone, tone - 10, tone - 30, 255))
            d.ellipse([x - s * 0.28, y - s * 0.12, x - s * 0.06, y + s * 0.08], fill=(20, 15, 12, 255))
            d.ellipse([x + s * 0.06, y - s * 0.12, x + s * 0.28, y + s * 0.08], fill=(20, 15, 12, 255))
        else:
            x2, y2 = x + math.cos(a) * s * 1.6, y + math.sin(a) * s * 0.5
            d.line([(x, y), (x2, y2)], fill=(tone, tone - 12, tone - 35, 255), width=max(2, int(s / 6)))
            for ex, ey in ((x, y), (x2, y2)):
                d.ellipse([ex - s / 7, ey - s / 7, ex + s / 7, ey + s / 7], fill=(tone, tone - 12, tone - 35, 255))
    # Eyes in the dark beyond.
    for _ in range(7):
        x, y = cx + rng.uniform(-160, 160), cy + rng.uniform(-40, 50)
        gap = rng.uniform(8, 16)
        for ex in (x - gap / 2, x + gap / 2):
            glow_sprite(img, ex, y, 7, (255, 60, 20), 170)
            d.ellipse([ex - 2, y - 1.5, ex + 2, y + 1.5], fill=(255, 190, 90, 255))
    return img.convert("RGB")


# --- halls: the room renderer, reshaped -------------------------------------------------------------------

def hall(ceiling_y, floor_y, half, back_z, wall, floor_fn, lights, ambient, fog, openings=None):
    kind, X, Y, Z, N = R.surfaces(ceiling_y, floor_y, half, back_z)
    alb = np.zeros((H, W, 3))
    for k, u in ((0, X), (3, Z), (4, Z)):
        m = kind == k
        alb[m] = R.stone_blocks(u, Y, 0.34, 0.15, 30 + k, wall, 0.5)[m]
    m = kind == 1
    alb[m] = floor_fn(X, Z)[m]
    m = kind == 2
    alb[m] = R.stone_blocks(X, Z, 0.5, 0.5, 36, tuple(c * 0.45 for c in wall), 0.3)[m]
    alb *= (R.value_noise((H, W), 50, 4, seed=37) * 0.5 + 0.75)[..., None]
    gap = np.zeros((H, W), bool)
    if openings is not None:  # ways off it: gaps in the side walls, opening on the dark
        gap = ((kind == 3) | (kind == 4)) & openings(Z)
        alb[gap] = 0.0
    rgb = R.shade(alb, kind, X, Y, Z, N, lights, ambient, fog)
    rgb[gap] = np.array([0.018, 0.02, 0.026]) * (0.6 + 0.8 * R.value_noise((H, W), 10, 3, seed=38))[gap][..., None]
    return rgb, kind, X, Y, Z


def level_labyrinth():
    openings = lambda z: ((z % 3.1) > 2.2) & (z > 1.5)
    rgb, kind, X, Y, Z = hall(-3.2, 0.7, 0.75, 14.0, (0.36, 0.35, 0.36),
                              lambda x, z: R.flagstones(x, z, 0.4, 51, (0.3, 0.3, 0.31)),
                              [((0.3, -0.3, 0.5), (1.0, 0.72, 0.45), 1.2, 0.9), ((0.0, -0.5, 12.0), (0.5, 0.6, 0.75), 1.2, 3.0)],
                              (0.003, 0.003, 0.004), 7.0, openings)
    xs, ys = grid()
    mist = np.clip((Z - 4) / 10, 0, 1) * (0.6 + 0.4 * R.value_noise((H, W), 8, 3, seed=52))
    rgb = rgb * (1 - mist[..., None] * 0.7) + mist[..., None] * np.array([0.12, 0.14, 0.18])
    rgb[kind == 2] *= 0.15                                     # no ceiling to speak of: the walls go up into the dark
    return R.to_image(R.vignette(rgb, 0.55))


def level_gauntlet():
    plates = lambda x, z: np.where(((np.floor(z / 0.7) % 3) == 1)[..., None] & (np.abs(x) < 0.4)[..., None],
                                   np.array([0.22, 0.2, 0.18]), R.flagstones(x, z, 0.35, 61, (0.33, 0.31, 0.28)))
    flames = [(-0.55 if i % 2 else 0.55, -0.1, 1.6 + i * 1.4) for i in range(7)]
    lights = [((fx * 0.9, fy, fz), (1.0, 0.45, 0.12), 1.3, 0.45) for fx, fy, fz in flames]
    lights.append(((0.2, -0.3, 0.4), (1.0, 0.7, 0.4), 0.8, 0.8))
    rgb, kind, X, Y, Z = hall(-0.8, 0.62, 0.55, 11.0, (0.34, 0.31, 0.28), plates, lights, (0.002, 0.002, 0.002), 6.0)
    img = R.to_image(R.vignette(rgb, 0.5)).convert("RGBA")
    # Jets of fire from the walls, and arrow slits between them.
    for fx, fy, fz in flames:
        x = VP[0] + F * fx / fz
        y = VP[1] + F * fy / fz
        s = 1.0 / fz
        direction = -1 if fx > 0 else 1
        rng = np.random.default_rng(int(fz * 100))
        glow_sprite(img, x + direction * 90 * s, y, 150 * s, (255, 110, 30), 90)
        layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
        ld = ImageDraw.Draw(layer)
        for k in range(90):
            t = rng.uniform(0, 1) ** 1.3                          # along the jet: 0 at the vent
            spread = t * 90 * s
            px = x + direction * t * 520 * s
            py = y + rng.normal(0, 1) * spread - t * t * 40 * s     # it rises a little as it goes
            r = (44 - 24 * t) * s * rng.uniform(0.6, 1.3)
            heat = 1 - t
            colour = (255, int(90 + 160 * heat ** 1.5), int(30 + 170 * heat ** 4), int(220 * (1 - t) ** 0.7))
            ld.ellipse([px - r, py - r * 0.7, px + r, py + r * 0.7], fill=colour)
        img.alpha_composite(layer.filter(ImageFilter.GaussianBlur(max(1.2, 5 * s))))
        ImageDraw.Draw(img).rectangle([x - 6 * s, y - 14 * s, x + 6 * s, y + 14 * s], fill=(20, 16, 12, 255))  # the vent
    return img.convert("RGB")


def level_fortress():
    xs, ys = grid()
    back_z, half = 9.0, 6.0
    kind, X, Y, Z, N = R.surfaces(-5.0, 0.7, half, back_z)
    alb = np.zeros((H, W, 3))
    # The fortress front: the back wall, up to its battlements; above that, the cavern's dark.
    merlon = (np.floor((X + 20) / 0.45) % 2) == 0
    wall_top = np.where(np.abs(X) > 1.8, -2.0, -1.4)          # two towers either side of the gate
    wall_top = np.where(merlon, wall_top - 0.3, wall_top)
    front = (kind == 0) & (Y >= wall_top)
    gate = front & (np.abs(X) < 0.55) & (Y > -0.3 - 0.5 * np.sqrt(np.clip(1 - (X / 0.55) ** 2, 0, 1)))
    alb[front] = R.stone_blocks(X, Y, 0.45, 0.2, 71, (0.3, 0.28, 0.3), 0.5)[front]
    m = kind == 1
    rock = R.value_noise((H, W), 25, 4, seed=72)
    alb[m] = (np.array([0.25, 0.22, 0.2]) * (0.6 + 0.6 * rock)[..., None])[m]
    lights = [((0.0, 0.3, back_z - 0.3), (1.0, 0.25, 0.08), 4.0, 2.2), ((-1.4, -1.0, back_z - 0.2), (1.0, 0.5, 0.2), 1.2, 0.8),
              ((1.4, -1.0, back_z - 0.2), (1.0, 0.5, 0.2), 1.2, 0.8)]
    rgb = R.shade(alb, kind, X, Y, Z, N, lights, (0.002, 0.002, 0.003), 12.0)
    rgb[gate] = np.array([1.6, 0.35, 0.1]) * (0.7 + 0.3 * R.value_noise((H, W), 6, 2, seed=73))[gate][..., None]
    dark = ~front & (kind != 1)
    rgb[dark] = np.array([0.02, 0.012, 0.015]) * (0.5 + R.value_noise((H, W), 5, 3, seed=74))[dark][..., None]
    img = R.to_image(R.vignette(rgb, 0.55)).convert("RGBA")
    for side in (-1.4, 1.4):
        R.torch_sprite(img, VP[0] + F * side / back_z, VP[1] + F * -1.0 / back_z, 0.5)
    return img.convert("RGB")


def level_moria():
    """The halls of the dwarves: two rows of great round pillars going on into the dark."""
    xs, ys = grid()
    dx, dy = xs - VP[0], ys - VP[1]
    floor_y, ceil_y = 0.8, -4.0
    with np.errstate(divide="ignore", invalid="ignore"):
        zf = np.where(dy > 0, floor_y * F / dy, np.inf)
        zc = np.where(dy < 0, ceil_y * F / dy, np.inf)
    best = np.minimum(zf, zc)
    kind = np.where(zf <= zc, 1, 2)
    nx = np.zeros((H, W)); nz = np.zeros((H, W))
    a = (dx / F) ** 2 + 1
    for row in (-1.6, 1.6, -4.0, 4.0):
        for i in range(16):
            px, pz, r = row, 2.0 + i * 2.4, 0.42 if abs(row) < 3 else 0.5
            b = -2 * (dx / F * px + pz)
            c = px * px + pz * pz - r * r
            disc = b * b - 4 * a * c
            with np.errstate(invalid="ignore"):
                t = (-b - np.sqrt(disc)) / (2 * a)
            ok = (disc > 0) & (t > 0) & (t < best)
            best = np.where(ok, t, best); kind = np.where(ok, 3, kind)
            hx = dx / F * t - px
            hz = t - pz
            nx = np.where(ok, hx / r, nx); nz = np.where(ok, hz / r, nz)
    Z = np.where(np.isfinite(best), best, 60)
    X, Y = dx * Z / F, dy * Z / F
    N = np.zeros((H, W, 3))
    N[kind == 1] = (0, -1, 0); N[kind == 2] = (0, 1, 0)
    N[kind == 3, 0] = nx[kind == 3]; N[kind == 3, 2] = nz[kind == 3]
    alb = np.zeros((H, W, 3))
    m = kind == 1; alb[m] = R.flagstones(X, Z, 0.6, 81, (0.32, 0.3, 0.28))[m]
    m = kind == 2; alb[m] = np.array([0.1, 0.1, 0.1])
    ang = np.arctan2(nz, nx)
    m = kind == 3; alb[m] = (np.array([0.42, 0.4, 0.37]) * (0.8 + 0.2 * np.cos(ang * 12))[..., None])[m]     # fluted
    alb *= (R.value_noise((H, W), 50, 4, seed=82) * 0.4 + 0.8)[..., None]
    lights = [((0.3, -0.5, 0.6), (1.0, 0.7, 0.42), 1.6, 1.4), ((0.0, -1.0, 24.0), (0.4, 0.55, 0.8), 1.4, 5.0)]
    rgb = R.shade(alb, kind, X, Y, Z, N, lights, (0.003, 0.003, 0.004), 14.0)
    fog = np.clip((Z - 8) / 30, 0, 1)[..., None]
    rgb = rgb * (1 - fog * 0.85) + fog * np.array([0.03, 0.04, 0.06])
    return R.to_image(R.vignette(rgb, 0.5))


def danger():
    rgb, kind, X, Y, Z = hall(-1.6, 0.7, 2.2, 16.0, (0.3, 0.27, 0.27),
                              lambda x, z: R.flagstones(x, z, 0.6, 91, (0.28, 0.25, 0.24)),
                              [((0.0, -0.4, 15.5), (1.0, 0.12, 0.05), 9.0, 4.0), ((0.3, -0.3, 0.5), (1.0, 0.6, 0.35), 0.5, 0.8)],
                              (0.002, 0.001, 0.001), 14.0)
    smoke = R.value_noise((H, W), 7, 4, seed=92)
    rgb = rgb * (0.75 + 0.35 * smoke)[..., None]
    img = R.to_image(R.vignette(rgb, 0.6)).convert("RGBA")
    d = ImageDraw.Draw(img)
    rng = np.random.default_rng(93)
    for _ in range(26):   # eyes, many, in the dark along the walls
        side = rng.choice([-1, 1])
        z = rng.uniform(2.5, 12)
        x = VP[0] + F * side * rng.uniform(1.4, 2.1) / z
        y = VP[1] + F * rng.uniform(-0.8, 0.4) / z
        gap = 90 / z
        colour = (255, 200, 60) if rng.random() < 0.3 else (255, 50, 20)
        for ex in (x - gap / 2, x + gap / 2):
            glow_sprite(img, ex, y, max(4, 40 / z), colour, 150)
            d.ellipse([ex - 12 / z - 1, y - 7 / z - 1, ex + 12 / z + 1, y + 7 / z + 1], fill=colour + (255,))
    return img.convert("RGB")


def death():
    xs, ys = grid()
    horizon = H * 0.62
    t = np.clip(ys / horizon, 0, 1)[..., None]
    sky = np.array([0.08, 0.07, 0.16]) * (1 - t) + np.array([0.75, 0.38, 0.28]) * t ** 2.2
    ground_t = np.clip((ys - horizon) / (H - horizon), 0, 1)[..., None]
    grass = R.value_noise((H, W), 60, 4, seed=101)[..., None]
    ground = np.array([0.05, 0.07, 0.04]) * (0.6 + 0.8 * grass) * (0.6 + 0.6 * ground_t)
    rgb = np.where((ys < horizon)[..., None], sky, ground)
    # A band of mist along the ground.
    mist = np.exp(-((ys - horizon - 30) / 70) ** 2)[..., None] * (0.5 + 0.5 * R.value_noise((H, W), 6, 3, seed=102))[..., None]
    rgb = rgb + mist * np.array([0.25, 0.2, 0.22]) * 0.6
    img = R.to_image(rgb).convert("RGBA")
    d = ImageDraw.Draw(img)
    # Far hills and a ruined wall on the horizon.
    hill = [(0, horizon)] + [(x, horizon - 40 - 30 * math.sin(x / 170) - 15 * math.sin(x / 53)) for x in range(0, W + 20, 20)] + [(W, horizon)]
    d.polygon(hill, fill=(22, 18, 28, 255))
    # A dead tree, left: a trunk and branches that fork and thin.
    def branch(x, y, angle, length, width, depth):
        if depth == 0 or length < 6:
            return
        x2, y2 = x + math.cos(angle) * length, y - math.sin(angle) * length
        d.line([(x, y), (x2, y2)], fill=(14, 11, 14, 255), width=max(1, int(width)))
        for turn in (-0.45, 0.38):
            branch(x2, y2, angle + turn + 0.1 * math.sin(depth * 3.1), length * 0.72, width * 0.66, depth - 1)
    branch(300, horizon + 60, math.pi / 2, 170, 26, 8)
    # The headstone, and its grave.
    gx, gy, gw, gh = W * 0.58, H * 0.78, 190, 260
    d.polygon([(gx - gw * 0.9, gy + 40), (gx + gw * 0.9, gy + 40), (gx + gw * 1.1, gy + 110), (gx - gw * 1.1, gy + 110)], fill=(26, 22, 18, 255))
    stone = Image.new("RGBA", (gw * 2, gh + 40), (0, 0, 0, 0))
    sd = ImageDraw.Draw(stone)
    sd.rounded_rectangle([10, 60, gw * 2 - 10, gh + 40], radius=30, fill=(118, 114, 110, 255))
    sd.ellipse([10, 0, gw * 2 - 10, 150], fill=(118, 114, 110, 255))
    sa = np.asarray(stone, float)
    lichen = R.value_noise((gh + 40, gw * 2), 10, 4, seed=103)
    sa[..., :3] *= (0.75 + 0.35 * lichen)[..., None]
    shade_x = np.linspace(1.1, 0.7, gw * 2)[None, :]                          # lit from the sunset, left
    sa[..., :3] *= shade_x[..., None]
    stone = Image.fromarray(np.clip(sa, 0, 255).astype(np.uint8), "RGBA")
    sd = ImageDraw.Draw(stone)
    for i, (x0, x1) in enumerate(((120, 260), (100, 280))):                    # a cross, carved
        pass
    sd.rectangle([gw - 12, 60, gw + 12, 200], fill=(60, 58, 56, 255))
    sd.rectangle([gw - 50, 95, gw + 50, 118], fill=(60, 58, 56, 255))
    img.alpha_composite(stone, (int(gx - gw), int(gy - gh + 40)))
    # Candles in the grass, and their glow.
    for cx, cy in ((gx - 230, gy + 80), (gx + 240, gy + 95), (gx - 120, gy + 125)):
        glow_sprite(img, cx, cy - 26, 36, (255, 170, 80), 120)
        d.rectangle([cx - 5, cy - 26, cx + 5, cy], fill=(220, 210, 190, 255))
        d.ellipse([cx - 4, cy - 40, cx + 4, cy - 26], fill=(255, 210, 110, 255))
    return img.convert("RGB")


def main():
    out = sys.argv[1]
    wanted = sys.argv[2:]
    for name, make in PICTURES.items():
        if wanted and name not in wanted:
            continue
        path = os.path.join(out, name + ".jpg")
        make().save(path, quality=88)
        print(path, os.path.getsize(path))


PICTURES = {
    "stairs-down": stairs_down,
    "stairs-up": stairs_up,
    "stairs-up-town": stairs_up_town,
    "level-cavern": level_cavern,
    "level-labyrinth": level_labyrinth,
    "level-fortress": level_fortress,
    "level-moria": level_moria,
    "level-lair": level_lair,
    "level-gauntlet": level_gauntlet,
    "danger": danger,
    "death": death,
}

if __name__ == "__main__":
    main()
