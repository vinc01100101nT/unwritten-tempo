---
name: refused-press-left-a-charge-run-holding
description: A skill press the server REFUSED left the pressing machine's charge-driven run going forever, holding the caster still — no WASD, no T, no skills. Fixed by CastAnswered.refused.
metadata:
  type: project
---

**Symptom (2026-10-12, first auto-combat play-test):** after auto-combat pressed a few skills the character froze for
good — no WASD, no T, nothing — and stayed frozen after auto-combat was stopped. The editor log showed NO error.

**Measured:** the server log had one line at the freeze: it refused `skill.sword.onslaught` (Petal Storm) as "not on the
hotbar this server holds for them". Petal Storm is a charge-driven run (`repeatCostsCharge`) with `holdCasterForRun`.

**Root cause:** on a player's machine a skill is only a picture (`SkillRunner.Watching` is true, so `OnTick` returns
before it spends anything); its run ends when the pile the SERVER spends reaches 0 in the mirrored state store. A press
the server refused never spends that pile, so the picture's run never ended and re-took the hold every beat. Any
refused press of a charge-driven, holding skill could do it — auto-combat only made a refusal likely.

**Fix:** `GameNetwork.CastAnswered` carries `refused`; `RunCastRequest` returns whether it cast; the owner's machine calls
`SkillRunner.ServerRefusedPress()` (stops `runRoutine`, ends a wind-up, `Unlock()`s a holding skill), and
`AutoCombatClient.ServerRefused` skips that auto-skill for 10 s. The refusal log now ends "Exactly: …" (on the
hotbar / in the auto slots and what they hold / learned and weapon) — read it to learn WHY a skill was not theirs.

**Still open when written:** why the server refused Petal Storm at all (its auto slots should have held it) — the next
server log's "Exactly:" line answers it.
