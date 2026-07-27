# Combat Mechanics

*Damage types, elements, and matchups. Unwritten uses a pre-Renewal Ragnarok-style combat model.*

## The three damage types

Every hit is one of three types, and each behaves differently:

- **Physical** — uses your **Attack** against the target's **Defense**. Can miss, can crit.
- **Magic** — uses your **Magic Attack** against **Magic Defense**. **Never misses**, and **cannot crit**.
- **Critical** — a forced critical hit that **ignores Defense**.

## Three always-on rules

These are hard rules the combat math always honors:

1. **Critical hits ignore Defense** — armor doesn't save you from a crit.
2. **Magic never misses** — accuracy and dodge don't apply to spells.
3. **Tanks resist mages** — high Defense and HP hold up well against the magic arc, so a durable build is the natural answer to casters.

## The archetype triangle *(design intent)*

Builds are meant to settle into a rock-paper-scissors of three archetypes:

- **AGI / LUK (crit)** beats **STR / VIT (tank)** — crit partly bypasses Defense.
- **STR / VIT (tank)** beats **INT / DEX (mage)** — HP and Magic Defense soak the burst.
- **INT / DEX (mage)** beats **AGI / LUK (crit)** — magic ignores dodge.

> These are intended tendencies (not absolute counters), and the exact numbers are still being tuned.

## Elements

Attacks and monsters carry an **element**, and element-vs-element interactions modify damage — the classic Ragnarok matchup web. The elements are:

**Neutral · Water · Earth · Fire · Wind · Poison · Holy · Shadow · Ghost · Undead**

For example, Fire is strong into Earth, Water is strong into Fire, and Holy bites hardest into Undead and Shadow. *(The full multiplier table is being tuned.)*

## Monster sizes *(planned matchup)*

Monsters come in **Small, Medium, and Large** sizes, and weapon types are intended to matter against size — a dagger shines against small swarms, while heavy two-handers are built to bite into large foes. *(This size-vs-weapon matchup is designed but not yet switched on in combat.)*

## Status ailments

Skills can inflict lasting conditions:

- **Damage over time:** Burn, Poison, Bleed
- **Control:** Stun, Freeze, Silence
- **Weakening:** Slow (movement), Blind (accuracy), Vulnerability (takes extra damage)

> *Status: the model and rules above are set; specific numbers, the element table, and the size matchup are in tuning.*
