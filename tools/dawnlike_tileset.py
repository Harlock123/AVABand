#!/usr/bin/env python3
"""Build the bundled DawnLike tileset (tilesets/dawnlike) from DawnLike 16x16 v1.81.

DawnLike, by DragonDePlatino with DawnBringer's 16-colour palette, is CC BY 4.0:
https://opengameart.org/content/dawnlike-16x16-universal-rogue-like-tileset-v181
Its sprites come as small sheets with no names; Tommy Ettinger's DawnLikeAtlas (CC BY 4.0,
https://github.com/tommyettinger/DawnLikeAtlas) names every cell in `image_names.tsv`, one block per
sheet, laid out cell for cell. This script reads those names, picks art for AVABand's terrain, traps,
monsters, object kinds, flavours and bases by name and keyword, and packs only the cells it uses into
one sheet, dawnlike.png. Creatures have two animation frames (sheets "…0" and "…1"); frame 0 is used.
Then it runs tileset_glyph_fallbacks.py to add "monster-glyph:" fallbacks.

Stdlib only (PNG reading/writing comes from dcss_flavor_tiles.py).

Usage:
    dawnlike_tileset.py <DawnLike folder (Characters/, Items/, Objects/...)> <image_names.tsv> <AVABand repo root>
"""
import glob, json, os, re, subprocess, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dcss_flavor_tiles import png_read, png_write  # noqa: E402

T = 16
# Section names in image_names.tsv that differ from the sheet file names.
SECTION_ALIASES = {"Boots": "Boot", "Chest_Closed": "Chest0", "Chest_Open": "Chest1", "Decor": "Decor0",
                   "Door_Closed": "Door0", "Door_Open": "Door1", "Ground": "Ground0", "Hill": "Hill0",
                   "Map": "Map0", "Pit": "Pit0", "Quadruped0": "Quadraped0", "Trap": "Trap0", "Tree": "Tree0"}

# ---------------------------------------------------------------- terrain
# (lit, torch-lit, remembered) cells, or a single one; a list layers bottom to top.
FLOOR = {"lit": "day tile floor c", "torch": "morning tile floor c", "dark": "night tile floor c"}


def wall(kind, overlay=None):
    v = {"lit": f"lit {kind} wall flat", "torch": f"bright {kind} wall flat", "dark": f"dark {kind} wall flat"}
    return {k: [n, overlay] for k, n in v.items()} if overlay else v


def on_floor(name):
    return {k: [f, name] for k, f in FLOOR.items()}


TERRAIN = {
    "floor": FLOOR,
    "granite_wall": wall("brick"),
    "permanent_wall": wall("deep"),
    "magma_vein": wall("rock"),
    "quartz_vein": wall("snow"),
    "magma_with_treasure": wall("rock", "yellow ore up"),
    "quartz_with_treasure": wall("snow", "glowing ore up"),
    "rubble": on_floor("boulder"),
    "passable_rubble": on_floor("pile of stones"),
    "lava": "lava tile",
    "closed_door": on_floor("closed wooden door front"),
    "open_door": on_floor("open wooden door front"),
    "broken_door": on_floor("broken wooden door"),
    "up_staircase": "large stairs up",
    "down_staircase": "large stairs down",
    "shop_general": on_floor("restaurant sign"),
    "shop_armoury": on_floor("armory sign"),
    "shop_weaponsmith": on_floor("smithy sign"),
    "shop_bookseller": on_floor("full bookshelves"),
    "shop_alchemist": on_floor("assorted shelves"),
    "shop_magic": on_floor("crystal shelves"),
    "shop_black_market": on_floor("spooky sign"),
    "shop_home": on_floor("home sign"),
}

TRAPS = {
    "glyph_warding": "ward a", "web": "webbing a", "trap_door": "trap door tile", "pit": "pit tile",
    "spiked_pit": "spiked pit tile", "poison_pit": "spiked pit tile", "rune_of_summon_foe": "magic portal tile",
    "summon_rune": "magic portal tile", "rune_of_necromancy": "symbol black", "rune_of_dragonsong": "symbol red",
    "hellhole": "hole tile", "teleport_rune": "teleportation trap tile", "fire_rune": "fire trap tile",
    "acid_rune": "symbol blue", "slow_dart": "dart trap tile", "weakness_dart": "dart trap tile",
    "dexterity_loss_dart": "dart trap tile", "constitution_loss_dart": "dart trap tile",
    "blinding_gas_trap": "sleeping gas trap tile", "confusion_gas": "sleeping gas trap tile",
    "poison_gas": "sleeping gas trap tile", "sleep_gas": "sleeping gas trap tile",
    "aggravation_trap": "squeaky board tile", "alarm": "squeaky board tile", "mine_trap": "land mine tile",
    "blast_trap": "land mine tile", "mind_blasting_trap": "magic trap tile", "brain_smashing_trap": "magic trap tile",
    "rock_fall_trap": "falling rock trap tile", "earthquake_trap": "rolling boulder trap tile",
    "block_fall_trap": "falling rock trap tile", "area_blast_trap": "fire trap tile",
    "blinding_flash_trap": "magic trap tile", "blinding_trap": "polymorph trap tile",
    "mana_drain_trap": "anti magic field tile", "knife_trap": "arrow trap tile", "petrifying_trap": "statue trap tile",
}

# ---------------------------------------------------------------- objects
BASES = {
    "sword": "long sword", "hafted": "mace", "polearm": "long poleaxe", "digger": "pick axe", "sling": "sling",
    "bow": "longbow", "crossbow": "crossbow", "shot": "mid pellets", "arrow": "arrow", "bolt": "crossbow bolt",
    "soft_armour": "lacquered armor", "hard_armour": "iron armor", "shield": "large shield", "helm": "helmet",
    "gloves": "leather glove", "boots": "leather boots", "cloak": "spidersilk cape", "light": "brass lantern",
    "amulet": "jasper amulet", "ring": "gold ring", "magic_book": "fire tome", "prayer_book": "light tome",
    "nature_book": "natural tome", "shadow_book": "dark tome", "potion": "clear potion", "scroll": "white scroll",
    "food": "food ration", "flask": "bottle", "crown": "princely crown", "dragon_armour": "firedrake armor",
    "mushroom": "red cap mushroom", "wand": "glass wand", "staff": "quarterstaff", "rod": "unknown metal tube",
    "chest": "closed chest", "gold": "pile of gold coins",
}
KINDS = {
    # Weapons.
    "dagger": "dagger", "main_gauche": "stiletto", "rapier": "silver saber", "short_sword": "short sword",
    "cutlass": "scimitar", "tulwar": "orcish short sword", "scimitar": "scimitar", "katana": "katana",
    "long_sword": "long sword", "broad_sword": "broadsword", "bastard_sword": "elven broadsword",
    "zweihander": "two handed sword", "executioners_sword": "tsurugi", "blade_of_chaos": "runesword",
    "whip": "bullwhip", "mace": "mace", "war_hammer": "war hammer", "morning_star": "morning star", "flail": "flail",
    "lead_filled_mace": "club", "ball_and_chain": "flail", "two_handed_great_flail": "nunchaku",
    "quarterstaff": "quarterstaff", "throwing_hammer": "hammer", "maul": "war hammer", "great_hammer": "war hammer",
    "mace_of_disruption": "morning star", "mighty_hammer": "war hammer",
    "spear": "spear", "awl_pike": "dwarvish spear", "trident": "trident", "pike": "silver spear",
    "halberd": "angled poleaxe", "throwing_axe": "axe", "beaked_axe": "beaked polearm", "broad_axe": "axe",
    "battle_axe": "battle axe", "lochaber_axe": "pole cleaver", "great_axe": "battle axe", "scythe": "scythe",
    "glaive": "single edged polearm", "lance": "lance", "scythe_of_slicing": "pole sickle",
    "lucerne_hammer": "hooked polearm",
    "sling": "sling", "short_bow": "shortbow", "long_bow": "longbow", "light_crossbow": "crossbow",
    "heavy_crossbow": "crossbow", "iron_shot": "mid pellets", "rounded_pebble": "pebble",
    "mithril_shot": "silver pellets", "arrow": "arrow", "seeker_arrow": "shining arrow",
    "mithril_arrow": "silver arrow", "bolt": "crossbow bolt", "seeker_bolt": "missile", "mithril_bolt": "missile",
    "shovel": "hammer", "pick": "pick axe", "mattock": "mattock",
    # Armour.
    "robe": "monk robes", "soft_leather_armour": "lacquered armor", "studded_leather_armour": "animal hide",
    "hard_leather_armour": "bronze armor", "soft_armour_leather_scale_mail": "brass armor",
    "chain_mail": "chain shirt", "metal_scale_mail": "scale armor", "augmented_chain_mail": "banded mail",
    "bar_chain_mail": "banded mail", "metal_brigandine_armour": "breastplate", "partial_plate_armour": "breastplate",
    "metal_lamellar_armour": "scale armor", "full_plate_armour": "full plate", "ribbed_plate_armour": "full plate",
    "mithril_chain_mail": "mirror plate", "mithril_plate_mail": "mirror plate", "adamantite_plate_mail": "grandmaster mail",
    "leather_shield": "small shield", "wicker_shield": "dwarvish shield", "small_metal_shield": "orcish shield",
    "large_metal_shield": "large shield", "knights_shield": "white handed shield", "mithril_shield": "polished silver shield",
    "hard_leather_cap": "elven leather helm", "metal_cap": "dented pot", "iron_helm": "helmet", "steel_helm": "visored helm",
    "iron_crown": "headband", "golden_crown": "princely crown", "jewel_encrusted_crown": "kingly crown",
    "massive_iron_crown": "kingly crown",
    "leather_gloves": "leather glove", "gauntlets": "iron gauntlet", "set_of_mithril_gauntlets": "surgical glove",
    "set_of_caestus": "clumsy mitt", "set_of_alchemists_gloves": "leather glove",
    "leather_sandals": "leather shoes", "leather_boots": "leather boots", "pair_of_iron_shod_boots": "quiet boots",
    "pair_of_steel_shod_boots": "exquisite boots", "pair_of_mithril_shod_boots": "mountaineer boots",
    "pair_of_ethereal_slippers": "elven boots",
    "cloak": "spidersilk cape", "fur_cloak": "animal hide", "elven_cloak": "spidersilk cape",
    "ethereal_cloak": "spidersilk cape",
    "black_dragon_scale_mail": "darkwyrm armor", "blue_dragon_scale_mail": "stormwyrm armor",
    "white_dragon_scale_mail": "icewyrm armor", "red_dragon_scale_mail": "firedrake armor",
    "green_dragon_scale_mail": "glendrake armor", "multi_hued_dragon_scale_mail": "sheenwyrm armor",
    "shining_dragon_scale_mail": "lightwyrm armor", "law_dragon_scale_mail": "lightwyrm armor",
    "gold_dragon_scale_mail": "sanddrake armor", "chaos_dragon_scale_mail": "dreadwyrm armor",
    "balance_dragon_scale_mail": "bogwyrm armor", "power_dragon_scale_mail": "sheenwyrm armor",
    # Light, food, gold, chests.
    "wooden_torch": "tallow candle", "lantern": "brass lantern", "phial": "radiant potion", "star": "prism",
    "arkenstone": "gleaming clear gem", "flask_of_oil": "bottle",
    "ration_of_food": "food ration", "slime_mold": "slime mold", "hard_biscuit": "fortune cookie", "apple": "apple",
    "handful_of_dried_fruits": "cherries", "scrap_of_flesh": "strip of meat", "slice_of_meat": "marbled cut of meat",
    "honey_cake": "pancake", "piece_of_elvish_waybread": "lembas wafer", "flask_of_whisky": "ornate bottle",
    "pint_of_fine_wine": "sanguine potion", "sip_of_miruvor": "sparkly potion", "swig_of_orcish_liquor": "murky potion",
    "draught_of_the_ents": "emerald potion",
    "copper": "pile of copper coins", "silver": "pile of silver coins", "gold": "pile of gold coins",
    "garnets": "dull red gem", "opals": "gleaming white gem", "sapphires": "gleaming blue gem",
    "rubies": "gleaming red gem", "diamonds": "gleaming clear gem", "emeralds": "gleaming green gem",
    "mithril": "silvery metal stone", "adamantite": "gleaming black gem",
    "small_wooden_chest": "closed chest", "large_wooden_chest": "closed big chest", "small_iron_chest": "closed safe",
    "large_iron_chest": "closed big safe", "small_steel_chest": "closed ice chest", "large_steel_chest": "closed big safe",
    # Artifact-only kinds (no flavour).
    "amulet": "jasper amulet", "pendant": "amethyst pendant", "necklace": "pearl necklace", "elfstone": "jade pendant",
    "jewel": "amulet of yendor", "ring": "unique ring b", "band": "plain ring", "ring_of_power": "unique ring a",
    "ring_of_fire": "unique ring c", "ring_of_adamant": "unique ring d", "ring_of_firmament": "unique ring e",
}
BOOKS = {
    "magic_book": ["fire tome", "ice tome", "lightning tome", "wind tome", "elemental tome"],
    "prayer_book": ["light tome", "order tome", "gold book", "shimmering tome", "illustrated manuscript"],
    "nature_book": ["natural tome", "earth tome", "water tome", "field guide", "poison tome"],
    "shadow_book": ["dark tome", "dread tome", "chaos tome", "book of ruin", "book of the dead"],
}

FLAVOURS = {
    "potion": {
        "clear": "clear potion", "light brown": "brown potion", "icky green": "dark green potion",
        "azure": "sky blue potion", "blue": "brilliant blue potion", "cerulean": "sky blue potion",
        "black": "black potion", "brown": "brown potion", "ochre": "golden potion", "bubbling": "bubbly potion",
        "chartreuse": "emerald potion", "cloudy": "cloudy potion", "copper speckled": "sparkly potion",
        "crimson": "ruby potion", "cyan": "cyan potion", "ultramarine": "brilliant blue potion",
        "dark green": "dark green potion", "dark red": "sanguine potion", "light yellow": "yellow potion",
        "green": "emerald potion", "muddy": "murky potion", "grey": "smoky potion", "vespertine": "puce potion",
        "hazy": "cloudy potion", "indigo": "purple red potion", "light blue": "sky blue potion",
        "light green": "emerald potion", "magenta": "magenta potion", "metallic blue": "brilliant blue potion",
        "metallic red": "ruby potion", "metallic green": "dark green potion", "metallic purple": "magenta potion",
        "misty": "milky potion", "orange": "orange potion", "orange speckled": "fizzy potion", "pink": "pink potion",
        "light teal": "cyan potion", "light violet": "puce potion", "purple": "purple red potion",
        "purple speckled": "swirly potion", "red": "ruby potion", "teal": "cyan potion",
        "mustard yellow": "golden potion", "smoky": "smoky potion", "tangerine": "orange potion",
        "violet": "puce potion", "vermilion": "ruby potion", "white": "white potion", "yellow": "yellow potion",
        "light purple": "magenta potion", "pungent": "smelly potion", "lavender": "puce potion",
        "viscous pink": "syrupy potion", "oily yellow": "dripping potion", "gloopy green": "murky potion",
        "shimmering": "effervescent potion", "coagulated crimson": "sanguine potion", "light pink": "pink potion",
        "gold": "golden potion",
    },
    "ring": {
        "alexandrite": "unique ring f", "amethyst": "unique ring g", "aquamarine": "glass ring",
        "azurite": "sapphire ring", "beryl": "emerald ring", "bloodstone": "ruby ring", "calcite": "clay ring",
        "carnelian": "coral ring", "corundum": "topaz ring", "diamond": "diamond ring", "emerald": "emerald ring",
        "fluorite": "unique ring h", "garnet": "engagement ring", "hematite": "iron ring", "jade": "jade ring",
        "jasper": "agate ring", "lapis lazuli": "unique ring i", "malachite": "unique ring j", "marble": "granite ring",
        "moonstone": "moonstone ring", "onyx": "black onyx ring", "opal": "opal ring", "pearl": "pearl ring",
        "quartz": "shiny ring", "quartzite": "wire ring", "rhodonite": "unique ring k", "tiger eye": "tiger eye ring",
        "topaz": "topaz ring", "turquoise": "unique ring l", "zircon": "unique ring m", "platinum": "steel ring",
        "bronze": "bronze ring", "gold": "gold ring", "obsidian": "obsidian ring", "silver": "silver ring",
        "amber": "brass ring", "jet": "twisted ring", "jewelled": "unique ring a", "adamantite": "wooden ring",
    },
    "amulet": {
        "driftwood": "keepsake necklace", "agate": "jasper amulet", "ivory": "choker necklace",
        "obsidian": "misshapen talisman", "bone": "cow bell", "brass": "fuzzy amulet", "pewter": "balance talisman",
        "tortoise shell": "squid netsuke", "azure": "aquamarine pendant", "crystal": "fake amulet of yendor",
        "silver": "sapphire pendant", "copper": "garnet pendant", "carved oak": "keepsake necklace",
        "dragon tooth": "misshapen talisman", "ruby": "ruby pendant", "mithril": "amethyst pendant",
        "adamant": "balance talisman", "flint stone": "jasper amulet", "sea shell": "pearl necklace",
        "mother-of-pearl": "pearl necklace",
    },
    "wand": {
        "aluminium": "aluminum wand", "cast iron": "iron wand", "chromium": "cold steel wand", "copper": "copper wand",
        "gold": "glimmering wand", "iron": "iron wand", "magnesium": "white wand", "molybdenum": "dull wand",
        "nickel": "zinc wand", "rusty": "rusted wand", "silver": "silver wand", "steel": "steel wand",
        "tin": "tin wand", "titanium": "platinum wand", "tungsten": "black wand", "zirconium": "crystal wand",
        "zinc": "zinc wand", "aluminium-plated": "mirrored wand", "copper-plated": "hot wand",
        "gold-plated": "jeweled wand", "nickel-plated": "short wand", "silver-plated": "long wand",
        "steel-plated": "fortified wand", "tin-plated": "bent wand", "zinc-plated": "hexagonal wand",
        "mithril-plated": "chilly wand", "mithril": "glowing wand", "runed": "runed wand", "bronze": "curved wand",
        "brass": "brass wand", "platinum": "platinum wand", "lead": "thick wand", "lead-plated": "swollen wand",
        "ivory": "ivory wand", "pewter": "perforated wand",
    },
    "mushroom": {
        "spotted": "spotted mushroom", "rubbery": "hardball mushroom", "crumbly": "puffball fungus",
        "moldy": "slimy mushroom", "mottled": "big snout mushroom", "firm": "umbrella mushroom", "woody": "saddle mushroom",
        "wrinkled": "hog ear mushrooms", "furry": "disc fungus", "fragile": "fairy step mushrooms",
        "marbled": "christmas fungus", "gelatinous": "globe fungus", "speckled": "red cap mushroom",
        "glowing": "luminous mushroom", "striped": "big snout mushroom", "slimy": "slimy mushroom",
        "moist": "globe fungus", "withered": "saddle mushroom", "waxy": "umbrella mushroom", "smelly": "hog ear mushrooms",
    },
}

# ---------------------------------------------------------------- monsters
# (regex on the lower-case Angband name, DawnLike sprite name). First match wins.
DRAGON_COLOURS = [  # colour words -> DawnLike drake/wyrm family
    (r"multi-hued|many colours", "sheenwyrm"), (r"blue|storm|thunder|sky", "storrmwyrm"),
    (r"white|ice\b|scatha", "icewyrm"), (r"green|swamp", "glendrake"), (r"bile", "bogwyrm"),
    (r"black|death|shadow|annihilation|ancalagon", "darkwyrm"), (r"hell|itangast|fire", "firedrake"),
    (r"red|glaurung", "kingwyrm"), (r"gold|smaug", "sanddrake"), (r"crystal|ethereal|law", "lightwyrm"),
    (r"chaos", "sheenwyrm"), (r"balance|dracoli", "dreadwyrm"),
]
BABY = {"storrmwyrm": "baby stormwyrm"}  # the atlas spells the adult "storrmwyrm"

MONSTERS = [
    # Mimics and lurkers look like what they pretend to be.
    (r"potion mimic", "sinister potion"), (r"scroll mimic", "ancient white scroll"), (r"ring mimic", "twisted ring"),
    (r"chest mimic", "strange object"), (r"creeping copper", "pile of copper coins"),
    (r"creeping silver|creeping mithril", "pile of silver coins"), (r"creeping gold", "pile of gold coins"),
    (r"creeping adamantite", "pile of stone coins"), (r"^lurker", "lurker above"), (r"^trapper", "lurker below"),
    # , mushrooms and plants.
    (r"shrieker", "shrieker"), (r"purple mushroom", "violet fungus"), (r"grey mushroom", "umbrella mushroom"),
    (r"yellow mushroom", "luminous mushroom"), (r"spotted mushroom", "spotted mushroom"),
    (r"clear mushroom", "slimy mushroom"), (r"magic mushroom", "fairy step mushrooms"),
    (r"shambling mound", "swamp fern"), (r"memory moss", "mould"),
    # A: Ainur.
    (r"nienna", "weeping angel"), (r"mandos|meássë", "dark angel"), (r"ulmo", "ocean spirit"),
    (r"yavanna", "forest spirit"), (r"manwë|varda|makar", "archon"), (r"saruman", "archmage"),
    (r"sauron, the sorcerer|mouth of sauron", "wizard of yendor"), (r"^maia", "angel"),
    # Dragons.
    (r"pseudo-dragon", "baby sphinx"), (r"wyvern", "wyvern"), (r"kavlax", "hydra"),
    # B: birds.
    (r"phoenix", "phoenix"), (r"crow|craban", "nighthawk"), (r"blood falcon", "aluminum falcon"),
    (r"giant roc", "condor"), (r"winged horror", "doom chickabay"),
    # C: dogs.
    (r"scruffy little dog", "little terrier"), (r"^jackal", "jackal"), (r"fang,", "terrier"), (r"grip,", "big terrier"),
    (r"white wolf", "winter wolf"), (r"^wolf$", "wolf"), (r"^warg", "warg"), (r"blink dog", "cogdog"),
    (r"werewolf|draugluin", "werewolf"), (r"wolf chieftain", "rabid wolf"), (r"^hellhound", "hell hound"),
    (r"huan", "big hound"), (r"carcharoth", "cerberus"), (r"wolf-sauron", "hell hound"),
    # E: elementals and spirits.
    (r"air spirit|air elemental|ariel", "air elemental"), (r"water spirit|water elemental|waldern", "water elemental"),
    (r"earth spirit|earth elemental|quaker", "earth elemental"), (r"fire spirit|fire elemental|vargo", "fire elemental"),
    (r"invisible stalker", "stalker"), (r"ooze elemental", "brown pudding"), (r"smoke elemental", "fog cloud"),
    (r"ice elemental", "snow golem"), (r"magma elemental", "lava demon"), (r"will o' the wisp", "wisp"),
    # F, I, K, a, c: insects.
    (r"dragon fly", "dragonfly"), (r"louse", "giant louse"), (r"fruit fly|hummerhorn", "tsetse fly"),
    (r"giant flea", "giant flea"), (r"neekerbreeker", "locust"), (r"firefly", "firefly"),
    (r"brown beetle", "giant beetle"), (r"red beetle|fire beetle", "spitting beetle"),
    (r"iridescent beetle", "assassin bug"), (r"white beetle", "chillbug"), (r"beetle", "killer beetle"),
    (r"soldier ant|army ant|queen ant", "soldier ant"), (r"(white|silver|blue) ant", "snow ant"),
    (r"(red|fire) ant", "fire ant"), (r"\bant$", "giant ant"), (r"centipede|carrion crawler", "centipede"),
    # G: ghosts.
    (r"poltergeist", "wisp"), (r"lost soul|moaning spirit", "spirit"), (r"glutton ghost|banshee|^ghost$|spectre", "ghost"),
    (r"^shade$|^shadow$|phantom|spirit troll", "shade"), (r"^dread$", "dark one"), (r"dreadmaster|dreadlord", "death"),
    # H: hybrids.
    (r"white harpy", "mountain fairy"), (r"black harpy", "forest fairy"), (r"hippogriff", "snow griffon"),
    (r"griffon", "griffon"), (r"gorgimaera", "vorpal jabberwock"), (r"chimaera|jabberwock", "jabberwock"),
    (r"manticore", "sphinx"), (r"minotaur", "minotaur"),
    # J: snakes.
    (r"large white snake", "garter snake"), (r"large grey snake", "snake"), (r"copperhead", "water moccasin"),
    (r"rattlesnake", "pit viper"), (r"king cobra", "cobra"), (r"black mamba", "asp"), (r"nightcrawler", "python"),
    (r"serpent-sauron", "python"), (r"serpent", "serpent"),
    # L, V, W, s, z: undead.
    (r"^lich$", "lich"), (r"master lich|feagwath", "master lich"), (r"demilich", "demilich"),
    (r"archlich|vecna", "archlich"), (r"black reaver", "dark one"),
    (r"^vampire$", "vampire"), (r"master vampire|thuringwethil", "vampire mage"), (r"vampire lord", "vampire lord"),
    (r"elder vampire|vampire-sauron", "vlad the impaler"),
    (r"wight", "barrow wight"), (r"nightwalker", "death"), (r"wraith|nightwing", "wraith"),
    (r"uvatha|adunaphel|akhorahil|ren the unclean|ji indur|dwar, dog|hoarmurath|khamûl|witch-king", "nazgul"),
    (r"skeleton troll", "shadow skeleton"), (r"skeleton etten", "plague skeleton"), (r"cantoras", "fire skeleton"),
    (r"^skeleton", "skeleton"), (r"hand druj", "fingers"), (r"eye druj", "eyeball"), (r"skull druj", "magic skull_0"),
    (r"ghoul|ghast", "ghoul"), (r"zombified kobold", "kobold zombie"), (r"zombified orc", "orc zombie"),
    (r"zombified human", "human zombie"), (r"mummified orc", "orc mummy"), (r"mummified human", "human mummy"),
    (r"mummified troll", "giant mummy"), (r"mummified chieftain", "ettin mummy"),
    # O, P, T: ogres, giants, trolls.
    (r"lokkak|ogre chieftain", "ogre king"), (r"black ogre|ogre (shaman|mage)", "ogre lord"), (r"ogre", "ogre"),
    (r"fire giant", "fire giant"), (r"hill giant|^nan,", "hill giant"), (r"frost giant", "frost giant"),
    (r"stone giant", "stone giant"), (r"storm giant", "storm giant"), (r"cloud giant|gilim", "giant"),
    (r"cyclops", "cyclops"), (r"titan", "titan"), (r"morgoth", "surtur"),
    (r"snow troll", "ice troll"), (r"stone troll|bert|bill the|tom the|cave troll|mountain troll", "rock troll"),
    (r"troll (priest|scavenger)", "water troll"), (r"olog|troll chieftain|troll blackguard|rogrog", "olog hai"),
    (r"etten", "ettin"), (r"troll", "troll"),
    # R: reptiles.
    (r"rock lizard", "gecko"), (r"frog", "frog"), (r"salamander", "salamander"), (r"cave lizard", "lizard"),
    (r"night lizard", "komodo dragon"), (r"basilisk", "pyrolisk"), (r"tarrasque", "crocodile"),
    # S: spiders.
    (r"cave spider", "cave spider"), (r"phase spider", "phase spider"), (r"shelob|ungoliant", "shelob"),
    (r"tick", "giant tick"), (r"black scorpion", "scorpius"), (r"red scorpion", "scorpion"),
    (r"scorpion", "giant scorpion"), (r"spider|tarantula", "giant spider"),
    # U, u: demons.
    (r"vrock", "vrock"), (r"glabrezu", "orcus"), (r"nalfeshnee", "nalfeshnee"), (r"marilith", "marilith"),
    (r"balrog", "balrog"), (r"hezrou", "hezrou"), (r"barbazu", "barbed devil"), (r"bile demon", "juiblex"),
    (r"osyluth", "bone devil"), (r"gelugon", "ice devil"), (r"horned reaper", "horned devil"),
    (r"pit fiend", "pit fiend"), (r"fury", "erinys"), (r"pazuzu", "baalzebub"),
    (r"lemure", "lemure"), (r"tengu", "tengu"), (r"homunculus", "homunculus"), (r"quasit", "quasit"),
    (r"imp\b", "imp"), (r"nruling", "manes"), (r"bodak", "lava demon"),
    # X, Y.
    (r"umber hulk", "umber hulk"), (r"xorn|xaren", "xorn"), (r"yeti", "yeti"), (r"sasquatch", "ape"),
    # Z: hounds.
    (r"fire hound|plasma hound", "hell hound"), (r"cold hound", "winter wolf"), (r"light hound", "little hound"),
    (r"dark hound|nether hound", "little terrier"), (r"clear hound|water hound", "cogdog"),
    (r"energy hound|time hound", "big cogdog"), (r"air hound", "little cogdog"),
    (r"vibration hound|impact hound", "big hound"), (r"nexus hound|inertia hound", "big terrier"),
    (r"gravity hound", "terrier"), (r"multi-hued hound|chaos hound|tindalos|aether hound", "cerberus"),
    (r"hound", "hound"),
    # b: bats.
    (r"fruit bat", "baby bat"), (r"(brown|tan) bat", "white nosed bat"), (r"dragon bat", "giant bat"),
    (r"disenchanter bat", "werebat"), (r"bat\b", "vampire bat"),
    # e: eyes.
    (r"floating eye|disenchanter eye", "floating eye"), (r"radiation eye|bloodshot eye|evil eye", "evil eye"),
    (r"spectator|gauth|beholder|omarax", "eye tyrant"),
    # f: cats.
    (r"scrawny cat", "barn kitten"), (r"wild cat", "bobcat"), (r"panther", "panther"),
    (r"sabre-tooth|^tiger", "tiger"), (r"displacer beast", "fierce lynx"), (r"tevildo", "fierce puma"),
    # g: golems.
    (r"flesh golem", "flesh golem"), (r"clay golem", "clay golem"), (r"stone golem|pukelman|colossus", "stone golem"),
    (r"eog golem|iron golem", "iron golem"), (r"mithril golem", "glass golem"), (r"silent watcher", "gargoyle"),
    (r"colbran|bronze golem", "gold golem"), (r"drolem", "winged gargoyle"), (r"bone golem", "wax golem"),
    # h: dwarves, elves, hobbits.
    (r"bullroarer|scruffy looking hobbit", "hobbit"), (r"farmer maggot", "farmer man"),
    (r"red-hatted elf", "leprechaun"), (r"father christmas", "gnome king"), (r"sméagol", "gremlin"),
    (r"mind flayer", "mind flayer"), (r"green elf", "green elf"), (r"eöl", "elf lord"), (r"maeglin", "elf king"),
    (r"fëanorian", "wood elf"), (r"stonefoot", "dwarf knight"), (r"ironfist", "dwarf healer"),
    (r"nár|mîm, betrayer|fundin", "dwarf king"), (r"dwarven lord|blacklock|stiffbeard", "dwarf lord"),
    (r"dwarf|ibun|khîm", "dwarf"),
    # i, j, m, w: slimes, molds and worms.
    (r"white icky", "baby slug"), (r"clear icky", "quivering blob"), (r"blubbering icky", "brown pudding"),
    (r"grey icky", "gray ooze"), (r"green icky", "green slime"), (r"bloodshot icky", "acid blob"),
    (r"blue icky", "blue slime"),
    (r"white jelly", "gray ooze"), (r"silver jelly", "quivering blob"), (r"green ooze|green jelly", "green slime"),
    (r"yellow jelly|ochre jelly", "ochre jelly"), (r"blue jelly", "blue jelly"), (r"blue ooze", "blue slime"),
    (r"rot jelly", "brown pudding"), (r"red jelly", "acid blob"), (r"grape jelly", "violet fungus"),
    (r"spotted jelly", "spotted jelly"), (r"gelatinous cube", "gelatinous cube"), (r"black ooze", "black pudding"),
    (r"grey mold", "lichen"), (r"yellow mold", "yellow mold"), (r"brown mold", "brown mold"),
    (r"green mold", "green mold"), (r"red mold", "red mold"), (r"hairy mold", "mould"),
    (r"disenchanter mold", "violet fungus"), (r"shimmering mold|death mold", "ungenomold"),
    (r"(white|clear) worm", "maggot"), (r"(green|yellow) worm", "larva"), (r"red worm", "dung worm"),
    (r"(nether|abyss) worm", "tunnel worm"), (r"(blue|disenchanter) worm", "baby long worm"),
    (r"purple worm", "purple worm"), (r"wereworm", "long worm"),
    # k: kobolds.
    (r"kobold archer", "kobold ranger"), (r"kobold shaman", "kobold shaman"), (r"large kobold", "large kobold"),
    (r"mughash", "kobold lord"), (r"kobold", "kobold"),
    # l: trees.
    (r"old man willow|black-hearted", "demon tree"), (r"hasty ent", "blizzard tree"), (r"tree|huorn", "evil tree"),
    # n: nagas.
    (r"green naga", "guardian naga"), (r"black naga", "black naga"), (r"red naga", "red naga"),
    (r"guardian naga", "golden naga"), (r"spirit naga", "white naga"), (r"gorgon", "medusa"),
    # o: orcs.
    (r"cave orc", "deep orc"), (r"lagduf|ufthak", "militant orc"), (r"snaga", "orc"),
    (r"orc tracker|grishn", "orc rogue"), (r"orc shaman", "orc shaman"), (r"orc archer", "ordinary orc"),
    (r"golfimbul|bolg|azog", "orc king"), (r"orc captain|shagrat|gorbag", "orc knight"),
    (r"uruk|lugdush|ugl", "uruk"), (r"hill orc", "hill orc"),
    # p: people.
    (r"wormtongue", "aide"), (r"^soldier", "soldier"), (r"cutpurse|^rogue$", "thief"), (r"acolyte|^priest$", "priest"),
    (r"apprentice|^mage$|illusionist", "wizard"), (r"^scout|southron archer|^ranger$", "ranger"),
    (r"gallant|paladin", "knight"), (r"tamer", "nomad"), (r"ruffian|brigand", "bandit"),
    (r"brodda|ulfast|ulwarth", "brute"), (r"witch", "neferet the green"), (r"drúadan", "shaman karnov"),
    (r"^warrior$", "fighter"), (r"druid", "healer"), (r"blackguard|black knight|death knight", "slayer"),
    (r"champion|lorgan", "champion"), (r"of umbar", "desperado"),
    (r"necromancer|demonologist|sorcerer", "archmage"), (r"uldor|ulfang|berserker", "barbarian"),
    (r"beorn, the shape", "caveman"), (r"assassin", "assassin"), (r"master thief", "master of thieves"),
    (r"grand master mystic", "grand master"), (r"master mystic", "sensei"), (r"mystic", "monk"),
    (r"dúnadan of angmar|carn dûm", "exterminator"), (r"castamir|ar-pharaz", "king arthur"),
    (r"enchantress", "priestess"), (r"patriarch", "arch priest"), (r"gorlim", "convict"),
    (r"ranger chieftain", "orion"), (r"harowen", "ninja master"),
    # q: quadrupeds.
    (r"bear", "owlbear"), (r"catoblepas", "raging bull"), (r"mûmak", "mumak"), (r"night mare", "strong black horse"),
    # r: rodents.
    (r"mouse", "sewer rat"), (r"wererat", "wererat"), (r"rat\b", "giant rat"),
    # t: town people.
    (r"village idiot", "peasant man"), (r"street urchin|beggar", "prisoner"), (r"blubbering idiot", "farmer man"),
    (r"boil-covered", "convict"), (r"leper", "peasant woman"), (r"squint-eyed rogue", "thief"),
    (r"happy drunk", "tourist"), (r"merchant", "shopkeeper"), (r"mercenary", "bandit"), (r"veteran", "sergeant"),
    # v: vortices.
    (r"fire vortex|plasma vortex", "fire vortex"), (r"water vortex|wiruin", "steam vortex"),
    (r"cold vortex", "ice vortex"), (r"shardstorm|nexus vortex|time vortex", "dust vortex"),
    (r"vortex|unmagic", "energy vortex"),
    # y: yeeks (woodchucks: DawnLike's author suggests hiding his dragon in one).
    (r"terrified yeek", "rabbit"), (r"master yeek", "jackrabbit"), (r"orfax", "red squirrel"),
    (r"boldor", "rodent of unusual size"), (r"yeek", "woodchuck"),
]


def monster_sprite(name):
    if m := re.match(r"(\d)-headed hydra", name):
        return "baby hydra" if int(m.group(1)) <= 3 else "hydra"
    if re.search(r"quylthulg", name):
        return "meathead" if re.search(r"greater|master|emperor|qlzqqlzuup", name) else "small meathead"
    if re.search(r"dragon|drake|wyrm|dracoli|smaug|glaurung|ancalagon|scatha|itangast", name) \
            and not re.search(r"dragon fly|dragon bat|pseudo", name):
        for pattern, family in DRAGON_COLOURS:
            if re.search(pattern, name):
                break
        else:
            family = "kingwyrm"
        if re.search(r"baby|young", name):
            return BABY.get(family, "baby " + family)
        return family
    for pattern, sprite in MONSTERS:
        if sprite and re.search(pattern, name):
            return sprite
    return None


def load_names(tsv, sheets):
    """{sprite name: (sheet, column, row)} from DawnLikeAtlas's image_names.tsv."""
    names, cur, prev_blank = {}, None, True
    for line in open(tsv, encoding="utf-8").read().split("\n")[1:]:
        cells = [c.strip() for c in line.rstrip("\r").split("\t")]
        if prev_blank and cells[0] and not any(cells[1:]):
            cur, row, prev_blank = SECTION_ALIASES.get(cells[0], cells[0]), 0, False
            if cur not in sheets:
                raise SystemExit(f"no sheet for section {cur}")
            continue
        prev_blank = not any(cells)
        for col, n in enumerate(cells):
            if n:
                names.setdefault(n, (cur, col, row))
        row += 1
    return names


def main():
    src, tsv, repo = sys.argv[1:4]
    sheets = {os.path.splitext(os.path.basename(p))[0]: p for p in glob.glob(os.path.join(src, "*", "*.png"))}
    names = load_names(tsv, sheets)
    data = os.path.join(repo, "src", "Angband.Data", "data")
    out_dir = os.path.join(repo, "src", "Angband.Avalonia", "tilesets", "dawnlike")
    os.makedirs(out_dir, exist_ok=True)

    cells = []  # (sheet, col, row) in packing order
    index = {}

    def ref(sprite):
        if isinstance(sprite, list):
            return [ref(s) for s in sprite]
        if isinstance(sprite, dict):
            return {k: ref(v) for k, v in sprite.items()}
        if sprite not in names:
            raise SystemExit(f"no DawnLike sprite named {sprite!r}")
        cell = names[sprite]
        if cell not in index:
            index[cell] = len(cells)
            cells.append(cell)
        i = index[cell]
        return f"main:{i % 16},{i // 16}"

    tiles = {}
    for tid, sprite in TERRAIN.items():
        tiles["terrain:" + tid] = ref(sprite)
    for tid, sprite in TRAPS.items():
        tiles["trap:" + tid] = ref(sprite)
    tiles["player"] = ref("fighter")
    tiles["object:pile"] = ref("bag")
    for base, sprite in BASES.items():
        tiles["object-base:" + base] = ref(sprite)
    objects = json.load(open(os.path.join(data, "objects.json"), encoding="utf-8"))
    counts = {"object": 0, "flavor": 0, "monster": 0}
    for o in objects:
        sprite = KINDS.get(o["id"])
        if sprite is None and o["base"] in BOOKS:
            books = [x["id"] for x in objects if x["base"] == o["base"]]
            sprite = BOOKS[o["base"]][min(books.index(o["id"]), len(BOOKS[o["base"]]) - 1)]
        if sprite is None and o["base"] == "food" and o["id"].startswith("mushroom"):
            continue
        if sprite:
            tiles["object:" + o["id"]] = ref(sprite)
            counts["object"] += 1
    for group in json.load(open(os.path.join(data, "flavors.json"), encoding="utf-8")):
        table = FLAVOURS.get(group["id"], {})
        for fl in group.get("flavors", []):
            if (sprite := table.get(fl["name"].lower())):
                tiles[f"flavor:{group['id']}:{fl['name'].lower()}"] = ref(sprite)
                counts["flavor"] += 1

    monsters = json.load(open(os.path.join(data, "monsters.json"), encoding="utf-8"))
    unmatched = []
    for m in monsters:
        sprite = monster_sprite(m["name"].lower())
        if sprite:
            tiles["monster:" + m["id"]] = ref(sprite)
            counts["monster"] += 1
        else:
            unmatched.append(m["name"])

    # Pack the used cells, 16 to a row.
    loaded = {}
    rows = (len(cells) + 15) // 16
    w, h = 16 * T, rows * T
    out = [(0, 0, 0, 0)] * (w * h)
    for i, (sheet, col, row) in enumerate(cells):
        if sheet not in loaded:
            loaded[sheet] = png_read(sheets[sheet])
        sw, _, px = loaded[sheet]
        ox, oy = (i % 16) * T, (i // 16) * T
        for y in range(T):
            for x in range(T):
                out[(oy + y) * w + ox + x] = px[(row * T + y) * sw + col * T + x]
    png_write(os.path.join(out_dir, "dawnlike.png"), w, h, out)

    manifest = {
        "name": "DawnLike (16x16)",
        "author": "DragonDePlatino (art), DawnBringer (palette); sprite names from Tommy Ettinger's DawnLikeAtlas",
        "license": "CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/)",
        "source": "https://opengameart.org/content/dawnlike-16x16-universal-rogue-like-tileset-v181",
        "tileWidth": T, "tileHeight": T,
        "sheets": {"main": "dawnlike.png"},
        "opaque": False,
        "tiles": tiles,
    }
    path = os.path.join(out_dir, "tileset.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        f.write("\n")
    subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), "tileset_glyph_fallbacks.py"),
                    os.path.join(data, "monsters.json"), path], check=True)
    print(counts, f"{len(cells)} cells", "no art (glyph fallback):", unmatched)


if __name__ == "__main__":
    main()
