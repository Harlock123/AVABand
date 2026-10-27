# Getting started

AVABand is a game of Angband 4.2. You are an adventurer in the town above the pits of Angband.
Far below, on level 99, waits **Sauron**; on level 100, **Morgoth, Lord of Darkness**. Kill them
both and you have won. Die on the way and the game is over: there is one life, and the save is
the character, as in Angband. The game saves by itself on every level change, on exit and every
five minutes of play (Ctrl+S saves now; Ctrl+X saves and quits). If it ever crashes, the next start
offers to resume from the last save and says how long ago that was. Earlier saves of each living
character are kept too (the last five, ten minutes or more apart): Game → Load character → *Earlier
saves of this character* rolls one back if a save is ever damaged.

To play a character on another computer, use **Game → Export this character...** (or **Export...**
in the Load window) to copy its save to a USB stick or a shared folder. On the other computer,
choose **Import a save from another computer** on the title screen (or **Import a save...** in the
Load window). If that computer already has the same character, you're asked before it's replaced, and
the one replaced is kept among its earlier saves. Your Home goes with the character; the strongroom,
high scores, graveyard, museum and settings stay on each computer.

## Your first steps

The game opens on its **title screen**: *Continue* your last character (or, if they died, *Play
again* as them), *Create a new character*, *Play now*, *Load* another, see the *High scores*, or
*Exit* — a letter, the arrows and Enter, a click or the gamepad pick. New to the game? **Play now**
skips the creation screen: you start in the town as one of a few forgiving characters (a Human
Warrior, a Dwarf Priest, a Hobbit Rogue, a High-Elf Mage...) with a random name, the Classic
options and the standard kit, and the messages say who you are. *Create a new character* gives you
full control next time.

Using a screen reader? Turn on Options → *Screen reader support*: messages, prompts and tips are
then read out, and **Ctrl+Shift+D** describes what is around you (and which monsters could kill you).
In a shop, what's for sale is read with each note on whether it would suit you, row by row as you
move, and the service on offer; a full pack is said as it fills (Windows, macOS and Linux).

Moments of note — the stairs, Word of Recall, a trap door or Deep Descent, a cavern, labyrinth, moria level, lair, gauntlet or hard centre, a deadly level, a unique met
for the first time, a ghost of the fallen (or one laid to rest), a great foe slain, a quest done, death — show a scene for a moment; any key skips it (and does nothing else — keys pressed before it appeared are dropped, and so are the repeats of a key you were holding down, so they can't skip it unseen or act behind it). A unique or a ghost met, a ghost laid to rest, a great foe slain and a quest done always stay until you press Space, Enter or Escape (or click). Options → *Show scenes
at moments of note* turns them off. Word of Recall has its own: a rent torn in the room (or the town square) where you read it, and a
vast ghostly hand reaching out of it to take you. Scenes have sounds too (footsteps on the stairs,
a bell at death, the rent tearing and a thunderclap), at the effects volume. Too quick? Options → *Scenes stay until you press Space* holds
each one until you press Space (Escape skips the rest). Your own pictures in the AVABand `art` folder beside your saves
(`stairs-down.png`, `stairs-up.png`, `recall-town.png`, `unique.png`, `death.png`...) replace them.

The game opens **full screen** (**F11**, or View → Full screen, toggles it, and it's remembered). **Ctrl+T** switches between letters and pictures, and **F10** (View → Display settings) chooses
among nine tilesets — Angband's own Original, Nomad, Adam Bolt and David Gervais sets, Dungeon
Crawl, Tangaria, DawnLike, RLTiles and Hexany's tinted one-colour set — with a live preview.

**Help → Check for updates...** asks GitHub whether a newer AVABand is out, and if one is, offers
to open its download page. It only goes online when you choose it — or, with Options → *Check
GitHub for a newer AVABand when the game starts*, once at start-up, speaking up only if there is one.

**No sound?** Its Sound tab says why: no audio device found, the device busy, or OpenAL's library
unable to load (on Windows on ARM, usually a missing Microsoft Visual C++ runtime; install the
Visual C++ Redistributable it names). The game plays on in silence either way.

New to Angband? **Game → Tutorial** teaches the basics on a short level of its own, step by step,
in a few minutes (it is never saved or scored): moving and picking up, doors, traps, fighting, Word
of Recall, the Prancing Pony's quests, a shop's notes on what would suit you and its services, tidying
your pack, and the Knowledge screen. Tips also appear at the foot of the map the first time something happens (your first
shop, monster, trap, bad wound...), and the first time you are blinded, confused, poisoned,
paralysed, frightened, stunned or left in the dark — saying what cures it — each just once. Options → *Hints for new players* turns them off.

- **Make a character**: Game → New character (Ctrl+N) picks a race, a class, stats and birth
  options. If you are new, a Human or Dwarf **Warrior** is the forgiving choice. *Compare races*
  sets every race side by side: stat changes, hit die, experience and what each can do (click one
  to choose it).
- **Shop in town**: each shop is painted its own colour, with its wares in its windows, a striped
  awning, its name over the roof, and a lantern lit by the door at night. Walk onto a shop's number (1–8) to go in (9 is the Prancing Pony, the inn; 0 the Arcane Artificer, who cuts sockets for gems) (the keeper may greet you, or pass on a
  hint worth hearing). Buy a few **Flasks of Oil** to
  throw (`v`), **Potions of Cure Light Wounds**, and **Scrolls of Phase Door** to get away. Buying
  or selling more than one, you're asked how many: type a number and Enter, or `a` for all of them.
  Sold something by mistake? **Ctrl+Z** buys back the last thing you sold, and **Tab** after the selling
  list shows everything you've sold on this visit, to buy back at what you were paid (nothing, with
  "no selling" on). The list is cleared when you leave the shop.
  The Home (8) keeps whatever you leave there. Under each piece of armour or weapon a line says how it
  compares with what you have ("Better", "Worse" or "Mixed", in green, red or amber), and arrows, shots
  and bolts say whether they fit your launcher. Inspecting something in your pack, or looking at it
  on the floor, says the same.
- **Go down**: find the `>` staircase and press `>`. Each level is 50 feet deeper and a little
  more dangerous. Take the stairs back up (`<`) whenever you like — levels are new each time
  (unless you chose persistent levels).
- **Come back up**: a **Scroll of Word of Recall** takes you to town and, read again, back to the
  deepest level you reached.

## Staying alive

- Your hit points are on the status bar at the bottom. When they run low, **leave**: quaff a
  potion, read Phase Door, take the stairs, or run.
- If you die, the game-over window says **what happened**: the last few blows and hurts, with
  the hit points each left you; any escapes (Phase Door, Teleportation, Word of Recall...) and
  healing still in your pack; and one tip chosen by how you fell — too deep for your level, a
  breath you didn't resist, a unique, hunger or poison. Only things you have learned to recognise
  are named. The character dump keeps it under *Last moments*.
- Rest (`R`) to heal and regain spell points when nothing is about.
- Eat when you are hungry. The **Food** line in the sidebar shows how fed you are and how many meals
  you carry; it turns orange and pulses when you're weak from hunger, and then the game asks whether
  to eat (Options: *Eat by yourself when weak from hunger* does it for you). **Ctrl+Shift+E** eats
  your best food at once. Keep a few rations — the General Store (1) sells them; the game warns you
  as they run out, and before you leave town with none (by the stairs or by Word of Recall).
- A monster you don't know is dangerous until you know it isn't. Look at it (`x` or `l`, as your
  keys have it) to read what you have learned of it; the Knowledge screen (`~`) keeps it all —
  and its **Feats** page lists milestones across all your characters (deep levels, great foes,
  quests, the daily dungeon and the weekly challenge, a win with each class), with who reached each first. Some feats unlock
  something — another title-screen picture, a frame for your character cards (Settings → Display →
  *Card frame*), or another starting kit on the creation screen (the *Delver's kit* and the
  *Scholar's kit* trade the Word of Recall scroll for goods worth no more, so the gold left over is
  the same or a little more) — and the **Unlocks** page lists them all, with what earns each. **Game → The graveyard** keeps a
  headstone for every character who died or retired, with their story and the last words they left
  (Help → Monsters: your later characters may find them scratched into the floor) — and *Watch their last
  moments* replays how they fell, while *Save a clip* makes an animated picture (a GIF) of it to
  share (the menu when a character dies offers one too). **Game → The museum** gathers what all your characters have
  done: every artifact found, every unique slain and every kind of monster seen, each with who did it
  first, and empty places for those still to come. **Game → Daily dungeon** is the same dungeon and character
  for everyone each day — compare how far you got with friends, and swap replays: *Copy result*
  puts a line about your try on the clipboard to paste to them, and *Save replay as…* saves its
  replay to send.
  **Game → Weekly challenge** (or *This week's challenge* on the title screen) is the same idea for
  a whole week, Monday to Sunday (UTC): one dungeon and one character for everyone, and a **twist**
  that changes the rules — a different one each week. Try as often as you like; every try goes on
  the weekly table, best first, and the share line says how many tries the week has had. The
  twists take turns, so none comes two weeks running: *Darkness* (no room in the dungeon is lit —
  bring your own light), *Glass cannon* (you deal half as much damage again, and take half as much
  again), *Famine* (food goes twice as fast), *Swarm* (every dungeon level has half as many monsters
  again), *Restless foes* (every monster is a little faster, +2 speed), *Gold rush* (gold you find
  is worth twice as much) and *Lean times* (only the General Store will sell to you; the other shops
  and the caravan won't). Like the daily dungeon, the weekly challenge has no hirelings, ghosts, rivals,
  strongroom or challenge runs, and it has three feats of its own: *Weekly Warrior* (finish a try),
  *Week's Deepest* (1000 ft in one) and *Four Weeks Running*.
  **Game → Save a character card** makes a picture of your
  character to share (the graveyard can make one for a headstone too).

## Seasonal events

Now and then the calendar brings a **seasonal event**: **All Hallows' Eve** (24 October to
7 November) and **Midwinter** (18 December to 6 January), by your own computer's date. A character
made while one runs is **born in it**, and keeps it **for life** — a character born at All Hallows'
Eve still meets its monsters in December, and one made the week after it ends never will. Its
replays play it back the same whatever the date you watch them.

- **Its monsters** join the dungeon's at their own depths: for All Hallows' Eve the pumpkin-headed
  scarecrow (from 200 ft), swarms of belfry bats, the corpse-candle that leads travellers astray,
  and, from 1100 ft, **the Headless Horseman**; for Midwinter the frost sprite, packs of winter
  wolves, the rime troll, and from 1200 ft **Rimefang, Lord of the Winter Pack**. You can read
  their lore and find them in the museum like any other.
- **Keepsakes**: now and then one of them lets fall something with a themed name — a *Carved
  Pumpkin Lantern*, a *Sugar Skull*, a *Witch's Hat*; a *Holly-Wreath Cloak*, a *Gingerbread
  Biscuit*, a *Yule Candle*. Each is an ordinary lantern, ration, cap, cloak, biscuit or torch in
  everything but its name; the event's unique always drops one.
- **The town** says what season it is when you arrive, and the title screen dresses up while the
  event runs (put a `title-halloween` or `title-midwinter` picture in your art folder to use your own).
- **A feat for each**: *Trick or Treat* (slay the Headless Horseman) and *Midwinter's Night* (slay
  Rimefang).

The daily dungeon, the weekly challenge and the tutorial are never born in an event. If you'd
rather play without them, turn off **Settings → Display → Seasonal events**: characters made after
that are born in none (those already born keep theirs).

## Where to learn more

The other pages here cover moving and commands, your character, fighting, magic, objects,
monsters, the dungeon and the birth options. **F1** lists every command with the keys you have.
