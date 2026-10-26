# Mods

A mod changes or adds to the game's data — monsters, objects, vaults, shops, races and the rest —
without touching the game's own files. AVABand's data is a folder of JSON files (the `data` folder
next to the game); a mod is a folder holding files of the same names, laid over the top.

## Where mods go

Each mod is a folder inside your mods folder. **Settings → Mods → Open the mods folder** opens it
(making it first if need be). It is:

- **Windows** — `%APPDATA%\AVABand\mods` (that is, `C:\Users\you\AppData\Roaming\AVABand\mods`).
- **Linux** — `~/.config/AVABand/mods` (or `$XDG_CONFIG_HOME/AVABand/mods`).
- **macOS** — `~/.config/AVABand/mods`.

Mods load in the order of their folder names, after the game's own data, so a later mod wins over
an earlier one. They are read when the game starts: after adding, changing or switching off a mod,
restart AVABand.

## The rule: same id replaces, new id adds

Every entry in a list file has an `id` ("jackal", "dagger"). An entry in a mod with the same id as
one of the game's **replaces it whole** — write out every field you want it to have, not just the
one you're changing. An entry with a new id **is added**. The game's other entries stay as they are.

- `colors.json` merges by colour name, the same way.
- `town.json`, `constants.json` and `starting_kit.json` are read whole: a mod's copy replaces the game's.
- `hints.json` (the shopkeepers' sayings) and `names.json` (the random artifact namer's words) add to the game's.

A few lists are keyed by something other than an id: `histories.json` by `chart`, `pain.json` by
`type`, `slays.json` and `brands.json` by `code`, `object_properties.json` by `type` and `code`,
`player_properties.json` by `type` and `code` (or `name`).

## The files a mod can hold

The same names as the game's own; a mod needs only the ones it changes.

- **Monsters** — `monsters.json`, `ava_monsters.json`, `monster_bases.json`, `monster_spells.json`, `summons.json`, `pain.json`, `blow_methods.json`, `blow_effects.json`.
- **Objects** — `objects.json`, `ava_objects.json`, `object_bases.json`, `ava_object_bases.json`, `egos.json`, `ava_egos.json`, `artifacts.json`, `ava_artifacts.json`, `curses.json`, `ava_curses.json`, `flavors.json`, `object_properties.json`, `slays.json`, `brands.json`, `starting_kit.json`.
- **Characters** — `races.json`, `ava_races.json`, `classes.json`, `ava_classes.json`, `realms.json`, `spells.json`, `histories.json`, `player_properties.json`, `shapes.json`.
- **The world** — `terrain.json`, `ava_terrain.json`, `traps.json`, `chest_traps.json`, `dungeon_profiles.json`, `pits.json`, `town.json`, `stores.json`, `quests.json`, `ava_quests.json`.
- **Vaults and rooms** — in a `templates` folder inside the mod: `templates/vaults.json`, `templates/ava_vaults.json`, `templates/room_templates.json`.
- **Everything else** — `elements.json`, `timed_effects.json`, `colors.json`, `constants.json`, `hints.json`, `names.json`.

The best guide to a file's fields is the game's own copy in its `data` folder: copy an entry, give it
a new id, and change it. Tilesets, sound packs, music and ambience are not mods: they have folders of
their own (Settings → Display and Settings → Sound say where).

## mod.json

A mod may have a `mod.json` saying what it is, shown in Settings → Mods. Every field is optional;
without the file, the mod goes by its folder's name.

`{ "name": "Example mod", "author": "You", "version": "1.0", "description": "What it does, in a sentence or two." }`

## A worked example

**Settings → Mods → Make an example mod** writes this one for you, as `example-mod` in your mods
folder, switched on: restart and it's in play. It adds one monster and one item.

`example-mod/ava_monsters.json` — a new monster, the mud newt (an `R`, from 100 ft):

`[ { "id": "example_mud_newt", "name": "mud newt", "glyph": "R", "color": "Umber", "base": "reptile", "depth": 2, "rarity": 1, "speed": 0, "hitPoints": 6, "armour": 6, "sleep": 20, "hearing": 12, "smell": 20, "experience": 3, "blows": [ { "method": "bite", "effect": "hurt", "damage": "1d3" } ], "flags": [ "ANIMAL", "HURT_COLD", "CLEAR_WEB" ], "description": "A slick brown newt as long as your arm, at home in the wet mud of the upper caves." } ]`

`example-mod/ava_objects.json` — a new item, the miner's pick-knife (a dagger-like blade):

`[ { "id": "example_pick_knife", "name": "Miner's Pick-knife~", "base": "sword", "level": 2, "commonness": 20, "minDepth": 1, "cost": 40, "weight": 14, "damage": "1d5", "flags": [ "THROWING" ], "color": "Slate", "description": "A short, heavy blade with a spike on its back, made for prising ore from the rock." } ]`

The `base` must be one the game knows (`monster_bases.json`, `object_bases.json`); the `~` in a
name marks where the plural's ending goes, as in the game's own names. With graphical tiles, a new monster or item
the tileset has no picture for is drawn as its letter.

To change one of the game's monsters instead, copy its whole entry from `data/monsters.json` into
your mod's `monsters.json`, keep its id, and change what you like.

## Settings → Mods

Lists every mod folder found, with its `mod.json`, each data file it holds and how many entries
that file adds and how many of the game's it replaces. A switch turns each mod off or on; the
change is kept with your settings and takes effect at the next start (*Restart AVABand to apply*).

A mod that breaks the game's data — a JSON mistake, an unknown colour, a monster whose base doesn't
exist — is left out, and the game starts without it; its errors are shown in red under it. Fix
them and restart.

## Modded characters

A character played with mods is marked: *[Modded]* in the high scores, the graveyard, the daily
dungeon's table and the saves list, and a "Mods:" line naming them in its character dump. Once
modded, always — switching the mods off later doesn't take the mark away. Scores aren't changed:
the mark just keeps the tables honest.

## Tips

- **Give your ids a prefix** of your own (`mymod_`), so they never clash with the game's or another mod's — a clash replaces silently.
- **Check Settings → Mods** after a change: the counts show whether your entries replaced or added what you meant, and any errors are listed there.
- **Name files exactly**: a file the game doesn't read (`monster.json` for `monsters.json`) is noted on the Mods tab and ignored.
- **Keep a copy**: a mod is just a folder, so it is easy to share — zip it and send it.
