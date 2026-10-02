#!/usr/bin/env python3
"""The Grimoire's pictures (Help -> Grimoire): an icon for every spell, a cover for every spellbook and a
sigil for every realm, taken from Dungeon Crawl Stone Soup's CC0 tiles (gui/spells, gui/invocations,
item/book, monster/...) and scaled up without smoothing; and the parchment the pages are printed on,
drawn here (no source image). Written as

    art/grimoire/spells/<spell name, lower case, words joined by _>.png   (64x64)
    art/grimoire/books/<book id>.png                                      (96x96)
    art/grimoire/realms/<realm id>.png                                    (64x64)
    art/grimoire/parchment.png                                            (512x512, tiles)

Needs the DCSS "Full" tile pack and Pillow. Every spell in the game's spells.json must have an icon
here (the tool stops and names any that don't; a test checks the app's copy too).

Usage:
    grimoire_art.py <DCSS "Full" folder> <out folder, e.g. src/Angband.Avalonia/art/grimoire> [<spells.json>]
"""
import json
import os
import random
import re
import sys

from PIL import Image, ImageDraw, ImageFilter

S = "gui/spells/"
INV = "gui/invocations/"

# Each spell (by its name, as the books print it): the DCSS picture that suits it best.
SPELLS = {
    # Arcane (mage, rogue)
    "Magic Missile": S + "conjuration/magic_dart.png",
    "Light Room": S + "enchantment/corona.png",
    "Find Traps, Doors & Stairs": S + "divination/detect_traps.png",
    "Phase Door": S + "translocation/blink.png",
    "Electric Arc": S + "air/shock_new.png",
    "Detect Monsters": S + "divination/detect_creatures.png",
    "Fire Ball": S + "fire/fireball_new.png",
    "Object Detection": S + "divination/detect_items.png",
    "Detect Stairs": S + "divination/detect_secret_doors.png",
    "Recharging": S + "enchantment/infusion.png",
    "Reveal Monsters": S + "enchantment/see_invisible.png",
    "Identify Rune": S + "divination/identify.png",
    "Treasure Detection": S + "divination/forescry.png",
    "Frost Bolt": S + "ice/bolt_of_cold_new.png",
    "Acid Spray": S + "poison/mephitic_cloud_new.png",
    "Disable Traps, Destroy Doors": S + "earth/shatter_new.png",
    "Teleport Self": S + "translocation/teleport.png",
    "Teleport Other": S + "translocation/teleport_other_new.png",
    "Resistance": S + "air/insulation.png",
    "Tap Magical Energy": S + "necromancy/sublimation_of_blood_new.png",
    "Mana Channel": S + "enchantment/extension.png",
    "Door Creation": "dungeon/doors/runed_door.png",
    "Mana Bolt": S + "conjuration/iskenderuns_mystic_blast_new.png",
    "Teleport Level": S + "translocation/portal.png",
    "Detection": S + "divination/magic_mapping.png",
    "Dimension Door": S + "translocation/passage_of_golubria.png",
    "Thrust Away": S + "conjuration/force_lance.png",
    "Hit and Run": S + "air/swiftness_new.png",
    "Shock Wave": S + "air/static_discharge_new.png",
    "Explosion": S + "conjuration/fulminant_prism.png",
    "Banishment": S + "translocation/banishment.png",
    "Mass Banishment": S + "summoning/mass_abjuration.png",
    "Mana Storm": S + "conjuration/orb_of_destruction_big.png",
    # Divine (priest, paladin)
    "Call Light": S + "enchantment/corona.png",
    "Detect Evil": S + "divination/detect_curse.png",
    "Minor Healing": INV + "elyvilon_heal_other.png",
    "Bless": INV + "tso_bless_weapon.png",
    "Sense Invisible": S + "enchantment/see_invisible.png",
    "Heroism": S + "enchantment/berserker_rage_new.png",
    "Orb of Draining": S + "conjuration/orb_of_destruction_new.png",
    "Spear of Light": S + "conjuration/searing_ray.png",
    "Dispel Undead": S + "necromancy/dispel_undead_new.png",
    "Dispel Evil": S + "abjuration.png",
    "Protection from Evil": "monster/holy/angel_new.png",
    "Remove Curse": S + "remove_curse.png",
    "Portal": S + "translocation/controlled_blink_new.png",
    "Remembrance": S + "memorise.png",
    "Word of Recall": S + "summoning/recall_new.png",
    "Healing": "monster/holy/cherub.png",
    "Restoration": S + "necromancy/regeneration_new.png",
    "Clairvoyance": S + "divination/magic_mapping.png",
    "Enchant Weapon": S + "enchantment/sure_blade_new.png",
    "Enchant Armour": S + "earth/stoneskin_new.png",
    "Smite Evil": "monster/holy/paladin.png",
    "Glyph of Warding": "item/misc/misc_rune.png",
    "Demon Bane": "monster/holy/daeva.png",
    "Single Combat": S + "enchantment/song_of_slaying.png",
    "Banish Evil": S + "forceful_dismissal.png",
    "Word of Destruction": INV + "ru_apocalypse.png",
    "Holy Word": "monster/holy/seraph_top.png",
    "Spear of Oromë": S + "earth/lehudibs_crystal_spear_new.png",
    "Light of Manwë": "monster/holy/ophan.png",
    # Nature (druid, ranger)
    "Detect Life": S + "summoning/summon_small_mammals.png",
    "Fox Form": "monster/animals/jackal_new.png",
    "Remove Hunger": "item/food/bread_ration_new.png",
    "Stinking Cloud": S + "poison/poisonous_cloud_new.png",
    "Confuse Monster": S + "enchantment/confuse_new.png",
    "Slow Monster": S + "enchantment/slow_new.png",
    "Herbal Curing": S + "poison/cure_poison_new.png",
    "Resist Poison": S + "poison/resist_poison.png",
    "Turn Stone to Mud": S + "earth/ledas_liquefaction.png",
    "Sense Surroundings": S + "divination/magic_mapping.png",
    "Cure Poison": S + "poison/cure_poison_new.png",
    "Lightning Strike": S + "air/lightning_bolt_new.png",
    "Earth Rising": S + "earth/stone_arrow_new.png",
    "Trance": S + "ice/ensorcelled_hibernation_new.png",
    "Mass Sleep": S + "enchantment/mass_confusion_new.png",
    "Become Pukel-man": S + "earth/statue_form_new.png",
    "Eagle's Flight": "monster/griffon.png",
    "Bear Form": "monster/animals/grizzly_bear.png",
    "Tremor": S + "earth/lees_rapid_deconstruction_new.png",
    "Haste Self": S + "enchantment/haste_new.png",
    "Revitalize": S + "necromancy/regeneration_new.png",
    "Rapid Regeneration": INV + "elyvilon_heal_other.png",
    "Cover Tracks": S + "air/silence_new.png",
    "Create Arrows": "item/weapon/ranged/elven_arrow.png",
    "Decoy": S + "enchantment/projected_noise.png",
    "Brand Ammunition": S + "poison/poison_ammunition.png",
    "Meteor Swarm": S + "fire/fire_storm_new.png",
    "Rift": S + "translocation/gravitas.png",
    "Ice Storm": S + "ice/ice_storm_new.png",
    "Volcanic Eruption": S + "fire/bolt_of_magma_new.png",
    "River of Lightning": S + "air/chain_lightning_new.png",
    # Necromantic (necromancer, blackguard)
    "Nether Bolt": S + "necromancy/bolt_of_draining_new.png",
    "Create Darkness": S + "enchantment/darkness.png",
    "Bat Form": "monster/animals/bat.png",
    "Read Minds": S + "necromancy/haunt_new.png",
    "Seek Battle": S + "enchantment/cause_fear_new.png",
    "Berserk Strength": S + "enchantment/berserker_rage_new.png",
    "Whirlwind Attack": S + "air/tornado.png",
    "Shatter Stone": S + "earth/shatter_new.png",
    "Leap into Battle": "gui/abilities/jump.png",
    "Grim Purpose": S + "necromancy/deaths_door_new.png",
    "Tap Unlife": S + "necromancy/vampiric_draining_new.png",
    "Crush": S + "earth/maxwells_silver_hammer.png",
    "Sleep Evil": S + "ice/ensorcelled_hibernation_new.png",
    "Shadow Shift": INV + "dithmenos_shadow_step.png",
    "Disenchant": S + "translocation/disjunction.png",
    "Frighten": S + "enchantment/cause_fear_new.png",
    "Vampire Strike": S + "necromancy/vampiric_draining_new.png",
    "Dispel Life": S + "necromancy/pain_new.png",
    "Dark Spear": S + "necromancy/agony_new.png",
    "Warg Form": "monster/animals/warg.png",
    "Maim Foe": S + "necromancy/excruciating_wounds_new.png",
    "Howl of the Damned": S + "necromancy/symbol_of_torment_new.png",
    "Venom": S + "poison/poison_brand.png",
    "Werewolf Form": "monster/animals/wolf.png",
    "Relentless Taunting": S + "enchantment/discord.png",
    "Banish Spirits": S + "summoning/abjuration.png",
    "Annihilate": S + "necromancy/death_channel_new.png",
    "Grond's Blow": INV + "qazlal_upheaval.png",
    "Unleash Chaos": INV + "qazlal_elemental_force.png",
    "Fume of Mordor": S + "poison/olgrebs_toxic_radiance_new.png",
    "Storm of Darkness": S + "summoning/summon_shadow_creatures_new.png",
    "Unholy Reprieve": S + "necromancy/borgnjors_revivification_new.png",
    "Forceful Blow": S + "earth/iron_shot_new.png",
    "Quake": INV + "qazlal_disaster_area.png",
    "Bloodlust": INV + "ru_sacrifice_health.png",
    "Power Sacrifice": INV + "ru_sacrifice_essence.png",
    "Zone of Unmagic": INV + "ru_sacrifice_arcana.png",
    "Vampire Form": "monster/undead/vampire_new.png",
    "Curse": S + "necromancy/cigotuvis_degeneration.png",
    "Command": S + "necromancy/control_undead_new.png",
}

# Each book's cover (DCSS item/book), coloured by realm: blues for the arcane, golds for the divine,
# greens and browns for nature, the darkest for the necromantic.
BOOKS = {
    "first_spells": "light_blue_new", "attacks_and_knowledge": "cyan_new", "magical_defences": "metal_blue_new",
    "arcane_control": "dark_blue_new", "wizards_tome_of_power": "glittering",
    "novices_handbook": "white_new", "cleansing_power": "gold", "healing_and_sanctuary": "silver",
    "battle_blessings": "bronze", "wrath_of_the_valar": "yellow_new",
    "lesser_charms": "light_green_new", "gifts_of_nature": "tan_new", "creature_dominion": "dark_green_new",
    "nature_craft": "light_brown_new", "wild_forces": "metal_green_new",
    "into_the_shadows": "dark_gray_new", "dark_rituals": "purple_new", "fear_and_torment": "red_new",
    "deadly_powers": "book_of_the_dead_new", "corruption_of_spirit": "magenta_new",
}

# Each realm's sigil.
REALMS = {
    "arcane": S + "conjuration/orb_of_destruction_new.png",
    "divine": "monster/holy/angel_new.png",
    "nature": S + "summoning/summon_forest.png",
    "shadow": S + "necromancy/symbol_of_torment_new.png",
}


def slug(name):
    return re.sub(r"[^a-z0-9]+", "_", name.lower().replace("ë", "e")).strip("_")


def scaled(path, scale):
    im = Image.open(path).convert("RGBA")
    return im.resize((im.width * scale, im.height * scale), Image.NEAREST)


def parchment(size=512, seed=7):
    """Warm, mottled paper that tiles (its noise wraps around the edges)."""
    rnd = random.Random(seed)
    base = Image.new("RGB", (size, size), (226, 208, 168))
    noise = Image.new("L", (size, size))
    px = noise.load()
    for y in range(size):
        for x in range(size):
            px[x, y] = rnd.randint(0, 255)
    # Blur with wrap-around: tile 3x3, blur, cut the middle out.
    big = Image.new("L", (size * 3, size * 3))
    for i in range(3):
        for j in range(3):
            big.paste(noise, (i * size, j * size))
    soft = big.filter(ImageFilter.GaussianBlur(18)).crop((size, size, size * 2, size * 2))
    fine = big.filter(ImageFilter.GaussianBlur(1.2)).crop((size, size, size * 2, size * 2))
    out = base.load()
    sp, fp = soft.load(), fine.load()
    for y in range(size):
        for x in range(size):
            d = (sp[x, y] - 128) * 0.9 + (fp[x, y] - 128) * 0.12
            r, g, b = out[x, y]
            out[x, y] = (max(0, min(255, int(r + d))), max(0, min(255, int(g + d * 0.95))), max(0, min(255, int(b + d * 0.8))))
    # A few faint fibres.
    draw = ImageDraw.Draw(base, "RGBA")
    for _ in range(140):
        x, y = rnd.randrange(size), rnd.randrange(size)
        length, angle = rnd.randint(6, 26), rnd.uniform(0, 3.14)
        import math
        x2, y2 = x + length * math.cos(angle), y + length * math.sin(angle)
        draw.line([(x, y), (x2, y2)], fill=(120, 90, 50, 26), width=1)
    return base


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    src, out = sys.argv[1], sys.argv[2]
    spells_json = sys.argv[3] if len(sys.argv) > 3 else os.path.join(os.path.dirname(__file__), "..", "src", "Angband.Data", "data", "spells.json")
    names = {s["name"] for s in json.load(open(spells_json, encoding="utf-8"))}
    missing = sorted(names - SPELLS.keys())
    if missing:
        sys.exit("No icon for: " + ", ".join(missing))
    bad = [p for p in list(SPELLS.values()) + list(REALMS.values()) if not os.path.exists(os.path.join(src, p))]
    bad += [b for b in BOOKS.values() if not os.path.exists(os.path.join(src, "item", "book", b + ".png"))]
    if bad:
        sys.exit("Not in the DCSS folder: " + ", ".join(bad))

    for sub in ("spells", "books", "realms"):
        os.makedirs(os.path.join(out, sub), exist_ok=True)
    for name in sorted(names):
        scaled(os.path.join(src, SPELLS[name]), 2).save(os.path.join(out, "spells", slug(name) + ".png"))
    for book, cover in BOOKS.items():
        scaled(os.path.join(src, "item", "book", cover + ".png"), 3).save(os.path.join(out, "books", book + ".png"))
    for realm, path in REALMS.items():
        scaled(os.path.join(src, path), 2).save(os.path.join(out, "realms", realm + ".png"))
    parchment().save(os.path.join(out, "parchment.png"))
    print(f"{len(names)} spells, {len(BOOKS)} books, {len(REALMS)} realms and the parchment, in {out}")


if __name__ == "__main__":
    main()
