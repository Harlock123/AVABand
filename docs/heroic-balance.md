# Heroic stats: how much easier?

`dotnet run -c Release --project tools/balance heroic 60` plays the same seeds, depths and classes
(human warrior, mage and ranger, at the depth's level, with the bot's usual gear and potions) twice:
once with stats from Angband's roll (8 to 17 each), once from AVABand's heroic roll (14 to 18/50).
60 runs per row, up to 1000 turns each. Measured 2026-09-30.

| class | depth | survived % rolled → heroic | kills | exp gained |
|---|---:|---|---|---|
| warrior | 5 | 83.3 → 93.3 | 177.8 → 149.0 | 196 → 164 |
| warrior | 10 | 73.3 → 71.7 | 123.7 → 140.0 | 329 → 387 |
| warrior | 20 | 61.7 → 70.0 | 110.0 → 121.8 | 770 → 743 |
| warrior | 30 | 40.0 → 43.3 | 122.5 → 162.1 | 706 → 1028 |
| warrior | 40 | 21.7 → 28.3 | 63.4 → 82.8 | 805 → 1023 |
| warrior | 50 | 13.3 → 16.7 | 17.8 → 26.1 | 784 → 889 |
| mage | 5 | 43.3 → 68.3 | 109.1 → 136.4 | 168 → 187 |
| mage | 10 | 26.7 → 51.7 | 91.9 → 106.4 | 267 → 285 |
| mage | 20 | 26.7 → 36.7 | 72.4 → 91.5 | 331 → 443 |
| mage | 30 | 20.0 → 26.7 | 76.2 → 97.0 | 525 → 680 |
| mage | 40 | 8.3 → 11.7 | 6.0 → 42.8 | 762 → 1027 |
| mage | 50 | 10.0 → 13.3 | 35.0 → 31.8 | 706 → 950 |
| ranger | 5 | 75.0 → 76.7 | 111.2 → 139.6 | 168 → 185 |
| ranger | 10 | 53.3 → 75.0 | 77.0 → 104.1 | 249 → 326 |
| ranger | 20 | 45.0 → 73.3 | 137.0 → 120.5 | 616 → 912 |
| ranger | 30 | 26.7 → 41.7 | 56.4 → 79.2 | 642 → 895 |
| ranger | 40 | 26.7 → 26.7 | 66.6 → 85.7 | 1062 → 1637 |
| ranger | 50 | 15.0 → 26.7 | 31.7 → 40.2 | 452 → 768 |

What it shows:

- **Warriors** gain little: 3 to 10 points of survival at most depths. Strength and constitution
  help, but a warrior's gear and hit dice matter more.
- **Mages** gain the most early: at 250 and 500 ft the heroic mage survives about half as often
  again (43 → 68%, 27 → 52%), from more spell points and lower failure rates. The gap narrows deeper.
- **Rangers** gain a lot in the middle depths (45 → 73% at 1000 ft), from dexterity (shots and
  to-hit) and constitution.
- Across the board, heroic characters gain about 30% more experience in the same time.

So heroic characters are substantially easier, most of all for spellcasters and rangers in their
first thousand feet. They are tagged *[Heroic]* in the high scores; whether their score should also
count for less is a choice left open.
