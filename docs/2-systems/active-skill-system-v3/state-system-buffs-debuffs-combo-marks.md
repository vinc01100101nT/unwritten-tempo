---
status:
owner: vince
last-updated: 2026-08-30
tags:
  - systems-combat-design
---

# State System — Buffs, Debuffs & Combo Marks (V3)

> # :no_entry: NEVER APPEND. ALWAYS UPDATE IN PLACE.
> This doc must read as ONE current voice with zero outdated lines and zero contradictions. When something changes,
> **rewrite the passage that said the old thing** — never bolt a dated "update" section onto the end. Before finishing
> any edit, re-read the whole doc and confirm nothing elsewhere still states the old rule.

> **Status (2026-08-24): built, authored against, and shipping.** The store, stacks/charges, the gate, compute-before-consume
> and the no-self-feeding brake are all live code, and the sword pool (22 skills) is composed on top of them — Momentum,
> Deathblow, Lock Blades, Parry, Brace, Retribution and the outgoing-damage charges are all states in the sense this doc
> describes. Code-side record: [Implementation Notes](implementation-notes.md).

## Why this exists

Skill **pairing** (skill A sets up a condition that skill B pays off — Lunge → Lock Blades, Momentum → Deathblow) does
**not** need a separate "mark" system. Every buff and debuff is *already* a state on a target, and self-buffs already draw
icons on-screen. So marks are unified into the existing state system — no parallel mechanism — plus the few knobs pairing
and stacking require. Companion to **Active Skill System — V3**; combos are composable per skill, never hardcoded per weapon.

## One store, one pile per thing

Every unit — player or mob, no distinction — carries **one state store**. It is the single source for both gameplay and the
on-screen icons (player buff tray, mob nameplates), and it replaced the old scattered ailment arrays, the hand-written buff
windows and the separate vulnerability field.

Inside it, each tracked thing is its own **pile**:

- **Ailments — six.** Poison, Bleed and Burn carry damage per tick; Stun, Freeze and Silence are duration-only.
  *Slow and Blind are deliberately not here*: each only ever moved a sub-stat (move speed, accuracy), so they are authored
  as sub-stat debuffs and having them as ailments too was a second door to one number.
- **Sub-stat piles — one per sub-stat**, and each holds a **signed** bonus. A debuff is the same pile carrying a negative
  number; there is no mirror pile to build or keep in step. One pile *per stat* rather than one shared pile with the stat
  named inside it, because a pile's identity is what the modifier effects and the gate point at — two stats buffed by one
  skill must be two piles, or "which pile" cannot tell them apart.
- **Pools and windows that are not sub-stats:** Shield (a finite barrier HP pool), Taunt, and the disable-immunity window.
- **Charge piles:** Parry and Brace (each charge holds one incoming hit), and the two outgoing-damage piles — a boost and a
  penalty, kept **separate on purpose** so an enemy's penalty charge can never be spent by the attack you were saving your
  boost for. A unit carrying both simply pays both on the same swing.
- **The debt pile.** Delaying a hit needs **two** states, not one: the armed capacity, and the damage it postponed. Spending
  the last charge must make the armed state vanish while the debt it created outlives it and still comes due. Merging them
  is impossible, not merely untidy.

Applying any state **automatically marks its target with it.** The state's identity IS the mark — a combo reads whatever
state already exists, so no "empty/none" tag type is needed.

⚠️ **The pile list is append-only.** A pile is stored as a plain integer in every authored gate and modifier, so inserting a
new one in the middle would silently repoint saved skills at a different pile. New piles go at the END, always.

## Stacks and charges are ONE mechanism — LOCKED

A pile holds a **list of stacks**. Each stack carries **its own magnitude and its own duration**.

- **A stack** = one application. Momentum adds one per basic attack, each dying on its own timer.
- **A charge** = several stacks granted at once, sharing one window. "Lunge grants 3 charges of Lock Blades" IS three stacks
  applied together.

There is no separate charge system — a charge is a stack. Building two would be two doors to one room.

**Total = the SUM of the stacks.** Magnitude is **fixed per stack**: 10 stacks of +3% attack resolve to +30%. Because each
stack owns its magnitude, mixed piles total correctly (five at 3% + three at 5% = 30%), and a charge is worth whatever the
skill that granted it said — never what a later, different grant says.

- ⚠️ **Encoding consequence:** a stacking buff stores the **bonus** (0.03), not the multiplier (1.03) — summing multipliers
  gives nonsense. The reader computes `1 + sum`. Non-stacking states are unaffected.

**Rules:**

- **Expiry** — each stack dies on its own timer.
- **Overflow** — past the stack limit, the **oldest** stack drops.
- **Consumption** — takes the **oldest first**, so a stack about to expire is spent before a fresh one.
- **Stacking is opt-in per effect.** Off = one instance, newest-wins.
- A **safety ceiling** exists even for piles the design calls "uncapped" (Momentum). Design never reaches it; it removes the
  unbounded-growth risk.

### Stacks the caster never authored — earned from attack speed

A stack count can be left unauthored and **worked out at cast time** from how many swings the caster would land during that
effect's own duration. Faster attack speed then means more stacks in the *same* window, never a longer one. When this is on,
the stack limit and stacks-per-use no longer apply — the count is whatever the caster's speed earns. Each effect takes a
**share** of that count (1 = all of it, 0.5 = half, rounded up), so two effects on one skill sharing a duration stay in exact
proportion at any attack speed, with nothing to re-author when the duration changes.

### A cast's stacks can size any number — one rule, not damage's private one

An effect may declare that **its numbers answer to the pile this cast is cashing**. Each number then carries its own
operation and reading right beside it, so on one effect a damage multiplier can build on the pile while a duration ignores
it. Off — the default — every number is a plain number. This is the single rule for stack-sized numbers; damage does not get
a private version of it.

### The run of hits — the one pile nothing grants

A gate can read the **run of hits the fight is counting** rather than a skill's pile. Nothing grants it, there is only ever
one of it, and it is never spent — a run is ended by the fight (a switched target, a hit that dealt nothing), never by a
skill cashing it. It therefore has no "which pile" half, and it is only a live number at the moments a hit of yours just
landed.

## Compute BEFORE consume — LOCKED

**Every computation reads the live store. Never a snapshot, never a copy.** A snapshot would be a second place the value
lives, which is what single-source-of-truth forbids.

The order is: **check the requirement → run the effects (reading the store live) → consume.** Deathblow sees all 10 Momentum
stacks while computing its damage, and they are spent the instant that computation finishes. Consumption happens **once per
use**, at the moment the skill's effects actually land — not when the cast begins. Consumption has no inverse, deliberately:
do not add one.

## No-self-feeding rule — LOCKED

**A passive never fires off a chain it is already part of.** Applied at the one place triggers fire.

Every effect carries the set of passives that led to it; a passive already in that set is skipped, while any other passive
runs normally.

- **Lock Blades** → its auto-lunge hits carry "Lock Blades" → those hits can't fire Lock Blades again. Loop closed.
- **Passive A kills → passive B reacts** → legal, B was never in the chain.
- **A → B → A** is also blocked. The rule is "not already in the history", not merely "not the immediate parent" — otherwise
  two passives could still feed each other in a ring.

Because no passive can repeat within one chain, every chain is **finite by construction** (at most one link per passive you
own), with no depth counter to tune. It costs one machine word carried alongside each effect, and it forecloses very little:
unrelated passives can still respond to one another.

⚠️ **A passive's identity slot is fixed and never compacted.** A chain is stamped now but consumed frames later, so reusing
or shifting a slot makes a passive stop recognising itself and re-fire off its own damage — the exact loop the brake exists
to prevent.

### What stamps a passive's name on an effect

**Only damage dealt by the passive's own effects.** A passive that merely buffs, extends or grants something does **not**
stamp itself onto damage that an *active* deals.

This is what keeps **Encore** working: Encore extends Blade Dance, but Blade Dance's storm is still the active's damage, so
each kill can fire Encore again. If Encore dealt damage itself, that damage would carry Encore's name and couldn't re-fire it.

### Why this, and not charges

- **Charges are a budget** — how many times a thing may happen.
- **This rule is the brake** — what stops a thing from feeding itself.

Charges cannot serve as the brake: a skill spends its charge when its blow **lands**, a moment after it was triggered, and in
that gap the blow can trigger the next copy while the count still reads full. This rule closes that gap regardless of timing,
at any depth. It **replaces** the older partial guard, which only caught immediate chains — one rule, not two.

## The authoring fields

### On the GRANTING side (any state effect)

- **Stacking** (checkbox). Off = single instance. On → reveals **Stack Limit** (the ceiling; oldest drops past it) and
  **Stacks Per Apply** (how many stacks one application grants — this is the charge-granting knob).
- **Stacks from attack speed** (checkbox) + **share**, for the earned-count case above.

### On the REQUIRING side — per TRIGGER

The gate lives on the **trigger entry**, not per-effect and not once per skill: a skill has multiple triggers, and firing
through one trigger may read a state while another doesn't. When the requirement isn't met the whole skill simply does not
run — **no VFX, no cast, no cost paid**. This matters for Lock Blades, which must show no auto-lunge at all when out of
charges.

- **Whose stacks** — the id of the skill that grants them (Deathblow reads Momentum's), or the run of hits.
- **Which pile** — a skill can grant more than one stacking state, so the gate must name which. ⚠️ **A gate that reads a
  skill's stacks without naming the pile is an authoring error and is reported as one** — it would otherwise silently point
  at the first pile in the list and count nothing.
- **Minimum stacks** — "only fire at ≥3". 1 = just needs the state at all.
- **Consume** — spend ALL on use (Deathblow cashing Momentum), or a fixed count, or nothing at all for a pure condition.

### Deferred — Owner (Private / Shared)

The Owner axis (per-caster copies vs one shared pile) is **deferred**. There is no multiplayer yet, so the two are
indistinguishable and untestable today. The store is shaped so an owner can be added per stack later without reworking stacks.

## Overwrite policy (non-stacking) — LOCKED

For piles with stacking **off**: **newest wins.** The overwriting cast replaces both duration AND magnitude, **even if
weaker** — a fresh weak move-speed buff over a strong one is acceptable. Ailment refresh follows the same rule (it was
longest-wins before the store landed).

## What the on-screen icons show

The buff tray and the mob nameplates populate from **one rule**: any effect with a duration tags the state it applied with
the source skill's icon; instant effects tag nothing. It falls straight out of the classification — *has a duration ⇒ it's a
state ⇒ it's on the tray* — so no hand-maintained list can drift, and a new durational effect appears for free. Stacked piles
show their count.

## First consumers — all composed

- **Momentum → Deathblow** (stacks): basic attacks add one stack each with their own timers and FIFO expiry, each a fixed
  attack bonus; Deathblow reads the live total, then consumes all. Exercises independent per-stack timers, sum-of-stacks, and
  consume-all after compute.
- **Lunge → Lock Blades** (charges): casting Lunge grants charges; each basic attack you start triggers an auto-lunge and spends one.
  Exercises stacks-granted-at-once, the trigger gate, consume-per-use, and the no-self-feeding brake.
- **Parry, Brace and Retribution** exercise the charge piles and the two-state debt rule; **Blade Dance** exercises
  attack-speed-earned stacks; **Encore** exercises duration extension without stamping itself on the damage.

---

## Related

- [Active Skill System — V3](README.md) — the design doc this is a companion to; it owns the effects catalog.
- [Implementation Notes](implementation-notes.md) — the code-side record of the store, the stacks and the gate.
- [Sword — Skill Roster](sword-skill-roster.md) — the first consumers, all composed.
- [Skill Numbers & Balance Baseline](skill-numbers-balance-baseline.md) — the bands a state's magnitude and window are authored from.
- [Unified Unit Stats](unified-unit-stats.md) — the sub-stats these piles move, and the units that carry a store.
- [Networking — Implementation Notes](../networking/implementation-notes.md) — why a watching copy holds no states, and why the deferred Owner axis matters again.
