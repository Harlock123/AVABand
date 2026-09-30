# The quests, played by the bot (2026-09-30)

What the quest bot (`tools/balance quests`) and the level counts (`tools/balance profiles`) said
about AVABand's quests, and what changed because of it. The bot is not a player — it fights
breeders it should run from, plays mages and rangers poorly, and never plans — so its deaths say
as much about it as about the quests. It answers one question well: is anything *unreasonable*?

## Every quest, five seeds each for a warrior, a mage and a ranger

`dotnet run -c Release --project tools/balance quests 5` (done / died, per class: warrior / mage / ranger):

| quest | done | died | what killed it |
|---|---|---|---|
| The Sealed Door | 3/0/4 | 2/5/1 | Durgash three times; the rest, lice, a mouse, a priest, poison |
| The Burden | 2/2/2 | 2/3/3 | breeders, poison, a hydra, a jelly |
| The Broken Blade | 1/0/3 | 3/4/2 | Shagrat once; the rest, what lives at those depths |
| Consecration | 3/1/4 | 2/4/1 | Hathol twice; hounds, a hydra, a champion |
| The Letter | 5/5/5 | 0/0/0 | — |
| The Thief | 2/0/1 | 2/4/3 | the long hunt through 25 levels: rats, lice, poison, Wormtongue |
| **The Apprentice** | **5/3/5** | 0/2/0 | a worm mass, a louse |
| **The Warden's Fires** | **1/2/1** | 2/3/4 | **the Shade three times**, then fleas, hounds, poison |
| The notice board | 5/5/4 | 0/0/0 | — |

The bot's mages die everywhere (it casts and melees poorly), so read the warrior and ranger
columns. The older quests kill mostly through the levels they send you to; their own bosses
account for a death in five or so. Two things stood out:

## The Cartographer could hardly be done

`dotnet run -c Release --project tools/balance profiles 150` — how often each kind of level is made:

| depth | cavern | labyrinth | old mines |
|---:|---:|---:|---:|
| 3–8 (150–400 ft) | 0 | 0 | 0 |
| 10 (500 ft) | 0 | 0 | 2.7% |
| 15 (750 ft) | 6.7% | 4.7% | 4.0% |
| 20 (1000 ft) | 5.3% | 2.7% | 1.3% |
| 30 (1500 ft) | 4.0% | 4.0% | 2.7% |

Offered from level 6 (a character around 250 ft), it asked for three kinds of level that don't
exist above 750 ft and are one level in twenty or rarer below it: dozens of levels of luck.
**Now** the cartographer names where each is — a cavern a couple of levels past your deepest, a
labyrinth and the old mines a few beyond that — and the first new level made at each of those
depths is that kind (the dungeon can make all three even at 250 ft; checked). Any other cavern,
labyrinth or old mines you happen on still counts.

## The Warden's Fires was harsher than it should be

At the bot's usual level 32 it finished one run in four and died in nine of fifteen — three to the
Shade itself, which can't be hurt until all three fires burn, and hounds you meanwhile. At level 20,
where the inn first offers it (8 seeds each):

| | warrior done / died | ranger done / died |
|---|---|---|
| before: claws 3d6, spells 1 in 6, snuffs a fire 1 turn in 4 | 3/8, 3/8 (Shade 0) | 1/8, 7/8 (Shade 1) |
| **now**: claws 2d6, spells 1 in 9, 1 turn in 6 | 2/8, 4/8 (Shade 1) | 0/8, 8/8 (Shade 2) |

After the change the Shade is no longer what kills: the deaths are to the rest of a 1000 ft level,
at about the bot's usual rate there (a clvl-20 warrior survives 62% of 1000-turn visits to 1000
ft, a ranger 45% — `tools/balance play`). The quest is now about as dangerous as its depth.

## What only a person can tell

Whether the Warden's Fires is *fun* — racing the Shade to the braziers, luring it away from a lit
one — and whether the Apprentice's choice (your only Word of Recall, or let him try alone) feels
like a real choice. The bot always gives up the scroll.
