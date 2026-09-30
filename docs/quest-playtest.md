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

## The smarter bot, and the deep quests (2026-09-30, later)

The bot was rebuilt for the deep quests (see `docs/balance.md`, "A smarter bot"): kit for every
slot chosen for free action, see invisible and resistances, potions and Teleportation for its
level, leaving levels full of breeders, and playing a mage as a caster. The same report,
`quests 5`:

| quest | done | died | what killed it |
|---|---|---|---|
| The Sealed Door | 5/4/5 | 0/0/0 | — |
| The Burden | 3/3/3 | 0/0/0 | — |
| The Broken Blade | 3/4/4 | 0/0/0 | — |
| Consecration | 5/4/4 | 0/0/0 | — |
| The Letter | 5/5/5 | 0/0/0 | — |
| The Thief | 4/4/4 | 0/0/0 | — |
| The Apprentice | 5/5/5 | 0/0/0 | — |
| The Warden's Fires | 5/5/4 | 0/0/0 | — |
| **The Heart of the Mountain** (clvl 38) | 4/2/3 | 0/3/2 | Skorvath three times, Bill the Stone Troll, a death knight |
| **The Last Watch** (clvl 40) | 4/5/5 | 1/0/0 | an ethereal dragon |
| **The Seeing Stone** (clvl 45) | 3/0/4 | 0/4/0 | the Keeper once; a mature green dragon, a wolf, an ant |
| The notice board | 2/3/4 | 0/0/0 | — |

Not one death in the first nine quests in 135 runs, where the old bot died in about half: its
deaths were poison, paralysis, breeders and a mage in melee, which kit and sense now prevent.
What's left undone is the bot running out of its 6000 decisions (wandering the Thief's twenty
levels, or a shard it can't reach), not danger. Read these as "nothing unreasonable", not as how
hard a quest is: this bot's kit is better than most characters'.

The deep quests were tuned on these runs:

- **Skorvath** began as Angband's Scatha (whose id it had taken, silently replacing him — renamed,
  and a test now keeps AVABand's ids apart from Angband's): 2500 HP, so an 833-point frost breath,
  seven levels shallower than Scatha. Every warrior died to him. Now 1100 HP (a 367-point breath, 122
  resisted), breathing one turn in six, at 1900–2500 ft rather than 2200–2800. He still kills
  mages who stand and trade with him — the bot's mage casts frost bolts at a dragon immune to cold.
- **The Keeper of the Stone** was +20 speed, 3500 HP, spells one turn in four, at 3100–3900 ft: it
  killed three in eight, and the depth killed the rest. Now +15, 2600 HP, one in five, and the
  vault is at 2500–3100 ft. With the bot's mages choosing their spells properly (below) the Keeper
  still killed three mages in five with a nether ball of about 300 — a level-45 mage's whole life —
  so it now has only its nether bolt (about 130): still the most dangerous thing in the vault, but no
  longer one breath from a caster's death.
- **The Last Watch** needed nothing: holding 200 turns or killing Grishnag both work, and most runs
  end by Grishnag's death well before the horns.

## The bot's spells, and the report again

The bot's casters chose the highest-level attack spell they had, whatever the target — the mage
cast frost bolts at Skorvath, who is immune to cold. Now they cast what does most to the target:
each attack spell's average damage at their level (from its effect formula), nothing if the target
is immune to its element, double if it's hurt by it. `quests 5` after that (and the Keeper's change):

| quest | done | died | what killed it |
|---|---|---|---|
| The Sealed Door | 5/5/5 | 0/0/0 | — |
| The Burden | 3/2/3 | 0/1/0 | Shagrat |
| The Broken Blade | 3/2/3 | 0/0/0 | — |
| Consecration | 5/5/5 | 0/0/0 | — |
| The Letter | 5/5/5 | 0/0/0 | — |
| The Thief | 4/2/4 | 0/0/0 | — |
| The Apprentice | 5/5/5 | 0/0/0 | — |
| The Warden's Fires | 5/5/5 | 0/0/0 | — |
| The Heart of the Mountain | 4/5/4 | 0/0/0 | — (mages had died three in five) |
| The Last Watch | 4/5/3 | 1/0/2 | an ethereal dragon, Waldern, a nether hound |
| The Seeing Stone (Keeper with its ball) | 3/0/3 | 0/4/1 | the Keeper three times, drakes |
| The Seeing Stone (bolt only) | 2/0/4 | 2/3/0 | drakes and dragons of 2500–3100 ft; the Keeper once |
| The notice board | 2/3/4 | 0/0/0 | — |

The Stone is still hard on mages — not because of the Keeper now, but the depth: the drakes that
live at 2500–3100 ft. That's the dungeon, as it would be without the quest.
