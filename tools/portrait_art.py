#!/usr/bin/env python3
"""Portraits for the character creation screen: every race in every class (AVABand's multiclass pairs
too), composed from Dungeon Crawl Stone Soup's player "paper doll" tiles (CC0) — a race's base body,
its hair or beard, and a class's outfit (armour, robes, helm or hat, cloak, boots, what it holds) —
layered in DCSS's own doll order and scaled up without smoothing. Written as
art/portraits/<race>_<class>.png (and <race>.png, the race alone, for the race list).

Needs the DCSS "Full" tile pack (its player/ folder) and Pillow.

Usage:
    portrait_art.py <DCSS "Full" folder> <out folder, e.g. src/Angband.Avalonia/art/portraits> [--sheet sheet.png]
"""
import os
import sys

from PIL import Image

SCALE = 4  # 32x32 tiles, shown at 128x128

# Race: base body, then any hair or beard (DCSS player/base, player/hair, player/beard).
RACES = {
    "human": ["base/human_male", "hair/short_black"],
    "half_elf": ["base/human_male", "hair/elf_black"],
    "elf": ["base/elf_male", "hair/elf_yellow"],
    "hobbit": ["base/halfling_male", "hair/frodo"],
    "gnome": ["base/gnome_male"],
    "dwarf": ["base/dwarf_male", "beard/long_red"],
    "half_orc": ["base/orc_male"],
    "half_troll": ["base/troll_male"],
    "dunadan": ["base/human_male", "hair/aragorn", "beard/short_black"],
    "high_elf": ["base/elf_male", "hair/elf_white"],
    "kobold": ["base/kobold_male_new"],
}

# Class outfits, by doll part (each a DCSS player/<part>/<name>, or None).
CLASSES = {
    "warrior": dict(cloak=None, boots="middle_gray", legs="metal_gray", body="plate", gloves="gauntlet_blue",
                    hand1="long_sword_slant_new", hand2="shield_kite_1", helm=None),
    "mage": dict(cloak="blue", boots="short_purple", legs=None, body="robe_blue", gloves=None,
                 hand1="staff_mage", hand2=None, helm="wizard_blue"),
    "priest": dict(cloak="white", boots="short_brown", legs=None, body="robe_white", gloves=None,
                   hand1="mace_new", hand2="misc/book_white", helm="healer"),
    "druid": dict(cloak="green", boots="middle_brown", legs=None, body="robe_green", gloves=None,
                  hand1="staff_organic", hand2=None, helm="hood_green"),
    "necromancer": dict(cloak="black", boots="mesh_black", legs=None, body="robe_black_red", gloves="glove_black",
                        hand1="staff_skull", hand2=None, helm="hood_black_2"),
    "rogue": dict(cloak="gray", boots="short_brown", legs="pants_black", body="leather_short", gloves="glove_black",
                  hand1="dagger_slant_new", hand2=None, helm="bandana_ybrown"),
    "ranger": dict(cloak="green", boots="middle_brown", legs="pants_darkgreen", body="leather_green", gloves="glove_brown",
                   hand1="bow", hand2=None, helm="hood_green_2"),
    "paladin": dict(cloak="white", boots="middle_gold", legs="metal_gray", body="armor_blue_gold", gloves="glove_gold",
                    hand1="mace_ruby_new", hand2="shield_holy", helm=None),
    "blackguard": dict(cloak="red", boots="mesh_black", legs="pants_black", body="breast_black", gloves="glove_black_2",
                       hand1="axe_executioner_new", hand2="shield_skull", helm="horn_evil"),
    # AVABand's multiclasses: the warrior's armour and blade, the caster's book, hat and cloak.
    "warrior_mage": dict(cloak="blue", boots="middle_gray", legs="metal_gray", body="chainmail", gloves="gauntlet_blue",
                         hand1="long_sword_slant_new", hand2="misc/book_blue", helm="wizard_blue"),
    "warrior_priest": dict(cloak="white", boots="middle_gray", legs="metal_gray", body="plate", gloves="gauntlet_blue",
                           hand1="mace_new", hand2="misc/book_white", helm=None),
    "warrior_druid": dict(cloak="green", boots="middle_brown", legs="pants_darkgreen", body="scalemail", gloves="glove_brown",
                          hand1="long_sword_slant_new", hand2="misc/book_green", helm="hood_green"),
    "warrior_necromancer": dict(cloak="black", boots="mesh_black", legs="pants_black", body="breast_black", gloves="glove_black",
                                hand1="black_sword", hand2="misc/book_black", helm="hood_black_2"),
}

# The race alone (the race list): plain clothes.
COMMONER = dict(boots="short_brown", legs="pants_brown", body="shirt_white_1")

# DCSS's doll order (tiledoll: cloak behind the body; then boots, legs, body, arm, hands, hair, beard, helm; halo first).
ORDER = ["halo", "cloak", "base", "boots", "legs", "body", "gloves", "hand1", "hand2", "hair", "beard", "helm"]
FOLDER = {"hand1": "hand_right", "hand2": "hand_left", "helm": "head"}


def load(root, part, name):
    if name is None:
        return None
    path = os.path.join(root, "player", FOLDER.get(part, part), name + ".png")
    if not os.path.exists(path):
        raise SystemExit(f"missing tile: {path}")
    return Image.open(path).convert("RGBA")


def compose(root, race, cls):
    layers = {}
    for spec in RACES[race]:
        part, name = spec.split("/", 1)
        layers["base" if part == "base" else part] = load(root, part, name)
    for part, name in (COMMONER if cls is None else CLASSES[cls]).items():
        if name is not None:
            layers[part] = load(root, part, name)
    # A helm, hat or hood hides the hair (as DCSS draws it).
    if cls is not None and CLASSES[cls].get("helm"):
        layers.pop("hair", None)
    image = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    for part in ORDER:
        if part in layers:
            image.alpha_composite(layers[part].crop((0, 0, 32, 32)))
    return image.resize((32 * SCALE, 32 * SCALE), Image.NEAREST)


def main():
    root, out = sys.argv[1], sys.argv[2]
    sheet_path = sys.argv[sys.argv.index("--sheet") + 1] if "--sheet" in sys.argv else None
    os.makedirs(out, exist_ok=True)
    classes = [None] + list(CLASSES)
    sheet = Image.new("RGBA", (len(classes) * 128, len(RACES) * 128), (24, 24, 30, 255)) if sheet_path else None
    for y, race in enumerate(RACES):
        for x, cls in enumerate(classes):
            image = compose(root, race, cls)
            image.save(os.path.join(out, f"{race}.png" if cls is None else f"{race}_{cls}.png"), optimize=True)
            if sheet is not None:
                sheet.alpha_composite(image, (x * 128, y * 128))
    if sheet is not None:
        sheet.save(sheet_path)
    print(f"{len(RACES)} races x {len(classes)} (the race alone and {len(CLASSES)} classes) written to {out}")


if __name__ == "__main__":
    main()
