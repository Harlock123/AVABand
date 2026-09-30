# Balance check

`dotnet run -c Release --project tools/balance [levels per depth]` generates levels at depths 1–99
and reports what they hold, and how often a warrior of the depth's level (clvl = depth, at most 50,
12 hit points a level, starting kit) dies holding still for 300 turns there. The deaths column is
a crude, relative measure of danger, not a prediction of real play.

30 levels per depth, before the 4.2.5 data sync (2a23712) and after it (with 4.2.5's item
generation, egos, artifacts, traps, curses and lava):

| depth | objects before → after | egos % | artifacts per level | cursed % | gold per level | monsters | traps | deaths % |
|---:|---|---|---|---|---|---|---|---|
| 1 | 13.5 → 16.7 | 0.0 → 3.0 | 0.0 → 0.0 | 0.0 → 0.4 | 57 → 39 | 26 → 29 | 2.4 → 2.8 | 6.7 → 13.3 |
| 5 | 21.5 → 19.5 | 0.3 → 5.7 | 0.0 → 0.1 | 1.2 → 1.0 | 410 → 486 | 41 → 40 | 4.7 → 3.2 | 20.0 → 20.0 |
| 10 | 26.5 → 28.6 | 0.4 → 6.6 | 0.0 → 0.0 | 2.1 → 3.4 | 603 → 634 | 62 → 62 | 11.4 → 7.9 | 33.3 → 30.0 |
| 20 | 27.2 → 27.4 | 2.1 → 6.9 | 0.0 → 0.5 | 1.6 → 1.7 | 892 → 835 | 157 → 157 | 12.2 → 13.1 | 70.0 → 50.0 |
| 30 | 32.1 → 40.5 | 2.6 → 7.9 | 0.0 → 0.7 | 1.9 → 2.6 | 1698 → 1822 | 189 → 212 | 12.3 → 20.4 | 56.7 → 63.3 |
| 40 | 43.5 → 37.6 | 3.5 → 15.1 | 0.2 → 1.1 | 1.5 → 2.9 | 1717 → 1784 | 224 → 198 | 25.3 → 22.3 | 90.0 → 86.7 |
| 50 | 34.7 → 51.2 | 3.8 → 15.6 | 0.1 → 1.7 | 1.8 → 1.6 | 2160 → 2451 | 205 → 235 | 32.4 → 42.0 | 86.7 → 93.3 |
| 60 | 34.8 → 44.9 | 3.2 → 17.9 | 0.1 → 1.4 | 1.1 → 2.2 | 2662 → 1661 | 193 → 220 | 28.5 → 29.8 | 100.0 → 86.7 |
| 70 | 48.6 → 54.8 | 5.6 → 13.7 | 0.4 → 1.6 | 1.6 → 2.1 | 2731 → 1922 | 221 → 229 | 36.3 → 33.0 | 93.3 → 93.3 |
| 80 | 46.4 → 44.3 | 6.5 → 16.6 | 0.4 → 0.9 | 2.0 → 2.4 | 4818 → 2574 | 201 → 220 | 30.4 → 39.8 | 96.7 → 93.3 |
| 90 | 38.4 → 57.2 | 6.0 → 17.0 | 0.2 → 0.9 | 2.1 → 2.2 | 2871 → 2823 | 199 → 234 | 29.5 → 44.9 | 93.3 → 86.7 |
| 99 | 38.6 → 48.6 | 8.1 → 16.3 | 0.4 → 0.4 | 2.6 → 2.5 | 3012 → 3458 | 192 → 245 | 26.4 → 32.4 | 100.0 → 100.0 |

What changed, and why:

- **Egos** are two to five times as common: 4.2.5 makes a third and more of things good (33 + level
  in 100) and three in ten of those great, and its egos are commoner and shallower than ours were.
- **Artifacts** turn up about five times as often below 1000 ft: excellent objects get 4.2.5's
  artifact rolls, uniques' drops two more, and the special artifacts (the Phial, the Star, the rings
  of power) appear at all.
- **Curses** are about as common (one wearable in twenty), but now 4.2.5's 27, with powers.
- **Gold**, **monsters**, **traps** and **deaths** are within the noise of 30 levels; the danger of
  standing still is about what it was.

Nothing is badly off. The monster counts (about 200 a level from 1000 ft) come from 4.2.5's own
budget — 14 + 1d8 + a depth term, each with its escort or group — plus pits, nests and vaults; they
were the same before the sync.

## Playing it: the bot

`dotnet run -c Release --project tools/balance play [runs per depth]` plays each depth instead of
standing still. A warrior of the depth's level (clvl = depth, at most 50, with the hit points
that brings), wielding the best weapon and wearing the best body armour of thirty good objects made
for that depth, with healing potions (Cure Light Wounds shallow, Serious from 1000 ft, Critical from
2000 ft; 5 + depth/10 of them) and five Phase Door, explores the level (always to the nearest
spot at the edge of what it knows), walks up to and hits whatever is awake or no deeper than itself
(leaving breeders alone unless they are in the way), quaffs below half its hit points or when blind
or confused, blinks when nearly dead or afraid with something close, for up to 1000 turns. It is a
poor player, so its numbers are for comparing versions, not for judging the game's difficulty
absolutely.
`one <depth> <seed>` plays one run; with `BOT_TRACE=1` it prints the character and the last 60
messages.

20 runs per depth, at the start of this round of 4.2.5 ports (34fd754) and after it (4.2.5's summoning,
player_timed.txt, projection.txt's elements, chest traps, Remove Curse, make_object's book rule,
EASY_KNOW):

| depth | survived % | left level % | turns | kills | exp gained | level seen % | potions | blinks |
|---:|---|---|---|---|---|---|---|---|
| 1 | 100.0 → 95.0 | 0.0 → 0.0 | 1000.0 → 994.2 | 14.8 → 51.1 | 33.3 → 42.5 | 44.8 → 47.1 | 0.9 → 1.2 | 0.0 → 0.3 |
| 5 | 100.0 → 100.0 | 0.0 → 0.0 | 1000.0 → 1000.0 | 61.5 → 66.2 | 109.6 → 132.5 | 44.5 → 46.6 | 0.9 → 0.9 | 0.1 → 0.1 |
| 10 | 90.0 → 85.0 | 0.0 → 5.0 | 911.0 → 940.0 | 41.3 → 59.9 | 192.6 → 224.8 | 44.3 → 39.2 | 2.1 → 3.2 | 0.2 → 0.5 |
| 20 | 25.0 → 30.0 | 0.0 → 5.0 | 498.7 → 471.9 | 38.8 → 38.7 | 503.7 → 552.6 | 21.2 → 19.9 | 6.4 → 6.2 | 1.6 → 2.8 |
| 30 | 5.0 → 10.0 | 0.0 → 10.0 | 202.2 → 209.8 | 15.5 → 13.4 | 340.9 → 283.8 | 7.6 → 9.0 | 8.0 → 7.1 | 3.5 → 2.9 |
| 40 | 5.0 → 0.0 | 0.0 → 0.0 | 177.9 → 137.6 | 8.8 → 9.4 | 432.6 → 358.9 | 9.8 → 6.5 | 7.3 → 7.3 | 2.3 → 2.1 |
| 50 | 0.0 → 5.0 | 0.0 → 0.0 | 62.8 → 186.5 | 5.4 → 9.1 | 282.6 → 361.9 | 3.5 → 6.3 | 5.0 → 7.2 | 0.8 → 1.5 |
| 60 | 0.0 → 5.0 | 0.0 → 5.0 | 52.9 → 59.3 | 1.5 → 2.6 | 149.2 → 234.8 | 2.1 → 4.2 | 6.0 → 5.8 | 1.4 → 1.1 |
| 70 | 0.0 → 0.0 | 0.0 → 0.0 | 47.5 → 64.5 | 2.5 → 4.6 | 803.9 → 609.0 | 7.2 → 7.0 | 6.7 → 6.2 | 1.3 → 1.0 |
| 80 | 5.0 → 0.0 | 5.0 → 0.0 | 78.9 → 79.6 | 2.0 → 2.2 | 200.8 → 109.0 | 3.5 → 3.8 | 4.7 → 5.3 | 0.9 → 0.8 |
| 90 | 5.0 → 5.0 | 5.0 → 5.0 | 35.9 → 44.1 | 0.5 → 1.4 | 66.3 → 85.3 | 2.4 → 2.3 | 2.7 → 2.2 | 0.8 → 0.4 |
| 99 | 0.0 → 40.0 | 0.0 → 40.0 | 15.1 → 8.0 | 0.6 → 0.4 | 87.0 → 2.4 | 1.0 → 0.7 | 1.6 → 0.9 | 0.5 → 0.2 |

What changed, and why:

- **Within the noise** almost everywhere: survival, turns, kills and experience move by a run or
  two in twenty either way. The high elements now resist as 4.2.5 has them (6/(8+1d4) rather than
  3.x's figures) and cuts, poison and stunning heal with Constitution; neither shows at this size.
- **4950 ft**: the level holds Sauron (his quest). Four runs in ten are now sent up a level alive
  within a few turns (traced: Sauron, as a wolf or himself, and unseen casters using teleport-level)
  where none of twenty were before; runs there last under 15 turns either way. Why the share moved
  isn't established — the RNG takes a different course from the first level on, and summoning (now
  4.2.5's: tries up to the spell's dice until the summoned levels, squared, reach depth × the
  caster's level) changes who is around Sauron.
- **Kills at 50 ft** rose (15 → 51). The one death there (a run of 883 turns and 257 kills) was a
  warrior walled in by breeding giant white mice; how much of the rise is breeders is not measured.

## Level generation as 4.2.5 has it

Before (56e4c58, AVABand's own generators) and after the port of 4.2.5's `generate.c` and
`gen-*.c`. Standing still, 30 levels per depth:

| depth | objects | egos % | artifacts per level | cursed % | gold per level | monsters | traps | deaths % |
|---:|---|---|---|---|---|---|---|---|
| 1 | 15.2 → 12.9 | 2.0 → 1.8 | 0.1 → 0.0 | 0.4 → 0.3 | 58.9 → 52.7 | 28.9 → 29.1 | 1.9 → 1.2 | 13.3 → 26.7 |
| 5 | 19.5 → 13.1 | 6.7 → 4.8 | 0.0 → 0.0 | 1.2 → 1.0 | 471.3 → 470.8 | 41.2 → 39.9 | 4.7 → 2.0 | 26.7 → 33.3 |
| 10 | 25.0 → 14.5 | 7.3 → 1.8 | 0.1 → 0.0 | 3.7 → 3.0 | 516.3 → 603.8 | 65.4 → 53.2 | 8.5 → 2.5 | 26.7 → 60.0 |
| 20 | 31.3 → 14.2 | 8.1 → 7.0 | 0.4 → 0.1 | 2.0 → 2.8 | 884.3 → 876.7 | 170.1 → 77.5 | 12.5 → 2.5 | 43.3 → 73.3 |
| 30 | 34.2 → 15.2 | 9.5 → 4.6 | 0.4 → 0.0 | 2.9 → 2.2 | 1487.5 → 1907.8 | 202.4 → 107.9 | 14.7 → 3.4 | 73.3 → 70.0 |
| 40 | 59.0 → 16.4 | 14.1 → 5.5 | 1.5 → 0.1 | 2.7 → 3.3 | 1353.7 → 2278.7 | 265.8 → 111.8 | 29.5 → 2.9 | 86.7 → 63.3 |
| 50 | 50.4 → 16.4 | 13.0 → 6.7 | 0.8 → 0.2 | 2.6 → 1.0 | 1962.0 → 3445.7 | 222.8 → 98.0 | 39.8 → 4.3 | 83.3 → 80.0 |
| 60 | 40.9 → 14.7 | 14.6 → 7.3 | 1.0 → 0.2 | 2.1 → 1.1 | 1999.7 → 2647.8 | 216.8 → 103.2 | 34.6 → 5.2 | 90.0 → 86.7 |
| 70 | 48.2 → 14.7 | 14.3 → 8.4 | 1.1 → 0.2 | 2.5 → 2.5 | 2174.2 → 3222.3 | 219.1 → 96.6 | 23.3 → 4.7 | 93.3 → 86.7 |
| 80 | 48.2 → 19.5 | 19.0 → 10.9 | 1.5 → 0.2 | 1.8 → 1.7 | 2911.0 → 3316.0 | 203.5 → 104.3 | 36.0 → 7.0 | 93.3 → 96.7 |
| 90 | 41.8 → 15.2 | 15.3 → 8.6 | 0.9 → 0.1 | 2.2 → 1.1 | 4045.8 → 3556.2 | 204.6 → 103.9 | 45.6 → 4.0 | 96.7 → 90.0 |
| 99 | 43.6 → 13.6 | 15.9 → 11.5 | 0.4 → 0.1 | 2.1 → 1.2 | 3265.7 → 3299.3 | 207.1 → 108.7 | 28.9 → 3.0 | 100.0 → 93.3 |

Playing, 20 runs per depth:

| depth | survived % | left level % | turns | kills | exp gained | level seen % | potions | blinks |
|---:|---|---|---|---|---|---|---|---|
| 1 | 95.0 → 95.0 | 0.0 → 0.0 | 994.2 → 983.5 | 51.1 → 70.8 | 42.5 → 48.0 | 47.1 → 77.8 | 1.2 → 2.2 | 0.3 → 0.3 |
| 5 | 100.0 → 90.0 | 0.0 → 0.0 | 1000.0 → 939.1 | 66.2 → 150.0 | 132.5 → 225.6 | 46.6 → 66.6 | 0.9 → 2.2 | 0.1 → 0.5 |
| 10 | 85.0 → 75.0 | 5.0 → 0.0 | 940.0 → 862.0 | 59.9 → 115.9 | 224.8 → 382.2 | 39.2 → 65.4 | 3.2 → 3.3 | 0.5 → 1.3 |
| 20 | 30.0 → 35.0 | 5.0 → 5.0 | 471.9 → 574.2 | 38.7 → 43.3 | 552.6 → 624.6 | 19.9 → 33.4 | 6.2 → 5.4 | 2.8 → 1.8 |
| 30 | 10.0 → 20.0 | 10.0 → 5.0 | 209.8 → 316.3 | 13.4 → 16.7 | 283.8 → 524.5 | 9.0 → 20.5 | 7.1 → 6.5 | 2.9 → 2.8 |
| 40 | 0.0 → 20.0 | 0.0 → 0.0 | 137.6 → 317.8 | 9.4 → 23.3 | 358.9 → 804.2 | 6.5 → 11.1 | 7.3 → 7.8 | 2.1 → 1.3 |
| 50 | 5.0 → 5.0 | 0.0 → 5.0 | 186.5 → 167.9 | 9.1 → 8.2 | 361.9 → 824.9 | 6.3 → 9.9 | 7.2 → 6.7 | 1.5 → 1.4 |
| 60 | 5.0 → 0.0 | 5.0 → 0.0 | 59.3 → 97.0 | 2.6 → 4.5 | 234.8 → 564.7 | 4.2 → 10.7 | 5.8 → 7.1 | 1.1 → 1.3 |
| 70 | 0.0 → 0.0 | 0.0 → 0.0 | 64.5 → 58.6 | 4.6 → 3.4 | 609.0 → 586.9 | 7.0 → 10.0 | 6.2 → 7.2 | 1.0 → 1.7 |
| 80 | 0.0 → 5.0 | 0.0 → 5.0 | 79.6 → 65.5 | 2.2 → 4.0 | 109.0 → 251.3 | 3.8 → 6.2 | 5.3 → 3.6 | 0.8 → 0.5 |
| 90 | 5.0 → 0.0 | 5.0 → 0.0 | 44.1 → 33.7 | 1.4 → 2.2 | 85.3 → 135.6 | 2.3 → 3.3 | 2.2 → 4.3 | 0.4 → 1.0 |
| 99 | 40.0 → 15.0 | 40.0 → 15.0 | 8.0 → 13.6 | 0.4 → 0.8 | 2.4 → 63.9 | 0.7 → 1.3 | 0.9 → 1.3 | 0.2 → 0.3 |

What changed, and why:

- **Objects, traps and monsters** are about half, a tenth and a half of what they were. That is
  4.2.5's budget: a classic level gets Rand_normal(9, 3) objects in rooms, Rand_normal(3, 3)
  anywhere and as much gold, randint1(k)/5 traps in corridors (k being depth/3, at most 10), and
  14 + 1d8 + k monsters with their groups, plus what vaults, pits, nests and room templates hold.
  AVABand's old generators were more generous with all three. Artifacts per level and the share of
  egos fell too, but most of that was a bug, since fixed (see below).
- **Standing still is more dangerous** in the shallows (13 → 27% at 50 ft, 27 → 60% at 500 ft): the
  old spawner kept random monsters more than ten squares from where you start, and 4.2.5's
  `pick_and_place_distant_monster` is called with a distance of 0, so one can be asleep beside you.
  Deeper, fewer monsters make it a little less deadly.
- **Playing is easier and the bot sees more**: with half the monsters it survives longer, kills
  more and explores up to twice as much of each level, from
  500 ft to 2000 ft especially. Deeper than 3000 ft the runs are too short to tell; at 4950 ft
  it is sent up from Sauron's level three times in twenty rather than eight.
- **Gold per level** is up by a third or so from 2000 ft. Both generators plan about 3.3 piles of
  gold anywhere (Rand_normal(3, 3), none if it comes out below one); the port adds 4.2.5's gold in
  vaults and rooms — a quarter of `vault_objects`, the `$` of vaults and room templates. Counting
  the plans over 100 levels at 3000 ft: 421 piles against the old 323.

The object, trap and monster counts follow 4.2.5's own formulas, so they aren't a reason to
change the generator.

### A bug, and the egos explained

vault.txt gives most vaults `max-depth:0`, which 4.2.5 reads as no limit; the port read it
literally, so 129 of the 162 vaults — every lesser vault among them — could never be chosen. With
that fixed (30 levels per depth, standing still):

| depth | objects | egos % | artifacts per level | gold per level | monsters | traps |
|---:|---|---|---|---|---|---|
| 10 | 14.9 | 2.5 | 0.0 | 642 | 53 | 2.5 |
| 20 | 15.0 | 5.6 | 0.1 | 799 | 72 | 3.9 |
| 40 | 26.4 | 9.7 | 0.5 | 1274 | 115 | 5.0 |
| 60 | 22.9 | 15.4 | 0.7 | 2764 | 114 | 6.8 |
| 80 | 28.9 | 13.4 | 0.5 | 3976 | 118 | 6.8 |
| 99 | 34.4 | 19.7 | 0.1 | 3889 | 154 | 6.2 |

From 2000 ft the share of egos is back where it was before the port (13-20%). Above 1000 ft it
stays lower (2.5% against 7.3% at 500 ft), and that is 4.2.5's design: good objects come mostly
from vault treasure, made 3 to 20 levels deeper, and neither classic nor modified levels have
lesser vaults before level 20 or medium ones before 30. Over 100 levels at 500 ft the old
generator planned about 300 good objects and the port plans 2.

## A better bot

The bot now rests when nothing awake is in view and it is below 70% of its hit points (or half its
mana), backs into a corridor (a square with two open neighbours or fewer, within eight steps and
no nearer the foes) when two or more awake monsters come at it in the open, waits there a few
turns for them, and shoots what is awake and in the line of fire: it wields the launcher with the
most might among thirty good ones made for the depth and carries 40 plain missiles for it.
`BOT_PLAIN=1` plays as before. 20 warrior runs per depth, plain → better, on the same levels
(after the vault fix):

| depth | survived % | turns | kills | exp gained | level seen % | potions | rests | shots |
|---:|---|---|---|---|---|---|---|---|
| 1 | 100.0 → 100.0 | 1000.0 → 1000.0 | 61.1 → 62.3 | 48.2 → 43.9 | 80.2 → 79.3 | 2.0 → 1.1 | 0.3 | 10.6 |
| 5 | 90.0 → 95.0 | 939.1 → 978.6 | 135.3 → 116.2 | 185.9 → 177.4 | 67.1 → 68.7 | 2.3 → 1.3 | 3.2 | 20.0 |
| 10 | 70.0 → 90.0 | 821.3 → 980.0 | 107.5 → 132.6 | 374.1 → 387.1 | 63.0 → 64.6 | 3.8 → 1.8 | 2.9 | 24.4 |
| 20 | 40.0 → 55.0 | 635.9 → 719.2 | 39.4 → 57.6 | 590.7 → 671.8 | 35.4 → 38.0 | 5.0 → 5.4 | 18.7 | 20.9 |
| 30 | 10.0 → 40.0 | 322.6 → 509.8 | 25.4 → 39.2 | 603.5 → 614.8 | 22.3 → 22.9 | 7.0 → 6.1 | 9.4 | 14.1 |
| 40 | 15.0 → 20.0 | 277.3 → 225.4 | 17.4 → 7.6 | 583.1 → 391.7 | 12.6 → 9.9 | 7.1 → 7.2 | 2.6 | 12.4 |
| 50 | 15.0 → 10.0 | 162.5 → 203.0 | 8.6 → 7.5 | 453.8 → 370.6 | 9.8 → 10.8 | 5.9 → 6.3 | 1.4 | 7.1 |
| 60 | 5.0 → 15.0 | 146.6 → 156.8 | 9.8 → 3.9 | 488.0 → 277.5 | 12.9 → 5.5 | 7.1 → 7.5 | 5.3 | 7.9 |
| 70 | 0.0 → 10.0 | 67.0 → 163.7 | 2.8 → 5.8 | 416.6 → 1046.1 | 10.7 → 5.7 | 6.8 → 5.4 | 2.8 | 4.4 |
| 80 | 0.0 → 0.0 | 70.3 → 40.2 | 4.8 → 3.6 | 177.4 → 172.0 | 6.0 → 2.8 | 5.0 → 4.2 | 0.5 | 4.7 |
| 90 | 0.0 → 5.0 | 29.3 → 73.5 | 1.5 → 3.6 | 120.6 → 499.9 | 3.0 → 3.6 | 3.3 → 2.8 | 0.4 | 3.4 |
| 99 | 5.0 → 0.0 | 15.2 → 16.1 | 1.5 → 0.4 | 142.2 → 75.7 | 1.3 → 1.4 | 1.5 → 1.6 | 0.1 | 0.6 |

Better between 500 and 1500 ft (70 → 90%, 40 → 55%, 10 → 40% alive), with fewer potions
drunk; deeper, 20 runs are too few to tell. It is still a poor player: it doesn't read unknown
scrolls, pick things up, flee up stairs or avoid what it can't beat.

### A mage

`play [runs] mage` gives the bot a mage: the class's books up to its level, every spell it can
learn from them, and the strongest of mana storm, mana bolt, fire ball, acid spray, frost bolt and
magic missile it knows, can pay for and fails no more than one time in four, cast at anything
awake in the line of fire; otherwise the same gear, potions and play. 20 runs per depth:

| depth | survived % | turns | kills | exp gained | level seen % | potions | blinks | rests | casts and shots |
|---:|---|---|---|---|---|---|---|---|---|
| 1 | 80.0 | 919.9 | 92.3 | 55.4 | 67.9 | 3.6 | 1.2 | 7.0 | 12.7 |
| 5 | 75.0 | 847.2 | 125.2 | 201.8 | 54.8 | 3.4 | 1.6 | 13.1 | 28.1 |
| 10 | 40.0 | 647.7 | 52.8 | 199.8 | 34.3 | 4.6 | 2.2 | 25.1 | 16.9 |
| 20 | 20.0 | 454.8 | 57.2 | 541.4 | 27.9 | 5.7 | 2.6 | 22.0 | 23.2 |
| 30 | 0.0 | 166.8 | 19.7 | 184.2 | 10.4 | 7.1 | 2.7 | 5.1 | 9.4 |
| 40 | 10.0 | 197.2 | 6.8 | 1343.6 | 7.4 | 7.0 | 1.8 | 3.9 | 6.2 |
| 50 | 10.0 | 141.2 | 10.0 | 765.8 | 6.9 | 5.6 | 1.8 | 2.6 | 4.0 |
| 60 | 10.0 | 125.7 | 2.8 | 1019.5 | 7.0 | 3.6 | 1.3 | 1.1 | 2.8 |
| 70 | 0.0 | 46.1 | 2.1 | 600.2 | 2.7 | 3.8 | 0.8 | 3.2 | 2.6 |
| 80 | 0.0 | 55.2 | 1.3 | 342.0 | 1.9 | 2.9 | 0.8 | 0.5 | 1.3 |
| 90 | 15.0 | 60.3 | 4.2 | 435.6 | 2.2 | 2.5 | 0.4 | 0.3 | 1.1 |
| 99 | 5.0 | 7.2 | 0.3 | 605.3 | 0.9 | 1.2 | 0.4 | 0.1 | 0.7 |

It lives less often than the warrior at almost every depth, most of all at 50 ft (80% against
100%): it fights hand to hand with a mage's hit points whenever a monster reaches it or it runs out
of mana.

## A bot that loots

The bot now also picks up what it finds (walking to objects in view when nothing is awake), puts
on anything for an empty slot, tries unknown potions and scrolls when it is quiet (once each kind:
one that wants an aim or a choice it doesn't give is left alone), eats when hungry, keeps away
from monsters more than ten levels deeper than itself (and blinks when one comes close), and,
badly hurt with nothing left to drink, runs for the nearest stairs. 20 warrior runs per depth,
the bot before (ca57e76) → now:

| depth | survived % | turns | kills | level seen % | potions | pickups | tried |
|---:|---|---|---|---|---|---|---|
| 1 | 100.0 → 100.0 | 1000.0 → 910.0 | 56.1 → 53.0 | 80.0 → 57.7 | 1.1 → 1.1 | 6.2 | 3.5 |
| 5 | 95.0 → 100.0 | 977.7 → 933.3 | 132.7 → 129.3 | 63.9 → 51.6 | 1.6 → 1.8 | 9.3 | 3.6 |
| 10 | 100.0 → 100.0 | 1000.0 → 920.0 | 133.7 → 113.2 | 71.2 → 53.6 | 0.9 → 1.2 | 9.0 | 2.7 |
| 20 | 65.0 → 65.0 | 810.2 → 775.8 | 99.1 → 72.7 | 42.4 → 36.6 | 4.5 → 4.3 | 8.4 | 3.1 |
| 30 | 45.0 → 35.0 | 470.8 → 515.6 | 42.2 → 38.2 | 23.0 → 21.6 | 4.8 → 5.0 | 4.5 | 1.2 |
| 40 | 20.0 → 10.0 | 306.6 → 219.6 | 16.2 → 13.1 | 15.2 → 15.1 | 6.9 → 8.0 | 2.3 | 0.6 |
| 50 | 10.0 → 10.0 | 175.5 → 151.3 | 6.5 → 6.7 | 7.4 → 6.3 | 7.2 → 6.3 | 1.3 | 0.2 |
| 60 | 15.0 → 15.0 | 123.3 → 191.8 | 3.6 → 6.1 | 5.4 → 5.8 | 8.5 → 8.4 | 1.4 | 0.5 |
| 70 | 10.0 → 5.0 | 147.8 → 94.4 | 9.4 → 2.4 | 5.2 → 3.8 | 5.3 → 4.8 | 0.9 | 0.5 |
| 80 | 10.0 → 5.0 | 53.3 → 54.5 | 4.2 → 3.5 | 2.6 → 6.5 | 5.3 → 5.0 | 1.1 | 0.4 |
| 90 | 5.0 → 5.0 | 73.7 → 82.1 | 5.1 → 4.9 | 3.3 → 4.5 | 3.8 → 3.4 | 0.6 | 0.1 |
| 99 | 5.0 → 10.0 | 13.6 → 14.6 | 0.2 → 0.3 | 1.4 → 1.7 | 1.2 → 1.1 | 0.0 | 0.0 |

Within the noise for survival and kills; it sees less of each level (the time goes on looting),
and in the shallows it leaves one level in eight early — the unknown scrolls it reads include
Teleport Level and Deep Descent. It isn't a better survivor; what it adds is play that picks up,
wields and identifies things, which the soak test now covers.

## AVABand's quests, played by the bot

`dotnet run -c Release --project tools/balance quests 3` plays each quest end to end (tools/balance/Quests.cs):
a warrior, a mage and a ranger, three seeds each, at a level each quest suits (16 for the Sealed Door
up to 30 for Consecration), taking the quest at the Prancing Pony, going to each level it names and
heading for what it wants there, fighting and healing as the plain bot does.

```
quest          done  died  decisions  potions  levels   (per class: warrior / mage / ranger, 3 seeds each)
sealed_door    1/1/2 1/2/1       2384      7.1     3.4   killed by: a blue worm mass, Durgash the Keybearer, Durgash the Keybearer, poison
burden         1/2/1 2/1/2       1287      6.7     3.1   killed by: a giant fruit fly, poison, poison, a 3-headed hydra, a skeleton human
broken_blade   1/0/0 1/2/2       2650      5.9     4.3   killed by: an ironfist priest, a baby blue dragon, a blackguard, poison, a red dragon bat
consecration   3/2/1 0/1/1       1407      3.7     2.4   killed by: Hathol, Lord of the Barrow, a giant fruit fly
letter         3/3/3 0/0/0        507      1.3     1.6   killed by: 
thief          1/0/0 2/3/3       2535      7.8    22.1   killed by: a sabre-tooth tiger, a ranger, a red worm mass, poison, a stegocentipede, a giant white louse, a black ogre, a blue icky thing
board          3/2/2 0/1/0        445      2.1     7.8   killed by: a fatal wound
```

The first runs showed the two quest bosses were too strong for their depths — Durgash (420 HP, +5
speed) killed every character that met him, and Hathol (950 HP, +5 speed, a paralysing spell and a
terrifying touch — a frightened warrior can't strike at all) five in nine. Both were brought into line
with Angband's uniques of their depth: Durgash 260 HP at normal speed (Grishnákh and Golfimbul have
about 230), Hathol 600 HP at normal speed, without the paralysis or the terror. Now almost every death
is the bot's own — poison it doesn't cure, worm masses, a fruit fly — as in its plain games; the quest
bosses account for three in 63. The soak plays one of each quest (every way of the Letter) on seeds
where the bot sees it through, recorded and replayed, on every push.


## A smarter bot (for the deep quests)

Sent to 2000–3200 ft for the deep quests, the bot died within a few dozen turns: it wore only a
weapon, body armour and a bow (no free action, no see invisible, no resistances), and the quest bot,
set up in town, carried Cure Light Wounds at character level 45. Now (unless `BOT_PLAIN=1`):

- **Kit for every slot**: shield, cloak, helm, gloves, boots, light, amulet and two rings, each the
  best of thirty good objects made for the deeper of its depth and level, scored by armour and by
  the abilities it doesn't have yet (free action most, then see invisible and poison, confusion and
  blindness, the base four, the rest), plus speed, CON and STR (INT for a caster). Gloves only of
  Free Action for spellcasters.
- **Potions and escapes for its level**: healing potions by the deeper of depth and level (Healing
  itself from level 45), 5 + level/5 of them, and Scrolls of Teleportation from 1250 ft, read when
  badly hurt (sooner when out of potions).
- **Breeders**: five in view and it leaves by the nearest stairs.
- **Wary**: it drinks at 65% rather than 50% while something deeper than itself is awake near it.
- **Casters** (mage, necromancer) cast at what's next to them too, and blink away when out of mana.
- It eats when weak whatever is about, and no longer tries to read while confused, blind or amnesiac.

This kit is generous — thirty good objects to choose from in every slot is more than most
characters find — so this bot's survival is optimistic where the old one's was pessimistic; use it,
like the old one, to compare versions. 20 runs per depth, the bot before → now, on the same seeds:

| depth | warrior survived % | turns | potions | mage survived % | turns | potions |
|---:|---|---|---|---|---|---|
| 1 | 90 → 100 | 869 → 1000 | 2.5 → 0.5 | 65 → 95 | 627 → 861 | 4.0 → 1.1 |
| 5 | 85 → 95 | 854 → 903 | 1.9 → 0.8 | 45 → 95 | 750 → 914 | 4.7 → 0.9 |
| 10 | 65 → 100 | 753 → 937 | 3.4 → 0.4 | 50 → 80 | 664 → 905 | 4.7 → 2.7 |
| 20 | 65 → 100 | 681 → 914 | 5.0 → 0.1 | 35 → 95 | 479 → 844 | 6.6 → 2.1 |
| 30 | 55 → 100 | 749 → 965 | 5.8 → 0.8 | 20 → 100 | 383 → 875 | 6.6 → 2.1 |
| 40 | 35 → 95 | 495 → 876 | 6.8 → 2.6 | 20 → 95 | 162 → 888 | 5.4 → 3.0 |
| 50 | 5 → 90 | 194 → 942 | 6.9 → 3.9 | 5 → 90 | 158 → 894 | 5.4 → 4.7 |
| 60 | 10 → 75 | 125 → 824 | 5.8 → 4.7 | 0 → 50 | 94 → 639 | 5.2 → 4.4 |
| 70 | 0 → 70 | 106 → 827 | 5.6 → 7.1 | 5 → 40 | 78 → 686 | 1.9 → 7.4 |
| 80 | 5 → 55 | 165 → 597 | 4.3 → 7.4 | 15 → 25 | 131 → 308 | 1.4 → 4.0 |
| 90 | 0 → 35 | 84 → 425 | 5.4 → 5.5 | 5 → 20 | 77 → 366 | 3.3 → 4.7 |
| 99 | 15 → 15 | 26 → 196 | 2.6 → 4.8 | 20 → 30 | 80 → 286 | 2.6 → 3.4 |

The mage gains most (a caster's play, and kit that stops it being paralysed or poisoned to death):
it now lives about as often as the warrior down to 2500 ft. Below that both still die, the mage
twice as often. `docs/quest-playtest.md` has what it says about the quests.
