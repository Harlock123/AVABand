#!/usr/bin/env python3
"""Give every AVABand monster a Dungeon Crawl Stone Soup (CC0) tile.

For each monster in data/monsters.json without art in the DCSS tileset, pick a tile by exact name, then
by keyword rules, and add per-glyph fallbacks ("monster-glyph:<glyph>") so any future monster gets a
fitting creature. Only the PNGs used are copied into the tileset folder.

Usage:
    dcss_monster_tiles.py <DCSS "Full" folder> <data/monsters.json> <tilesets/dcss>
"""
import json, os, re, shutil, sys

# (regex on the lower-case name, DCSS path under monster/ without .png). First match wins.
RULES = [
    (r"creeping .*coins", "amorphous/acid_blob"), (r"lurker|trapper", "aberration/unseen_horror_new"),
    (r"potion mimic|scroll mimic|ring mimic", "abyss/wretched_star"),
    (r"dragon bat", "animals/fire_bat"),
    (r"tiger|panther|lion|leopard|jaguar|lynx|wildcat|\bcat\b|kitten|sabre", "../player/felids/cat_7"),
    (r"great hell wyrm|great wyrm of (chaos|law|balance)|dracolisk|dracolich", "undead/bone_dragon_new"),
    (r"(baby|young).*\b(blue)\b", "draconic/draconic_base-pale_new"),
    (r"(baby|young).*\bwhite\b", "draconic/draconic_base-white_new"),
    (r"(baby|young).*\bgreen\b", "draconic/draconic_base-green_new"),
    (r"(baby|young).*\bblack\b", "draconic/draconic_base-black_new"),
    (r"(baby|young).*\bred\b", "draconic/draconic_base-red_new"),
    (r"(baby|young).*\bgold", "draconic/draconic_base-yellow_new"),
    (r"(baby|young).*multi-hued", "draconic/draconic_base-mottle_new"),
    (r"(baby|young).*dragon", "draconic/draconic_base-brown_new"),
    (r"blue (dragon|wyrm)|storm (dragon|wyrm)", "dragons/storm_dragon_new"),
    (r"white (dragon|wyrm)|ice dragon|frost (dragon|wyrm|drake)", "dragons/ice_dragon_new"),
    (r"green (dragon|wyrm)", "dragons/swamp_dragon_new"),
    (r"black (dragon|wyrm)|shadow drake|death drake", "dragons/shadow_dragon"),
    (r"gold(en)? (dragon|wyrm)", "dragons/golden_dragon"),
    (r"(multi-hued|chaos|balance).*(dragon|wyrm|drake)", "dragons/mottled_dragon"),
    (r"(law|silver|ethereal).*(dragon|wyrm|drake)", "dragons/quicksilver_dragon_new"),
    (r"(bronze|crystal|pseudo).*(dragon|wyrm|drake)", "dragons/iron_dragon"),
    (r"red (dragon|wyrm)|fire drake|hell ?wyrm", "dragons/dragon"),
    (r"drake|wyvern", "dragons/wyvern_new"),
    (r"dragon(?! ?fly)|wyrm", "dragons/dragon"),
    (r"2-headed hydra", "dragons/hydra_2_new"), (r"3-headed hydra", "dragons/hydra_3_new"),
    (r"4-headed hydra", "dragons/hydra_4_new"), (r"hydra", "dragons/hydra_5_new"),
    (r"hell ?hound", "animals/hell_hound_new"), (r"hound", "animals/hound"),
    (r"warg|werewolf|wolf", "animals/warg"), (r"jackal|wild dog", "animals/jackal_new"),
    (r"\bdog\b|mongrel|mutt|cur\b", "animals/war_dog"),
    (r"orc (captain|chieftain|leader)|uruk|black orc|azog|bolg|lugdush|ugluk|shagrat|gorbag", "orc_knight_new"),
    (r"orc (shaman|sorcerer)", "orc_wizard_new"), (r"orc (priest)", "orc_priest_new"), (r"orc archer", "orc_new"),
    (r"\borc\b|snaga|grishn|golfimbul|ufthak|lagduf|ulfast", "orc_warrior_new"),
    (r"olog|eldrak", "iron_troll"), (r"troll (priest|scavenger)", "deep_troll_shaman"),
    (r"stone troll|cave troll|rock troll|half-troll", "rock_troll"), (r"troll", "troll"),
    (r"ogre (mage|shaman|chieftain)", "ogre_mage_new"), (r"ogre", "ogre_new"),
    (r"cyclops", "cyclops_new"), (r"titan", "titan_new"), (r"frost giant|ice giant", "frost_giant_new"),
    (r"fire giant", "fire_giant_new"), (r"stone giant", "stone_giant_new"), (r"ettin|two-headed", "ettin_new"),
    (r"storm giant|cloud giant", "titan_new"),
    (r"giant\b(?!.*(frog|louse|flea|ant|rat|spider|slug|centipede|beetle|white|black|gold|mouse|bat|leech|fly))", "hill_giant_new"),
    (r"kobold (chieftain|champion)|large kobold", "big_kobold_new"), (r"kobold (shaman|archer)", "kobold_demonologist"),
    (r"kobold", "kobold_new"),
    (r"tarantula", "animals/tarantella_new"), (r"phase spider", "animals/orb_spider"),
    (r"mirkwood|wolf spider", "animals/wolf_spider_new"), (r"spider|shelob|ungoliant|weaver", "animals/spider"),
    (r"soldier ant", "animals/soldier_ant_new"), (r"\bant\b|ant lion", "animals/giant_ant"),
    (r"\bbee\b|wasp|hornet", "animals/killer_bee"), (r"louse|flea|mite", "animals/giant_mite"),
    (r"fly\b|flies|mosquito|dragonfly|dragon fly", "animals/giant_blowfly"),
    (r"centipede", "animals/giant_centipede"), (r"beetle", "animals/giant_beetle"),
    (r"scorpion", "animals/giant_scorpion"), (r"cockroach|roach", "animals/giant_cockroach_new"),
    (r"mamba", "animals/black_mamba_new"), (r"python|anaconda|boa", "animals/ball_python"),
    (r"adder|viper|cobra", "animals/viper"), (r"snake|serpent|asp\b", "animals/snake"),
    (r"crocodile|alligator", "animals/crocodile"), (r"salamander", "salamander"),
    (r"lizard|gecko|iguana|chameleon|newt", "animals/giant_lizard"), (r"basilisk", "animals/basilisk"),
    (r"frog|toad", "animals/giant_frog"), (r"turtle|tortoise", "animals/turtle"),
    (r"polar bear|white bear", "animals/polar_bear"), (r"grizzly", "animals/grizzly_bear"),
    (r"black bear", "animals/black_bear_new"), (r"bear", "animals/bear"),
    (r"\brat\b|rats|mouse|mice|rodent|vole", "animals/grey_rat"),
    (r"fire bat|doombat", "animals/fire_bat"), (r"giant .*bat|vampire bat", "animals/giant_bat"), (r"\bbat\b", "animals/bat"),
    (r"crow|crebain|raven", "raven"), (r"eagle|hawk|falcon|bird|vulture|thrush", "animals/caustic_shrike"),
    (r"worm mass|worms", "animals/worm_new"), (r"purple worm|giant worm|were-?worm", "animals/rock_worm"),
    (r"worm|leech|slug", "animals/giant_slug"), (r"boar|hog|pig", "animals/hog_new"),
    (r"horse|pony|mumak|mûmak|elephant", "animals/elephant_new"), (r"yak|cow|bull|buffalo|ox\b", "animals/yak_new"),
    (r"sheep|goat|ram\b", "animals/sheep"),
    (r"beholder|eyes", "eyes/great_orb_of_eyes"), (r"eye\b|eyeball|gaze", "eyes/giant_eyeball"),
    (r"jelly", "amorphous/jelly"), (r"ooze|slime|pudding|blob", "amorphous/ooze_new"),
    (r"mushroom|shrieker|puffball|toadstool", "fungi_plants/wandering_mushroom_new"),
    (r"mold|mould|moss|fungus", "fungi_plants/giant_spore"),
    (r"old man willow|huorn|ent\b|tree", "fungi_plants/treant"),
    (r"skeleton|bones|skull", "undead/skeletal_warrior_new"),
    (r"zombi|zombie|corpse", "undead/rotting_hulk_new"), (r"mummy|mummified", "undead/mummy"),
    (r"poltergeist|moaning spirit|spirit|banshee|spectre|phantom|ghost|apparition|shade\b|phantasm",
     "undead/ghost_new"),
    (r"nazg|ringwraith|witch-king|khamûl|khamul|the dark elven|lord of", "undead/wight_king"),
    (r"wight", "undead/wight_new"), (r"wraith", "undead/wraith"), (r"vampire lord|master vampire", "undead/vampire_knight_new"),
    (r"vampire", "undead/vampire_new"), (r"lich|demilich", "undead/lich"), (r"ghoul|ghast", "undead/ghoul"),
    (r"shadow|dread", "undead/shadow_new"),
    (r"balrog", "demons/balrug_new"), (r"\bimp\b", "demons/imp"), (r"quasit", "quasit"),
    (r"homunculus|manes|lemure", "demons/lemure"), (r"vrock|hezrou|nalfeshnee|marilith|lesser balrog|pit fiend",
                                                     "demons/pit_fiend"),
    (r"demon|devil|fiend|bodak|succubus", "demons/red_devil_new"),
    (r"clay golem", "nonliving/clay_golem"), (r"stone golem|colbran", "nonliving/stone_golem"),
    (r"iron golem|steel golem|mithril golem|eog golem|drolem", "nonliving/iron_golem"),
    (r"flesh golem|bone golem", "nonliving/flesh_golem"), (r"golem", "nonliving/wood_golem"),
    (r"fire elemental|fire spirit", "nonliving/fire_elemental_new"),
    (r"water elemental|water spirit", "nonliving/water_elemental_new"),
    (r"earth elemental|earth spirit|xorn|xaren", "nonliving/earth_elemental"),
    (r"air elemental|air spirit", "nonliving/air_elemental_new"),
    (r"fire vortex|plasma vortex", "nonliving/fire_vortex"), (r"vortex", "nonliving/spatial_vortex"),
    (r"gargoyle", "nonliving/gargoyle"),
    (r"naga", "naga"), (r"centaur", "centaur"), (r"harpy", "harpy"), (r"hippogriff", "hippogriff_new"),
    (r"griffon|gryphon", "griffon"), (r"minotaur", "minotaur"), (r"manticore|chimera|vampire hybrid", "manticore"),
    (r"sphinx", "sphinx_new"), (r"phoenix", "phoenix"), (r"unicorn|pegasus", "holy/shedu_new"),
    (r"yeti|sasquatch", "ice_beast"), (r"quylthulg", "pulsating_lump"),
    (r"mind flayer", "giant_orange_brain"), (r"maia|ainu|istar|valar|eönwë|eonwe", "holy/angel_new"),
    (r"necromancer|death mage", "necromancer_new"), (r"illusionist|enchanter|enchantress|witch", "enchantress_human"),
    (r"mage|sorcerer|wizard|magus|conjurer|warlock|sage|saruman", "wizard"),
    (r"black knight|knight|death knight", "hell_knight_new"), (r"paladin|templar", "holy/paladin"),
    (r"dark elven|dark elf", "deep_elf_soldier"), (r"elven archer|ranger|archer", "deep_elf_master_archer"),
    (r"elven (priest|druid)|druid", "deep_elf_priest"), (r"\belf|elven|noldor|sindar|eldar", "elf_new"),
    (r"dwar|dwarf|mîm|mim\b|ibun|khîm|khim|nár|nar\b", "dwarf_new"), (r"hobbit|halfling|bullroarer", "halfling_new"),
    (r"gnome", "gnome"), (r"goblin", "goblin_new"), (r"boggart", "boggart_new"),
    # The rest, which nothing above fits: town folk, people, and 4.2's odder creatures.
    (r"farmer maggot", "halfling_old"), (r"father christmas", "unique/sigmund_new"),
    (r"blubbering idiot", "unique/norris"), (r"boil-covered|leper", "human_slave"), (r"beggar", "slave_freed"),
    (r"squint-eyed rogue|cutpurse|master thief", "unique/maurice_new"), (r"happy drunk|sméagol", "unique/crazy_yiuf"),
    (r"merchant", "unique/eustachio_new"), (r"mercenary|ruffian", "unique/duane"),
    (r"veteran|^warrior$|soldier", "unique/edmund_new"), (r"^rogue$|brigand|gorlim", "unique/jozef"),
    (r"southron assassin|harowen", "unique/sonja_new"), (r"apprentice", "unique/jessica_new"),
    (r"scout", "unique/robin"), (r"gallant", "unique/donald_new"), (r"tamer", "unique/natasha"),
    (r"acolyte|^priest$|patriarch", "deep_elf_high_priest"), (r"blackguard|dúnadan of angmar", "death_knight"),
    (r"mystic", "human_monk_ghost"), (r"berserker", "unique/rupert_new"),
    (r"easterling|ulwarth|uldor|ulfang|lorgan", "unique/terence_new"),
    (r"of umbar|castamir", "unique/louise"), (r"ar-pharaz", "unique/frederick_new"),
    (r"mouth of sauron", "undead/vampire_mage_new"), (r"beorn", "animals/grizzly_bear"),
    (r"stonefoot|ironfist|fundin", "deep_dwarf_berserker"), (r"fëanorian|feanorian", "deep_elf_blademaster"),
    (r"maeglin", "deep_elf_death_mage"),
    (r"white icky", "demons/ugly_thing_1"), (r"clear icky", "demons/very_ugly_thing"),
    (r"blubbering icky", "demons/ugly_thing_2"), (r"bloodshot icky", "demons/ugly_thing_3"),
    (r"grey icky", "demons/ugly_thing_4"), (r"green icky", "demons/ugly_thing_5"), (r"blue icky", "amorphous/azure_jelly_new"),
    (r"yeek", "animals/quokka_new"),
    (r"tick\b", "animals/giant_mite"), (r"wererat", "animals/orange_rat"), (r"umber hulk", "demons/beast"),
    (r"gelatinous cube", "amorphous/azure_jelly_old"), (r"craban", "raven"),
    (r"hummerhorn", "animals/giant_mosquito"), (r"neekerbreeker", "animals/giant_firefly"),
    (r"shambling mound", "fungi_plants/vine_stalker"), (r"chimaera|gorgimaera", "animals/catoblepas"),
    (r"jabberwock", "lindwurm"), (r"nruling", "demons/iron_imp_new"), (r"pukelman", "nonliving/ushabti"),
    (r"colossus", "nonliving/guardian_golem"), (r"carrion crawler", "demons/demonic_crawler"),
    (r"displacer beast", "../player/felids/cat_9"), (r"tevildo", "../player/felids/cat_10"),
    (r"spectator", "eyes/shining_eye_new"), (r"gauth", "eyes/eye_of_draining"),
    (r"invisible stalker", "aberration/unseen_horror_old"), (r"chest mimic", "../dungeon/chest_2_closed"),
    (r"smoke elemental", "demons/smoke_demon_new"), (r"ice elemental", "demons/blizzard_demon"),
    (r"magma elemental", "nonliving/molten_gargoyle"), (r"shardstorm", "nonliving/twister_1"),
    (r"will o' the wisp", "nonliving/orb_of_fire_new"), (r"etten", "ettin_old"), (r"night mare", "demons/hellion_new"),
    (r"kavlax", "unique/tiamat"), (r"ancalagon", "unique/tiamat_black"), (r"smaug", "golden_dragon"),
    (r"vargo", "nonliving/fire_elemental_old"), (r"waldern", "nonliving/water_elemental_old"),
    (r"quaker", "nonliving/iron_elemental"), (r"ariel", "nonliving/air_elemental_old"),
    (r"glabrezu", "demons/tormentor_new"), (r"barbazu", "demons/hairy_devil"), (r"osyluth", "demons/rotting_devil"),
    (r"gelugon", "demons/ice_devil"), (r"horned reaper", "demons/reaper_new"), (r"fury", "demons/hellwing"),
    (r"winged horror", "kenku_winged"), (r"gorgon", "naga_ritualist"), (r"hand druj", "undead/curse_toe"),
    (r"cantoras", "undead/ancient_lich_new"), (r"black reaver", "undead/ancient_lich_old"),
    (r"nightwing", "undead/shadow_wraith"), (r"nightwalker", "undead/silent_spectre"),
    (r"nightcrawler", "animals/anaconda_new"),
    (r"adunaphel|akhorahil|ren the unclean|ji indur|hoarmurath", "undead/wight_king"),
    (r"ossë|osse", "merfolk_avatar"), (r"makar", "holy/daeva"), (r"meássë|measse", "demons/sun_demon"),
    (r"wiruin", "nonliving/maelstrom_1"), (r"storm of unmagic", "nonliving/maelstrom_3"),
    (r"draugluin", "animals/wolf"), (r"carcharoth", "animals/hell_hound_old"), (r"tarrasque", "juggernaut"),
]

GLYPHS = {
    ",": "fungi_plants/wandering_mushroom_new", "A": "holy/angel_new", "B": "animals/caustic_shrike",
    "C": "animals/war_dog", "D": "dragons/dragon", "E": "nonliving/air_elemental_new", "F": "animals/giant_blowfly",
    "G": "undead/ghost_new", "H": "manticore", "I": "animals/giant_mosquito", "J": "animals/snake",
    "K": "animals/giant_beetle", "L": "undead/lich", "M": "dragons/hydra_5_new", "N": "human_new", "O": "ogre_new",
    "P": "hill_giant_new", "Q": "pulsating_lump", "R": "animals/giant_lizard", "S": "animals/spider", "T": "troll",
    "U": "demons/pit_fiend", "V": "undead/vampire_new", "W": "undead/wraith", "X": "nonliving/earth_elemental",
    "Y": "ice_beast", "Z": "animals/hound", "a": "animals/giant_ant", "b": "animals/bat",
    "c": "animals/giant_centipede", "d": "draconic/draconic_base-brown_new", "e": "eyes/giant_eyeball",
    "f": "../player/felids/cat_6", "g": "nonliving/stone_golem", "h": "dwarf_new", "i": "amorphous/acid_blob",
    "j": "amorphous/jelly", "k": "kobold_new", "l": "fungi_plants/treant", "m": "fungi_plants/giant_spore",
    "n": "naga", "o": "orc_warrior_new", "p": "human_new", "q": "animals/yak_new", "r": "animals/grey_rat",
    "s": "undead/skeletal_warrior_new", "t": "human_slave", "u": "demons/imp", "v": "nonliving/spatial_vortex",
    "w": "animals/worm_new", "y": "nonliving/insubstantial_wisp", "z": "undead/rotting_hulk_new",
    "~": "animals/giant_mosquito", "$": "amorphous/acid_blob", "!": "amorphous/acid_blob", "?": "amorphous/acid_blob",
    "=": "amorphous/acid_blob", "|": "nonliving/spectral_sbl", "#": "nonliving/earth_elemental",
    "+": "nonliving/earth_elemental", "&": "demons/red_devil_new", "*": "nonliving/crystal_golem",
    "@": "human_new", "x": "animals/giant_mite", "$ ": "amorphous/acid_blob",
}


def main():
    src, monsters_path, tileset_dir = sys.argv[1:4]
    monster_dir = os.path.join(src, "monster")
    files = {}
    for root, _, fs in os.walk(monster_dir):
        for f in sorted(fs):
            if f.endswith(".png"):
                base = re.sub(r"(_new|_old|-melee)?\.png$", "", f)
                files.setdefault(base, os.path.relpath(os.path.join(root, f), monster_dir))

    manifest_path = os.path.join(tileset_dir, "tileset.json")
    manifest = json.load(open(manifest_path, encoding="utf-8"))
    tiles = manifest["tiles"]
    used = set()

    def ref(path):
        rel = os.path.normpath(path if path.endswith(".png") else path + ".png")
        if not os.path.exists(os.path.join(monster_dir, rel)):
            raise SystemExit(f"missing DCSS tile: {rel}")
        used.add(rel)
        return os.path.normpath(os.path.join("monster", rel))

    monsters = json.load(open(monsters_path, encoding="utf-8"))
    counts = {"exact": 0, "rule": 0, "glyph": 0}
    for m in monsters:
        key = "monster:" + m["id"]
        if key in tiles:
            continue
        name = m["name"].lower()
        if m["id"] in files:
            tiles[key] = ref(files[m["id"]])
            counts["exact"] += 1
            continue
        for pattern, path in RULES:
            if re.search(pattern, name):
                tiles[key] = ref(path)
                counts["rule"] += 1
                break
        else:
            counts["glyph"] += 1
    for glyph in sorted({m["glyph"] for m in monsters} | set(GLYPHS) - {"$ "}):
        if glyph in GLYPHS:
            tiles["monster-glyph:" + glyph] = ref(GLYPHS[glyph])

    # Drop images a previous run copied that nothing uses any more.
    referenced = {v for v in tiles.values() if isinstance(v, str)}
    for root, _, fs in os.walk(os.path.join(tileset_dir, "monster")):
        for f in fs:
            rel = os.path.relpath(os.path.join(root, f), tileset_dir)
            if rel not in referenced and os.path.relpath(os.path.join(root, f), os.path.join(tileset_dir, "monster")) not in used:
                os.remove(os.path.join(root, f))
    for rel in sorted(used):
        dest = os.path.normpath(os.path.join(tileset_dir, "monster", rel))
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        shutil.copyfile(os.path.join(monster_dir, rel), dest)
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(counts, f"{len(used)} images")


if __name__ == "__main__":
    main()
