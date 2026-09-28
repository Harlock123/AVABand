#!/usr/bin/env python3
"""Give every flavour (unknown potions, rings, amulets, wands, staves, rods, mushrooms) a Dungeon
Crawl Stone Soup (CC0) tile.

Flavours already in the tileset keep their tiles. Each other flavour gets the DCSS image that
best fits it: a word of its name in the file name ("Gold" -> golden.png) counts most, then how
close the image's colour is to the flavour's colour, less a penalty for every flavour already
using that image, so flavours look as different from one another as the pack allows. Only the
PNGs used are copied into the tileset folder.

Where the pack still has too few images (60 potion flavours, 43 potion pictures; 35 woods, 10
staves), flavours sharing a picture are told apart by recolouring: the first keeps the original,
each of the others gets a copy under "tinted/" — turned to its own colour when it has one that
differs, otherwise made lighter, darker or a little shifted in hue.

Usage:
    dcss_flavor_tiles.py <DCSS "Full" folder> <data/flavors.json> <data/colors.json> <tilesets/dcss>
"""
import colorsys, json, os, re, shutil, struct, sys, zlib

# Where each flavour class finds its images, under the DCSS "Full" folder.
FOLDERS = {
    "potion": ["item/potion"],
    "ring": ["item/ring"],
    "amulet": ["item/amulet"],
    "wand": ["item/wand"],
    "staff": ["item/staff"],
    "rod": ["item/rod"],
    "mushroom": ["monster/fungi_plants"],
}
SKIP = re.compile(r"(^i-label|^unknown|^artefact|^blank|treant|^plant|briar|thorn|vine|oklob|bush)")
# Name words that point at an image whose file name doesn't say so.
SYNONYMS = {
    "gold": ["golden", "gold"], "golden": ["golden", "gold"], "crimson": ["ruby"], "vermilion": ["ruby"],
    "dark red": ["ruby"], "coagulated crimson": ["ruby"], "green": ["emerald"], "gloopy green": ["murky"],
    "metallic green": ["emerald"], "bubbling": ["bubbly", "fizzy"], "hazy": ["cloudy"], "misty": ["cloudy"],
    "shimmering": ["effervescent"], "muddy": ["murky"], "cerulean": ["sky_blue"], "ultramarine": ["brilliant_blue"],
    "metallic blue": ["brilliant_blue"], "metallic red": ["ruby"], "metallic purple": ["puce"],
    "light blue": ["sky_blue"], "teal": ["cyan"], "light teal": ["cyan"],
    "pungent": ["fizzy"], "iron": ["iron"], "cast iron": ["iron"], "rusty": ["iron", "copper"],
    "steel": ["steel", "iron"], "silver": ["silver"], "mithril": ["silver", "glass"], "platinum": ["silver"],
    "tiger eye": ["tiger_eye"], "quartz": ["glass", "crystal"], "quartzite": ["granite"], "marble": ["granite"],
    "obsidian": ["plain_black", "black"], "jet": ["plain_black", "black"], "lapis lazuli": ["sapphire", "blue"],
    "azurite": ["sapphire", "blue"], "carnelian": ["plain_red", "red"], "jasper": ["plain_red", "red"],
    "rhodonite": ["ring_gold_magenta"], "corundum": ["ruby"], "hematite": ["iron"], "zircon": ["diamond"],
    "amber": ["topaz", "yellow"], "jewelled": ["gold_red", "gold_blue"], "adamantite": ["steel"],
    "sea shell": ["pearl", "coral"], "mother-of-pearl": ["pearl"], "tortoise shell": ["penta_orange"],
    "dragon tooth": ["bone"], "flint stone": ["stone"], "carved oak": ["cylinder", "wood"],
    "ivory": ["ivory", "bone"], "pewter": ["lead", "silver"], "tin": ["lead"], "zinc": ["lead"],
    "aluminium": ["plastic", "silver"], "nickel": ["silver"], "chromium": ["silver"], "white gold": ["gold"],
    "engraved gold": ["gold"], "silver-gilt": ["silver"], "polished steel": ["steel"],
}
REUSE_PENALTY = 60   # colour-distance points a second use of an image costs
KEYWORD_BONUS = 400  # a name match beats any colour difference


def png_colour(path):
    """Average colour of the opaque, coloured pixels of a PNG (RGB, RGBA or palette, 8-bit)."""
    read = png_read(path)
    if read is None:
        return None
    _, _, rgba = read
    pixels = [(r, g, b) for r, g, b, a in rgba if a > 128]
    if not pixels:
        return None
    # The colour that makes a potion or gem what it is: its saturated pixels, if it has enough.
    vivid = [p for p in pixels if colorsys.rgb_to_hsv(*(v / 255 for v in p))[1] > 0.35 and max(p) > 40]
    use = vivid if len(vivid) > len(pixels) // 8 else pixels
    return tuple(sum(p[k] for p in use) / len(use) for k in range(3))


def png_read(path):
    """(width, height, [(r, g, b, a)...]) of an 8-bit PNG (RGB, RGBA, grey or palette); None otherwise."""
    data = open(path, "rb").read()
    i, idat, palette, trns = 8, b"", None, None
    while i < len(data):
        n, kind = struct.unpack(">I4s", data[i:i + 8])
        chunk = data[i + 8:i + 8 + n]
        if kind == b"IHDR":
            w, h, depth, ctype = struct.unpack(">IIBB", chunk[:10])
        elif kind == b"PLTE":
            palette = [tuple(chunk[j:j + 3]) for j in range(0, len(chunk), 3)]
        elif kind == b"tRNS":
            trns = chunk
        elif kind == b"IDAT":
            idat += chunk
        i += 12 + n
    if depth != 8:
        return None
    bpp = {6: 4, 2: 3, 3: 1, 4: 2, 0: 1}[ctype]
    raw, stride, prev, o, pixels = zlib.decompress(idat), w * bpp, bytearray(w * bpp), 0, []
    for _ in range(h):
        f, line = raw[o], bytearray(raw[o + 1:o + 1 + stride])
        o += 1 + stride
        for x in range(stride):
            a = line[x - bpp] if x >= bpp else 0
            b = prev[x]
            c = prev[x - bpp] if x >= bpp else 0
            if f == 1: line[x] = (line[x] + a) & 255
            elif f == 2: line[x] = (line[x] + b) & 255
            elif f == 3: line[x] = (line[x] + (a + b) // 2) & 255
            elif f == 4:
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        for x in range(0, stride, bpp):
            if ctype == 6: r, g, b_, al = line[x:x + 4]
            elif ctype == 2: (r, g, b_), al = line[x:x + 3], 255
            elif ctype == 3:
                r, g, b_ = palette[line[x]]
                al = trns[line[x]] if trns and line[x] < len(trns) else 255
            elif ctype == 4: r = g = b_ = line[x]; al = line[x + 1]
            else: r = g = b_ = line[x]; al = 255
            pixels.append((r, g, b_, al))
        prev = line
    return w, h, pixels


def png_write(path, w, h, pixels):
    """Writes RGBA pixels as a PNG."""
    raw = b"".join(b"\0" + bytes(v for p in pixels[y * w:(y + 1) * w] for v in p) for y in range(h))

    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)

    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


# (hue turn in degrees, brightness factor) for flavours whose own colour doesn't set them apart.
VARIANTS = [(0, 0.7), (0, 1.3), (25, 1.0), (-25, 1.0), (25, 0.75), (-25, 1.25), (50, 1.0), (-50, 1.0), (50, 0.8), (-50, 1.2)]


def tint(src_path, dest_path, target, variant):
    """A recoloured copy: its coloured pixels turned to the target's hue, or else varied by VARIANTS."""
    w, h, pixels = png_read(src_path)
    th, ts, _ = colorsys.rgb_to_hsv(*(v / 255 for v in target))
    base = png_colour(src_path)
    bh = colorsys.rgb_to_hsv(*(v / 255 for v in base))[0]
    hue_gap = min(abs(th - bh), 1 - abs(th - bh)) * 360
    to_target = ts >= 0.25 and hue_gap > 20
    turn, bright = (0, 1.0) if to_target else VARIANTS[variant % len(VARIANTS)]
    out = []
    for r, g, b, a in pixels:
        hh, ss, vv = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
        if a > 0 and ss > 0.2:
            hh = th if to_target else (hh + turn / 360) % 1.0
        vv = min(1.0, vv * bright)
        r2, g2, b2 = colorsys.hsv_to_rgb(hh, ss, vv)
        out.append((round(r2 * 255), round(g2 * 255), round(b2 * 255), a))
    png_write(dest_path, w, h, out)


def distance(a, b):
    return sum((x - y) ** 2 for x, y in zip(a, b)) ** 0.5


def main():
    src, flavors_path, colors_path, tileset_dir = sys.argv[1:5]
    palette = {k: tuple(int(v[i:i + 2], 16) for i in (1, 3, 5)) for k, v in json.load(open(colors_path)).items()}
    manifest_path = os.path.join(tileset_dir, "tileset.json")
    manifest = json.load(open(manifest_path, encoding="utf-8"))
    tiles = manifest["tiles"]
    added = {}

    for group in json.load(open(flavors_path, encoding="utf-8")):
        cls, flavours = group["id"], group.get("flavors", [])
        if cls not in FOLDERS or not flavours:
            continue
        images = {}
        for folder in FOLDERS[cls]:
            for f in sorted(os.listdir(os.path.join(src, folder))):
                if f.endswith(".png") and not SKIP.search(f):
                    rel = f"{folder}/{f}"
                    colour = png_colour(os.path.join(src, rel))
                    if colour:
                        images[rel] = colour
        uses = {rel: 0 for rel in images}
        for fl in flavours:
            if (existing := tiles.get(f"flavor:{cls}:{fl['name'].lower()}")) in uses:
                uses[existing] += 1
        for fl in flavours:
            key = f"flavor:{cls}:{fl['name'].lower()}"
            if key in tiles:
                continue
            name = fl["name"].lower()
            words = SYNONYMS.get(name, []) + [w.replace("-", "_") for w in re.split(r"[ ]", name)]
            target = palette.get(fl.get("color", ""), (128, 128, 128))

            def score(rel):
                stem = os.path.basename(rel)[:-4]
                bonus = KEYWORD_BONUS if any(re.search(rf"(^|_|-){re.escape(w)}(_|-|$)", stem) for w in words) else 0
                return distance(images[rel], target) + REUSE_PENALTY * uses[rel] - bonus

            best = min(sorted(images), key=score)
            uses[best] += 1
            tiles[key] = best
            added[key] = best

    # Flavours of a kind still sharing a picture: the first keeps it, the others get tinted copies.
    tinted = 0
    for group in json.load(open(flavors_path, encoding="utf-8")):
        cls = group["id"]
        if cls not in FOLDERS:
            continue
        by_image = {}
        for fl in group.get("flavors", []):
            key = f"flavor:{cls}:{fl['name'].lower()}"
            if isinstance(tiles.get(key), str) and "/tinted/" not in tiles[key]:
                by_image.setdefault(tiles[key], []).append(fl)
        for rel, sharing in by_image.items():
            for variant, fl in enumerate(sharing[1:]):
                slug = re.sub(r"[^a-z0-9]+", "_", fl["name"].lower()).strip("_")
                tinted_rel = f"{os.path.dirname(rel)}/tinted/{cls}_{slug}.png"
                source = os.path.join(src, rel) if os.path.exists(os.path.join(src, rel)) else os.path.join(tileset_dir, rel)
                tint(source, os.path.join(tileset_dir, tinted_rel),
                     palette.get(fl.get("color", ""), (128, 128, 128)), variant)
                tiles[f"flavor:{cls}:{fl['name'].lower()}"] = tinted_rel
                added.pop(f"flavor:{cls}:{fl['name'].lower()}", None)
                tinted += 1
    print(f"{tinted} flavours given tinted copies")

    for rel in sorted(set(added.values())):
        dest = os.path.join(tileset_dir, rel)
        if not os.path.exists(dest):
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            shutil.copyfile(os.path.join(src, rel), dest)
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"{len(added)} flavours given tiles; {len(set(added.values()))} images")
    for k, v in added.items():
        print(f"  {k} -> {v}")


if __name__ == "__main__":
    main()
