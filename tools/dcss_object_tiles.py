#!/usr/bin/env python3
"""Give AVABand's object kinds and bases Dungeon Crawl Stone Soup (CC0) item tiles.

Kinds without art get the tile PATHS names for them, else an exact-name match from DCSS's item
folder where one exists (a Katana, a Blue Dragon Scale Mail...); new bases get a representative
tile. Only used PNGs are copied.

Usage:
    dcss_object_tiles.py <DCSS "Full" folder> <data/objects.json> <tilesets/dcss>
"""
import json, os, re, shutil, sys

BASES = {
    "crown": "item/armor/headgear/helmet_art_1.png",
    "dragon_armour": "item/armor/torso/gold_dragon_armor_new.png",
    "mushroom": "monster/fungi_plants/wandering_mushroom_new.png",
    "wand": "item/wand/gem_bronze_new.png",
    "staff": "item/staff/staff_0.png",
    "rod": "item/rod/rod_0_new.png",
    "digger": "player/hand_right/pick_axe.png",
    "chest": "dungeon/chest_2_closed.png",
}
ALIASES = {  # AVABand kind id -> DCSS item name
    "black_dragon_scale_mail": "shadow_dragon_scale_mail", "white_dragon_scale_mail": "ice_dragon_scale_mail",
    "red_dragon_scale_mail": "fire_dragon_scale_mail", "multi_hued_dragon_scale_mail": "pearl_dragon_scale_mail",
    "gold_dragon_scale_mail": "gold_dragon_armor", "executioners_sword": "executioners_axe",
    "two_handed_great_flail": "great_mace", "great_axe": "war_axe", "heavy_crossbow": "arbalest",
    "robe": "robe_1", "quarterstaff": "quarterstaff", "lance": "lance",
}
# Kinds DCSS has no tile of the same name for, and the tile that suits them (path under the "Full" folder).
PATHS = {
    # Weapons.
    "tulwar": "item/weapon/scimitar_2.png", "executioners_sword": "item/weapon/greatsword_2.png",
    "blade_of_chaos": "item/weapon/demon_blade.png", "ball_and_chain": "item/weapon/dire_flail_1.png",
    "two_handed_great_flail": "item/weapon/great_flail_1.png", "throwing_hammer": "item/weapon/hammer_3.png",
    "maul": "item/weapon/mace_large_1_new.png", "great_hammer": "item/weapon/hammer_2_new.png",
    "mace_of_disruption": "item/weapon/mace_large_3.png", "mighty_hammer": "item/weapon/war_hammer.png",
    "throwing_axe": "item/weapon/ranged/tomahawk_1.png", "beaked_axe": "item/weapon/broad_axe_2.png",
    "lochaber_axe": "item/weapon/bardiche_2.png", "lance": "item/weapon/spear_5.png",
    "scythe_of_slicing": "item/weapon/scythe_3.png", "lucerne_hammer": "item/weapon/lucern_hammer.png",
    # Ammunition.
    "seeker_arrow": "item/weapon/ranged/elven_arrow.png", "mithril_arrow": "item/weapon/ranged/silver_arrow.png",
    "rounded_pebble": "item/weapon/ranged/stone_new.png", "mithril_shot": "item/weapon/ranged/sling_bullet_2_new.png",
    # Armour.
    "pair_of_iron_shod_boots": "item/armor/feet/boots_iron_2.png",
    "pair_of_steel_shod_boots": "item/armor/feet/boots_2_jackboots.png",
    "pair_of_mithril_shod_boots": "item/armor/feet/boots_3_stripe_new.png",
    "pair_of_ethereal_slippers": "item/armor/feet/low_boots.png",
    "steel_helm": "item/armor/headgear/helmet_2.png", "iron_crown": "item/armor/headgear/helmet_ego_1.png",
    "golden_crown": "item/armor/headgear/helmet_art_1.png",
    "jewel_encrusted_crown": "item/armor/headgear/helmet_art_3.png",
    "massive_iron_crown": "item/armor/headgear/helmet_art_2.png",
    "robe": "item/armor/torso/robe_1_new.png", "studded_leather_armour": "item/armor/torso/studded_leather_armor.png",
    "soft_armour_leather_scale_mail": "item/armor/torso/leather_armor_3.png",
    "augmented_chain_mail": "item/armor/torso/chain_mail_3.png", "bar_chain_mail": "item/armor/torso/chain_mail_2.png",
    "metal_brigandine_armour": "item/armor/torso/splint_mail_1.png",
    "partial_plate_armour": "item/armor/torso/plate_1.png",
    "metal_lamellar_armour": "item/armor/torso/banded_mail_2.png",
    "full_plate_armour": "item/armor/torso/plate_mail_1.png", "ribbed_plate_armour": "item/armor/torso/plate_mail_2.png",
    "mithril_chain_mail": "item/armor/torso/elven_ringmail.png",
    "mithril_plate_mail": "item/armor/torso/crystal_plate_mail.png",
    "adamantite_plate_mail": "item/armor/torso/orcish_platemail.png",
    "elven_cloak": "item/armor/back/cloak_3.png", "ethereal_cloak": "item/armor/back/cloak_4.png",
    "set_of_mithril_gauntlets": "item/armor/hands/gauntlet_1.png",
    "set_of_caestus": "item/armor/hands/glove_4_gauntlets.png",
    "set_of_alchemists_gloves": "item/armor/hands/glove_3_new.png",
    "small_metal_shield": "item/armor/shields/buckler_2_new.png",
    "large_metal_shield": "item/armor/shields/large_shield_2_new.png",
    "knights_shield": "item/armor/shields/shield_2_kite.png", "mithril_shield": "item/armor/shields/shield_1_elven.png",
    "white_dragon_scale_mail": "item/armor/torso/ice_dragon_armor_new.png",
    "red_dragon_scale_mail": "item/armor/torso/mottled_dragon_armor_new.png",
    "multi_hued_dragon_scale_mail": "item/armor/torso/pearl_dragon_armor.png",
    "shining_dragon_scale_mail": "item/armor/torso/shimmering_dragon_scales.png",
    "law_dragon_scale_mail": "item/armor/torso/silver_dragon_scale_mail_new.png",
    "chaos_dragon_scale_mail": "item/armor/torso/mottled_dragon_hide_new.png",
    "balance_dragon_scale_mail": "item/armor/torso/quicksilver_dragon_scale_mail.png",
    "power_dragon_scale_mail": "item/armor/torso/gold_dragon_armor_old.png",
    # Food and drink.
    "handful_of_dried_fruits": "item/food/sultana.png", "scrap_of_flesh": "item/food/chunk.png",
    "slice_of_meat": "item/food/meat_ration_new.png", "honey_cake": "item/food/honeycomb_new.png",
    "piece_of_elvish_waybread": "item/food/piece_of_ambrosia_new.png",
    "flask_of_whisky": "item/misc/misc_bottle.png", "pint_of_fine_wine": "item/misc/misc_bottle.png",
    "sip_of_miruvor": "item/misc/misc_bottle.png", "swig_of_orcish_liquor": "item/misc/misc_bottle.png",
    "draught_of_the_ents": "item/misc/misc_bottle.png",
    # Lights.
    "star": "item/misc/misc_crystal_new.png", "arkenstone": "item/misc/misc_stone_new.png",
    # Books: each realm's five in its own colours (the base tile's family).
    "first_spells": "item/book/red_new.png", "attacks_and_knowledge": "item/book/red_old.png",
    "magical_defences": "item/book/pink.png", "arcane_control": "item/book/magenta_new.png",
    "wizards_tome_of_power": "item/book/book_orange.png",
    "novices_handbook": "item/book/light_green_new.png", "cleansing_power": "item/book/light_green_old.png",
    "healing_and_sanctuary": "item/book/white_new.png", "battle_blessings": "item/book/light_gray_new.png",
    "wrath_of_the_valar": "item/book/gold.png",
    "lesser_charms": "item/book/dark_brown_new.png", "gifts_of_nature": "item/book/dark_brown_old.png",
    "creature_dominion": "item/book/light_brown_new.png", "nature_craft": "item/book/tan_new.png",
    "wild_forces": "item/book/leather_new.png",
    "into_the_shadows": "item/book/purple_new.png", "dark_rituals": "item/book/purple_old.png",
    "fear_and_torment": "item/book/dark_gray_new.png", "deadly_powers": "item/book/book_indigo.png",
    "corruption_of_spirit": "item/book/book_of_the_dead_new.png",
}


def main():
    src, objects_path, tileset_dir = sys.argv[1:4]
    files = {}
    for root, _, fs in os.walk(os.path.join(src, "item")):
        for f in sorted(fs):
            if f.endswith(".png"):
                base = re.sub(r"(_new|_old)?\.png$", "", f)
                base = re.sub(r"_\d+$", "", base)
                files.setdefault(base, os.path.relpath(os.path.join(root, f), src))
    manifest_path = os.path.join(tileset_dir, "tileset.json")
    manifest = json.load(open(manifest_path, encoding="utf-8"))
    tiles = manifest["tiles"]
    used = set()

    def ref(rel):
        if not os.path.exists(os.path.join(src, rel)):
            raise SystemExit(f"missing DCSS tile: {rel}")
        used.add(rel)
        return rel

    for base, rel in BASES.items():
        tiles.setdefault(f"object-base:{base}", ref(rel))
    matched = 0
    for kind in json.load(open(objects_path, encoding="utf-8")):
        key = f"object:{kind['id']}"
        if key in tiles:
            continue
        if kind["id"] in PATHS:
            tiles[key] = ref(PATHS[kind["id"]])
            matched += 1
            continue
        name = ALIASES.get(kind["id"], kind["id"])
        if name in files:
            tiles[key] = ref(files[name])
            matched += 1
    for rel in sorted(used):
        dest = os.path.join(tileset_dir, rel)
        if not os.path.exists(dest):
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            shutil.copyfile(os.path.join(src, rel), dest)
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"{matched} kinds matched; {len(used)} images")


if __name__ == "__main__":
    main()
