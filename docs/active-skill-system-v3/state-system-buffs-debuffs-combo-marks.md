# State System — Buffs, Debuffs & Combo Marks (V3)

## Why this exists

Skill **pairing** (skill A sets up a condition that skill B pays off — e.g. Sword's Parry → Riposte) does **not** need a separate "mark" system. Every buff/debuff is *already* a state on a target (self-buffs already draw icons on-screen). So we **unify marks into the existing buff/debuff system** — no parallel mechanism — and add the few knobs that pairing + stacking + multiplayer ownership require. Companion to **Active Skill System — V3**; feeds weapon-roster authoring (combos are now composable, not hardcoded per weapon).

## The STATE hierarchy (Skill Composer effect reorg)

Effects that apply a lasting condition live under one parent category:

- **STATE**
  - **BUFF** — all buffs (Damage / AttackSpeed / CastSpeed / Guard / Regen / Shield / Haste …).
  - **DEBUFF** — all statuses, **renamed from "Status"** so it reads as the sibling of Buff (Poison / Bleed / Burn / Slow / Stun / Freeze / Silence / Blind / Vulnerability …).

Applying any buff/debuff **automatically marks its target with that state.** The state's identity IS the mark — a combo reads whatever buff/debuff already exists (no "empty/none" tag type needed).

## New fields

### On the SET side (any Buff/Debuff effect)

- **Stacking** (checkbox). Off = single instance. On → reveals **Stack Limit** (int). Each application adds a stack carrying its **own duration**; expiry is **FIFO** (oldest stack drops first); over the limit drops the oldest.
- **Owner** (Private / Shared) — *independent axis from Stacking*:
  - **Private** — each caster gets their own instance/stack pile on the target.
  - **Shared** — one instance/pile on the target that everyone contributes to.

### On the REQUIRE side (an effect that gates on a state)

- **Require State** (dropdown of all Buffs + Debuffs — one shared list, pick not type).
- **Consume** (checkbox) — clear the state (or one stack) on use.
- **Min Stacks** (int) — only shown if the required state can stack ("detonate at ≥3").
- **Track Owner** (checkbox) — only shown when the required state is **Private**; the effect only fires for the caster who applied it.

## The two axes (Owner × Stacking = 2×2)

- **Private + non-stack** — per-caster single refreshing copy (e.g. each player's own Haste; Parry→Riposte).
- **Private + stack** — a personal combo counter.
- **Shared + non-stack** — one instance anyone can refresh/overwrite.
- **Shared + stack** — one additive shared pile (raid debuff — everyone stacks Sunder).

## Overwrite policy (non-stacking) — LOCKED

- **Private** — no conflict; each caster owns their copy.
- **Shared, non-stacking** — **newest wins.** The overwriting cast replaces both duration AND magnitude, **even if weaker** (a fresh weak Haste over a strong one is acceptable). This is a deliberate change from today's `StatusController.Apply` behavior, which takes the **longer** timer (`until = Max(until, now+seconds)`) and is owner-blind.

## Code reality (measured, not assumed)

- `**StatusController**` stores state in fixed arrays keyed by the `Ailment` enum: one `until[]` / `mag[]` / `nextTick[]` per ailment per unit — **single timer, refresh-only (`Max`), no stacks, no owner.**
- **Buffs** live on `Character` as separate hand-written timed windows (`dmgBuffUntil`, `aspdBuffUntil`, `castBuffUntil`, `guardUntil`, `regenUntil`, `shieldUntil`, `leechUntil`, `reflectBonusUntil`).
- **Implication:** unifying these into one per-unit **state store** that supports stacks (a small FIFO list per state) + owner id (Private match) is a **real refactor of both**, not a drop-in. That's the build. Payoff: every future combo pair (Warhammer, Spear, Dagger, Scythe) becomes pure Skill-Composer authoring, and buffs/debuffs stop being two scattered code paths.

## First consumer

Sword **Parry** (self Def-Guard buff, Private, non-stacking) → **Riposte** (Require State = that Guard buff, Consume, Track Owner). No new mark type; Riposte just requires Parry's buff.
