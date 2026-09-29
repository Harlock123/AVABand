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
