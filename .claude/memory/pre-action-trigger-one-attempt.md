---
name: pre-action-trigger-one-attempt
description: "A trigger that PRECEDES an action must be gated by an open/close attempt flag, never by its own parallel timer — the timer always expires first and gates nothing."
metadata: 
  node_type: memory
  type: project
  originSessionId: 38ccde61-b0b2-4cef-b2af-28d7ab1bb477
---

**Symptom (2026-08-11):** `skill.sword.auto_lunge` (a passive on the new `OnBasicAttackStart` trigger) re-fired
continuously — the player chain-lunged, visibly ignoring attack speed, and often never got a swing in between.

**Root cause:** the fire-site (`Player3DAttacker.AnnounceAttackIntent`) was gated by its OWN timer stamped
`Time.time + SwingTime`, running in parallel to the swing's `nextSwing`. One lunge cycle costs *dash travel + swing*,
but the intent timer only waited *swing* — so it had ALWAYS expired by the time the controller regained control, and
therefore gated nothing. The observed rate was set by how fast a dash finished, not by attack speed. Nothing tied
"an attack was announced" to "an attack happened".

**Fix:** the announce and the swing that follows are ONE attempt, tracked by a bool (`attemptOpen`), and the parallel
timer was DELETED so the swing cadence is the only clock:
- `AnnounceAttackIntent` — opens the attempt; refuses while one is open or a swing isn't due (`Time.time >= nextSwing`).
- `TryAttack` — closes it when the swing actually starts.
- `Cancel()` — closes it when the attack is abandoned (hard CC / new order / combat cleared). This is the single choke
  point every cancel already routes through, so "cancelled for any reason" needed no new hooks. A stuck-open attempt
  would silence the passive forever, so this arm is load-bearing, not defensive.

**The general rule (applies to any future pre-action trigger, e.g. an `OnCastStart`):** arm once, consume by the action
it precedes, one flag per action kind owned by whoever owns that action. Never a shared latch that any event may clear
(multiple writers = this bug from another direction), and never a second timer beside the action's own cadence
(two clocks for one rhythm = the single-source-of-truth rule in CLAUDE.md violation).

**Related locked rule, same change:** charged states are spent on FIRE and never refunded — a cancelled swing keeps the
charge spent. Documented in `StateGate`'s class comment. A refund/deferred-pay path would make cancelling free
(cancel-spam re-triggers a charged effect for nothing) and would need the store to track owed charges. `Consume` has no
inverse on purpose. Parry follows the same rule (`UnitMitigation` spends the charge on the hit it erases).

**Gotcha for diagnosis:** an empty/expired gate that "should" throttle proves nothing — check whether the gate's clock
can still be unexpired at the moment it's read. See [[find-or-create-hides-duplicates]] for the sibling failure shape.
