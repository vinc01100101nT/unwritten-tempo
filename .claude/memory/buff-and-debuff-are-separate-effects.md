---
name: buff-and-debuff-are-separate-effects
description: HARD design rule — a buff effect only buffs; a debuff is its own named effect. Never express a debuff as a negative magnitude on a buff.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: dc29f1e3-014e-4f28-81af-65199a08afda
---

**A BUFF effect only ever BUFFS. A DEBUFF is its own separate, named effect.** Never express "weaken the
enemy" as a negative number on a buff knob. Magnitude stays positive and the EFFECT NAME carries the
direction: `Buff` raises a stat, a separate weaken/debuff kind lowers one. Stated by the user 2026-08-13,
emphatically, after I proposed unlocking negatives on the `Buff` slider to author Disarm.

**Why:** a negative-on-a-buff is a dead knob wearing the wrong name — the author reads "Buff: damage
-0.3" and has to decode the sign to learn it is a debuff. That is exactly what
[[name-knobs-concretely-one-job]] forbids: one named thing, one direction, no decoding. It also makes the
composer UI lie (a slider labelled "bonus (0.3 = +30%)" that can mean a penalty).

**How to apply:** when a design needs an enemy stat lowered (Disarm = cut enemy attack damage), add a
DEBUFF effect kind named for what it reduces, with a positive magnitude meaning "reduce by that much" —
do not widen the `Buff` slider's range below 0, and do not reuse `Buff` with a sign flip.

**Known existing violation of this rule (NOT changed — needs the user's call):** the `Haste` effect kind
already does exactly the banned thing. Its composer slider runs `-0.9 .. 2` and its tooltip reads
*"NEGATIVE slows instead (Haste's mob-debuff job): -0.4 = 40% slower."* — `SkillComposerWindow.cs:1085`.
So move speed is authored as one signed knob while the rule says it should be two named effects. Raise it
before adding any new stat debuff, so the new one does not copy the wrong pattern. Related:
[[magnitude-always-bonus-fraction]].
