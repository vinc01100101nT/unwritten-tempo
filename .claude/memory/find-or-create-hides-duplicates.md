---
name: find-or-create-hides-duplicates
description: "Find-or-create (GetComponent ?? AddComponent) can't fail, so it silently makes duplicates of a \"single source\"; and one broken member of a uniform system means check the whole class, not just that member."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 52f1b21a-c216-426e-94d3-10993632fd09
---

Two linked lessons from the 2026-08-06 unified-state-store bug (player Haste sped the player but showed no icon; mob Haste did nothing).

**1. A find-or-create call CANNOT report absence — it manufactures a duplicate.**
`GetComponent<T>() ?? AddComponent<T>()` (and every `getOrCreate` / `setdefault` / `find_or_create` sibling) never fails. When the lookup misses, it creates a second instance and returns it, *looking like success*. In this bug `GetComponent<UnitStates>()` returning null didn't mean "this unit has no store" — it meant "not on **this** GameObject" — and the fallback turned that into a second store. Two stores, each half-right: gameplay read one, the buff tray read the other.

**Why:** this is the "an empty/null result is NOT proof of absence" rule in disguise. The detector (`GetComponent`) is scoped to one GameObject; the *concept* ("this unit's store") spans several. A pattern meant to enforce *exactly one* of something must **assert or resolve an explicit owner** — never create on miss.

**How to apply:** before writing find-or-create for anything described as "the one X per unit/session/app", ask *what scope does this lookup actually cover, and is that the same scope the concept lives at?* If they differ, resolve the owner explicitly (here: `Character.Instance.States`) and let a genuine miss be visible. Treat any "there is exactly one X" claim as a **measurable fact** — verify it against real scene/config/DB data before writing it in a doc or handoff. This one was asserted in a handoff from reading code, and a single grep of the scene file disproved it. See [[player-two-gameobjects-one-store]] for the concrete instance.

**2. One broken member of a uniform system = check the WHOLE class before fixing.**
The user reported Haste; I diagnosed and proposed a fix for Haste. But Haste was one symptom of shared plumbing that broke **every** player ailment and **every** mob speed state. Fixing the reported instance would have left the rest bugged, and the user (rightly) called this out as cherry-picking.

**Why:** when N things share one mechanism, a defect in the mechanism is N bugs, not 1. Fixing the reported one produces a fix that "works" in the demo and fails everywhere else — the most expensive kind of wrong.

**How to apply:** on any bug in a system with uniform members (states/effects/enum cases/routes/event kinds), **enumerate the members and check each against the suspect mechanism** before proposing a fix. Report the true blast radius ("this breaks 12 of 20 states"), and fix at the mechanism, not the instance. Say the class out loud in the diagnosis so the fix's scope is reviewable.
