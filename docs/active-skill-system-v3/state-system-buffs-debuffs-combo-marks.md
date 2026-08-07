# State System — Buffs, Debuffs & Combo Marks (V3)

> **Status (2026-08-07): the stacking slice below is BUILT in code** — stacks/charges, the skill-level combo gate,
> compute-before-consume, and the no-self-feeding brake. Compile-verified, **not yet Play-tested**. The remaining work is
> CONTENT (authoring Lock Blades + Momentum) plus the open auto-pursue question. Code-side record:
> [Implementation Notes](implementation-notes.md).

## Why this exists

Skill **pairing** (skill A sets up a condition that skill B pays off — e.g. Sword's Lunge → Lock Blades) does **not** need a separate "mark" system. Every buff/debuff is *already* a state on a target (self-buffs already draw icons on-screen). So we **unify marks into the existing buff/debuff system** — no parallel mechanism — and add the few knobs that pairing + stacking require. Companion to **Active Skill System — V3**; feeds weapon-roster authoring (combos are composable, not hardcoded per weapon).

## The STATE hierarchy (Skill Composer effect reorg)

Effects that apply a lasting condition live under one parent category:

- **STATE**
  - **BUFF** — all buffs (Damage / AttackSpeed / CastSpeed / Guard / Regen / Shield / Haste …).
  - **DEBUFF** — all statuses, **renamed from "Status"** so it reads as the sibling of Buff (Poison / Bleed / Burn / Slow / Stun / Freeze / Silence / Blind / Vulnerability …).

Applying any buff/debuff **automatically marks its target with that state.** The state's identity IS the mark — a combo reads whatever buff/debuff already exists (no "empty/none" tag type needed).

## Stacks and charges are ONE mechanism — LOCKED

A state holds a **list of stacks**. Each stack carries **its own magnitude and its own duration**.

- **A stack** = one application. Momentum adds one per basic attack, each dying on its own timer.
- **A charge** = several stacks granted at once, sharing one window. Lunge granting "3 charges of Lock Blades" IS three stacks applied together.

There is no separate charge system — a charge is a stack. Building two would be two doors to one room.

**Total = the SUM of the stacks.** Magnitude is **fixed per stack**: 10 stacks of +3% attack resolve to +30%. Because each stack owns its magnitude, mixed piles total correctly (five stacks at 3% + three at 5% = 30%).

- ⚠️ **Encoding consequence:** a stacking buff must store the **bonus** (0.03), not the multiplier (1.03) — summing multipliers gives nonsense. The reader computes `1 + sum`. Non-stacking buffs are unaffected.

**Rules:**

- **Expiry** — each stack dies on its own timer.
- **Overflow** — past the stack limit, the **oldest** stack drops.
- **Consumption** — takes the **oldest first**, so a stack about to expire is spent before a fresh one.
- **Stacking is opt-in per effect.** Off = today's behaviour exactly (one stack, newest-wins). Nothing existing changes.
- A **safety ceiling** exists even for states the design calls "uncapped" (Momentum). Design never reaches it; it removes the unbounded-growth risk.

## Compute BEFORE consume — LOCKED

**Every computation reads the live store. Never a snapshot, never a copy.** A snapshot would be a second place the value lives, which is exactly what single-source-of-truth forbids.

Therefore the order is: **check the requirement → run the effects (reading the store live) → consume.** Deathblow sees all 10 Momentum stacks while computing its damage, and they are spent the instant that computation finishes. Consumption happens **once per cast**, at the moment the skill's effects actually land — not when the cast begins.

## No-self-feeding rule — LOCKED

**A passive never fires off a chain it is already part of.** Applied at the one place triggers fire.

Every effect remembers **which passives caused it**. When a trigger fires, a passive already in that history is skipped; any other passive runs normally.

- **Lock Blades** → its auto-lunge hits carry "Lock Blades" → those hits can't fire Lock Blades again. Loop closed.
- **Passive A kills → passive B reacts** → legal, B was never in the chain.
- **A → B → A** is also blocked. The rule is "not already in the history", not merely "not the immediate parent" — otherwise two passives could still feed each other in a ring.

Because no passive can repeat within one chain, every chain is **finite by construction** (at most one link per passive you own), with no depth counter to tune.

### What stamps a passive's name on an effect

**Only damage dealt by the passive's own effects.** A passive that merely buffs, extends, or grants something does **not** stamp itself onto damage that an *active* deals.

This is what keeps **Encore** working: Encore extends Bladedance, but Bladedance's storm is still the active's damage, so each kill can fire Encore again. If Encore instead dealt damage itself, that damage would carry Encore's name and couldn't re-fire it.

### Why this, and not charges

- **Charges are a budget** — how many times a thing may happen.
- **This rule is the brake** — what stops a thing from feeding itself.

Charges cannot serve as the brake: a skill spends its charge when its blow **lands**, a moment after it was triggered, and in that gap the blow can trigger the next copy while the count still reads full. This rule closes that gap regardless of timing, at any depth.

It **replaces** the older partial guard, which only caught immediate chains and missed delayed ones — one rule, not two.

**Cost:** carrying a small "who caused this" history with each effect instead of a single yes/no flag. Same places to touch, marginally wider payload, and it forecloses far less: unrelated passives can still respond to one another.

## New fields

### On the SET side (any Buff/Debuff effect)

- **Stacking** (checkbox). Off = single instance. On → reveals:
  - **Stack Limit** (int) — the ceiling; oldest drops past it.
  - **Stacks Per Apply** (int) — how many stacks one application grants. This is the charge-granting knob (Lunge sets 3).

### On the REQUIRE side — SKILL-level

The gate lives on the **skill**, not per-effect: the whole skill (active or passive) simply does not run when its requirement isn't met — **no VFX, no cast, nothing**. This matters for Lock Blades, which must show no auto-lunge at all when out of charges.

- **Require State** (dropdown of all Buffs + Debuffs — one shared list).
- **Min Stacks** (int) — "only fire at ≥3".
- **Consume** — how many stacks the use spends: a count, or *all* (Deathblow). Only one of the two is ever shown, so there's no meaningless field.

Both the pressed-skill path and the passive path check this through **one** shared gate, so no path can forget it.

- ⚠️ **Once a state id is saved on a skill asset, the state list becomes append-only** — new states must be added at the end or existing skills silently repoint. Same discipline the effect list already follows.

### Deferred — Owner (Private / Shared)

The Owner axis (per-caster copies vs one shared pile) is **deferred**. There is no multiplayer yet, so the two are indistinguishable and untestable today. The store is shaped so an owner can be added per stack later without reworking stacks.

## Overwrite policy (non-stacking) — LOCKED

Applies to states with Stacking **off**: **newest wins.** The overwriting cast replaces both duration AND magnitude, **even if weaker** (a fresh weak Haste over a strong one is acceptable). This is already live in the unified store.

## Code reality (measured)

The unified per-unit state store is **built** — one store per unit holding every buff and debuff, the single source for both gameplay and the on-screen icons (player tray + mob nameplates). It replaced the old scattered ailment arrays, the hand-written buff windows, and the separate vulnerability field.

⚠️ **That migration has not been Play-tested.** Stacking builds directly on it, so a failure could come from either layer — combat-test the store before adding stacks, or phase 1's "nothing existing changed" claim has no baseline to be measured against.

**What stacking still adds:** per-stack lists in the store, the two authoring sides above, a stack count on the on-screen icons, and the passive-origin rule.

## Build order

1. **Stacks in the store** — non-stacking behaviour provably unchanged.
2. **Set side + stack count on icons** — author a stacking state and watch it climb and expire. (The count on icons is what makes stacking observable at all, so it ships here.)
3. **Require side** — the gate, and compute-before-consume.
4. **No-self-feeding rule** — the brake. **Prerequisite for Lock Blades**, not a follow-up.
5. **Content: Lock Blades + Momentum.** Lock Blades also needs its open roster question answered first: "auto-pursue" must resolve to **one** mechanism engine-wide, chosen once so every weapon that reuses it behaves identically.

## First consumers

- **Lock Blades** (charges) — casting Lunge grants 3 charges; each landed hit triggers an auto-lunge and spends one. Exercises: stacks-granted-at-once, the skill-level gate, consume-per-use, and the no-self-feeding brake.
- **Momentum** (stacks) — basic attacks add one stack each, own timers, FIFO expiry; each stack is a fixed +% attack; Deathblow reads the live total, then consumes all. Exercises: independent per-stack timers, sum-of-stacks, and consume-all after compute.
