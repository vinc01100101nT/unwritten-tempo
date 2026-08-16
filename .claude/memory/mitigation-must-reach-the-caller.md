---
name: mitigation-must-reach-the-caller
description: "Bug — mitigation applied inside ApplyHitToHP never reached the caller, so absorbed hits still flashed, reflected, charged the ult and paid the attacker"
metadata: 
  node_type: memory
  type: project
  originSessionId: c033d9ec-424c-4812-b20d-61eefcb38f29
---

**Symptom (found 2026-08-16 via Brace):** a hit Brace held back popped a BLUE number *and* a red one from the
mob, for the same single hit.

**Root cause:** `Character.ApplyHitToHP(int dmg)` took the damage BY VALUE. It reassigned its local copy for
the Shield absorb and the Brace defer, but the caller's `dmg` still held the pre-mitigation figure — and that
stale number fed `OnDamaged` (hurt flash), `Reflect` (thorns), `AddUltCharge`, and the RETURN value. The mob
reads that return as "did I connect?" (`MonsterAI3D.cs:449`), so it popped its red digit, inflicted its on-hit
ailment and collected XP for a hit that cost nothing.

**Not a Brace bug — it predated Brace.** `AfterShield` sits on the same line, so a fully-absorbed Shield hit
already did all five. Brace only made it visible by printing its own number next to the wrong one.

**Fix:** `ApplyHitToHP` now RETURNS what actually landed; both intakes assign from it and gate everything
downstream on `> 0`. Its doc comment says the return is not what the caller passed in, so nobody re-introduces it.

**The lesson, generally:** when a function mutates a parameter to represent "what survived", it must RETURN
that — a by-value reassign is invisible to every caller. Grep for other by-value "mitigation" params before
trusting the same shape elsewhere. See [[find-or-create-hides-duplicates]] for the sibling "fix the CLASS not
the instance" instinct that found it.
