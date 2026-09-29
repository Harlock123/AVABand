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
poor player — it never retreats into a corridor, never uses a launcher, never rests — so its
numbers are for comparing versions, not for judging the game's difficulty absolutely.
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
