---
name: stand-and-fight-latch-outside-reach
description: "Player stops chasing and needs a re-click" was the stand-and-fight latch staying on PAST reach — a target parked in that band froze the attacker; fixed 2026-09-26 by moving the margin inside reach.
metadata:
  type: project
---

**Symptom (user's words):** "when the player is flinched, it also stops chasing its target" — the locked target
bites you, your character stands there neither swinging nor stepping in, and only a re-click gets it going.

**Root cause (measured with the text-play client, not the flinch):** `UnitOffense.EngageLatch` turned on at reach and
stayed on until reach × `stayEngagedFactor` (1.4). A target standing between those (spider at 0.327 vs sword reach
0.325; band up to 0.455) kept the attacker "engaged" (feet stopped) while the strict swing check refused (out of
reach). Client log said `CANNOT REACH ... 0.327 away and this weapon reaches 0.325` and the stall lasted until the
spider moved. Re-clicking worked only because `Player3DAttacker.Cancel` resets the latch. The flinch never dropped
the order — measured the order staying `Attack` through dozens of hits. Creatures used the same latch (their hold is
capped at the player's reach, so they parked exactly in that band and bit forever).

**Fix (user chose "stop inside reach"):** latch now turns on at reach ÷ factor and off past reach
(`EngageLatch.Ask`). Creatures that walk in pass min(hold, own reach); creatures holding a shooting distance
(`MonsterAI3D.HoldsAPocket`) use `AskHolding` (on at the distance, off at distance × factor) because their feet are
steered by the pocket, not the latch. The flinch's 0.4 s leg pause was kept on purpose (user's choice).

**Gotcha:** the headless client's log file flushes late — lines can appear a minute after the event; read it after
a later event, and prove coverage with a line you know was written.

Related: [[ai-plays-headless-client]].
