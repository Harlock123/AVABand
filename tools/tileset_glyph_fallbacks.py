#!/usr/bin/env python3
"""Add "monster-glyph:<glyph>" fallbacks to a tileset that maps monsters by Angband name.

For each glyph, the tile of the shallowest non-unique monster with that glyph (that the tileset has art
for) becomes the fallback, so monsters the tileset predates are still drawn as a creature of their kind.

Usage:
    tileset_glyph_fallbacks.py <data/monsters.json> <tileset.json> [...]
"""
import json, sys


def main():
    monsters = sorted(json.load(open(sys.argv[1], encoding="utf-8")), key=lambda m: (m["depth"], m["id"]))
    for path in sys.argv[2:]:
        manifest = json.load(open(path, encoding="utf-8"))
        tiles = manifest["tiles"]
        added = 0
        for unique_ok in (False, True):
            for m in monsters:
                key = "monster-glyph:" + m["glyph"]
                if key in tiles or ("UNIQUE" in m.get("flags", []) and not unique_ok):
                    continue
                art = tiles.get("monster:" + m["id"]) or tiles.get("monster-name:" + m["name"].lower())
                if art is not None:
                    tiles[key] = art
                    added += 1
        with open(path, "w", encoding="utf-8") as f:
            json.dump(manifest, f, indent=2, ensure_ascii=False)
            f.write("\n")
        print(path, "glyph fallbacks:", added)


if __name__ == "__main__":
    main()
