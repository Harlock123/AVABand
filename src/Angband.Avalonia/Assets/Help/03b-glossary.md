# Stats and terms explained

What the numbers on your character sheet (`C`) and the sidebar mean, and what changes them. The
figures are AVABand's own rules (mostly Angband 4.2.5's).

## How stats are written

Each of the five stats runs from **3** to **18**, then on in steps of ten written **18/10**, 18/20 ...
up to **18/220**, the most a stat can be. On the sheet, *Base* is what you were born with, *Race* and
*Class* add to it, and *Current* is what counts now: that, plus anything your gear adds, less any
**drain**.

- **Potions raise a stat one step** (18/40 to 18/50, or 15 to 16) — Strength, Intelligence, Wisdom,
  Dexterity, Constitution, Augmentation, the Draught of the Ents and the like — but only up to
  **18/100**. Past that, only gear (rings, amulets, egos and artifacts with +STR and so on) takes a
  stat higher. A potion drunk at 18/100 or above still restores the stat if it's drained, and says
  you're as strong (or smart, or wise...) as you can be made; its description says so beforehand.
- **Drain**: some monsters and traps lower a stat for a while. It comes back when you gain a level,
  or with a Mushroom of Vigor, a Potion of Life, a Rod of Restoration, a Sip of Miruvor (Strength
  and Constitution) or any potion that raises that stat.
- **On the sheet**, a stat's Current value is **gold** when no potion will raise it any further,
  and **red** while it's drained (red wins); the key under the stats says so.

## Strength (STR)

- **To-damage**: from −2 at 3 up to +20 at 18/220, added to your blows.
- **Carrying**: how much you carry before you're slowed (see *Burden* below).
- **Blows**: with your weapon's weight and your Dexterity, how often you strike (see *Blows*).
- **Heavy weapons**: a weapon heavier than your Strength can hold is wielded with trouble.
- **Digging**, how far you can **throw**, and a warrior's, paladin's or blackguard's shield bash.

## Intelligence (INT) and Wisdom (WIS)

Each realm of magic casts from one of them: **Intelligence** for arcane magic (mages, rogues) and
necromancy (necromancers, blackguards); **Wisdom** for divine magic (priests, paladins) and nature
magic (druids, rangers). The multiclasses use their caster's. Your casting stat sets:

- **Mana**: a share for every level you've gained since your class's first spell — nothing below
  8, more the higher the stat. Raising the stat raises your mana at once.
- **Failure**: a higher stat takes more off every spell's failure chance, and lowers the least it
  can ever be (see *Fail* below).

**Wisdom** also improves your **saving throw**, whatever your class. Intelligence doesn't change
how well you use wands, staffs and rods; that's your *Devices* skill.

## Dexterity (DEX)

- **Blows**: with Strength and your weapon's weight (see *Blows*).
- **To-hit** (−3 to +20) and **armour** (−4 to +15).
- **Keeping your purse and pack**: the chance a thief's touch steals nothing grows with Dexterity
  and your level.

## Constitution (CON)

- **Hit points**: each new level adds a roll of your hit dice plus Constitution's share, from −2.5
  at 3 to +12.5 a level at 18/200 and up. Only levels gained afterwards feel a raise: hit points
  already gained stay as they were.
- **Recovering** from cuts, poison and stunning is quicker.
- With *AVABand's encumbrance*, each step above 15 lets you carry 2 lb more unhindered.

## Blows

**How do you get more blows a turn? Strength and Dexterity, and a lighter weapon — not speed.**

- Your weapon's weight (never less than your class's minimum) is set against your Strength (times
  your class's multiplier): the lighter the weapon for your Strength, and the higher your
  Dexterity, the less of a turn each blow takes — so more of them.
- Each class has a most: a warrior 6, a paladin, rogue, ranger or blackguard 5, a mage, priest,
  druid or necromancer 4.
- Gear with **+blows** (extra attacks) adds whole blows on top, even past that most.
- A weapon too heavy for your Strength: one blow, and less accurate — you're told when you wield it.
- **Speed doesn't add blows** to a turn, but it gives you more turns, so you strike more often all
  the same: Strength and Dexterity for more blows a turn, speed for more turns.
- Blows have a decimal (*2.3/turn*): each blow costs a share of a turn. The shops' notes and
  inspecting a weapon show the blows you'd have with it.
- A Human's off-hand weapon adds one more blow at the end of the round.
- The birth option *Angband 4.2's blows* off: each class strikes a fixed number of times (a
  warrior 2, a mage 1...), plus any +blows on gear, and heavy weapons don't hinder.

## Shots and might

- **Shots**: how many missiles a turn your launcher fires: 1.0, more with **+shots** on gear, and
  a ranger gains a tenth more every third level.
- **Might**: your launcher's **multiplier** (x2 for a sling or short bow, x3 for a long bow or
  light crossbow, x4 for a heavy crossbow) plus any **+might** on gear. It multiplies each arrow's,
  bolt's or shot's damage, and sets the range: 6 squares plus 2 for each point of it, at most 20.

## Speed

**Normal** is +0. At **+10** you get twice as many turns as a normal monster, at +20 three times; at
−10 half as many. Speed comes from gear (boots of Speed, a ring of Speed...), haste (potions, spells,
a Staff of Speed), sprinting and terror; you lose it to slowing, to being gorged with food (−10), to
stoneskin (−5) and to carrying too much (see *Burden*).

## Burden

Strength sets a **weight limit**; half of it you carry unhindered, and every tenth of the limit
beyond that costs a point of speed: *Burdened* (−1), *Strained* (−2, −3), *Overloaded* (−4 or worse).
With *AVABand's encumbrance*, Strength keeps counting past 18/70, Constitution helps, and what you
wear weighs three quarters. Bags of holding, (Porter) gear, Heroism and Berserk Strength, and some
races carry more.

## Fighting numbers

- **Fight (+h,+d)**: your own to-hit and to-damage, before your weapon's: from Dexterity and
  Strength, other gear (rings, gloves...), and blessings, heroism and the like. A weapon or launcher
  adds its own (+h,+d) to what it does.
- **Armour** (AC): what your armour and its enchantment give, plus Dexterity and spells; the more
  you have, the more often monsters miss and the less some of their blows hurt.
- **Melee, Shooting, Throwing**: how good you are with each — added to your to-hit when you attack.
- **Hit points** (HP): how much harm you can take; at 0 you die. **SP** (spell points, mana): what
  you cast with. Both come back slowly, faster when resting.

## Skills

- **Saving throw**: the chance, in %, of shrugging off a monster's spell or blow, or a trap.
- **Stealth**: how quietly you move; higher, and monsters sleep longer.
- **Disarm** and **(magic)**: disarming physical traps and magical runes.
- **Devices**: how often wands, staffs and rods work.
- **Search**: noticing traps and secret doors.
- **Digging**: tunnelling through rubble, veins and walls — from Strength and your digger.
- Skills come from your race and class, and grow with your level.

## Senses

- **Infravision**: how far you see warm-blooded monsters in the dark, in feet.
- **Light**: how many squares your light reaches (a necromancer sees without one).

## Experience and depth

- **Level**: from 1 to 50. **Exp** is your experience; **Next** what the next level needs. Races
  and classes learn at different rates, and the multiclasses need half as much again.
- Losing experience can cost levels; *(max N)* shows the highest you reached.
- **Depth** is how far down you are (50 ft a level); **Max** the deepest you've been.

## Fail

A spell's **failure chance** (`m` shows it): the spell's own, less 3 for each level you are above
it, less your casting stat's bonus; more if you lack the mana (5 a point), if you're afraid (20) or
a necromancer in the light (25). It never goes below the least your stat allows (5% for most
classes; mages, priests, druids and necromancers can reach 0%), nor over 50%. With **AVABand's easier
casting** (a birth option, on) that is then halved. Stunning adds 15 (25 if badly stunned) and
amnesia makes it much worse; it's never more than 95%.
