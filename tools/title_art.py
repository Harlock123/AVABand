#!/usr/bin/env python3
"""Draw AVABand's title screen: Thangorodrim, the three smoking peaks over the gates of Angband.

Everything is procedural (CC0, made for AVABand): a night sky reddened by the fires below, smoke
rising from the three peaks, a far range, the peaks themselves rim-lit by their own glow, the
gate at the foot of the tallest, a scorched plain and drifting embers. The logo is drawn on its
own, transparent, so the game can scale it separately.

Needs numpy and Pillow (pip install numpy pillow).

Usage:
    title_art.py <background.png> <logo.png> --font CinzelDecorative-Black.ttf --subtitle-font Cinzel.ttf
"""
import argparse

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

W, H = 1920, 1080
RNG = np.random.default_rng(4242)


def fractal_noise(w, h, octaves=6, base=4, persistence=0.55):
    """Value noise, summed over octaves, in 0..1."""
    out = np.zeros((h, w))
    amp, total = 1.0, 0.0
    for o in range(octaves):
        cells = base * 2 ** o
        grid = RNG.random((cells + 1, int(cells * w / h) + 2))
        img = Image.fromarray((grid * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
        out += amp * np.asarray(img, dtype=float) / 255
        total += amp
        amp *= persistence
    return out / total


def lerp(a, b, t):
    return a + (b - a) * t


def ridge(x0, x1, peak_x, peak_y, base_y, roughness, seed_shift=0):
    """A mountain silhouette's top edge (y for every column), steep near the peak, by midpoint
    displacement on a triangle."""
    xs = np.arange(W)
    left = np.clip((xs - x0) / max(1, peak_x - x0), 0, 1)
    right = np.clip((x1 - xs) / max(1, x1 - peak_x), 0, 1)
    shape = np.where(xs < peak_x, left, right) ** 1.6
    y = lerp(base_y, peak_y, shape)
    # jagged detail, stronger higher up
    detail = np.zeros(W)
    amp = roughness
    step = 256
    while step >= 2:
        pts = RNG.standard_normal(W // step + 2) * amp
        detail += np.interp(xs, np.arange(len(pts)) * step, pts)
        amp *= 0.55
        step //= 2
    y += detail * (0.35 + shape)
    # crags: sharp spires and notches along the flanks
    for _ in range(int((x1 - x0) / 55)):
        cx = RNG.uniform(x0, x1)
        w = RNG.uniform(8, 30)
        h = RNG.uniform(10, 55) * (0.4 + shape[int(np.clip(cx, 0, W - 1))])
        spike = np.clip(1 - np.abs(xs - cx) / w, 0, 1) ** 1.3 * h
        y -= spike if RNG.random() < 0.7 else -spike * 0.5
    y[(xs < x0) | (xs > x1)] = H + 10
    return y


def fill_below(mask_img, ys):
    """A mask filled from each column's y down to the bottom."""
    rows = np.arange(H)[:, None]
    return rows >= ys[None, :]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("background")
    ap.add_argument("logo")
    ap.add_argument("--font", required=True)
    ap.add_argument("--subtitle-font", required=True)
    a = ap.parse_args()

    img = np.zeros((H, W, 3))
    yy, xx = np.mgrid[0:H, 0:W]
    v = yy / H

    # --- sky: night blue above, the red of the fires near the horizon ---
    top = np.array([6, 7, 16]) / 255
    mid = np.array([26, 12, 28]) / 255
    low = np.array([96, 26, 12]) / 255
    t1 = np.clip(v / 0.45, 0, 1)[..., None]
    t2 = np.clip((v - 0.45) / 0.3, 0, 1)[..., None]
    img[:] = lerp(lerp(top, mid, t1), low, t2)

    # a glow behind the central peak
    cx, cy = W * 0.52, H * 0.62
    d = np.sqrt(((xx - cx) / (W * 0.42)) ** 2 + ((yy - cy) / (H * 0.55)) ** 2)
    glow = np.clip(1 - d, 0, 1) ** 2.2
    img += glow[..., None] * np.array([150, 55, 10]) / 255

    # clouds, lit from below
    clouds = fractal_noise(W, H, 7, 3)
    cl = np.clip((clouds - 0.48) * 3.0, 0, 1) * np.clip(1.1 - v * 1.2, 0, 1)
    lit = np.clip(v * 1.4 + glow * 0.8, 0, 1)[..., None]
    cloud_col = lerp(np.array([18, 16, 26]) / 255, np.array([120, 45, 20]) / 255, lit)
    img = lerp(img, cloud_col, cl[..., None] * 0.85)

    # stars, only in the clear upper sky
    stars = RNG.random((H, W)) > 0.9993
    stars &= (v < 0.45) & (cl < 0.15)
    bright = RNG.random((H, W)) * 0.7 + 0.3
    img[stars] = np.maximum(img[stars], (bright[stars] * np.array([[0.85], [0.85], [1.0]])).T)

    # --- smoke plumes rising from the three peaks ---
    smoke_noise = fractal_noise(W, H, 7, 5, 0.6)
    peaks = [(W * 0.30, H * 0.40, 0.7), (W * 0.52, H * 0.20, 1.0), (W * 0.73, H * 0.36, 0.75)]
    for px, py, size in peaks:
        height = np.clip((py - yy) / (py + 1), 0, 1)                 # 0 at the peak, 1 at the top
        drift = px + (py - yy) * 0.35 * size                           # the wind takes it east
        width = 30 + (py - yy).clip(0) * 0.55 * size
        col = np.exp(-(((xx - drift) / np.maximum(width, 1)) ** 2))
        dens = col * (yy < py + 20) * np.clip(smoke_noise * 2.2 - 0.55, 0, 1) * (1 - height * 0.5)
        under = np.clip(1 - height * 1.6, 0, 1)[..., None]         # ember-lit near the vent
        shade = np.clip(smoke_noise - 0.35, 0, 1)[..., None] * 1.5  # billows catch the light
        smoke_col = lerp(np.array([48, 36, 40]) / 255, np.array([190, 80, 30]) / 255, under) * (0.7 + shade)
        img = lerp(img, smoke_col, np.clip(dens * 1.1 * size, 0, 0.9)[..., None])

    # --- mountains: far range, then Thangorodrim, then foreground crags ---
    far = ridge(-100, W + 100, W * 0.18, H * 0.58, H * 0.72, 18)
    far2 = ridge(W * 0.55, W + 200, W * 0.88, H * 0.55, H * 0.72, 16)
    far = np.minimum(far, far2)
    m = fill_below(None, far)
    img[m] = lerp(img[m], np.array([28, 14, 22]) / 255, 0.85)

    peak_masks = []
    for (px, py, size), (x0, x1) in zip(peaks, [(W * 0.10, W * 0.47), (W * 0.30, W * 0.78), (W * 0.58, W * 0.97)]):
        ys = ridge(x0, x1, px, py, H * 0.80, 55 * size)
        peak_masks.append((ys, px))
    # draw the side peaks first, then the tall one in front
    for ys, px in sorted(peak_masks, key=lambda p: -np.min(p[0])):
        mask = fill_below(None, ys)
        base_col = np.array([10, 6, 9]) / 255
        img[mask] = base_col
        # rim light: a thin edge along the ridge facing the glow, and lava seams
        edge = mask & ~fill_below(None, ys + 3)
        facing = np.clip(1 - np.abs(xx - W * 0.52) / (W * 0.5), 0, 1)
        img[edge] = lerp(img[edge], np.array([230, 90, 25]) / 255, (0.35 + 0.65 * facing[edge])[:, None])
    # lava seams down the central peak
    ys_c = min(peak_masks, key=lambda p: np.min(p[0]))[0]
    for k in range(4):
        x = W * 0.52 + RNG.normal(0, 25)
        y = H * 0.22 + RNG.uniform(10, 60)
        dx = RNG.normal(0, 3)
        pts = []
        while y < H * 0.62 + RNG.uniform(0, 80):
            pts.append((x, y))
            dx = 0.8 * dx + RNG.normal(0, 4)
            x += dx
            y += RNG.uniform(4, 9)
        seam = Image.new("L", (W, H))
        ImageDraw.Draw(seam).line(pts, fill=255, width=2)
        seam = np.asarray(seam.filter(ImageFilter.GaussianBlur(1.2)), dtype=float) / 255
        seam *= fill_below(None, ys_c + 12)
        img = lerp(img, np.array([255, 110, 30]) / 255, (seam * 0.45)[..., None])

    # --- the gate at the foot of the central peak ---
    gate = Image.new("L", (W, H))
    gd = ImageDraw.Draw(gate)
    gx, gy, gw, gh = W * 0.52, H * 0.80, 56, 120
    gd.rectangle([gx - gw / 2, gy - gh + gw / 2, gx + gw / 2, gy], fill=255)
    gd.ellipse([gx - gw / 2, gy - gh, gx + gw / 2, gy - gh + gw], fill=255)
    gate_a = np.asarray(gate, dtype=float) / 255
    gate_glow = np.asarray(gate.filter(ImageFilter.GaussianBlur(40)), dtype=float) / 255
    img += gate_glow[..., None] * np.array([255, 100, 25]) / 255 * 0.9
    inner = np.clip(1 - (yy - (gy - gh)) / gh, 0, 1) * 0.5 + 0.5
    img = lerp(img, (np.array([255, 150, 60]) / 255) * inner[..., None], gate_a[..., None] * 0.95)
    # flanking towers
    for side in (-1, 1):
        tx = gx + side * 95
        tower = Image.new("L", (W, H))
        td = ImageDraw.Draw(tower)
        td.polygon([(tx - 22, gy), (tx - 15, gy - 190), (tx, gy - 230), (tx + 15, gy - 190), (tx + 22, gy)], fill=255)
        t_a = np.asarray(tower, dtype=float) / 255
        img = lerp(img, np.array([6, 3, 5]) / 255, t_a[..., None])
        # a lit window
        img[int(gy - 170):int(gy - 158), int(tx - 3):int(tx + 3)] = np.array([255, 140, 50]) / 255

    # --- the plain: scorched ground and near crags ---
    plain = H * 0.80 + fractal_noise(W, 1, 5, 6)[0] * 18
    mask = fill_below(None, plain)
    ground = lerp(np.array([22, 9, 8]) / 255, np.array([5, 3, 4]) / 255, np.clip((yy - H * 0.8) / (H * 0.2), 0, 1)[..., None])
    img[mask] = ground[mask]
    # the road to the gate, faintly lit
    road = np.clip(1 - np.abs(xx - gx) / (40 + (yy - gy).clip(0) * 1.6), 0, 1) * (yy > gy)
    img += (road * 0.25)[..., None] * np.array([200, 80, 25]) / 255
    for side, (x0, x1) in ((-1, (-200, W * 0.33)), (1, (W * 0.70, W + 200))):
        crag = ridge(x0, x1, W * (0.08 if side < 0 else 0.93), H * 0.70, H * 1.02, 30)
        m = fill_below(None, crag)
        img[m] = np.array([4, 2, 3]) / 255
        edge = m & ~fill_below(None, crag + 2)
        img[edge] = lerp(img[edge], np.array([180, 70, 20]) / 255, 0.5)

    # --- embers drifting up ---
    ember = Image.new("L", (W, H))
    ed = ImageDraw.Draw(ember)
    for _ in range(110):
        x = RNG.normal(W * 0.52, W * 0.18)
        y = H - abs(RNG.normal(0, H * 0.28))
        r = RNG.uniform(0.7, 2.2)
        ed.ellipse([x - r, y - r, x + r, y + r], fill=int(RNG.uniform(120, 255)))
    e_a = np.asarray(ember.filter(ImageFilter.GaussianBlur(1.0)), dtype=float) / 255
    e_glow = np.asarray(ember.filter(ImageFilter.GaussianBlur(5)), dtype=float) / 255
    img += e_glow[..., None] * np.array([255, 90, 20]) / 255 * 0.8
    img = lerp(img, np.array([255, 190, 90]) / 255, e_a[..., None])

    # --- vignette and film grain ---
    vig = np.clip(1 - (((xx - W / 2) / (W * 0.75)) ** 2 + ((yy - H * 0.55) / (H * 0.8)) ** 2), 0, 1) ** 0.6
    img *= (0.35 + 0.65 * vig)[..., None]
    img += RNG.normal(0, 0.012, img.shape)
    Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).save(a.background, optimize=True)

    # --- the logo: AVABand in gold, with its subtitle ---
    LW, LH = 1400, 420
    font = ImageFont.truetype(a.font, 200)
    sub_font = ImageFont.truetype(a.subtitle_font, 44)
    try:
        sub_font.set_variation_by_axes([600])
    except Exception:
        pass
    text = "AVABand"
    mask = Image.new("L", (LW, LH))
    md = ImageDraw.Draw(mask)
    bbox = md.textbbox((0, 0), text, font=font)
    tx = (LW - (bbox[2] - bbox[0])) / 2 - bbox[0]
    ty = 40 - bbox[1]
    md.text((tx, ty), text, font=font, fill=255)
    m = np.asarray(mask, dtype=float) / 255
    ly = np.mgrid[0:LH, 0:LW][0]
    text_top, text_bot = ty + bbox[1], ty + bbox[3]
    g = np.clip((ly - text_top) / max(1, text_bot - text_top), 0, 1)
    # molten gold: pale at the top, deep amber below, a bright band across the middle
    gold = lerp(np.array([255, 236, 170]), np.array([150, 70, 12]), g[..., None])
    gold += (np.exp(-((g - 0.42) / 0.07) ** 2) * 60)[..., None]
    # bevel: light from above-left
    emb = np.asarray(mask.filter(ImageFilter.EMBOSS), dtype=float) - 128
    gold += emb[..., None] * 0.8
    logo = np.zeros((LH, LW, 4))
    # a dark outline and a fiery glow behind
    outline = np.asarray(mask.filter(ImageFilter.MaxFilter(9)), dtype=float) / 255
    glow_a = np.asarray(mask.filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(22)), dtype=float) / 255
    logo[..., :3] = np.array([255, 90, 20])
    logo[..., 3] = glow_a * 170
    logo[..., :3] = lerp(logo[..., :3], np.array([20, 8, 4]), outline[..., None])
    logo[..., 3] = np.maximum(logo[..., 3], outline * 235)
    logo[..., :3] = lerp(logo[..., :3], np.clip(gold, 0, 255), m[..., None])
    logo[..., 3] = np.maximum(logo[..., 3], m * 255)
    out = Image.fromarray(np.clip(logo, 0, 255).astype(np.uint8), "RGBA")
    d = ImageDraw.Draw(out)
    sub = "THE PITS OF ANGBAND"
    sb = d.textbbox((0, 0), sub, font=sub_font)
    sx = (LW - (sb[2] - sb[0])) / 2 - sb[0]
    sy = text_bot + 38
    for dx, dy in ((0, 3), (2, 2), (-2, 2)):
        d.text((sx + dx, sy + dy), sub, font=sub_font, fill=(10, 4, 2, 200))
    d.text((sx, sy), sub, font=sub_font, fill=(225, 190, 140, 255))
    # rules either side of the subtitle
    for side in (-1, 1):
        x_in = sx - 24 if side < 0 else sx + (sb[2] - sb[0]) + 24
        x_out = x_in + side * 160
        cy_ = sy + (sb[3] - sb[1]) / 2 + sb[1] + 2
        d.line([(x_in, cy_), (x_out, cy_)], fill=(200, 150, 90, 200), width=2)
        d.ellipse([x_in - 4 * side - 4, cy_ - 4, x_in - 4 * side + 4, cy_ + 4], fill=(230, 180, 110, 230))
    out = out.crop(out.getbbox())
    out.save(a.logo, optimize=True)
    print(f"{a.background}: {W}x{H}; {a.logo}: {out.size[0]}x{out.size[1]}")


if __name__ == "__main__":
    main()
