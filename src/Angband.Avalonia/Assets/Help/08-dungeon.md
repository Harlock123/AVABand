# The dungeon

## Levels

Each level is 50 feet. Most are rooms and corridors (classic, or the looser "modified" kind); some
are Moria-style ragged caves (levels 10-39), labyrinths (from 13), caverns (from 15), lairs — half
rooms, half a cavern full of one kind of monster — and gauntlets (from 20), and hard centres, a
great vault ringed by caverns (from 50). A gauntlet's maze can't be mapped, and you can't teleport
in it or in the cavern you arrive in (a short blink of ten squares still works). A lair's monsters
guard a hoard at its heart — gold, a fine object, often a gem — and the cavern beyond a gauntlet's
maze holds a reward for whoever runs it. Vaults hold the
best treasure and the worst monsters. Arriving on a level out of the ordinary, you're told what
kind it is ("You are in a gauntlet...").
**Level feelings** tell you on arrival how dangerous the level seems, and once you have explored
a little, how good its treasure is (`Ctrl+F` repeats them).
Rooms near the surface are mostly lit, deeper ones mostly dark. A **lit room** has candles in
brass sconces along its walls (a warm glow on the wall in ASCII); a dark room's walls are bare, and
you see only as far as your own light (option *Sconces on the walls of lit rooms*).

## Shrines

Now and then — about one new level in six or seven, from 100 ft down — a level has a **shrine** in
one of its rooms: an altar (`_`) hallowed to one of the Valar — **Elbereth**, the Star-kindler;
**Aulë** the Smith; **Yavanna**, Giver of Fruits; **Mandos** the Doomsman (from 250 ft) — or, from
500 ft, a red-lit **dark altar of Morgoth**. You're told whose it is when you first see it, and
looking at it says so too. Step onto it (or press `_` while standing on it) and choose:

- **Pray** — a gamble. Most prayers are answered with a blessing of that power's kind (Elbereth's
  blessing, protection from evil or sight of the unseen; Aulë's heroism or resistance; Yavanna's
  healing; Mandos's restoring), and a rare one with something lasting (a point of a stat). Some
  bring a curse instead: slowness, the level woken around you, your life drained, the dead
  summoned. Morgoth's altar answers more often with great gifts — two of them — and far more often
  with two curses.
- **Offer gold or something from your pack** — a known reward, named before you choose: an
  offering of gold (more the deeper you are, twice as much at the dark altar), or a thing worth as
  much as far as you know it. Worth half as much earns only a blessing; three times as much, the
  gift and a blessing too. Elbereth maps the whole level; Aulë makes a good thing for your calling;
  Yavanna heals every hurt and cures every ill; Mandos makes known every rune on what you wear and
  restores your life and strength; Morgoth gives something great, made deep.
- **Desecrate** — a great risk for a great reward: a hoard of gold and a fine thing, or the power's
  wrath. Morgoth's altar pays more often than a shrine of the Valar (only the boldest try those).
- **Leave it** — nothing happens; it waits.

Each shrine answers once, then goes dark. The feats *Pilgrim* (use ten shrines) and *Blasphemer*
(desecrate one and live) are for those who use them. Turn shrines off with the birth option
*Shrines*; there are none in the daily dungeon, the weekly challenge or the tutorial.

## Traps, doors and rubble

Traps are hidden until you notice them: you see one when your search skill (better with level, and
with gear of searching) is up to how well hidden it is. If you suspect something, press **`S`** to
search carefully: a turn spent on the squares around you with your skill raised, more for every turn
you keep searching in the same spot (`0`, a number, then `S` searches that many turns). It finds secret
doors by touch, even in the dark, and tells you when there's nothing there. Running stops at a known one. Walking at a
trap you know of tries to disarm it, as `D` does; `-` walks onto it on purpose. Strange runes are
magical, disarmed with your magical disarming skill; the rest with the physical one. Feather
falling lets you float down pits and trap doors unhurt, and trap immunity keeps you safe from all.
Locked doors may take several tries (opening retries by itself). Rubble and walls can be
tunnelled (`T`), faster with a digger. Lava burns whoever stands in it (fire resistance and
feather falling soften it): a step that would cost more than a third of your hit points asks
first, running stops short of it and travel goes round. Monsters that fear fire won't set foot
in it, and one left in it burns. Monsters never set off traps (nor do they in Angband 4.2.5).

## Cursed levels

With the birth option *Cursed levels* (on by default), now and then — about one new level in ten,
from 250 ft down, never two in a row, and never a quest's level — a level lies under a **curse**. As
you arrive, before you've moved, you're shown it and told its terms, and asked: **stay** and take
them, or **take the stairs** you stand on and leave it. Leaving straight away costs you nothing but
the level (with connected stairs you arrive on a staircase back the way you came; come by a trap
door, Word of Recall or stairs that don't connect, and one is made under you). Escape stays. Each
curse is a bargain, its danger and its reward made with the level:

- **Teeming** — half as many monsters again; half as many objects again, every one of them good.
- **Restless dead** — bands of the undead, stronger than the depth; every undead slain here is
  worth twice the experience.
- **Blood moon** — every monster here is faster (+5 speed); everything found here, on the floor or
  dropped, is made as if 250 ft deeper.
- **Darkness** — every room is dark and your light reaches a square less (a light of one square
  stays one); gold here is worth twice as much, and something excellent lies hidden on the level.

While you're on a cursed level the status bar says so ("Cursed: Teeming"). You needn't clear it:
stay as long as you like and leave when you will. A kept level (persistent levels) keeps its curse.
Two feats go with them: *Cursed and Kept* (1,000 turns spent on cursed levels) and *Defied the
Curse* (leave a cursed level 500 ft down or deeper alive, having slain 30 monsters on it). Never in
the daily dungeon, the weekly challenge or the tutorial.

## Stairs

With *Generate connected stairs* (the default) you arrive on a staircase leading back the way you
came. With *Force player descent* you can never go up.

## Persistent levels

With the birth option *Persistent levels*, levels are kept: go back up and the level is as you
left it, with its monsters (a little recovered) and its treasure. The stairs line up from one
level to the next, and Word of Recall asks which kept level to return to.

## The level map

`M` shows the whole level squeezed into a window; the map on screen scrolls to follow you. `W`
(or `L` with Angband's keys) scrolls the view half a screen at a time to look about; Esc comes back.
