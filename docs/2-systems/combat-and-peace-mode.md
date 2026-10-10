# Combat and Peace Mode

Every unit — a person, another player, a creature — has its weapon either **in hand** (combat) or **put away on its
body** (peace). The server alone decides which; every screen draws what it is told.

## The four phases

`Sheathed` (put away) → `Drawing` → `Drawn` (in hand) → `Sheathing` → `Sheathed`. A request that arrives while the
weapon is moving is remembered (the newest wins) and carried out when the move ends. A new life and a weapon swap
put the weapon away at once, with nothing pending.

**How long a move takes:** 0.2 s + 0.006 s × the weapon's weight (0.74 s at weight 90, 0.56 s at 60, 0.2 s bare
handed). Drawing and putting away take the same time. The legs keep moving throughout.

## What takes it out and what puts it away

| Takes it out | Puts it away |
| --- | --- |
| The **X** key while it is away | The **X** key while it is out |
| An attack order (the swing waits until it is in hand) | Starting to walk (Shift) |
| A weapon skill pressed while it is away — the press is held, and pressed again, aimed fresh, once it is in hand (dropped if the weapon is put away or you change map first) | Turning sprint (V) on — the sprint starts once the weapon is going back |
| A creature that starts hunting | A creature that stops hunting |

Being hit never takes it out, and nothing puts it away on a timer.

**The server enforces it.** A swing, or a cast of a skill that belongs to a weapon, asked for while the weapon is
not in hand is refused and the weapon is taken out instead (said in the server's log). A person whose accepted
position reports run faster than 1.3 × the run speed (× a Haste, never lowered by a Slow) three times in a row with
the weapon out has it put away, also logged — except during a glide or a shove the server itself ordered.

## What peace pays

Only while the weapon is **put away** (not while it is going back) **and** 5 s have passed since the unit last dealt
or took a blow:

- **Natural health and mana recovery ×2.** The timed recovery change from a skill or a worn word is added on top,
  undoubled. Healing skills and potions are untouched.
- **Creatures notice the unit from half the distance.** A creature choosing whom to hunt weighs a unit at peace as
  twice as far away, and starts the chase only inside its own notice distance, halved for prey at peace.
- **Sprinting** — the sprint gear exists only in peace.

## What combat pays: the draw buff

Taking a weapon out raises the moment **"When you draw your weapon"** for the unit's passives, at most once every
15 s per unit (longer than any draw buff lasts). A weapon swap removes whatever the old weapon's draw passive put on
the unit. The per-weapon draw passives themselves are agreed (three stats each, 4 s, learned with that weapon's
mastery points) but **not yet authored** — they wait on their names.

## How it looks

- A weapon has an **in-hand pose** (its carry arms over travel, its own standing idle) shown only while it is in
  hand. At peace the body uses the shared idle, walk, run and sprint with empty hands. The carry arms may hold
  both arms or only one: a weapon whose carry pose holds one arm (the dagger's right) leaves the other swinging
  with the run like an empty hand.
- A weapon with put-away and take-out clips plays them on the arms only, over whatever the legs are doing, scrubbed
  by the move's progress; each hand lets go of, or takes, its copy at its own frame of the clip.
- **Where the weapon rests** is set per weapon model on its grip: a body bone and a place on it (a second place for
  the other copy of a pair weapon). **Today the Katar, the Dagger and the Axe have one** — the two katars cross in
  an X on the lower back; the dagger hangs on the right hip, slanted backward and down; the two axes cross in an X
  on the back, heads up behind the shoulders. Every other weapon is **hidden while put away** until its resting
  place is set.
- **A put-away may carry a spin.** A weapon's stance row can name two frames of its put-away clip, a number of
  turns and a lift: between those frames the weapon in the main hand rises off the fist by the lift and turns flat
  over it that many times, then settles back before the hand lets go. The take-out, which is the put-away played
  backwards, turns it back over the same frames. **Today only the Dagger has one**: the right arm is held straight
  out to the side and the dagger makes 2 turns over the fist.

## Related

- [Unified Unit Stats](active-skill-system-v3/unified-unit-stats.md) — the regeneration ticker peace doubles.
- [Networking — Implementation Notes](networking/implementation-notes.md) — the sprint gear is told, not measured.
