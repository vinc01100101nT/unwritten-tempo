---
name: paced-release-held-two-minutes
description: Auto-combat froze the character for ~2 minutes — a skill's movement-paced release animation divided by a walking pace that a same-frame skill root had left at 0. Fixed with StatusController.PaceMult and a 4× cap in StanceController.ActionDuration.
metadata:
  type: project
---

**Symptom (2026-10-12):** auto-combat hunted fine for minutes, then the character froze — no WASD, no T — even after
auto-combat was stopped. No error in any log.

**Measured** with the "keys not answered" instrument (Player3DController.AutoCombat.cs, `ReportWhyInputIsBlocked`):
`held still by a skill for 120.1s more | runs: skill.sword.onslaught (0 charges) skill.sword.blade_dance (0 charges)`.

**Root cause:** Petal Storm and Thousand Petals release with stance 20 (PhysicalSkill1), whose speedSource is
MoveGait, so its hold = natural length ÷ `Player3DController.GaitSpeedMult`. That pace was written every free frame from
`StatusController.MoveMult`, which reads 0 while ActionLocked. Auto-combat presses INSIDE Update's Tick, so the press
rooted the caster and the same frame then wrote a pace of 0; the next press — on the very first free frame, before the
pace was rewritten — divided by 0 (clamped to 0.01): 1.2 s × 100 = 120 s. A hand press almost never lands on that frame.

**Fix:** the pace reads `StatusController.PaceMult` (Slow × Haste, not zeroed by a root), and
`StanceController.ActionDuration` never times a paced hold slower than 0.25 (at most 4× the clip).
