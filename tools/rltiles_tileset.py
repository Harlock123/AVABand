#!/usr/bin/env python3
"""Build the RLTiles (32x32, public domain) tileset: src/Angband.Avalonia/tilesets/rltiles.

RLTiles (https://rltiles.sourceforge.net/, GitHub mirror https://github.com/statico/rltiles) has
tiles for NetHack and for Dungeon Crawl. The Dungeon Crawl half is what the bundled "dcss" set grew
from, so this set takes its creatures, dungeon features and traps from the NetHack half (nh-mon0,
nh-mon1, nh-dngn) and its objects from the shared item folder, and uses a Dungeon Crawl tile only
where NetHack has nothing fitting (block walls, hydras, bears, griffons...). No image that is also
in tilesets/dcss is used — every chosen tile is compared with the dcss folder's images and the
script stops if one matches (90% or more of its pixels the same) — except a few (SHARED_WITH_DCSS:
the black dragon, iron and stone golems, centipede, minotaur and sling), whose stand-ins were worse
than the sameness.

The source tiles are 8-bit BMPs on a teal (#476c6c) background. Creatures, objects and traps get
that colour made transparent (so "opaque" is false and the floor shows through); terrain has it
replaced by the NetHack room floor. Flavours (potions, rings, amulets, wands, staves, rods,
mushrooms) are given the pack image nearest in name and colour; where several flavours share an
image, the others get a recoloured copy in their own colour. Every image used is packed into one
sheet, rltiles.png. Standard library only.

Usage:
    rltiles_tileset.py <rltiles checkout> <src/Angband.Data/data> <src/Angband.Avalonia/tilesets/rltiles>
then:
    tileset_glyph_fallbacks.py <data>/monsters.json <tilesets/rltiles>/tileset.json
"""
import colorsys, json, os, re, struct, sys, unicodedata

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dcss_flavor_tiles import png_read, png_write  # noqa: E402

BG = (71, 108, 108)
COLUMNS = 32
FLOOR = "nh-dngn/floor_of_a_room"

# ---------------------------------------------------------------------------------------------
# Terrain and traps. "floor:" = drawn on the NetHack floor (terrain); plain paths keep transparency.
TERRAIN = {
    "floor": {"lit": "floor:nh-dngn/floor_light", "torch": "floor:nh-dngn/floor_light",
              "dark": "floor:nh-dngn/floor_of_a_room"},
    "granite_wall": "floor:dc-dngn/dngn_stone_wall",
    "permanent_wall": "floor:dc-dngn/wall/relief0",
    "magma_vein": "floor:dc-dngn/dngn_rock_wall_00",
    "quartz_vein": "floor:dc-dngn/dngn_rock_wall_15",
    "magma_with_treasure": ["floor:dc-dngn/dngn_rock_wall_00", "item/gem/yellow"],
    "quartz_with_treasure": ["floor:dc-dngn/dngn_rock_wall_15", "item/gem/yellow"],
    "rubble": ["floor:nh-dngn/floor_light", "item/misc/misc_stone"],
    "passable_rubble": ["floor:nh-dngn/floor_light", "item/gem/rock"],
    "lava": "floor:nh-dngn/molten_lava",
    "closed_door": "floor:nh-dngn/closed_door_h",
    "open_door": "floor:nh-dngn/open_door_h",
    "broken_door": "floor:nh-dngn/doorway",
    "up_staircase": "floor:nh-dngn/staircase_up",
    "down_staircase": "floor:nh-dngn/staircase_down",
}
SHOPS = {  # a doorway with a typical ware on it
    "shop_general": "food/food_ration", "shop_armoury": "armor/plate_mail",
    "shop_weaponsmith": "weapon/long_sword", "shop_bookseller": "book/parchment",
    "shop_alchemist": "potion/milky", "shop_magic": "wand/jeweled",
    "shop_black_market": "misc/crystal_ball", "shop_home": "misc/large_box",
}
TRAPS = {
    "glyph_warding": "anti_magic_trap_field", "web": "web", "trap_door": "trap_door", "pit": "pit",
    "spiked_pit": "spiked_pit", "poison_pit": "spiked_pit", "rune_of_summon_foe": "magic_trap",
    "summon_rune": "magic_trap", "rune_of_necromancy": "polymorph_trap", "rune_of_dragonsong": "fire_trap",
    "hellhole": "hole", "teleport_rune": "teleportation_trap", "fire_rune": "fire_trap", "acid_rune": "rust_trap",
    "slow_dart": "dart_trap", "weakness_dart": "dart_trap", "dexterity_loss_dart": "dart_trap",
    "constitution_loss_dart": "dart_trap", "blinding_gas_trap": "sleeping_gas_trap",
    "confusion_gas": "sleeping_gas_trap", "poison_gas": "sleeping_gas_trap", "sleep_gas": "sleeping_gas_trap",
    "aggravation_trap": "squeaky_board", "alarm": "squeaky_board", "mine_trap": "land_mine",
    "blast_trap": "land_mine", "mind_blasting_trap": "magic_trap", "brain_smashing_trap": "magic_trap",
    "rock_fall_trap": "falling_rock_trap", "earthquake_trap": "rolling_boulder_trap",
    "block_fall_trap": "falling_rock_trap", "area_blast_trap": "land_mine",
    "blinding_flash_trap": "magic_trap", "blinding_trap": "magic_trap", "mana_drain_trap": "anti_magic_trap_field",
    "knife_trap": "arrow_trap", "petrifying_trap": "statue_trap",
}
PLAYER = "nh-mon0/0man/valkyrie"

# ---------------------------------------------------------------------------------------------
# Monsters: a NetHack monster name (looked up in nh-mon0/nh-mon1), or a path under the checkout.
# An AVABand monster whose name is a NetHack monster's gets that tile; the others take the first
# rule whose pattern is found in their lower-case name.
RULES = [
    # s skeletons
    (r"skull druj", "dc-mon0/z/flying_skull"), (r"eye druj", "dc-mon1/g/eye_of_devastation"), (r"skeleton|druj|cantoras", "skeleton"),
    # z zombies and mummies
    (r"ghast", "ghoul"), (r"zombified kobold", "kobold_zombie"), (r"zombified orc", "orc_zombie"),
    (r"zombified human", "human_zombie"), (r"mummified (orc|chieftain)", "orc_mummy"),
    (r"mummified human", "human_mummy"), (r"mummified troll", "giant_mummy"),
    # Mimics and creeping coins look like the treasure they pretend to be.
    (r"potion mimic", "item/potion/swirly"), (r"scroll mimic", "item/scroll/zelgo_mer"),
    (r"ring mimic", "item/ring/twisted"), (r"chest mimic", "item/misc/chest"),
    (r"creeping copper", "item/gem/orange"), (r"creeping silver", "item/gem/white"),
    (r"creeping gold", "item/gem/yellow"), (r"creeping mithril", "item/gem/blue"),
    (r"creeping adamantite", "item/gem/green"),
    # , mushrooms
    (r"shrieker|spotted mushroom", "shrieker"), (r"yellow mushroom", "yellow_mold"),
    (r"purple mushroom|magic mushroom", "violet_fungus"), (r"memory moss", "green_mold"),
    (r"mushroom patch|shambling mound", "lichen"),
    # A Ainur
    (r"sauron, the sorcerer", "nh-mon0/0man/dark_one"), (r"^saruman", "nh-mon0/0man/wizard_of_balance"),
    (r"meássë", "nh-mon0/0man/minion_of_huhetotl"), (r"ossë|maia of ulmo", "couatl"),
    (r"makar|maia of mandos|maia of aulë", "archon"), (r"maia of (nienna|yavanna)", "aleax"),
    (r"maia of (oromë|manwë)", "ki-rin"), (r"maia", "angel"),
    # B birds
    (r"phoenix|blood falcon", "pyrolisk"), (r"winged horror", "winged_gargoyle"),
    (r"crow|craban|roc\b", "raven"),
    # C dogs
    (r"scruffy little dog", "little_dog"), (r"^fang|^grip", "dog"), (r"huan", "large_dog"),
    (r"white wolf", "winter_wolf_cub"), (r"blink dog", "dingo"), (r"werewolf|draugluin|wolf-sauron", "nh-mon0/d/werewolf"),
    (r"warg|wolf chieftain", "winter_wolf"), (r"^wolf", "coyote"), (r"hellhound", "hell_hound_pup"),
    (r"carcharoth", "cerberus"),
    # D dragons
    (r"ancient blue|storm wyrm|sky dragon", "blue_dragon"), (r"ancient white|ice wyrm|scatha", "white_dragon"),
    (r"ancient green|swamp wyrm", "dc-mon1/d/swamp_dragon"), (r"ancient black|ancalagon", "black_dragon"), (r"annihilation", "gray_dragon"),
    (r"ancient red|hell wyrm", "red_dragon"), (r"ancient gold|bile wyrm|glaurung", "yellow_dragon"),
    (r"ancient multi-hued|many colours|wyrm of chaos", "nh-mon0/0man/chromatic_dragon"),
    (r"dracolich", "dc-mon1/d/skeletal_dragon"), (r"death drake|wyrm of thunder", "gray_dragon"),
    (r"great crystal drake|wyrm of balance|kavlax", "shimmering_dragon"),
    (r"ethereal dragon|wyrm of law", "silver_dragon"), (r"itangast|smaug|dracolisk", "orange_dragon"),
    # E elementals and spirits
    (r"air spirit|air elemental|ariel", "djinni"), (r"water spirit|water elemental|waldern", "water_demon"),
    (r"earth spirit|earth elemental|quaker", "earth_elemental"),
    (r"fire spirit|fire elemental|vargo|magma elemental", "dc-mon1/e/efreet"),
    (r"invisible stalker", "stalker"), (r"ooze elemental", "black_pudding"), (r"smoke elemental", "fog_cloud"),
    (r"ice elemental", "ice_vortex"), (r"will o' the wisp", "yellow_light"),
    # F dragon flies
    (r"white dragon fly", "dc-mon0/b/butterfly"), (r"green dragon fly", "dc-mon0/b/butterfly2"),
    (r"black dragon fly", "dc-mon0/b/butterfly5"), (r"gold dragon fly", "dc-mon0/b/butterfly3"),
    # G ghosts
    (r"tselakus", "death"), (r"dreadlord", "famine"), (r"dreadmaster", "pestilence"),
    (r"lost soul|moaning spirit|phantom|dread|^shadow$", "shade"), (r"poltergeist|ghost|banshee|spectre|spirit troll", "ghost"),
    # H hybrids
    (r"harpy|fury", "erinys"), (r"hippogriff", "dc-mon1/h/hippogriff"), (r"griffon", "dc-mon1/h/griffon"),
    (r"chimaera|gorgimaera", "leocrotta"), (r"manticore", "dc-mon1/h/sphinx"), (r"minotaur", "minotaur"), (r"baphomet", "yeenoghu"),
    # I insects
    (r"louse|flea", "grid_bug"), (r"fruit fly|hummerhorn|neekerbreeker|firefly", "xan"),
    # J snakes
    (r"large white snake|brownlands", "garter_snake"), (r"large grey snake|rattlesnake|black mamba", "pit_viper"),
    (r"copperhead", "water_moccasin"), (r"king cobra", "cobra"), (r"nightcrawler", "long_worm"),
    (r"serpent of chaos|serpent-sauron", "python"),
    # K beetles
    (r"stag beetle|slicer beetle|death watch", "dc-mon1/b/boulder_beetle"), (r"beetle", "dc-mon1/b/boring_beetle"),
    # L liches
    (r"archlich|feagwath|vecna", "arch-lich"), (r"black reaver", "master_lich"),
    # M hydras (dc-mon1/d/hydraN has N+1 heads)
    (r"2-headed hydra", "dc-mon1/d/hydra1"), (r"3-headed hydra", "dc-mon1/d/hydra2"),
    (r"4-headed hydra", "dc-mon1/d/hydra3"), (r"5-headed hydra", "dc-mon1/d/hydra4"), (r"hydra", "dc-mon1/d/hydra5"),
    # O ogres
    (r"ogre chieftain|lokkak", "ogre_king"), (r"black ogre|ogre shaman|ogre mage", "ogre_lord"), (r"ogre", "ogre"),
    # P giants
    (r"morgoth", "nh-mon0/0man/lord_surtur"), (r"cyclops", "nh-mon0/0man/cyclops"), (r"titan", "titan"),
    (r"cloud giant|gilim|\bnan\b", "giant"),
    # Q quylthulgs
    (r"rotting quylthulg", "brown_pudding"), (r"draconic quylthulg", "black_pudding"),
    (r"demonic quylthulg|master quylthulg|qlzqqlzuup", "juiblex"), (r"quylthulg", "quivering_blob"),
    # R reptiles and frogs
    (r"rock lizard", "lizard"), (r"cave lizard", "newt"), (r"night lizard", "iguana"),
    (r"green frog", "dc-mon1/f/spiny_frog"), (r"red frog", "dc-mon1/f/giant_brown_frog"),
    (r"salamander", "salamander"), (r"basilisk|tarrasque", "crocodile"),
    # S spiders, ticks and scorpions
    (r"scorpion", "scorpion"), (r"^wood spider|tick|phase spider", "cave_spider"), (r"spider|tarantula|shelob|ungoliant", "giant_spider"),
    # T trolls
    (r"snow troll", "ice_troll"), (r"cave troll", "water_troll"), (r"etten", "ettin"),
    (r"stone troll|bert|bill the|tom the|mountain troll", "olog-hai"),
    (r"^olog|troll blackguard|troll chieftain|rogrog", "olog-hai"), (r"troll", "water_troll"),
    # U major demons
    (r"balrog|lungorthin|gothmog", "balrog"), (r"glabrezu|horned reaper", "horned_devil"), (r"barbazu", "barbed_devil"),
    (r"bile demon", "juiblex"), (r"pit fiend", "geryon"), (r"osyluth", "bone_devil"), (r"gelugon", "ice_devil"), (r"pazuzu", "demogorgon"),
    # V vampires
    (r"master vampire|elder vampire", "vampire_mage"), (r"thuringwethil", "vampire_lord"),
    (r"vampire-sauron", "vlad_the_impaler"),
    # W wights and wraiths
    (r"uvatha|adunaphel|akhorahil|ren the|ji indur|dwar,|hoarmurath|khamûl|witch-king", "nazgul"),
    (r"wight", "barrow_wight"), (r"wraith|nightwing|nightwalker", "dc-mon1/w/freezing_wraith"),
    # X
    (r"xaren", "xorn"),
    # Z hounds: NetHack's dogs
    (r"fire hound|plasma hound", "hell_hound_pup"), (r"cold hound", "winter_wolf_cub"), (r"chaos hound|tindalos", "winter_wolf"),
    (r"nether hound|time hound|dark hound", "jackal"), (r"water hound", "coyote"), (r"light hound|energy hound|multi-hued hound", "dingo"),
    (r"earth hound|impact hound|gravity hound|inertia hound", "coyote"),
    (r"air hound|clear hound|aether hound|vibration hound|nexus hound", "fox"),
    # a ants
    (r"queen ant", "dc-mon1/q/queen_ant"), (r"army ant", "soldier_ant"), (r"red ant|fire ant", "fire_ant"),
    (r"\bant\b", "soldier_ant"),
    # b bats
    (r"fruit bat|disenchanter bat", "bat"), (r"vampire bat|gorgoroth|doombat", "vampire_bat"), (r"\bbat\b", "giant_bat"),
    # c centipedes
    (r"centipede", "centipede"), (r"carrion crawler", "scorpion"),
    # d young dragons and drakes
    (r"(baby|young) blue", "baby_blue_dragon"), (r"(baby|young) white", "baby_white_dragon"),
    (r"(baby|young) green", "baby_green_dragon"), (r"(baby|young) black|shadow drake", "baby_black_dragon"),
    (r"(baby|young) red", "baby_red_dragon"), (r"(baby|young) gold", "baby_yellow_dragon"),
    (r"(baby|young) multi-hued|chaos drake", "baby_shimmering_dragon"), (r"pseudo-dragon", "baby_orange_dragon"),
    (r"law drake|ethereal drake", "baby_silver_dragon"), (r"balance drake|crystal drake", "baby_gray_dragon"),
    (r"wyvern", "dc-mon1/d/wyvern"),
    (r"mature white", "white_dragon"), (r"mature blue", "blue_dragon"), (r"mature green", "dc-mon1/d/swamp_dragon"),
    (r"mature red", "red_dragon"), (r"mature gold", "yellow_dragon"), (r"mature black", "black_dragon"),
    (r"mature multi-hued", "nh-mon0/0man/chromatic_dragon"),
    # e eyes
    (r"spectator|gauth|beholder|omarax", "beholder"), (r"radiation eye|bloodshot eye|evil eye|eye druj", "dc-mon1/g/eye_of_devastation"),
    (r"\beye\b", "dc-mon1/g/shining_eye"),
    # f cats
    (r"scrawny cat", "kitten"), (r"wild cat", "lynx"), (r"sabre-tooth", "jaguar"), (r"displacer", "panther"),
    (r"tevildo", "large_cat"),
    # g golems
    (r"clay golem", "leather_golem"), (r"colbran", "dc-mon0/0golem/electric_golem"), (r"bone golem", "paper_golem"),
    (r"iron golem", "iron_golem"), (r"stone golem", "stone_golem"),
    (r"golem|drolem|pukelman|silent watcher|colossus", "gold_golem"),
    # h dwarves, elves, hobbits
    (r"father christmas", "nh-mon0/0man/croesus"), (r"bullroarer|maggot|sméagol|hobbit", "hobbit"),
    (r"red-hatted elf", "nh-mon0/0man/woodland-elf"), (r"green elf", "nh-mon0/0man/green-elf"),
    (r"eöl", "nh-mon0/0man/high-elf"), (r"fëanorian", "nh-mon0/0man/elf-lord"), (r"maeglin", "nh-mon0/0man/elvenking"),
    (r"blacklock mage|stiffbeard sorcerer", "gnomish_wizard"), (r"dark dwarven lord|mîm,|fundin", "dwarf_king"),
    (r"ironfist|nár|ibun|khîm", "dwarf_lord"), (r"dwarf|stonefoot", "dwarf"),
    # i icky things
    (r"(white|clear|grey) icky", "gelatinous_cube"), (r"icky", "quivering_blob"),
    # j jellies
    (r"green ooze|green jelly", "green_slime"), (r"blue ooze|white jelly", "gray_ooze"), (r"black ooze", "black_pudding"),
    (r"silver jelly", "blue_jelly"), (r"yellow jelly", "ochre_jelly"), (r"rot jelly|red jelly", "brown_pudding"),
    (r"grape jelly", "blue_jelly"),
    # k kobolds
    (r"mughash", "kobold_lord"), (r"kobold", "kobold"),
    # l trees
    (r"\btree\b|willow|huorn|\bent\b", "nh-dngn/tree"),
    # m molds
    (r"grey mold", "lichen"), (r"hairy mold|death mold", "brown_mold"), (r"disenchanter mold", "violet_fungus"),
    (r"shimmering mold", "yellow_mold"),
    # n nagas
    (r"green naga", "guardian_naga"), (r"spirit naga", "golden_naga"), (r"gorgon", "nh-mon0/0man/medusa"),
    # o orcs
    (r"azog", "nh-mon0/0man/goblin_king"), (r"snaga", "hobgoblin"), (r"cave orc", "orc"),
    (r"orc archer", "mordor_orc"), (r"orc tracker|grishn", "hill_orc"),
    (r"orc captain|lagduf|golfimbul|shagrat|gorbag|bolg", "orc-captain"), (r"uruk|ufthak|lugdush|ugl", "uruk-hai"),
    # p people
    (r"mouth of sauron", "nh-mon0/0man/wizard_of_yendor"), (r"wormtongue|necromancer", "nh-mon0/0man/healer"),
    (r"^soldier", "nh-mon0/0man/watchman"), (r"cutpurse|^rogue", "nh-mon0/0man/rogue"),
    (r"^acolyte", "nh-mon0/0man/acolyte"), (r"^apprentice", "nh-mon0/0man/apprentice"),
    (r"scout|southron archer", "nh-mon0/0man/hunter"), (r"gallant", "nh-mon0/0man/page"),
    (r"tamer", "nh-mon0/0man/caveman"), (r"ruffian|brigand", "nh-mon0/0man/thug"),
    (r"brodda|ulfast|ulwarth|uldor|berserker", "nh-mon0/0man/barbarian"), (r"^witch", "nh-mon0/0man/priestess"),
    (r"drúadan", "nh-mon0/0man/shaman_karnov"), (r"^priest", "nh-mon0/0man/priest"),
    (r"^warrior", "nh-mon0/0man/warrior"), (r"illusionist", "nh-mon0/0man/doppelganger"),
    (r"^druid", "nh-mon0/0man/hippocrates"), (r"blackguard|dúnadan of angmar", "nh-mon0/0man/captain"),
    (r"ranger", "nh-mon0/0man/ranger"), (r"paladin", "nh-mon0/0man/knight"),
    (r"easterling champion|ulfang|lorgan", "nh-mon0/0man/chieftain"),
    (r"sangahyando|castamir", "nh-mon0/0man/roshi"), (r"angamaitë", "nh-mon0/0man/samurai"),
    (r"black knight", "nh-mon0/0man/lieutenant"), (r"death knight", "nh-mon0/0man/lord_sato"),
    (r"^mage", "nh-mon0/0man/wizard"), (r"^sorcerer", "nh-mon0/0man/wizard_of_balance"),
    (r"beorn, the shape", "nh-mon0/0man/orion"), (r"assassin", "nh-mon0/0man/master_assassin"),
    (r"harowen", "nh-mon0/0man/ninja"), (r"grand master mystic", "nh-mon0/0man/grand_master"),
    (r"mystic", "nh-mon0/0man/monk"), (r"master thief", "nh-mon0/0man/master_of_thieves"),
    (r"demonologist", "nh-mon0/0man/master_kaen"), (r"lord of carn", "nh-mon0/0man/pelias"),
    (r"enchantress", "nh-mon0/0man/norn"), (r"patriarch", "nh-mon0/0man/arch_priest"),
    (r"gorlim", "nh-mon0/0man/prisoner"), (r"ar-pharaz", "nh-mon0/0man/king_arthur"),
    # q quadrupeds
    (r"bear", "dc-mon1/u/black_bear"), (r"catoblepas", "rothe"), (r"mûmak", "mumak"), (r"night mare", "warhorse"),
    # r rodents
    (r"mouse", "rabid_rat"), (r"spotted jelly", "blue_jelly"), (r"^lemure", "manes"), (r"\brat\b", "giant_rat"),
    # t town people
    (r"village idiot", "nh-mon0/0man/tourist"), (r"urchin", "nh-mon0/0man/student"),
    (r"blubbering idiot", "nh-mon0/0man/twoflower"), (r"wretch|leper", "nh-mon0/0man/prisoner"),
    (r"beggar", "nh-mon0/0man/human"), (r"squint-eyed rogue", "nh-mon0/0man/rogue"),
    (r"drunk", "nh-mon0/0man/guide"), (r"merchant", "nh-mon0/0man/shopkeeper"),
    (r"mercenary", "nh-mon0/0man/soldier"), (r"veteran", "nh-mon0/0man/watch_captain"),
    # u minor demons
    (r"nruling", "manes"), (r"draebor", "imp"), (r"bodak", "sandestin"), (r"quasit", "imp"),
    # v vortices
    (r"fire vortex|plasma vortex", "flaming_sphere"), (r"water vortex|wiruin", "steam_vortex"),
    (r"cold vortex", "ice_vortex"), (r"energy vortex|shimmering vortex", "energy_vortex"),
    (r"nexus vortex|shardstorm|chaos vortex", "dust_vortex"), (r"vortex|unmagic", "fog_cloud"),
    # w worms
    (r"wereworm", "long_worm"), (r"(purple|nether|abyss|disenchanter) worm mass", "baby_purple_worm"),
    (r"worm mass", "baby_long_worm"),
    # x
    (r"lurker", "lurker_above"),
    # y yeeks
    (r"yeek|orfax|boldor", "monkey"),
]

# ---------------------------------------------------------------------------------------------
# Objects. Paths under item/ (or elsewhere in the checkout).
BASES = {
    "sword": "weapon/long_sword", "hafted": "weapon/mace", "polearm": "weapon/halberd", "digger": "misc/pick_axe",
    "sling": "weapon/sling", "bow": "weapon/bow", "crossbow": "weapon/crossbow", "shot": "gem/grey_stone",
    "arrow": "weapon/arrow", "bolt": "weapon/crossbow_bolt", "soft_armour": "armor/leather_armor",
    "hard_armour": "armor/plate_mail", "shield": "armor/shield2", "helm": "armor/helm2", "gloves": "armor/glove1",
    "boots": "armor/boots_brown2", "cloak": "armor/cloak2", "light": "misc/brass_lantern", "amulet": "amulet/oval",
    "ring": "ring/wire", "magic_book": "book/light_blue", "prayer_book": "book/white",
    "nature_book": "book/dark_green", "shadow_book": "book/dark_gray", "potion": "potion/smoky",
    "scroll": "scroll/zelgo_mer", "food": "food/food_ration", "flask": "misc/can_of_grease",
    "crown": "armor/urand_dyrovepreva", "dragon_armour": "armor/red_dragon_scale_mail",
    "mushroom": "../nh-mon1/f/shrieker", "wand": "wand/iridium", "staff": "wand/staff01", "rod": "wand/long",
    "chest": "misc/chest", "gold": "gem/yellow",
}
KINDS = {
    # Swords.
    "dagger": "weapon/dagger", "main_gauche": "weapon/elven_dagger", "rapier": "weapon/silver_saber",
    "short_sword": "weapon/short_sword", "cutlass": "weapon/sabre2", "long_sword": "weapon/long_sword",
    "broad_sword": "weapon/broadsword", "bastard_sword": "weapon/long_sword2",
    "zweihander": "weapon/two_handed_sword", "tulwar": "weapon/falchion", "scimitar": "weapon/scimitar",
    "katana": "weapon/katana2", "executioners_sword": "weapon/greatsword", "blade_of_chaos": "weapon/runesword",
    # Hafted.
    "whip": "weapon/bullwhip", "mace": "weapon/mace", "war_hammer": "weapon/hammer",
    "morning_star": "weapon/morning_star", "flail": "weapon/flail", "lead_filled_mace": "weapon/mace2",
    "ball_and_chain": "misc/heavy_iron_ball", "two_handed_great_flail": "weapon/spiked_flail",
    "quarterstaff": "weapon/quarterstaff", "throwing_hammer": "weapon/hammer2", "maul": "weapon/mace_large",
    "great_hammer": "weapon/mace_large2", "mace_of_disruption": "weapon/eveningstar",
    "mighty_hammer": "weapon/morningstar2",
    # Polearms.
    "spear": "weapon/spear", "awl_pike": "weapon/spetum", "trident": "weapon/trident", "pike": "weapon/spear2",
    "halberd": "weapon/halberd", "throwing_axe": "weapon/hand_axe", "beaked_axe": "weapon/bec_de_corbin",
    "broad_axe": "weapon/broad_axe2", "battle_axe": "weapon/battle_axe", "lochaber_axe": "weapon/bardiche",
    "great_axe": "weapon/war_axe", "scythe": "weapon/scythe", "glaive": "weapon/glaive", "lance": "weapon/lance",
    "scythe_of_slicing": "weapon/fauchard", "lucerne_hammer": "weapon/guisarme",
    # Diggers, launchers, ammunition.
    "shovel": "misc/pick_axe", "pick": "misc/pick_axe", "mattock": "weapon/dwarvish_mattock",
    "short_bow": "weapon/bow", "long_bow": "weapon/elven_bow", "light_crossbow": "weapon/crossbow",
    "heavy_crossbow": "weapon/crossbow2", "iron_shot": "gem/grey_stone", "rounded_pebble": "gem/stone",
    "mithril_shot": "gem/white", "arrow": "weapon/arrow", "seeker_arrow": "weapon/ya", "bolt": "weapon/crossbow_bolt",
    # Armour.
    "soft_leather_armour": "armor/leather_armor3", "hard_leather_armour": "armor/leather_jacket",
    "robe": "armor/robe", "studded_leather_armour": "armor/leather_armour2",
    "soft_armour_leather_scale_mail": "armor/elven_leather_armor", "chain_mail": "armor/chain_mail",
    "metal_scale_mail": "armor/scale_mail", "augmented_chain_mail": "armor/orcish_chain_mail",
    "bar_chain_mail": "armor/ring_mail", "metal_brigandine_armour": "armor/banded_mail",
    "partial_plate_armour": "armor/bronze_plate_mail", "metal_lamellar_armour": "armor/scale_mail2",
    "full_plate_armour": "armor/plate_mail", "ribbed_plate_armour": "armor/breast_plate1",
    "mithril_chain_mail": "armor/elven_mithril_coat", "mithril_plate_mail": "armor/dwarvish_mithril_coat",
    "adamantite_plate_mail": "armor/dwarven_ringmail",
    "leather_shield": "armor/small_shield", "wicker_shield": "armor/small_shield2",
    "small_metal_shield": "armor/shield2", "large_metal_shield": "armor/large_shield",
    "knights_shield": "armor/shield_kite3", "mithril_shield": "armor/shield_of_reflection",
    "hard_leather_cap": "armor/cap2", "metal_cap": "armor/cap3", "iron_helm": "armor/helm2",
    "steel_helm": "armor/visored_helmet", "jewel_encrusted_crown": "armor/urand_dyrovepreva",
    "leather_gloves": "armor/glove1", "gauntlets": "armor/glove5", "set_of_mithril_gauntlets": "armor/riding_gloves",
    "set_of_caestus": "armor/fencing_gloves", "set_of_alchemists_gloves": "armor/padded_gloves",
    "leather_sandals": "armor/riding_boots", "leather_boots": "armor/boots_brown2",
    "pair_of_iron_shod_boots": "armor/iron_shoes", "pair_of_steel_shod_boots": "armor/combat_boots",
    "pair_of_mithril_shod_boots": "armor/snow_boots", "pair_of_ethereal_slippers": "armor/boots_blue1",
    "cloak": "armor/cloak2", "fur_cloak": "armor/animal_skin", "elven_cloak": "armor/elven_cloak",
    "ethereal_cloak": "armor/opera_cloak",
    "black_dragon_scale_mail": "armor/black_dragon_scale_mail", "blue_dragon_scale_mail": "armor/blue_dragon_scale_mail",
    "white_dragon_scale_mail": "armor/white_dragon_scale_mail", "red_dragon_scale_mail": "armor/red_dragon_scale_mail",
    "green_dragon_scale_mail": "armor/green_dragon_scales",
    "multi_hued_dragon_scale_mail": "armor/shimmering_dragon_scale_mail",
    "shining_dragon_scale_mail": "armor/silver_dragon_scale_mail", "law_dragon_scale_mail": "armor/gray_dragon_scale_mail",
    "gold_dragon_scale_mail": "armor/yellow_dragon_scale_mail", "chaos_dragon_scale_mail": "armor/orange_dragon_scale_mail",
    "balance_dragon_scale_mail": "armor/mpttled_dragon_armour", "power_dragon_scale_mail": "armor/gold_dragon_hide",
    # Lights.
    "wooden_torch": "misc/tallow_candle", "lantern": "misc/brass_lantern", "phial": "misc/misc_crystal",
    "star": "misc/misc_orb", "arkenstone": "misc/misc_stone",
    # Books: each realm in its own colours.
    "first_spells": "book/light_blue", "attacks_and_knowledge": "book/dark_blue", "magical_defences": "book/metal_blue",
    "arcane_control": "book/cyan", "wizards_tome_of_power": "book/turquoise",
    "novices_handbook": "book/white", "cleansing_power": "book/light_gray", "healing_and_sanctuary": "book/silver",
    "battle_blessings": "book/shining", "wrath_of_the_valar": "book/vellum",
    "lesser_charms": "book/dark_green", "gifts_of_nature": "book/metal_green", "creature_dominion": "book/tan",
    "nature_craft": "book/mottled", "wild_forces": "book/wrinkled",
    "into_the_shadows": "book/dark_gray", "dark_rituals": "book/velvet", "fear_and_torment": "book/violet",
    "deadly_powers": "book/magenta", "corruption_of_spirit": "book/book_of_the_dead",
    # Food and drink.
    "ration_of_food": "food/food_ration", "slime_mold": "food/slime_mold", "hard_biscuit": "food/cram_ration",
    "handful_of_dried_fruits": "food/grape", "scrap_of_flesh": "food/tripe_ration",
    "slice_of_meat": "food/huge_chunk_of_meat", "honey_cake": "food/honeycomb",
    "piece_of_elvish_waybread": "food/lembas_wafer", "flask_of_whisky": "potion/smoky",
    "pint_of_fine_wine": "potion/puce", "sip_of_miruvor": "potion/milky", "swig_of_orcish_liquor": "potion/black",
    "draught_of_the_ents": "potion/dark_green", "flask_of_oil": "misc/can_of_grease",
    # Chests and treasure.
    "small_wooden_chest": "misc/large_box", "large_wooden_chest": "misc/chest", "small_iron_chest": "misc/chest",
    "large_iron_chest": "misc/chest", "small_steel_chest": "misc/chest", "large_steel_chest": "misc/chest",
    "copper": "gem/orange", "silver": "gem/white", "garnets": "gem/red", "gold": "gem/yellow", "opals": "gem/white",
    "sapphires": "gem/blue", "rubies": "gem/red", "diamonds": "gem/white", "emeralds": "gem/green",
    "mithril": "gem/blue", "adamantite": "gem/green",
}
PILE = "misc/sack"

# Flavours: where each class finds its pictures, and words of flavour names that point at a picture.
FLAVOR_POOLS = {
    "potion": ["potion/" + n for n in ("black", "dark_green", "milky", "puce", "smoky", "swirly")],
    "ring": ["ring/" + n for n in ("brass", "clay", "copper", "coral", "engagement", "gold_white", "ivory",
                                   "plain_cyan", "plain_dark", "plain_magenta", "shiny", "twisted", "wire",
                                   "wooden", "urand_mage", "urand_robustness", "urand_shadows", "urand_shaolin")],
    "amulet": ["amulet/" + n for n in ("celtic_red", "circular", "concave", "crystal_green", "crystal_red",
                                       "eye_magenta", "hexagonal", "octagonal", "oval", "penta_green", "pyramidal",
                                       "ring_green", "spherical", "square", "stone2_blue", "stone2_red",
                                       "stone3_green", "triangular", "urand_bloodlust", "urand_cekugob",
                                       "urand_four_winds")],
    "wand": ["wand/" + n for n in ("aluminum", "iridium", "iron", "platinum", "steel", "tin", "uranium", "zinc",
                                   "runed", "spiked", "jeweled", "crystal", "glass", "hexagonal")],
    "staff": ["wand/staff0" + n for n in "125679"],
    "rod": ["wand/" + n for n in ("long", "short", "curved", "marble", "ebony", "maple", "pine", "oak", "balsa",
                                  "gem_wood")],
    "mushroom": ["../nh-mon1/f/" + n for n in ("brown_mold", "green_mold", "lichen", "red_mold", "shrieker",
                                              "violet_fungus", "yellow_mold")],
}
FLAVOR_WORDS = {
    "black": ["black", "plain_dark"], "dark green": ["dark_green"], "smoky": ["smoky"], "misty": ["milky"],
    "cloudy": ["milky"], "hazy": ["milky"], "bubbling": ["swirly"], "shimmering": ["swirly"], "vespertine": ["puce"],
    "coral": ["coral"], "ivory": ["ivory"], "jewelled": ["engagement"], "silver": ["shiny"], "platinum": ["gold_white"],
    "bronze": ["brass"], "brass": ["brass"], "copper": ["copper"], "gold": ["gold_white"], "sapphire": ["urand_mage"],
    "jade": ["urand_shaolin"], "hematite": ["urand_shadows"], "obsidian": ["plain_dark"], "jet": ["plain_dark"],
    "moonstone": ["urand_robustness"], "pearl": ["urand_robustness"], "ruby": ["urand_bloodlust", "stone2_red"],
    "crystal": ["crystal"], "azure": ["urand_cekugob", "stone2_blue"], "carved oak": ["oak"], "driftwood": ["wooden"],
    "aluminium": ["aluminum"], "iron": ["iron"], "cast iron": ["iron"], "steel": ["steel"], "tin": ["tin"],
    "zinc": ["zinc"], "runed": ["runed"], "rusty": ["iron"], "mithril": ["platinum"],
    "spotted": ["shrieker"], "moldy": ["green_mold", "brown_mold"], "furry": ["brown_mold"],
}
REUSE_PENALTY = 90
KEYWORD_BONUS = 400


# ---------------------------------------------------------------------------------------------
def bmp_read(path):
    """(width, height, [(r, g, b)...]) of an uncompressed 4-, 8- or 24-bit BMP."""
    d = open(path, "rb").read()
    offset, header = struct.unpack("<I", d[10:14])[0], struct.unpack("<I", d[14:18])[0]
    w, h = struct.unpack("<ii", d[18:26])
    bpp = struct.unpack("<H", d[28:30])[0]
    palette = [tuple(d[14 + header + i * 4:17 + header + i * 4][::-1]) for i in range(1 << bpp)] if bpp <= 8 else []
    stride = (w * bpp + 31) // 32 * 4
    pixels = []
    for y in range(abs(h)):
        base = offset + (abs(h) - 1 - y if h > 0 else y) * stride
        for x in range(w):
            if bpp == 8:
                pixels.append(palette[d[base + x]])
            elif bpp == 4:
                v = d[base + x // 2]
                pixels.append(palette[v >> 4 if x % 2 == 0 else v & 15])
            elif bpp == 24:
                pixels.append(tuple(d[base + 3 * x:base + 3 * x + 3][::-1]))
            else:
                raise SystemExit(f"{path}: {bpp}-bit BMPs are not supported")
    return w, abs(h), pixels


def hsv(rgb):
    return colorsys.rgb_to_hsv(*(v / 255 for v in rgb))


def colour_of(pixels):
    """The colour that makes a potion or gem what it is: its saturated pixels, if it has enough."""
    solid = [p[:3] for p in pixels if p[3]]
    vivid = [p for p in solid if hsv(p)[1] > 0.35 and max(p) > 40]
    use = vivid if len(vivid) > len(solid) // 8 else solid
    return tuple(sum(p[k] for p in use) / len(use) for k in range(3))


def distance(a, b):
    return sum((x - y) ** 2 for x, y in zip(a, b)) ** 0.5


def tint(pixels, target, variant):
    """Recolours the coloured part of a picture to target (RGB); variant n > 0 also shifts lightness."""
    th, ts, tv = hsv(target)
    solid = [i for i, p in enumerate(pixels) if p[3]]
    chosen = [i for i in solid if hsv(pixels[i][:3])[1] > 0.25 and max(pixels[i][:3]) > 40]
    if len(chosen) < len(solid) // 10:  # a grey picture: tint its light pixels instead
        chosen = [i for i in solid if hsv(pixels[i][:3])[2] > 0.3]
    mean_v = sum(hsv(pixels[i][:3])[2] for i in chosen) / max(len(chosen), 1) or 1
    light = [1.0, 1.25, 0.75, 1.45, 0.6][variant % 5]
    ns = ts if ts < 0.15 else min(1.0, 0.35 + ts * 0.65)
    out = list(pixels)
    for i in chosen:
        v = hsv(pixels[i][:3])[2]
        nv = min(1.0, v * max(tv, 0.25) / mean_v * light)
        r, g, b = colorsys.hsv_to_rgb((th + 0.04 * (variant // 5)) % 1.0, ns, nv)
        out[i] = (round(r * 255), round(g * 255), round(b * 255), pixels[i][3])
    return out


class Builder:
    def __init__(self, src, tileset_dir):
        self.src, self.dir = src, tileset_dir
        self.cells, self.index = [], {}
        self.nethack = {}
        for top in ("nh-mon0", "nh-mon1"):
            for root, _, files in os.walk(os.path.join(src, top)):
                for f in sorted(files):
                    if f.endswith(".bmp"):
                        rel = os.path.relpath(os.path.join(root, f), src)[:-4]
                        # The d/ and r/ were-creatures (animal form) win over 0man's human form.
                        if f[:-4] not in self.nethack or "/0man/" in self.nethack[f[:-4]]:
                            self.nethack[f[:-4]] = rel
        self.dcss = self._load_dcss(os.path.join(os.path.dirname(os.path.abspath(tileset_dir)), "dcss"))
        self.floor = self.load(FLOOR)

    @staticmethod
    def _load_dcss(folder):
        images = []
        for root, _, files in os.walk(folder):
            for f in files:
                if f.endswith(".png"):
                    read = png_read(os.path.join(root, f))
                    if read and read[0] == 32 and read[1] == 32:
                        images.append((os.path.relpath(os.path.join(root, f), folder),
                                       [p[:3] if p[3] >= 128 else None for p in read[2]]))
        return images

    def path(self, ref):
        """A reference as a path under the checkout: NetHack monster names are looked up."""
        return self.nethack.get(ref, ref)

    def load(self, rel):
        w, h, pixels = bmp_read(os.path.join(self.src, rel + ".bmp"))
        if (w, h) != (32, 32):
            raise SystemExit(f"{rel}: {w}x{h}, not 32x32")
        return [(0, 0, 0, 0) if p == BG else (*p, 255) for p in pixels]

    # The few pictures that may be the same as the dcss set's: without them the stand-ins were worse
    # than the sameness (black dragons drawn as grey ones, a sling as a leash, golems as gold ones).
    SHARED_WITH_DCSS = {"black_dragon", "iron_golem", "stone_golem", "centipede", "minotaur", "sling"}

    def check_not_dcss(self, rel, pixels):
        if os.path.basename(rel) in self.SHARED_WITH_DCSS:
            return
        mine = [p[:3] if p[3] else None for p in pixels]
        for name, theirs in self.dcss:
            same = sum(1 for a, b in zip(mine, theirs) if a == b and a is not None)
            either = sum(1 for a, b in zip(mine, theirs) if a is not None or b is not None)
            if same >= 0.9 * max(either, 1):
                raise SystemExit(f"{rel} is the same picture as dcss/{name}; choose another")

    def usable(self, ref):
        """Whether a tile is not one of the dcss folder's pictures."""
        try:
            self.check_not_dcss(ref, self.load(self.path(ref)))
            return True
        except SystemExit:
            return False

    def cell(self, ref, tint_to=None, variant=0):
        """Packs a tile (once) and returns its "main:col,row" reference."""
        on_floor = ref.startswith("floor:")
        rel = self.path(ref[6:] if on_floor else ref)
        key = (rel, on_floor, tint_to, variant)
        if key not in self.index:
            pixels = self.load(rel)
            self.check_not_dcss(rel, pixels)
            if on_floor:
                pixels = [f if p[3] == 0 else p for p, f in zip(pixels, self.floor)]
            if tint_to is not None:
                pixels = tint(pixels, tint_to, variant)
            self.index[key] = len(self.cells)
            self.cells.append(pixels)
        n = self.index[key]
        return f"main:{n % COLUMNS},{n // COLUMNS}"

    def item(self, rel, **kw):
        return self.cell(os.path.normpath(os.path.join("item", rel)), **kw)

    def write_sheet(self, path):
        rows = (len(self.cells) + COLUMNS - 1) // COLUMNS
        w, h = COLUMNS * 32, rows * 32
        out = [(0, 0, 0, 0)] * (w * h)
        for n, pixels in enumerate(self.cells):
            x0, y0 = n % COLUMNS * 32, n // COLUMNS * 32
            for y in range(32):
                out[(y0 + y) * w + x0:(y0 + y) * w + x0 + 32] = pixels[y * 32:(y + 1) * 32]
        png_write(path, w, h, out)


def plain(name):
    return unicodedata.normalize("NFC", name.lower())


def refs(builder, spec):
    if isinstance(spec, dict):
        return {k: refs(builder, v) for k, v in spec.items()}
    if isinstance(spec, list):
        return [refs(builder, s) for s in spec]
    return builder.cell(spec)


def main():
    src, data_dir, tileset_dir = sys.argv[1:4]
    b = Builder(src, tileset_dir)
    tiles = {}
    counts = {}

    # Terrain, shops, traps, player.
    for tid, spec in TERRAIN.items():
        tiles["terrain:" + tid] = refs(b, spec)
    for tid, ware in SHOPS.items():
        tiles["terrain:" + tid] = [b.cell("floor:nh-dngn/doorway"), b.item(ware)]
    for tid, trap in TRAPS.items():
        tiles["trap:" + tid] = b.cell("nh-dngn/" + trap)
    tiles["player"] = b.cell(PLAYER)

    # Monsters.
    by_name, by_rule, unmatched = 0, 0, []
    for m in json.load(open(os.path.join(data_dir, "monsters.json"), encoding="utf-8")):
        name = plain(m["name"])
        exact = name.replace(" ", "_")
        if exact in b.nethack and b.usable(exact):
            tiles["monster:" + m["id"]] = b.cell(exact)
            by_name += 1
            continue
        for pattern, ref in RULES:
            if re.search(pattern, name):
                tiles["monster:" + m["id"]] = b.cell(ref)
                by_rule += 1
                break
        else:
            unmatched.append(m["name"])
    counts["monsters by NetHack name"], counts["monsters by rule"] = by_name, by_rule
    if unmatched:
        print("monsters without a tile (the glyph fallback will draw them):", ", ".join(unmatched))

    # Objects.
    for base, rel in BASES.items():
        tiles["object-base:" + base] = b.item(rel)
    kinds = json.load(open(os.path.join(data_dir, "objects.json"), encoding="utf-8"))
    known = {k["id"] for k in kinds}
    for kid, rel in KINDS.items():
        if kid not in known:
            raise SystemExit(f"KINDS names an unknown object kind: {kid}")
        tiles["object:" + kid] = b.item(rel)
    tiles["object:pile"] = b.item(PILE)
    counts["object kinds"] = len(KINDS)

    # Flavours.
    colours = json.load(open(os.path.join(data_dir, "colors.json"), encoding="utf-8"))
    rgb = {k: tuple(int(v[i:i + 2], 16) for i in (1, 3, 5)) for k, v in colours.items()}
    flavours = 0
    for group in json.load(open(os.path.join(data_dir, "flavors.json"), encoding="utf-8")):
        pool = FLAVOR_POOLS.get(group["id"])
        if not pool:
            continue
        pictures = {rel: colour_of(b.load(os.path.normpath(os.path.join("item", rel)))) for rel in pool}
        uses = {rel: 0 for rel in pool}
        for fl in group.get("fixed", []) + group.get("flavors", []):
            name, target = fl["name"].lower(), rgb[fl["color"]]
            words = FLAVOR_WORDS.get(name, []) + [w for part in name.split() for w in FLAVOR_WORDS.get(part, [])]
            words += [name.replace(" ", "_")] + name.split()

            def score(rel):
                hit = any(w == rel.split("/")[-1] or w in rel.split("/")[-1].split("_") for w in words)
                return distance(target, pictures[rel]) + REUSE_PENALTY * uses[rel] - (KEYWORD_BONUS if hit else 0)

            best = min(pool, key=score)
            recolour = uses[best] > 0 or distance(target, pictures[best]) > 150
            if recolour and hsv(pictures[best])[1] < 0.15 and hsv(target)[1] < 0.15 and uses[best] == 0:
                recolour = False  # a grey picture for a grey flavour
            tiles[f"flavor:{group['id']}:{name}"] = (
                b.item(best, tint_to=target, variant=uses[best]) if recolour else b.item(best))
            uses[best] += 1
            flavours += 1
    counts["flavours"] = flavours

    sheet = os.path.join(tileset_dir, "rltiles.png")
    b.write_sheet(sheet)
    manifest = {
        "name": "RLTiles (32x32)",
        "author": "RLTiles contributors: Denzi, Alex Korol, Edger, Wan-ichi, So-Miya, Haruko Numata, Tatsuya, "
                  "Kelly Youngblood, Paul Pliska, John Harris, Dainokata, Zmy",
        "license": "Public domain, with a credit requested (https://rltiles.sourceforge.net/)",
        "source": "https://rltiles.sourceforge.net/",
        "tileWidth": 32,
        "tileHeight": 32,
        "sheets": {"main": "rltiles.png"},
        "opaque": False,
        "tiles": tiles,
    }
    with open(os.path.join(tileset_dir, "tileset.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        f.write("\n")
    counts["tiles in the sheet"] = len(b.cells)
    print(counts)


if __name__ == "__main__":
    main()
