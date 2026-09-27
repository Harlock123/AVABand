#!/usr/bin/env python3
"""Import vaults and interesting rooms from Angband 4.2's vault.txt into AVABand's templates.

Angband's vault symbols are translated to AVABand's template legend (see TemplateLegend.cs). Letters
stay letters: a monster of that glyph. Every part of a template must be reachable from outside,
walking or tunnelling through granite and rubble; ones sealed by permanent rock are skipped.
Angband's data is available under the GPL v2 or the Angband licence.

Usage:
    angband_vault_import.py <vault.txt> <data/templates dir> [--report]
"""
import json, os, re, sys, unicodedata

KINDS = {"Interesting room": ("room", "rooms.json"), "Lesser vault": ("lesser_vault", "vaults.json"),
         "Lesser vault (new)": ("lesser_vault", "vaults.json"), "Medium vault": ("medium_vault", "vaults.json"),
         "Medium vault (new)": ("medium_vault", "vaults.json"), "Greater vault": ("greater_vault", "vaults.json"),
         "Greater vault (new)": ("greater_vault", "vaults.json")}
SYMBOLS = {"%": "%", "#": "#", "@": "X", "*": "#", ":": ";", "`": "~", "/": ".", ";": ".", "&": "*", "+": "+",
           "^": "^", "<": "<", ">": ">", "1": ",", "2": "&", "3": "3", "4": "4", "5": "5", "6": "@", "7": "7",
           "0": "0", "9": "9", "8": "8", "~": "5", "$": "$", "]": "3", "|": "3", "=": "3", '"': "3", "!": "3",
           "?": "3", "_": "3", "-": "3", ",": "3", " ": " ", ".": "."}
TRAVERSABLE = set(".+D^*,&@98$<>:;03457") | {c for c in "abcdefghijklmnopqrstuvwxyzABCEFGHIJKLMNOPQRSTUVWYZ"}
# Granite and rubble can be tunnelled through; only permanent rock ('X') truly seals a vault.
DIGGABLE = TRAVERSABLE | {"#"}


def slug(name):
    plain = unicodedata.normalize("NFKD", name).encode("ascii", "ignore").decode()
    return re.sub(r"[^a-z0-9]+", "_", plain.lower().replace("'", "")).strip("_")


def convert_symbol(c):
    if c in SYMBOLS:
        return SYMBOLS[c]
    if c.isalpha():
        return "&" if c in "DX" else c
    return "."


def connected(rows):
    cells = {(x, y) for y, r in enumerate(rows) for x, c in enumerate(r) if c in DIGGABLE}
    if not cells:
        return False
    start = next(iter(sorted(cells)))
    seen, stack = {start}, [start]
    while stack:
        x, y = stack.pop()
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                n = (x + dx, y + dy)
                if n in cells and n not in seen:
                    seen.add(n)
                    stack.append(n)
    if len(seen) != len(cells):
        return False
    for x, y in cells:
        for dx, dy in ((0, -1), (0, 1), (-1, 0), (1, 0)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < len(rows) and 0 <= nx < len(rows[ny]) and rows[ny][nx] == "%":
                return True
    return False


def parse(path):
    entries, cur = [], None
    for raw in open(path, encoding="utf-8"):
        line = raw.rstrip("\n")
        if line.startswith("#") or not line.strip():
            continue
        key, _, value = line.partition(":")
        if key == "name":
            cur = {"name": value, "D": []}
            entries.append(cur)
        elif cur is not None:
            if key == "D":
                cur["D"].append(value)
            else:
                cur[key] = value
    return entries


def main():
    vault_txt, templates_dir = sys.argv[1:3]
    report = "--report" in sys.argv
    files = {}
    for name in ("rooms.json", "vaults.json"):
        files[name] = json.load(open(os.path.join(templates_dir, name), encoding="utf-8"))
    ids = {t["id"] for f in files.values() for t in f}
    names = {t["name"] for f in files.values() for t in f}
    added, skipped = {n: 0 for n in files}, []
    for e in parse(vault_txt):
        if e.get("type") not in KINDS or e["name"] in names:
            continue
        kind, file = KINDS[e["type"]]
        width = max(len(r) for r in e["D"])
        rows = ["".join(convert_symbol(c) for c in r.ljust(width)) for r in e["D"]]
        if not connected(rows):
            skipped.append(f"{e['type']}: {e['name']}")
            continue
        tid = slug(e["name"]) or "vault"
        n = 2
        while tid in ids:
            tid = f"{slug(e['name'])}_{n}"
            n += 1
        ids.add(tid)
        lo = int(e.get("min-depth", "0") or 0)
        hi = int(e.get("max-depth", "0") or 0)
        template = {"id": tid, "name": e["name"], "kind": kind}
        if lo:
            template["minDepth"] = lo
        if hi:
            template["maxDepth"] = hi
        template["weight"] = 1
        template["rows"] = rows
        files[file].append(template)
        added[file] += 1
    for name, templates in files.items():
        with open(os.path.join(templates_dir, name), "w", encoding="utf-8") as f:
            json.dump(templates, f, indent=2, ensure_ascii=False)
            f.write("\n")
    print(f"added {added}; skipped {len(skipped)} (sealed or not reachable)")
    if report:
        for s in skipped:
            print("  skipped", s)


if __name__ == "__main__":
    main()
